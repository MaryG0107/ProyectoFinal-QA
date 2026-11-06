#!/usr/bin/env bash
# =============================================================
# Pipeline de infraestructura en un solo comando:
#   1) Terraform crea la red, el firewall, la IP y la VM en Google Cloud
#   2) Ansible instala Docker y despliega FlashStore
#
# Uso (desde la raíz del repo, en la VM controladora):
#   scripts/gcp-up.sh                                  # release GO (Atomic)
#   scripts/gcp-up.sh -e flashstore_inventory_strategy=Unsafe   # NO-GO
# Todo lo que va después del script se pasa a ansible-playbook.
# =============================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SECRETS="${FLASHSTORE_SECRETS:-$HOME/.gcp/flashstore-secrets.env}"
TF_DIR="$ROOT/infrastructure/terraform"

# Credenciales para el proveedor y para el estado remoto (bucket GCS)
export GOOGLE_APPLICATION_CREDENTIALS="${GOOGLE_APPLICATION_CREDENTIALS:-$HOME/.gcp/terraform-flashstore.json}"

# El reloj desfasado hace que Google rechace el token (Invalid JWT)
"$ROOT/scripts/check-clock.sh" || [[ $? -eq 2 ]]   # 2 = no se pudo comprobar: se continúa

if [[ ! -f "$SECRETS" ]]; then
  echo "ERROR: no existe $SECRETS. Ejecute primero scripts/gcp-secrets-init.sh" >&2
  exit 2
fi
if [[ ! -f "$TF_DIR/terraform.tfvars" ]]; then
  echo "ERROR: falta $TF_DIR/terraform.tfvars (copie example.tfvars y ponga su IP)" >&2
  exit 2
fi

set -a
# shellcheck disable=SC1090
source "$SECRETS"
set +a

echo "==> [1/2] Terraform"
terraform -chdir="$TF_DIR" init -input=false -backend-config=backend.hcl
terraform -chdir="$TF_DIR" apply -input=false -auto-approve

echo "==> [2/2] Ansible"
cd "$ROOT/ansible"
ansible-playbook playbooks/site.yml "$@"

echo
terraform -chdir="$TF_DIR" output
