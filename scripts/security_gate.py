#!/usr/bin/env python3
"""
FlashStore - compuerta de seguridad del pipeline 2.

Lee los reportes que dejan las herramientas en reports/security/ y escribe
reports/cli/security.json con el mismo formato que usa el CLI, para que
"flashstore report go-no-go --security" lo tome como un criterio más.

Política (se bloquea el despliegue si se incumple cualquiera):
  - gitleaks: 0 secretos encontrados
  - dotnet (paquetes NuGet): 0 vulnerabilidades High o Critical
  - npm audit (dependencias de producción de la PWA): 0 high o critical
  - trivy (imágenes Docker): 0 HIGH o CRITICAL con parche disponible
  - checkov (Terraform): 0 controles fallidos (los riesgos aceptados se
    marcan en el código con #checkov:skip y su justificación)
  - ZAP (PWA y API): 0 alertas de riesgo alto
Si falta el reporte de una herramienta, cuenta como falla: no se puede dar
GO sobre algo que no se ejecutó.

Uso: python3 scripts/security_gate.py [--dir reports/security] [--out reports/cli] [--fail]
Código de salida: 0 siempre, salvo con --fail (1 si no pasa).
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys
from datetime import datetime, timezone

BLOCKING = {"HIGH", "CRITICAL"}


def load(path: str):
    with open(path, encoding="utf-8") as fh:
        text = fh.read().strip()
    return json.loads(text) if text else None


def tool(name: str, passed: bool, blocking: int, summary: str, extra: dict | None = None) -> dict:
    result = {"tool": name, "passed": passed, "blocking": blocking, "summary": summary}
    if extra:
        result.update(extra)
    return result


def missing(name: str, pattern: str) -> dict:
    return tool(name, False, 1, f"Sin reporte ({pattern}): la herramienta no se ejecutó o falló antes de terminar.")


def check_gitleaks(base: str) -> dict:
    path = os.path.join(base, "gitleaks.json")
    if not os.path.exists(path):
        return missing("gitleaks", "gitleaks.json")
    data = load(path) or []
    rules = sorted({f.get("RuleID", "?") for f in data})
    files = sorted({f.get("File", "?") for f in data})[:10]
    return tool("gitleaks", len(data) == 0, len(data),
                "Sin secretos en el repositorio." if not data else f"{len(data)} posibles secretos ({', '.join(rules)}).",
                {"files": files})


def check_dotnet(base: str) -> dict:
    path = os.path.join(base, "dotnet-vulnerable.json")
    if not os.path.exists(path):
        return missing("dotnet", "dotnet-vulnerable.json")
    data = load(path) or {}
    found: dict[str, str] = {}
    for project in data.get("projects", []):
        for fw in project.get("frameworks", []):
            for pkg in fw.get("topLevelPackages", []) + fw.get("transitivePackages", []):
                for vuln in pkg.get("vulnerabilities", []):
                    sev = str(vuln.get("severity", "")).upper()
                    key = f"{pkg.get('id')} {pkg.get('resolvedVersion')}"
                    if sev in BLOCKING or key not in found:
                        found[key] = sev
    blocking = sum(1 for s in found.values() if s in BLOCKING)
    return tool("dotnet", blocking == 0, blocking,
                f"Paquetes NuGet con vulnerabilidades: {len(found)} (altas/críticas: {blocking}).",
                {"packages": found})


def check_npm(base: str) -> dict:
    path = os.path.join(base, "npm-audit.json")
    if not os.path.exists(path):
        return missing("npm", "npm-audit.json")
    data = load(path) or {}
    counts = data.get("metadata", {}).get("vulnerabilities", {})
    blocking = int(counts.get("high", 0)) + int(counts.get("critical", 0))
    return tool("npm", blocking == 0, blocking,
                f"npm audit (producción): {counts.get('total', 0)} en total, altas/críticas: {blocking}.",
                {"counts": counts})


def check_trivy(base: str) -> dict:
    files = sorted(glob.glob(os.path.join(base, "trivy-*.json")))
    if not files:
        return missing("trivy", "trivy-*.json")
    per_image = {}
    blocking = 0
    for path in files:
        data = load(path) or {}
        sev = {"CRITICAL": 0, "HIGH": 0}
        for res in data.get("Results", []) or []:
            for vuln in res.get("Vulnerabilities", []) or []:
                s = str(vuln.get("Severity", "")).upper()
                if s in sev:
                    sev[s] += 1
        image = data.get("ArtifactName") or os.path.basename(path)
        per_image[image] = sev
        blocking += sev["CRITICAL"] + sev["HIGH"]
    return tool("trivy", blocking == 0, blocking,
                f"Imágenes analizadas: {len(per_image)}; HIGH/CRITICAL con parche: {blocking}.",
                {"images": per_image})


def check_checkov(base: str) -> dict:
    path = os.path.join(base, "checkov", "results_json.json")
    if not os.path.exists(path):
        return missing("checkov", "checkov/results_json.json")
    data = load(path) or {}
    reports = data if isinstance(data, list) else [data]
    failed = passed = skipped = 0
    failed_ids = []
    for rep in reports:
        summary = rep.get("summary", {})
        failed += int(summary.get("failed", 0))
        passed += int(summary.get("passed", 0))
        skipped += int(summary.get("skipped", 0))
        for chk in rep.get("results", {}).get("failed_checks", []):
            failed_ids.append(f"{chk.get('check_id')} {chk.get('resource')}")
    return tool("checkov", failed == 0, failed,
                f"Terraform: {passed} controles aprobados, {failed} fallidos, {skipped} riesgos aceptados.",
                {"failed": failed_ids})


def check_zap(base: str) -> dict:
    files = sorted(glob.glob(os.path.join(base, "zap", "zap-*.json")))
    if not files:
        return missing("zap", "zap/zap-*.json")
    high = 0
    medium = 0
    alerts = []
    for path in files:
        data = load(path) or {}
        for site in data.get("site", []) or []:
            for alert in site.get("alerts", []) or []:
                risk = int(alert.get("riskcode", 0))
                if risk >= 3:
                    high += 1
                elif risk == 2:
                    medium += 1
                if risk >= 2:
                    alerts.append(f"[{alert.get('riskdesc', risk)}] {alert.get('name')} ({os.path.basename(path)})")
    return tool("zap", high == 0, high,
                f"ZAP: {high} alertas de riesgo alto, {medium} de riesgo medio (no bloquean).",
                {"alerts": alerts[:30]})


def main() -> int:
    parser = argparse.ArgumentParser(description="Compuerta de seguridad de FlashStore")
    parser.add_argument("--dir", default="reports/security")
    parser.add_argument("--out", default="reports/cli")
    parser.add_argument("--fail", action="store_true", help="Salir con 1 si no pasa")
    args = parser.parse_args()

    checks = [check_gitleaks(args.dir), check_dotnet(args.dir), check_npm(args.dir),
              check_trivy(args.dir), check_checkov(args.dir), check_zap(args.dir)]
    passed = all(c["passed"] for c in checks)
    failing = [c["tool"] for c in checks if not c["passed"]]

    summary = ("Seguridad: sin hallazgos bloqueantes en las 6 herramientas."
               if passed else f"Seguridad: hallazgos bloqueantes en {', '.join(failing)}.")
    result = {
        "name": "security",
        "passed": passed,
        "summary": summary,
        "executedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "details": {c["tool"]: c for c in checks},
    }

    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "security.json"), "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=2)

    lines = ["# Resumen de seguridad - FlashStore", "",
             f"- **Fecha (UTC):** {result['executedAt']}",
             f"- **Resultado:** {'✅ PASA' if passed else '❌ FALLA'}", "",
             "| Herramienta | Resultado | Bloqueantes | Detalle |", "|---|---|---|---|"]
    for c in checks:
        lines.append(f"| {c['tool']} | {'PASA' if c['passed'] else 'FALLA'} | {c['blocking']} | {c['summary'].replace('|', '/')} |")
    with open(os.path.join(args.dir, "resumen.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    print("\n".join(lines))
    return 1 if (args.fail and not passed) else 0


if __name__ == "__main__":
    sys.exit(main())
