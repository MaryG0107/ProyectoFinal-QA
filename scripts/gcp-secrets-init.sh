#!/usr/bin/env bash
# =============================================================
# Genera el archivo de secretos para el despliegue en Google Cloud.
# Se guarda FUERA del repositorio: ~/.gcp/flashstore-secrets.env (600)
# Solo se ejecuta una vez; si ya existe no lo sobrescribe.
# =============================================================
set -euo pipefail

SECRETS="${FLASHSTORE_SECRETS:-$HOME/.gcp/flashstore-secrets.env}"

if [[ -f "$SECRETS" ]]; then
  echo "Ya existe $SECRETS (no se modifica)."
  exit 0
fi

mkdir -p "$(dirname "$SECRETS")"
chmod 700 "$(dirname "$SECRETS")"

umask 077
cat > "$SECRETS" <<SECRETS_EOF
# Secretos de FlashStore en Google Cloud. NO subir a GitHub.
FLASHSTORE_POSTGRES_PASSWORD=$(openssl rand -hex 20)
FLASHSTORE_RABBITMQ_PASSWORD=$(openssl rand -hex 20)
FLASHSTORE_JWT_KEY=$(openssl rand -hex 32)
SECRETS_EOF
chmod 600 "$SECRETS"

echo "Secretos generados en $SECRETS (permisos 600)."
