provider "azurerm" {
  features {
    log_analytics_workspace {
      # The environment is destroyed after every session. Without this, a deleted workspace
      # sits soft-deleted for 14 days and the next apply has to recover it.
      permanently_delete_on_destroy = true
    }
  }

  # The pipeline identities only have rights on one resource group, so they can't register
  # resource providers at subscription scope. Providers were registered once in the bootstrap.
  resource_provider_registrations = "none"
  # Shared keys are disabled on the receipts account, so storage calls must use Entra tokens.
  storage_use_azuread = true
}

# Created by hand in the bootstrap (docs/bootstrap.md), so Terraform reads it instead of owning it.
data "azurerm_resource_group" "main" {
  name = "rg-expense-claims"
}

data "azurerm_client_config" "current" {}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-expense-claims"
  location            = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  # Stops ingestion for the day after 100 MB, so a noisy app can't run up a bill (D3).
  daily_quota_gb = 0.1


}
# Turns Microsoft Sentinel on for the workspace. Detection rules come in later steps.
resource "azurerm_sentinel_log_analytics_workspace_onboarding" "main" {
  workspace_id = azurerm_log_analytics_workspace.main.id
}
# R2/E3: an approval or rejection by someone who isn't the employee's manager, judged from the
# SetManager lines in the same logs. The API already refuses this, so this catches an API bug or
# a database edit. Silent if no SetManager line exists for the employee.
resource "azurerm_sentinel_alert_rule_scheduled" "approval_by_non_manager" {
  name                       = "approval-by-non-manager"
  log_analytics_workspace_id = azurerm_sentinel_log_analytics_workspace_onboarding.main.workspace_id
  display_name               = "Approval or rejection by someone who isn't the employee's manager"
  severity                   = "High"
  query_frequency            = "PT5M"
  query_period               = "P1D"
  trigger_operator           = "GreaterThan"
  trigger_threshold          = 0

  query = <<-KQL
    let lookback = 15m;
    let src = ContainerAppConsoleLogs_CL;
    let audit = src
    | where Log_s contains "AUDIT action="
    | extend action = extract(@"action=(\S+)", 1, Log_s),
             actor = extract(@"actor=(\S+)", 1, Log_s),
             claim = extract(@"claim=(\S+)", 1, Log_s),
             employee = extract(@"employee=(\S+)", 1, Log_s),
             detail = extract(@"detail=(.*)$", 1, Log_s);
    let managerSet = audit
    | where action == "SetManager"
    | project SetAt = TimeGenerated, employeeId = extract(@"user (\S+):", 1, detail), newManager = extract(@"-> (\S+)", 1, detail);
    audit
    | where TimeGenerated > ago(lookback)
    | where action in ("Approve", "Reject")
    | join kind=inner managerSet on $left.employee == $right.employeeId
    | where SetAt <= TimeGenerated
    | summarize arg_max(SetAt, newManager) by TimeGenerated, claim, actor, employee
    | where actor != newManager
    | project TimeGenerated, claim, actor, employee, expectedManager = newManager
  KQL
}
