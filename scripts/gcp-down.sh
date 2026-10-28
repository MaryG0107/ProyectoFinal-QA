#!/usr/bin/env bash
# =============================================================
# Destruye TODO lo que creó Terraform en Google Cloud (VM, disco,
# IP, firewall y red) para no consumir crédito.
# Uso:  scripts/gcp-down.sh
# =============================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TF_DIR="$ROOT/infrastructure/terraform"

terraform -chdir="$TF_DIR" destroy -input=false -auto-approve

# La próxima VM tendrá otra llave de host y otra IP.
rm -f "$HOME/.ssh/known_hosts_flashstore_gcp" "$ROOT/ansible/inventory/gcp.ini"

echo "Infraestructura destruida. Verifique en la consola: Compute Engine > VM instances (debe estar vacío)."
