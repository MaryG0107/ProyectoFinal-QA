# Plan de pruebas – FlashStore

**Proyecto:** FlashStore, e-commerce de alta concurrencia para ventas flash (Proyecto 3)
**Curso:** 048 – Aseguramiento de la Calidad del Software · Universidad Mariano Gálvez, Campus Cobán
**Equipo:** María Guadalupe Sagui (MaryG0107) · Fernando (kcfernando-dev)
**Repositorio:** https://github.com/MaryG0107/ProyectoFinal-QA
**Versión del documento:** 1.0 · noviembre de 2026

Documentos relacionados: `docs/matriz-riesgos.xlsx`, `docs/casos-prueba.xlsx`, `docs/registro-defectos.md`, `docs/decisiones-tecnicas.md`, `docs/informe-final.md`.

---

## 1. Introducción y objetivo

FlashStore vende productos en campañas de tiempo limitado. El riesgo central del negocio es la **sobreventa**: cuando cientos de clientes compran al mismo tiempo las últimas unidades, el sistema puede aceptar más pedidos que el stock disponible.

El objetivo de este plan es demostrar, con evidencia reproducible, que:

1. Con **5 unidades** y **300 compradores simultáneos** se crean **exactamente 5 pedidos** y nunca hay sobreventa.
2. Un pedido reenviado (doble clic, reintento de la cola offline) **no se duplica**.
3. Los eventos de pedidos, pagos e inventario **no se pierden** aunque RabbitMQ o el consumidor fallen.
4. La aplicación no tiene vulnerabilidades altas o críticas, ni en el código, ni en sus dependencias, ni en las imágenes, ni en el ambiente desplegado.
5. La versión solo se promueve si el pipeline da **GO**; cualquier criterio crítico incumplido da **NO-GO** y bloquea la versión.

## 2. Alcance y fuera de alcance

**Dentro del alcance**

| Módulo | Qué se prueba |
|---|---|
| Autenticación | Registro, login con JWT, límite de intentos, logout que invalida la sesión en Redis |
| Catálogo y categorías | Listado, búsqueda, filtro por categoría, detalle, destacados, cache Redis (MISS/HIT/BYPASS) |
| Inventario | Descuento atómico, escenario de las últimas 5 unidades, devolución de stock al cancelar |
| Carrito | Carrito temporal en Redis (TTL 30 min), agregar y vaciar |
| Pedidos | Creación con `Idempotency-Key`, consulta propia, control de acceso entre clientes, cancelación |
| Pago simulado | Tarjeta aprobada, tarjeta rechazada, evento `PaymentConfirmed` / `PaymentRejected` |
| Cupones y promociones | `POST /api/coupons/validate` y pruebas de regresión después de implementarlos |
| Mensajería | Outbox, consumidores idempotentes, reintentos con espera exponencial y DLQ |
| PWA (web, escritorio y móvil) | Flujos de compra, cola offline, cabeceras de seguridad, accesibilidad y usabilidad |
| Infraestructura | Terraform, Ansible, pipelines de Jenkins, compuertas de calidad y seguridad |

**Fuera del alcance**

- Pasarela de pago real: el pago es simulado con números de tarjeta de prueba.
- Envío real de correos o notificaciones push: las notificaciones se guardan en la base de datos.
- Alta disponibilidad multi-región y pruebas de recuperación ante desastres de Google Cloud.
- Aplicación móvil nativa: se usa la misma PWA instalada en el teléfono (decisión D-01).

## 3. Arquitectura de la solución

```text
Clientes (PWA: web · escritorio · móvil)
        │  HTTPS/JSON + JWT  (Nginx sirve la PWA y hace proxy de /api, D-14)
        ▼
FlashStore.Api (.NET 8) ──SQL──► PostgreSQL 16 (pedidos, inventario, outbox, idempotencia)
        │  cache/sesión ───────► Redis 7 (catálogo, destacados, carrito, sesiones)
        │  outbox ─────────────► RabbitMQ (flashstore.events, flashstore.dlx)
        ▼
FlashStore.Worker (.NET 8): consumidores de pedidos, inventario y notificaciones
```

| Ambiente | Dónde | Cómo se crea |
|---|---|---|
| Desarrollo | Laptop y VM Ubuntu (VirtualBox) | `docker compose up -d --build` |
| **QA** | Google Cloud, VM e2-medium en us-central1 | **Pipeline 1** (`Jenkinsfile.infra`): Terraform + Ansible |
| CI | Jenkins 2.568 en la VM Ubuntu | Dos jobs: `flashstore-infra` y `flashstore-seguridad` |

