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
