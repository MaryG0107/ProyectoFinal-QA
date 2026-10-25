# FlashStore

E-commerce de alta concurrencia para campañas de venta flash.
Proyecto final del curso **048 – Aseguramiento de la Calidad del Software** (UMG).

> **Estado:** Fase 1 (backend, datos, Redis, RabbitMQ, Docker) ✅ · Fase 2 (PWA web/desktop/móvil con cola offline) ✅

---

## Arquitectura

```text
             ┌──────────────── Clientes (PWA) ────────────────┐
             │   Web (navegador) · Desktop (instalada) · Móvil │
             └───────────────────────┬────────────────────────┘
                                     │ HTTPS / JSON + JWT
                         ┌───────────▼───────────┐
                         │   FlashStore.Api      │  .NET 8 – API REST
                         │  (Swagger, JWT, CORS) │
                         └──┬────────┬────────┬──┘
              cache/sesión  │        │ SQL    │ outbox → publica
                    ┌───────▼──┐ ┌───▼──────┐ │   ┌────────────────────┐
                    │  Redis   │ │PostgreSQL│◄┘   │      RabbitMQ      │
                    └───────▲──┘ └───▲──────┘     │ flashstore.events  │
                            │        │            │ flashstore.dlx     │
                         ┌──┴────────┴──┐ consume └─────────┬──────────┘
                         │FlashStore.   │◄──────────────────┘
                         │Worker        │  pedidos · inventario · notificaciones
                         └──────────────┘
```

| Capa | Proyecto | Responsabilidad |
|---|---|---|
| Dominio | `backend/src/FlashStore.Domain` | Reglas de negocio puras: validación de pedidos, cálculo de totales, pago simulado, eventos, llaves de cache |
| Infraestructura | `backend/src/FlashStore.Infrastructure` | PostgreSQL (Npgsql + Dapper), Redis, RabbitMQ y patrón Outbox |
| API | `backend/src/FlashStore.Api` | Endpoints REST, JWT, roles, rate limiting, cabeceras de seguridad |
| Worker | `backend/src/FlashStore.Worker` | Consumidores de RabbitMQ con idempotencia, reintentos y DLQ |
| PWA | `frontend/` | React + Vite + Service Worker; cola offline en IndexedDB; Nginx con proxy `/api` |
| Pruebas unitarias | `tests/unit/FlashStore.UnitTests` | xUnit sobre las reglas de negocio |

Las decisiones de diseño están justificadas en [`docs/decisiones-tecnicas.md`](docs/decisiones-tecnicas.md).

---

## Requisitos

| Herramienta | Versión |
|---|---|
| Docker Engine + Docker Compose | 24 o superior |
| .NET SDK (solo para desarrollar y correr pruebas fuera de Docker) | 8.0 |
| k6 (pruebas de rendimiento) | 0.50 o superior |

---

## Cómo levantar el ambiente

```bash
# 1. Clonar el repositorio
git clone https://github.com/MaryG0107/ProyectoFinal-QA.git
cd ProyectoFinal-QA

# 2. Crear el archivo de variables (NUNCA se sube al repositorio)
cp .env.example .env
#    Edite .env y cambie todas las claves. Para la llave JWT:
openssl rand -base64 48

# 3. Construir y levantar
docker compose up -d --build

# 4. Verificar
docker compose ps
curl http://localhost:5000/health
```

Respuesta esperada de `/health`:

```json
{ "status": "healthy", "postgres": "up", "redis": "up", "rabbitmq": "up", "inventoryStrategy": "Atomic" }
```

| Servicio | URL / puerto en el host |
|---|---|
| **PWA (web, desktop y móvil)** | **http://localhost:3000** |
| API + Swagger | http://localhost:5000/swagger |
| RabbitMQ (consola web) | http://localhost:15672 (usuario y clave de `.env`) |
| PostgreSQL | `localhost:5433` |
| Redis | `localhost:6380` |

> Los puertos evitan los que ya usan Jenkins (8080), SonarQube (9000) y Dependency-Track (8081/8082) en la VM.
> Si la VM está en VirtualBox con NAT, agregue el reenvío de los puertos 3000, 5000, 5433 y 15672 (IP anfitrión `127.0.0.1`).

---

## Usuarios de prueba

| Correo | Contraseña | Rol |
|---|---|---|
| `admin@flashstore.test` | `Admin1234!` | admin |
| `cliente1@flashstore.test` … `cliente300@flashstore.test` | `Test1234!` | customer |

