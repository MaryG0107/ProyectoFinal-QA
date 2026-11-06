#!/usr/bin/env python3
"""
FlashStore - compuerta de calidad del pipeline 2.

Lee los resultados de lint y de SonarQube en reports/quality/ y escribe
reports/cli/quality.json (criterio "Calidad" del GO/NO-GO).

Política (si falla cualquiera, NO-GO):
  - ESLint (PWA): 0 errores (las advertencias no bloquean)
  - dotnet format (.NET): sin cambios de formato pendientes
  - SonarQube: Quality Gate en estado OK
Si falta un reporte, cuenta como falla.

Uso: python3 scripts/quality_gate.py [--dir reports/quality] [--out reports/cli]
"""
import argparse
import json
import os
from datetime import datetime, timezone


def load(path):
    with open(path, encoding="utf-8") as fh:
        text = fh.read().strip()
    return json.loads(text) if text else None


def eslint(base):
    path = os.path.join(base, "eslint.json")
    if not os.path.exists(path):
        return {"tool": "eslint", "passed": False, "summary": "Sin reporte de ESLint."}
    data = load(path) or []
    errors = sum(f.get("errorCount", 0) for f in data)
    warnings = sum(f.get("warningCount", 0) for f in data)
    return {"tool": "eslint", "passed": errors == 0,
            "summary": f"ESLint: {errors} errores, {warnings} advertencias en {len(data)} archivos."}


def dotnet_format(base):
    path = os.path.join(base, "dotnet-format.exit")
    if not os.path.exists(path):
        return {"tool": "dotnet-format", "passed": False, "summary": "Sin resultado de dotnet format."}
    code = int(open(path, encoding="utf-8").read().strip() or 1)
    return {"tool": "dotnet-format", "passed": code == 0,
            "summary": "dotnet format: sin cambios pendientes." if code == 0
            else f"dotnet format: hay archivos con formato pendiente (código {code})."}


def sonar(base):
    path = os.path.join(base, "sonar-qualitygate.json")
    if not os.path.exists(path):
        return {"tool": "sonarqube", "passed": False, "summary": "Sin resultado del Quality Gate de SonarQube."}
    data = load(path) or {}
    status = data.get("projectStatus", {}).get("status", "DESCONOCIDO")
    failed = [c.get("metricKey") for c in data.get("projectStatus", {}).get("conditions", [])
              if c.get("status") == "ERROR"]
    return {"tool": "sonarqube", "passed": status == "OK",
            "summary": f"SonarQube Quality Gate: {status}" + (f" (falla: {', '.join(failed)})" if failed else "") + "."}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dir", default="reports/quality")
    parser.add_argument("--out", default="reports/cli")
    args = parser.parse_args()

    checks = [eslint(args.dir), dotnet_format(args.dir), sonar(args.dir)]
    passed = all(c["passed"] for c in checks)
    failing = [c["tool"] for c in checks if not c["passed"]]
    result = {
        "name": "quality",
        "passed": passed,
        "summary": "Calidad: lint y Quality Gate en verde." if passed
        else f"Calidad: fallas en {', '.join(failing)}.",
        "executedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "details": {c["tool"]: c for c in checks},
    }
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "quality.json"), "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=2)
    for c in checks:
        print(f"| {c['tool']} | {'PASA' if c['passed'] else 'FALLA'} | {c['summary']} |")
    print(result["summary"])


if __name__ == "__main__":
    main()
