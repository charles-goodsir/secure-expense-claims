# Private access: the server's name resolves to its VNet address only through this zone.
# The zone name must end in .postgres.database.azure.com.
resource "azurerm_private_dns_zone" "postgres" {
  name                = "expense-claims.postgres.database.azure.com"
  resource_group_name = data.azurerm_resource_group.main.name
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                  = "postgres-vnet"
  resource_group_name   = data.azurerm_resource_group.main.name
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  virtual_network_id    = azurerm_virtual_network.main.id
}

# The Entra admin on the server. The migration job runs as this identity in step 8, so it
# owns the schema; the app identity gets DML only (I5).
resource "azurerm_user_assigned_identity" "migrations" {
  name                = "id-expense-claims-migrations"
  location            = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
}

resource "azurerm_postgresql_flexible_server" "main" {
  # Server names are global, so add a suffix.
  name                = "psql-expense-claims-cg21836"
  location            = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  version             = "18"
  sku_name            = "B_Standard_B1ms"
  storage_mb          = 32768

  # No public address, reachable only from the VNet (I6).
  delegated_subnet_id           = azurerm_subnet.postgres.id
  private_dns_zone_id           = azurerm_private_dns_zone.postgres.id
  public_network_access_enabled = false

  # Entra only: no password exists to leak or rotate (S3).
  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = data.azurerm_client_config.current.tenant_id
  }

  backup_retention_days = 7

  # The server can't be created until the DNS zone is linked to the VNet.
  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]

  lifecycle {
    # Azure picks an availability zone when none is set; without this every plan wants to change it.
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_active_directory_administrator" "migrations" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = data.azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  object_id           = azurerm_user_assigned_identity.migrations.principal_id
  principal_name      = azurerm_user_assigned_identity.migrations.name
  principal_type      = "ServicePrincipal"
}