Las 21 decisiones de diseño, con sus alternativas y justificación, están en `docs/decisiones-tecnicas.md`.

## 4. Riesgos y prioridades

La matriz completa, con probabilidad, impacto, exposición y mitigación, está en `docs/matriz-riesgos.xlsx`. Los riesgos de exposición **alta** se probaron primero:

| ID | Riesgo | Prioridad | Prueba que lo cubre |
|---|---|---|---|
| R-01 | Sobreventa en la venta relámpago | Crítica | Venta de 300 compradores (CLI + k6), pipeline 2 |
| R-02 | Pedidos duplicados por reintentos | Alta | 10 envíos con la misma `Idempotency-Key`, Insomnia, Cypress offline |
| R-03 | Manipulación de precios desde el cliente | Alta | Insomnia (precio enviado ignorado), D-10 |
| R-04 | Broken Access Control (ver o cancelar pedidos ajenos) | Alta | Insomnia, grupo 06 |
| R-05 | Pérdida de eventos si RabbitMQ cae | Alta | Escenario Outbox, `flashstore infra chaos` |
| R-06 | Dependencias o imágenes con vulnerabilidades | Alta | npm audit, NuGet, Dependency-Check, trivy |
| R-07 | Secretos expuestos | Alta | gitleaks, revisión de logs de Jenkins (DEF-01) |
| R-08 | Lentitud en la apertura de la venta | Media | k6: carga, estrés y pico |

## 5. Estrategia de pruebas

Pirámide de pruebas con automatización en cada nivel y una **regla de calidad única**: una prueba crítica fallida, una vulnerabilidad alta o crítica sin tratar o un umbral de rendimiento incumplido dan **NO-GO**.

| Nivel / tipo | Herramienta | Automatizada en |
|---|---|---|
| Unitarias | xUnit (.NET) | Pipeline 2 (`flashstore test unit`) |
| Integración | Insomnia, CLI `flashstore` (`infra cache/queue/outbox/chaos`) | Pipeline 2 y manual |
| API | Insomnia (`inso`): 59 solicitudes, 93 aserciones | Pipeline 2 (`flashstore test api`) |
| Funcionales / smoke / E2E | Cypress (10 pruebas) | Pipeline 2 |
| Móvil | Maestro (flujos sobre la PWA) | Ejecución en la PC de pruebas |
| Concurrencia | CLI `sale simulate` (300 compradores) y k6 `flash-sale.js` | Pipeline 2 |
| Rendimiento | k6 `carga.js`: carga, estrés y pico | Pipeline 2 |
| Calidad de código | ESLint, dotnet format, SonarQube + Quality Gate | Pipeline 2 |
| SAST | Semgrep CE (en lugar de Fortify, D-20), SonarQube | Pipeline 2 |
| SCA | npm audit, `dotnet list package --vulnerable`, OWASP Dependency-Check | Pipeline 2 |
| Contenedores e IaC | trivy (imágenes), checkov (Terraform) | Pipeline 2 |
| Secretos | gitleaks | Pipeline 2 |
| DAST | OWASP ZAP (baseline sobre la PWA y escaneo de la API con OpenAPI) | Pipeline 2 |
| Usabilidad y accesibilidad | 5 participantes con tareas + SUS; Lighthouse | Manual |

**Flujo de trabajo:** cada cambio sigue Issue → rama → Pull Request → aprobación de la otra cuenta → merge. La rama `main` está protegida.

## 6. Ambientes y datos de prueba

| Dato | Valor |
|---|---|
| Producto crítico | Producto 1, "Consola FlashBox X – Edición Limitada", **5 unidades** |
| Clientes de prueba | `cliente1` a `cliente300@flashstore.test`, contraseña `Test1234!` |
| Administrador | `admin@flashstore.test` |
| Tarjetas | `4111111111111111` aprueba · `4000000000000002` rechaza |
| Reinicio de la venta | `flashstore db reset-flash-sale --yes` (SQL `tests/performance/reset-flash-sale.sql` + limpieza del cache) |
| Límite de login | 10 por minuto por IP en producción; 1000 en QA para la venta de 300 compradores |
| Secretos | Generados con `scripts/gcp-secrets-init.sh`, guardados como credenciales de Jenkins; nunca en el repositorio |

