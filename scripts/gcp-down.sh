#!/usr/bin/env bash
# =============================================================
# Destruye TODO lo que creó Terraform en Google Cloud (VM, disco,
# IP, firewall y red) para no consumir crédito.
# Uso:  scripts/gcp-down.sh
# =============================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TF_DIR="$ROOT/infrastructure/terraform"

# Credenciales para el proveedor y para el estado remoto (bucket GCS)
export GOOGLE_APPLICATION_CREDENTIALS="${GOOGLE_APPLICATION_CREDENTIALS:-$HOME/.gcp/terraform-flashstore.json}"

# El reloj desfasado hace que Google rechace el token (Invalid JWT)
"$ROOT/scripts/check-clock.sh" || [[ $? -eq 2 ]]   # 2 = no se pudo comprobar: se continúa

terraform -chdir="$TF_DIR" init -input=false -backend-config=backend.hcl
terraform -chdir="$TF_DIR" destroy -input=false -auto-approve

# La próxima VM tendrá otra llave de host y otra IP.
rm -f "$HOME/.ssh/known_hosts_flashstore_gcp" "$ROOT/ansible/inventory/gcp.ini"

echo "Infraestructura destruida. Verifique en la consola: Compute Engine > VM instances (debe estar vacío)."
