# CLI `flashstore`

Herramienta de línea de comandos para operar y probar FlashStore sin abrir ninguna interfaz gráfica:
levantar el ambiente, consultar datos, provocar fallas controladas, simular la venta relámpago y decidir GO/NO-GO.

- **.NET 8, sin paquetes NuGet de terceros**: solo la librería estándar (menos dependencias, menos vulnerabilidades).
- Habla con la **API** por HTTP y con **PostgreSQL, Redis y RabbitMQ** con `docker exec` (no abre puertos extra).
- Funciona con Docker **local** o con Docker **dentro de la VM** por SSH (`--ssh mary@localhost:2222`).
- Cada verificación guarda su resultado en `reports/cli/*.json`; `report go-no-go` los reúne y devuelve **0 = GO / 1 = NO-GO**,
  para que Jenkins detenga el despliegue automáticamente.

## Instalación

Desde la raíz del repositorio (Windows PowerShell o Linux):

```powershell
dotnet pack cli/FlashStore.Cli -c Release
dotnet tool install --global --add-source cli/FlashStore.Cli/nupkg flashstore
flashstore version
```

> Después de instalar, **cierre y vuelva a abrir la terminal** para que reconozca el comando.
> Para actualizar tras cambios: `dotnet tool update --global --add-source cli/FlashStore.Cli/nupkg flashstore`
> (antes vuelva a ejecutar `dotnet pack`).

Sin instalar, también funciona con: `dotnet run --project cli/FlashStore.Cli -- health`

## Configuración

| Opción | Variable de entorno | Por defecto | Para qué |
|---|---|---|---|
| `--api` | `FLASHSTORE_API` | `http://localhost:5000` | URL de la API |
| `--ssh` | `FLASHSTORE_SSH` | (vacío = Docker local) | Ejecutar los comandos de Docker en la VM |
| `--dir` | `FLASHSTORE_DIR` | local: carpeta con `docker-compose.yml` · remoto: `~/ProyectoFinal-QA` | Carpeta de Docker Compose |
| `--web` | `FLASHSTORE_WEB` | `http://localhost:3000` | PWA (para `test e2e`) |
| — | `FLASHSTORE_ADMIN_EMAIL` / `FLASHSTORE_ADMIN_PASSWORD` | usuario admin de prueba | Comandos de administrador |

Ejemplo para la laptop con FlashStore en la VM (PowerShell):

```powershell
$env:FLASHSTORE_SSH = "mary@localhost:2222"
flashstore env status
```

> Con `--ssh`, cada comando de Docker abre una conexión SSH. Para no escribir la contraseña cada vez,
> configure una llave: `ssh-keygen -t ed25519` y luego
> `type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh -p 2222 mary@localhost "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"`.

## Comandos

| Comando | Qué hace |
|---|---|
| `env up [--build]` · `env down` · `env status` | Levanta, apaga (conserva datos) o muestra los contenedores |
| `env logs <servicio> [--tail N]` · `env restart <servicio>` | Logs o reinicio de postgres, redis, rabbitmq, api, worker o web |
| `health` | Salud de la API y dependencias (código 1 si algo está caído) |
| `db stats` | Usuarios, pedidos, pagos, outbox y stock del producto 1 |
| `db reset-flash-sale --yes` | Borra los pedidos y deja el producto 1 con 5 unidades |
| `product list` · `product show <id>` · `product stock <id> <cantidad>` | Catálogo y ajuste de inventario |
| `order list [--limit N] [--status PAID]` · `order show <uuid>` | Pedidos de todos los clientes |
| `cache stats` · `cache clear-catalog` | Redis: llaves por tipo; limpiar solo el catálogo |
| `queue status` · `queue dlq` | RabbitMQ: mensajes y consumidores; colas de fallidos |
| `outbox status` | Eventos pendientes de publicar |
| `chaos stop\|start <redis\|rabbitmq\|worker>` | Simula la caída de un servicio |
| `sale simulate [--buyers 300] [--product 1] [--quantity 1]` | Venta relámpago concurrente (escenario crítico) |
| `test unit` · `test api` · `test e2e` · `test load [--reset]` | dotnet test, Insomnia (inso), Cypress y k6 |
| `report go-no-go [--strict]` | Decisión GO/NO-GO con `reports/go-no-go.md` |

**Códigos de salida:** `0` correcto o GO · `1` falla o NO-GO · `2` uso incorrecto.

## Escenario crítico: las últimas 5 unidades

```powershell
# 1. Permitir 300 inicios de sesión desde una IP: en .env, LOGIN_RATE_LIMIT=1000, y aplicar:
flashstore env up
# 2. Producto 1 con 5 unidades y sin pedidos anteriores
flashstore db reset-flash-sale --yes
# 3. 300 compradores al mismo tiempo
flashstore sale simulate --buyers 300
```

| Modo del servidor | Resultado esperado | Código |
|---|---|---|
| `INVENTORY_STRATEGY=Atomic` | 5 pedidos creados, 295 rechazados (409), stock final 0 | 0 (GO) |
| `INVENTORY_STRATEGY=Unsafe` | Más de 5 pedidos: **sobreventa** detectada | 1 (NO-GO) |

## Criterios GO/NO-GO

| Criterio | Fuente | Obligatorio |
|---|---|---|
| Salud (API, PostgreSQL, Redis, RabbitMQ) | `health` (se mide en el momento) | Sí |
| Inventario en modo `Atomic` | `health` | Sí |
| Venta relámpago sin sobreventa | `sale simulate` | Sí |
| Pruebas unitarias | `test unit` | Sí |
| Pruebas de API | `test api` | Sí |
| Pruebas E2E | `test e2e` | Con `--strict` |
| Rendimiento (umbrales k6) | `test load` | Con `--strict` |
| 0 mensajes en DLQ | `queue status` | Con `--strict` |

GO solo si todos los obligatorios **pasan** y ningún criterio ejecutado **falla**.