El ambiente QA se crea desde cero en cada sesión con el pipeline 1 y se destruye al terminar (`ACCION=destruir`), así que **siempre arranca limpio**.

## 7. Pruebas unitarias

71 pruebas xUnit sobre las reglas de negocio y el CLI: validación de pedidos, cálculo de totales, simulador de pagos, eventos y llaves de cache, decisión GO/NO-GO, argumentos del CLI y construcción de comandos SSH.

**Criterio de salida:** 0 fallas. **Resultado:** 71/71 aprobadas (VM Linux y pipeline 2).

## 8. Pruebas de integración

| Integración | Cómo se prueba | Evidencia |
|---|---|---|
| API ↔ PostgreSQL | Insomnia: pedidos, stock e idempotencia persistidos; `flashstore db stats` | Pipeline 2, etapa "Pruebas funcionales" |
| API ↔ Redis | Header `X-Cache` (MISS → HIT → invalidación); Redis detenido → BYPASS y salud `degraded` | `flashstore infra cache`, `infra chaos` |
| API ↔ RabbitMQ | RabbitMQ detenido → los pedidos se crean y quedan en el outbox; al volver se publican | `flashstore infra outbox`, `infra chaos` |
| Consumidor ↔ RabbitMQ | Worker detenido → mensajes en cola; falla simulada → 3 reintentos y DLQ | `flashstore queue dlq` (0 mensajes en DLQ en el pipeline) |

## 9. Pruebas de API

Colección de Insomnia en `tests/api/flashstore-insomnia.json`, organizada en 9 grupos: salud, autenticación, catálogo, carrito, pedidos, pagos, autorización y seguridad, administración y cierre de sesión. Incluye casos **positivos, negativos, de autenticación (401), de autorización (403) y de errores de validación (400)**.

**Criterio de salida:** 93/93 aserciones. **Resultado:** 93/93 en el Collection Runner y con `inso` en el pipeline, contra la nube.

## 10. Pruebas funcionales y smoke

Cypress (`tests/cypress`), 10 pruebas contra la PWA desplegada:

| Archivo | Pruebas |
|---|---|
| 01-catálogo | Muestra productos y la consola; búsqueda; búsqueda sin resultados (negativa); agregar sin sesión pide login; cabeceras CSP |
| 02-login | Contraseña incorrecta (negativa); login correcto |
| 03-compra | Pago aprobado → pedido Pagado; tarjeta rechazada (negativa) |
| 04-offline | Pedido guardado sin conexión y enviado al reconectar |

**Smoke:** `npm run cy:smoke` (archivos 01 a 03). **Criterio de salida:** 0 fallas.

## 11. Pruebas de regresión

Después de agregar **cupones** (decisión D-12) se vuelve a ejecutar el **pipeline 2 completo** sobre la versión nueva: unitarias, Insomnia, Cypress, venta relámpago, rendimiento y seguridad. La regresión pasa si todos los criterios que pasaban antes siguen pasando y los casos nuevos de cupones también pasan.

## 12. Pruebas móviles

La app móvil es la PWA instalada en Android (decisión D-01). Los flujos de Maestro están en `tests/maestro/`: login, búsqueda, agregar al carrito, compra con pago aprobado, tarjeta rechazada (negativo) y login con contraseña incorrecta (negativo).

**Criterio de salida:** todos los flujos en verde.

## 13. Pruebas de seguridad

| Riesgo del enunciado | Cómo se prueba |
|---|---|
| Manipulación de precios | El precio enviado por el cliente se ignora; siempre se lee de PostgreSQL (Insomnia) |
| Broken Access Control | Cliente A no puede ver ni cancelar pedidos de B (403); endpoints de admin con token de cliente (403) |
| Injection y XSS | Entradas maliciosas en búsqueda y registro (Insomnia); ZAP; Semgrep; CSP estricta |
| Dependencias vulnerables | npm audit, NuGet, OWASP Dependency-Check, trivy |
| Sesiones y checkout | Logout invalida el token en Redis; `Idempotency-Key` obligatoria; límite de login (429) |
| Validación de entradas | Cantidades negativas o cero, productos inexistentes, cuerpos vacíos (400) |

**Herramientas en el pipeline:** gitleaks, checkov, Semgrep, SonarQube, npm audit, NuGet, Dependency-Check, trivy y OWASP ZAP.
**Criterio de salida:** 0 secretos, 0 hallazgos ERROR de Semgrep, Quality Gate en verde, 0 vulnerabilidades HIGH/CRITICAL con parche disponible, 0 alertas altas de ZAP. Los riesgos aceptados se documentan con justificación (checkov CKV_GCP_40 y CKV_GCP_38).

