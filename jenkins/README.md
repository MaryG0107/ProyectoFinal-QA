# Jenkins: pipelines de FlashStore (Fase 5)

Jenkins corre en la VM Ubuntu (`http://localhost:8080` desde la laptop) y maneja dos pipelines:

| Job | Archivo | Qué hace |
|---|---|---|
| `flashstore-infra` | `Jenkinsfile.infra` | **Pipeline 1.** Valida Terraform, muestra el plan, pide aprobación, crea o destruye la infraestructura en Google Cloud, despliega con Ansible y verifica con el CLI |
| `flashstore-seguridad` | `Jenkinsfile` | **Pipeline 2.** Análisis de secretos, Terraform, dependencias e imágenes; ZAP contra la app desplegada; pruebas unitarias, de API y la venta relámpago; compuerta de seguridad y decisión **GO/NO-GO** |

```
Pipeline 1 (flashstore-infra)                     Pipeline 2 (flashstore-seguridad)
Preparar → validar → plan → [Aprobación] →        Preparar (IP desde el estado remoto)
apply → Ansible → CLI health / db stats           → gitleaks ∥ checkov ∥ dotnet ∥ npm audit
          │                                       → test unit → trivy → ZAP → inso + venta 300
          └── estado en gs://flashstore-qa-tfstate-958936877144 ──► → compuerta → report go-no-go
                                                                     (NO-GO = build en rojo)
```

## Requisitos en la VM

- Jenkins 2.4xx o más reciente, con el usuario `jenkins` en el grupo `docker`.
- Terraform, Ansible, gcloud, .NET 8 SDK, Node/npm e `inso`. Ver el Paso de preparación más abajo.
- Plugins: Pipeline, Git, Credentials Binding, Timestamper, Workspace Cleanup, **Pipeline: Stage View** y **HTML Publisher**.

## Credenciales (Manage Jenkins → Credentials → System → Global)

| ID | Tipo | Contenido |
|---|---|---|
| `gcp-terraform-sa` | Secret file | `~/.gcp/terraform-flashstore.json` |
| `flashstore-gcp-ssh` | SSH Username with private key | Usuario `mary`, llave `~/.ssh/flashstore_gcp` |
| `flashstore-secrets` | Secret file | `~/.gcp/flashstore-secrets.env` |

Ninguna llave ni contraseña se escribe en el repositorio ni aparece en el log: Jenkins las enmascara.

## Crear los jobs

New Item → nombre → **Pipeline** → OK:

- **Pipeline → Definition:** *Pipeline script from SCM*
- **SCM:** Git. **Repository URL:** `https://github.com/MaryG0107/ProyectoFinal-QA.git`
- **Branch Specifier:** `*/develop`
- **Script Path:** `Jenkinsfile.infra` (job `flashstore-infra`) o `Jenkinsfile` (job `flashstore-seguridad`)

La primera ejecución de cada job solo carga los parámetros. A partir de la segunda aparece **Build with Parameters**.

## Escenarios GO / NO-GO

| Escenario | Pipeline 1 | Pipeline 2 | Resultado esperado |
|---|---|---|---|
| Release GO (`v1.0.0-rc2`) | `ACCION=crear`, `INVENTORY_STRATEGY=Atomic` | por defecto | Venta: 5 pedidos con 5 unidades, compuerta en verde → **GO** (build verde) |
| Release NO-GO (`v1.0.0-rc1`) | `ACCION=crear`, `INVENTORY_STRATEGY=Unsafe` | por defecto | Sobreventa y estrategia Unsafe → **NO-GO** (build rojo, no se promueve) |

Al terminar: pipeline 1 con `ACCION=destruir`.

## Problemas conocidos

- **`Invalid JWT ... iat and exp`**: el reloj de la VM está desfasado (por ejemplo, después de suspender la laptop). `scripts/check-clock.sh` lo detecta al inicio de cada pipeline. Para corregirlo: `sudo timedatectl set-ntp false && sudo timedatectl set-ntp true`.
- **Timeout de SSH o de la PWA**: la IP pública de la casa cambió. Los pipelines la detectan solos con `ifconfig.me`; para pruebas manuales hay que actualizar `terraform.tfvars`.
