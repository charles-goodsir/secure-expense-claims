resource "azurerm_virtual_network" "main" {
  name                = "vnet-expense-claims"
  location            = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  address_space       = ["10.20.0.0/16"]
}

# Container Apps environment. A workload-profiles environment needs at least a /27,
# delegated to Container Apps.
resource "azurerm_subnet" "apps" {
  name                 = "snet-apps"
  resource_group_name  = data.azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.20.0.0/24"]
  # Lets the storage firewall recognise traffic from this subnet (I6, cheap version).
  service_endpoints = ["Microsoft.Storage"]

  delegation {
    name = "container-apps"
    service_delegation {
      name    = "Microsoft.App/environments"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

# Postgres Flexible Server with private access is injected into its own delegated subnet,
# so it has no public address at all (I6).
resource "azurerm_subnet" "postgres" {
  name                 = "snet-postgres"
  resource_group_name  = data.azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.main.name
  address_prefixes     = ["10.20.1.0/28"]
  # Azure adds this when the Flexible Server is created (backups and WAL go to Storage).
  # Declared here so plans don't try to remove it.
  service_endpoints = ["Microsoft.Storage"]

  delegation {
    name = "postgres"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}