## 14. Pruebas de rendimiento

Umbrales declarados **antes** de ejecutar (están escritos en los scripts de k6):

| Escenario | Carga | p95 | Errores |
|---|---|---|---|
| Venta relámpago (`flash-sale.js` / `sale simulate`) | 300 compradores, 5 unidades | p95 de `POST /api/orders` < 800 ms | 0 errores 5xx y `orders_created ≤ 5` |
| Carga (`carga.js`) | 50 usuarios constantes, 2 min | < 800 ms | < 1 % |
| Estrés (`carga.js`) | Sube por etapas hasta 200 usuarios | < 1500 ms | < 5 % |
| Pico (`carga.js`) | De 10 a 300 usuarios en 10 s | < 2000 ms | < 5 % |

Los resultados y el análisis del cuello de botella están en el informe final.

## 15. Pruebas de usabilidad y accesibilidad

**Usabilidad:** 5 participantes realizan 5 tareas en la PWA desde el teléfono o la laptop, sin ayuda:

1. Buscar "audífonos" y abrir el producto.
2. Iniciar sesión con la cuenta de prueba.
3. Agregar un producto al carrito y pagar con la tarjeta aprobada.
4. Ver el estado del pedido en "Mis pedidos".
5. Intentar pagar con la tarjeta rechazada y entender el mensaje de error.

Se mide si completa la tarea, el tiempo y los errores, y al final cada participante responde el cuestionario **SUS** (System Usability Scale). **Meta:** tasa de éxito ≥ 80 % y SUS ≥ 68.

**Accesibilidad:** Lighthouse (Chrome DevTools) sobre la PWA en modo móvil. **Meta:** puntaje de accesibilidad ≥ 90 y ningún problema de contraste ni de etiquetas faltantes.

## 16. Criterios de entrada y salida

**Entrada:** ambiente QA creado por el pipeline 1 con `health` en verde; datos de prueba cargados; código en una rama con Pull Request.

**Salida (GO).** Todos estos criterios deben cumplirse. Los evalúa `flashstore report go-no-go --strict --security`:

| Criterio | Condición |
|---|---|
| Salud | API, PostgreSQL, Redis y RabbitMQ en línea |
| Inventario | Estrategia `Atomic` |
| Venta relámpago | 300 compradores, 5 unidades: 0 sobreventa |
| Unitarias / API / E2E | 0 fallas |
| Rendimiento | Umbrales de k6 cumplidos |
| Mensajería | 0 mensajes en las DLQ |
| Calidad | ESLint sin errores, dotnet format sin cambios, Quality Gate de SonarQube en verde |
| Seguridad | 0 hallazgos bloqueantes en las 8 herramientas |

## 17. Gestión de defectos

Los defectos se registran como **GitHub Issues** con la etiqueta `bug` y en `docs/registro-defectos.md`, con: pasos para reproducir, resultado esperado, resultado obtenido, severidad, prioridad, evidencia, causa, corrección y verificación.

| Severidad | Definición |
|---|---|
| Crítica | Pérdida de dinero o de datos, sobreventa, secreto expuesto |
| Alta | Vulnerabilidad explotable o función principal bloqueada |
| Media | Función afectada con alternativa disponible |
| Baja | Problema estético o de documentación |

## 18. Métricas y resultados

Se reportan en el informe final: pruebas ejecutadas y aprobadas por tipo, defectos por severidad y estado, hallazgos por herramienta antes y después de corregir, p95 y tasa de error por escenario, puntaje SUS y de Lighthouse, y la evolución del pipeline 2 (NO-GO → GO).

## 19. Riesgos residuales

Ver la sección del mismo nombre en el informe final y la hoja "Residuales" de la matriz de riesgos.

## 20. Decisión GO / NO-GO

La decisión la toma el **pipeline 2** con evidencia, no una persona: si `report go-no-go` devuelve código 1, el build falla y la versión no se promueve.

| Release | Configuración | Resultado esperado |
|---|---|---|
| `v1.0.0-rc1` | `INVENTORY_STRATEGY=Unsafe` | **NO-GO** por sobreventa |
| `v1.0.0-rc2` | `INVENTORY_STRATEGY=Atomic` | **GO** |
