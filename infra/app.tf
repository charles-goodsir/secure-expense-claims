locals {
  # The image Trivy scanned and CI pushed, pinned by digest. A tag can be moved to point at
  # different content; a digest can't. Bump this in a PR to deploy a new build.
  api_image = "ghcr.io/charles-goodsir/expense-claims-api@sha256:58cbf3466c110039d718709bc54e26ac46d9f0b2aad5e0f636efce89454fdd79"
}

resource "azurerm_postgresql_flexible_server_database" "claims" {
  name      = "expenseclaims"
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# The API's own identity. It gets blob access here and DML-only database rights from the
# migration job in step 8b, nothing else (E5).
resource "azurerm_user_assigned_identity" "api" {
  name                = "id-expense-claims-api"
  location            = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
}

# Scoped to the receipts container, not the account or resource group (E5).
resource "azurerm_role_assignment" "api_receipts" {
  scope                = azurerm_storage_container.receipts.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.api.principal_id
  principal_type       = "ServicePrincipal"
}

# A workload-profiles environment, because only those can use a delegated /24 subnet.
# Apps on the Consumption profile still scale to zero and bill per use.
resource "azurerm_container_app_environment" "main" {
  name                       = "cae-expense-claims"
  location                   = data.azurerm_resource_group.main.location
  resource_group_name        = data.azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  infrastructure_subnet_id   = azurerm_subnet.apps.id

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

resource "azurerm_container_app" "api" {
  name                         = "ca-expense-claims-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.api.id]
  }

  template {
    # Scales to zero when idle, and never past one replica (D3).
    min_replicas = 0
    max_replicas = 1

    container {
      name   = "api"
      image  = local.api_image
      cpu    = 0.25
      memory = "0.5Gi"

      # ASPNETCORE_ENVIRONMENT is deliberately unset, so the app runs as Production: JWT bearer
      # auth, no dev sign-in, no startup migrations (S2).
      env {
        name  = "ManagedIdentity__ClientId"
        value = azurerm_user_assigned_identity.api.client_id
      }
      # No password in it: the managed identity's token is the password.
      env {
        name  = "ConnectionStrings__Claims"
        value = "Host=${azurerm_postgresql_flexible_server.main.fqdn};Database=${azurerm_postgresql_flexible_server_database.claims.name};Username=${azurerm_user_assigned_identity.api.name};SSL Mode=VerifyFull"
      }
      env {
        name  = "Receipts__ContainerUri"
        value = "${azurerm_storage_account.receipts.primary_blob_endpoint}${azurerm_storage_container.receipts.name}"
      }
    }
  }

  ingress {
    external_enabled = true
    # .NET container images listen on 8080 by default.
    target_port = 8080
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }
}

output "api_url" {
  value = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}
