#!/usr/bin/env python3
"""
Guarda el resultado de una etapa del pipeline con el formato del CLI
(reports/cli/<nombre>.json), para que "flashstore report go-no-go" lo use.

Uso: python3 scripts/record_result.py <nombre> <codigo_salida> "<resumen>" [--out reports/cli]
  nombre        test-e2e, test-load, ...
  codigo_salida 0 = pasa; cualquier otro valor = falla
"""
import argparse
import json
import os
from datetime import datetime, timezone

parser = argparse.ArgumentParser()
parser.add_argument("name")
parser.add_argument("exit_code", type=int)
parser.add_argument("summary")
parser.add_argument("--out", default="reports/cli")
args = parser.parse_args()

passed = args.exit_code == 0
result = {
    "name": args.name,
    "passed": passed,
    "summary": f"{args.summary}: {'aprobadas' if passed else f'FALLA (código {args.exit_code})'}.",
    "executedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    "details": {"exitCode": args.exit_code},
}
os.makedirs(args.out, exist_ok=True)
with open(os.path.join(args.out, f"{args.name}.json"), "w", encoding="utf-8") as fh:
    json.dump(result, fh, ensure_ascii=False, indent=2)
print(result["summary"])
