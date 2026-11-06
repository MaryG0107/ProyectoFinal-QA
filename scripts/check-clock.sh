#!/usr/bin/env bash
# =============================================================
# Verifica que el reloj local no esté desfasado respecto a Google.
# Google rechaza los tokens firmados con una hora incorrecta
# ("Invalid JWT ... iat and exp"), lo que rompe Terraform y gcloud.
# Uso: scripts/check-clock.sh [segundos_tolerados]   (por defecto 60)
# Código 0 = reloj correcto, 1 = desfasado, 2 = no se pudo comprobar.
# =============================================================
set -euo pipefail

MAX_SKEW="${1:-60}"

remote_date="$(curl -sI --max-time 10 https://www.google.com | tr -d '\r' | grep -i '^date:' | cut -d' ' -f2- || true)"
if [[ -z "$remote_date" ]]; then
  echo "ADVERTENCIA: no se pudo leer la hora de Google (¿sin internet?)." >&2
  exit 2
fi

remote_epoch="$(date -u -d "$remote_date" +%s)"
local_epoch="$(date -u +%s)"
skew=$(( local_epoch - remote_epoch ))
abs_skew=${skew#-}

if (( abs_skew > MAX_SKEW )); then
  echo "ERROR: el reloj está desfasado ${skew} s respecto a Google (máximo ${MAX_SKEW} s)." >&2
  echo "Corríjalo con: sudo timedatectl set-ntp false && sudo timedatectl set-ntp true" >&2
  echo "o bien:       sudo date -u -s \"\$(curl -sI https://www.google.com | grep -i '^date:' | cut -d' ' -f2-)\"" >&2
  exit 1
fi

echo "Reloj correcto (desfase ${skew} s)."
