# =============================================================
# Plantilla de variables. Uso:
#   cp example.tfvars terraform.tfvars
#   (terraform.tfvars está en .gitignore y NO se sube)
# =============================================================

project_id = "flashstore-qa"

# Su IP pública con /32. Se obtiene con:  curl -s https://ifconfig.me
# Agregue otra línea por cada red que deba entrar (p. ej. la PC del compañero).
allowed_cidrs = [
  "203.0.113.10/32",
]

# Valores por defecto (descomente para cambiarlos)
# region               = "us-central1"
# zone                 = "us-central1-a"
# environment          = "qa"
# machine_type         = "e2-medium"
# disk_size_gb         = 30
# ssh_user             = "mary"
# credentials_file     = "~/.gcp/terraform-flashstore.json"
# ssh_public_key_path  = "~/.ssh/flashstore_gcp.pub"
# ssh_private_key_path = "~/.ssh/flashstore_gcp"