El **producto 1** ("Consola FlashBox X – Edición Limitada") inicia con **5 unidades**.

---

## Endpoints principales

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| POST | `/api/auth/register` | — | Registro de cliente |
| POST | `/api/auth/login` | — | Login (máx. 10 intentos/min por IP) → JWT |
| POST | `/api/auth/logout` | JWT | Cierra la sesión (borra `session:{jti}` en Redis) |
| GET | `/api/products` | — | Catálogo (cache Redis, header `X-Cache`), filtros `search` y `categoryId` |
| GET | `/api/products/{id}` | — | Detalle de producto (cache Redis) |
| GET | `/api/products/featured` | — | Productos destacados (cache Redis) |
| POST / PUT / PATCH | `/api/products…` | admin | Crear, editar y cambiar stock (invalida el cache) |
| POST / GET | `/api/cart` | JWT | Obtener el carrito temporal (Redis, TTL 30 min) |
| POST | `/api/cart/items` | JWT | Agregar producto al carrito |
| POST | `/api/orders` | JWT | Crear pedido. **Header `Idempotency-Key` obligatorio** |
| GET | `/api/orders` · `/api/orders/{id}` | JWT | Mis pedidos (un cliente no puede ver pedidos ajenos → 403) |
| POST | `/api/orders/{id}/cancel` | JWT | Cancelar pedido pendiente (el Worker devuelve el stock) |
| POST | `/api/payments` | JWT | Pago simulado (`4111111111111111` aprueba, `4000000000000002` rechaza) |
| GET | `/api/notifications` | JWT | Notificaciones generadas por el Worker |
| GET | `/api/admin/outbox` | admin | Eventos pendientes de publicar |
| GET | `/health` | — | Estado de PostgreSQL, Redis y RabbitMQ |

La documentación completa está en Swagger: http://localhost:5000/swagger

---

## Flujo de compra (ejemplo con curl)

```bash
API=http://localhost:5000

# Login
TOKEN=$(curl -s -X POST $API/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"cliente1@flashstore.test","password":"Test1234!"}' | jq -r .token)

# Crear pedido (la Idempotency-Key la genera el cliente)
KEY=$(cat /proc/sys/kernel/random/uuid)
ORDER=$(curl -s -X POST $API/api/orders -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"items":[{"productId":2,"quantity":1}]}' | jq -r .id)

# Pagar
curl -s -X POST $API/api/payments -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"$ORDER\",\"cardNumber\":\"4111111111111111\"}"

# Unos segundos después el Worker cambia el estado a PAID
curl -s $API/api/orders/$ORDER -H "Authorization: Bearer $TOKEN" | jq .status
curl -s $API/api/notifications -H "Authorization: Bearer $TOKEN" | jq
```

---

## Escenarios de demostración (defensa práctica)

### 1. Cache Redis: MISS → HIT → invalidación → TTL

```bash
curl -si http://localhost:5000/api/products/2 | grep X-Cache     # MISS
curl -si http://localhost:5000/api/products/2 | grep X-Cache     # HIT
docker exec flashstore-redis redis-cli TTL product:2             # segundos restantes (máx. 60)
# Un administrador cambia el precio (PUT /api/products/2) → la llave se borra → el siguiente GET es MISS
```

### 2. Redis caído

```bash
docker stop flashstore-redis
curl -si http://localhost:5000/api/products | grep X-Cache        # BYPASS: el catálogo sigue funcionando
curl -s  http://localhost:5000/health                             # "degraded"
docker start flashstore-redis
```

### 3. RabbitMQ caído: los eventos no se pierden (Outbox)

```bash
docker stop flashstore-rabbitmq
#   → crear pedidos: la API responde 201 normalmente
#   → GET /api/admin/outbox (como admin) muestra "pending" > 0
docker start flashstore-rabbitmq
#   → en pocos segundos "pending" vuelve a 0 y el Worker procesa los eventos
```

### 4. Consumidor detenido: los mensajes esperan en la cola

```bash
docker stop flashstore-worker
#   → pagar un pedido: queda en PENDING_PAYMENT
#   → en http://localhost:15672 la cola flashstore.orders acumula mensajes
docker start flashstore-worker
#   → el pedido pasa a PAID
```

### 5. Reintentos y DLQ

