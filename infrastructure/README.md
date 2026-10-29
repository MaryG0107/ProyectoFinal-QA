# Infraestructura de FlashStore en Google Cloud (Fase 4)

Terraform crea la infraestructura y Ansible la configura y despliega la aplicación. Todo se ejecuta desde la **VM Ubuntu local**, que hace de máquina controladora (más adelante también desde Jenkins). La razón del cambio de Azure a Google Cloud está en la decisión **D-17** de `docs/decisiones-tecnicas.md`.

```
VM Ubuntu local (controladora)                 Google Cloud · proyecto flashstore-qa
┌──────────────────────────────┐               ┌───────────────────────────────────────┐
│ terraform  ──── API GCP ─────┼──────────────▶│ VPC flashstore-qa-vpc (10.10.0.0/24)  │
│                              │               │  firewall: 22, 3000, 5000             │
│ ansible-playbook ─── SSH ────┼──────────────▶│     solo desde allowed_cidrs          │
│                              │               │  IP estática                          │
│ flashstore (CLI) ─ SSH/HTTP ─┼──────────────▶│  VM flashstore-qa-vm (e2-medium)      │
└──────────────────────────────┘               │   Ubuntu 24.04 + Docker Compose       │
                                               │   postgres · redis · rabbitmq         │
                                               │   api · worker · web (PWA)            │
                                               └───────────────────────────────────────┘
```

## Qué crea cada herramienta

| Herramienta | Archivo | Qué hace |
|---|---|---|
| Terraform | `terraform/main.tf` | VPC propia, subred, 2 reglas de firewall, IP estática, VM Ubuntu 24.04 con la llave SSH del equipo, e inventario de Ansible con la IP real |
| Terraform | `terraform/variables.tf` | Variables con validaciones (rechaza `0.0.0.0/0` y limita el disco entre 20 y 50 GB) |
| Ansible | `ansible/roles/base` | Zona horaria, paquetes base y 2 GB de swap |
| Ansible | `ansible/roles/docker` | Docker Engine + Compose desde el repositorio oficial |
| Ansible | `ansible/roles/flashstore` | Copia la versión commiteada del repo, genera `.env` (600), ejecuta `docker compose up -d --build` y espera `/health` |
| Scripts | `scripts/gcp-up.sh` / `gcp-down.sh` | Crear y desplegar todo, o destruirlo todo, con un solo comando |
| Scripts | `scripts/check-clock.sh` | Detiene la ejecución si el reloj está desfasado (Google rechaza el token) |
| Jenkins | `Jenkinsfile.infra` | Lo mismo que `gcp-up.sh`/`gcp-down.sh`, con aprobación manual (ver `jenkins/README.md`) |

## Seguridad

- Terraform usa la cuenta de servicio `terraform-flashstore` con `roles/compute.admin` y `roles/iam.serviceAccountUser`. No usa la cuenta personal ni el rol Owner.
- La llave JSON vive en `~/.gcp/terraform-flashstore.json` (permisos 600), fuera del repositorio.
- Los secretos de la app (Postgres, RabbitMQ, JWT) se generan con `scripts/gcp-secrets-init.sh` en `~/.gcp/flashstore-secrets.env` y llegan a Ansible como variables de entorno. Los pasos que los usan tienen `no_log`.
- Solo se abren 22, 3000 y 5000, y solo para las IP del equipo. PostgreSQL, Redis y RabbitMQ no tienen puertos públicos.
- `terraform.tfvars`, `*.tfstate` y `ansible/inventory/gcp.ini` están en `.gitignore`.

## Costos

- `e2-medium` + disco de 30 GB + IP estándar: alrededor de 1 USD por día encendida.
- Alerta de presupuesto `flashstore-limite` de 20 USD (avisos al 50 %, 90 % y 100 %).
- Al terminar cada sesión: `scripts/gcp-down.sh`.

## Estado remoto (D-18)

El estado de Terraform vive en el bucket `gs://flashstore-qa-tfstate-958936877144` (prefijo `flashstore/qa`, con versionado). Así lo comparten la VM controladora y Jenkins. Se crea una sola vez:

```bash
gcloud storage buckets create gs://flashstore-qa-tfstate-958936877144 \
  --project=flashstore-qa --location=us-central1 \
  --uniform-bucket-level-access --public-access-prevention
gcloud storage buckets update gs://flashstore-qa-tfstate-958936877144 --versioning
gcloud storage buckets add-iam-policy-binding gs://flashstore-qa-tfstate-958936877144 \
  --member="serviceAccount:terraform-flashstore@flashstore-qa.iam.gserviceaccount.com" \
  --role="roles/storage.objectAdmin"
terraform -chdir=infrastructure/terraform init -backend-config=backend.hcl -migrate-state
```

## Uso

Requisitos en la controladora: `gcloud`, `terraform`, `ansible`, la llave `~/.gcp/terraform-flashstore.json` y la llave SSH `~/.ssh/flashstore_gcp`.

```bash
# Una sola vez
scripts/gcp-secrets-init.sh
cp infrastructure/terraform/example.tfvars infrastructure/terraform/terraform.tfvars
nano infrastructure/terraform/terraform.tfvars      # su IP pública /32

# Revisar el plan sin crear nada
export GOOGLE_APPLICATION_CREDENTIALS=~/.gcp/terraform-flashstore.json
terraform -chdir=infrastructure/terraform init -backend-config=backend.hcl
terraform -chdir=infrastructure/terraform plan

# Crear infraestructura + desplegar (release GO)
scripts/gcp-up.sh

# Release NO-GO (sobreventa intencional)
cd ansible && set -a && source ~/.gcp/flashstore-secrets.env && set +a
ansible-playbook playbooks/site.yml -e flashstore_inventory_strategy=Unsafe

# Destruir todo
scripts/gcp-down.sh
```

Ansible despliega **solo lo que está commiteado** (`git archive`). Para desplegar una etiqueta: `-e flashstore_git_ref=v1.0.0-rc2`.

## Validaciones hechas

- `ansible-lint`: 0 fallas, perfil *production*.
- `ansible-playbook --syntax-check`: correcto.
- `shellcheck` de los scripts: sin observaciones.
- `terraform fmt -check`, `validate`, `plan`, `apply` y `destroy`: ejecutados en la controladora (evidencias de la fase 4).
- `checkov`: 23 controles aprobados, 0 fallidos, 2 riesgos aceptados y justificados en `main.tf`.
