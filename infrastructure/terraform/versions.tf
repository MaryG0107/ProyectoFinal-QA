# =============================================================
# FlashStore - Terraform: versiones y proveedores
# Ruta A (ajustada): Google Cloud + Terraform + Ansible (ver D-17)
# =============================================================

terraform {
  required_version = ">= 1.6.0"

  # Estado remoto compartido entre la VM controladora y Jenkins (D-18).
  # El nombre del bucket va en backend.hcl:
  #   terraform init -backend-config=backend.hcl
  backend "gcs" {
    prefix = "flashstore/qa"
  }

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