```bash
# En .env: SIMULATE_CONSUMER_FAILURE=notifications
docker compose up -d worker
#   → crear un pedido: el Worker reintenta 3 veces (1 s, 2 s, 4 s) y envía el mensaje
#     a flashstore.notifications.dlq (visible en la consola de RabbitMQ)
# Volver a dejar SIMULATE_CONSUMER_FAILURE vacío y: docker compose up -d worker
```

### 6. Las últimas 5 unidades – escenario NO-GO vs GO

```bash
# Preparación (en .env: LOGIN_RATE_LIMIT=1000)
docker exec -i flashstore-postgres psql -U flashstore -d flashstore < tests/performance/reset-flash-sale.sql
docker exec flashstore-redis redis-cli DEL product:1 products:list products:featured

# NO-GO: en .env INVENTORY_STRATEGY=Unsafe  → docker compose up -d api
k6 run -e BASE_URL=http://localhost:5000 tests/performance/flash-sale.js
#   → orders_created muy por encima de 5: el umbral falla (sobreventa detectada)

# GO: en .env INVENTORY_STRATEGY=Atomic → docker compose up -d api, repetir la preparación
k6 run -e BASE_URL=http://localhost:5000 tests/performance/flash-sale.js
#   → orders_created = 5, stock final 0: todos los umbrales pasan
```

**Resultado de la simulación previa** (mismo SQL de la API, 300 compradores simultáneos contra PostgreSQL 16):

| Estrategia | Pedidos creados | Rechazados sin stock | Unidades vendidas | Stock inicial |
|---|---|---|---|---|
| `Unsafe` (defecto intencional) | **236** | 64 | **236** | 5 |
| `Atomic` (corrección) | **5** | 295 | **5** | 5 |
| Doble envío: 10 solicitudes con la misma `Idempotency-Key` | **1** | — | — | — |

---

## Pruebas unitarias

```bash
dotnet test tests/unit/FlashStore.UnitTests
```

46 casos sobre validación de pedidos, cálculo de totales y descuentos, pago simulado (algoritmo de Luhn), eventos y llaves de cache.

---

## Estructura del repositorio

```text
flashstore/
├── FlashStore.sln
├── docker-compose.yml          ← ambiente local / QA
├── .env.example                ← variables (copiar a .env)
├── backend/src/                ← Domain, Infrastructure, Api, Worker
├── db/init/                    ← esquema y datos de prueba de PostgreSQL
├── frontend/                   ← PWA React (web, desktop y móvil)
├── mobile/                     ← instalación de la PWA en Android y flujos Maestro
├── scripts/                    ← sync-env.ps1 (envía el .env de Windows a la VM)
├── cli/                        ← CLI "flashstore" (Fase 3)
├── infrastructure/terraform/   ← Azure + Terraform (Fase 4)
├── ansible/                    ← playbooks (Fase 4)
├── tests/                      ← unit, integration, api, cypress, maestro, performance, security
├── docs/                       ← plan de pruebas, arquitectura, matriz de riesgos, decisiones
└── reports/                    ← evidencias: sonar, fortify, dependency-check, zap, performance
```

## CLI `flashstore`

Todo el proyecto se puede operar desde la terminal. Instalación y referencia completa en [`cli/README.md`](cli/README.md).

```powershell
dotnet pack cli/FlashStore.Cli -c Release
dotnet tool install --global --add-source cli/FlashStore.Cli/nupkg flashstore

flashstore health                         # salud de la API y dependencias
flashstore db reset-flash-sale --yes      # producto 1 con 5 unidades
flashstore sale simulate --buyers 300     # escenario crítico: 0 sobreventa = código 0
flashstore report go-no-go                # decisión GO/NO-GO (código 0 = GO)
```

## Hoja de ruta

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Backend, base de datos, Redis, RabbitMQ, Docker, pruebas unitarias, k6 del escenario crítico | ✅ |
| 2 | PWA (web, desktop y móvil) con cola offline | ✅ |
| 3 | CLI `flashstore` | ✅ |
| 4 | Terraform (Azure) + Ansible | ⏳ |
| 5 | Jenkins: pipeline de infraestructura y pipeline de calidad/seguridad con GO/NO-GO | ⏳ |
| 6 | Insomnia, Cypress, Maestro, ZAP, k6 (load/stress/spike), Lighthouse | ⏳ |
| 7 | Cupones + regresión | ⏳ |
| 8 | Plan de pruebas, matriz de riesgos, informe final y presentación | ⏳ |
