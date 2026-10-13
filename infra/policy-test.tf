# Throwaway (Phase 4 step 3): proves the pipeline identity can't write policy assignments.
# Revert after the test.
resource "azurerm_resource_group_policy_assignment" "pipeline_cannot_assign" {
  name                 = "pipeline-cannot-assign"
  resource_group_id    = data.azurerm_resource_group.main.id
  policy_definition_id = "/providers/Microsoft.Authorization/policyDefinitions/404c3081-a854-4457-ae30-26a93ef643f9"
  parameters           = jsonencode({ effect = { value = "Audit" } })
}
