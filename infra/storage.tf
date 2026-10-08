resource "azurerm_storage_account" "receipts" {
  # Globally unique, 3-24 lowercase letters and digits.
  name                     = "stexpenseclaimscg21836"
  location                 = data.azurerm_resource_group.main.location
  resource_group_name      = data.azurerm_resource_group.main.name
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false
  # No account keys or SAS: the app reaches blobs with its managed identity only (S3).
  shared_access_key_enabled = false

  network_rules {
    default_action             = "Deny"
    virtual_network_subnet_ids = [azurerm_subnet.apps.id]
    # Lets Azure's own services (e.g. Defender scanning) through.
    bypass = ["AzureServices"]
  }

  blob_properties {
    delete_retention_policy {
      days = 7
    }
  }
}

# Created through the management plane (storage_account_id), so the pipeline doesn't
# have to get past the firewall to make it.
resource "azurerm_storage_container" "receipts" {
  name                  = "receipts"
  storage_account_id    = azurerm_storage_account.receipts.id
  container_access_type = "private"
}
