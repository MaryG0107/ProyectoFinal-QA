# =============================================================
# FlashStore - Terraform: variables
# Los valores reales van en terraform.tfvars (NO se sube al repo).
# Plantilla: example.tfvars
# =============================================================

variable "project_id" {
  description = "ID del proyecto de Google Cloud"
  type        = string
}

variable "credentials_file" {
  description = "Ruta a la llave JSON de la cuenta de servicio terraform-flashstore"
  type        = string
  default     = "~/.gcp/terraform-flashstore.json"
}

variable "region" {
  description = "Región de Google Cloud"
  type        = string
  default     = "us-central1"
}

variable "zone" {
  description = "Zona de Google Cloud"
  type        = string
  default     = "us-central1-a"
}

variable "environment" {
  description = "Nombre del ambiente (se usa en etiquetas y nombres)"
  type        = string
  default     = "qa"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,14}$", var.environment))
    error_message = "environment debe ir en minúsculas, empezar con letra y tener de 2 a 15 caracteres."
  }
}

variable "machine_type" {
  description = "Tipo de máquina. e2-medium (2 vCPU, 4 GB) alcanza para los 6 contenedores de FlashStore."
  type        = string
  default     = "e2-medium"
}

variable "disk_size_gb" {
  description = "Tamaño del disco de arranque en GB"
  type        = number
  default     = 30

  validation {
    condition     = var.disk_size_gb >= 20 && var.disk_size_gb <= 50
    error_message = "disk_size_gb debe estar entre 20 y 50 GB para controlar el costo."
  }
}

variable "ssh_user" {
  description = "Usuario Linux que se crea en la VM (Ansible entra con este usuario)"
  type        = string
  default     = "mary"
}

variable "ssh_public_key_path" {
  description = "Llave pública SSH que se instala en la VM"
  type        = string
  default     = "~/.ssh/flashstore_gcp.pub"
}

variable "ssh_private_key_path" {
  description = "Llave privada SSH que usará Ansible (solo se escribe su RUTA en el inventario)"
  type        = string
  default     = "~/.ssh/flashstore_gcp"
}

variable "allowed_cidrs" {
  description = "Redes que pueden entrar por SSH (22), a la PWA (3000) y a la API (5000). Ej.: [\"190.56.10.20/32\"]"
  type        = list(string)

  validation {
    condition     = length(var.allowed_cidrs) > 0 && alltrue([for c in var.allowed_cidrs : can(cidrhost(c, 0))])
    error_message = "allowed_cidrs debe tener al menos una red válida en formato CIDR (por ejemplo 190.56.10.20/32)."
  }

  validation {
    condition     = !contains(var.allowed_cidrs, "0.0.0.0/0")
    error_message = "No se permite 0.0.0.0/0: abriría SSH y la API a todo Internet."
  }
}
