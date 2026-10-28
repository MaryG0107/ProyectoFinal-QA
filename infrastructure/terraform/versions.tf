# =============================================================
# FlashStore - Terraform: versiones y proveedores
# Ruta A (ajustada): Google Cloud + Terraform + Ansible (ver D-17)
# =============================================================

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = ">= 6.0.0, < 8.0.0"
    }
    local = {
      source  = "hashicorp/local"
      version = ">= 2.5.0, < 3.0.0"
    }
  }
}

# La llave de la cuenta de servicio vive FUERA del repositorio (~/.gcp).
provider "google" {
  credentials = file(pathexpand(var.credentials_file))
  project     = var.project_id
  region      = var.region
  zone        = var.zone
}
