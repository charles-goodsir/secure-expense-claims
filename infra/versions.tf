terraform {
  required_version = "~> 1.16.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.81"
    }
  }

  # Not secret. The pipeline signs in with OIDC; there's no storage key anywhere.
  backend "azurerm" {
    resource_group_name  = "rg-tfstate"
    storage_account_name = "tfstatecg21836"
    container_name       = "expense-claims"
    key                  = "expense-claims.tfstate"
    use_azuread_auth     = true
  }
}
