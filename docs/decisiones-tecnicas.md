# Decisiones técnicas – FlashStore

Registro de las decisiones de arquitectura, con su justificación. Sirve como insumo para la sección 3 (Arquitectura) del plan de pruebas y para la defensa técnica.

| # | Decisión | Alternativas consideradas | Justificación |
|---|---|---|---|
| D-01 | Clientes **web, desktop y móvil** con una sola **PWA** (Progressive Web App) | .NET MAUI Blazor Hybrid; app nativa | Un solo código para las tres plataformas, instalable en Windows y Android, cola offline nativa (Service Worker + IndexedDB) y `npm audit` con dependencias reales. |
| D-02 | Backend en **.NET 8** (API REST + Worker) | Node.js / Express | Experiencia previa del equipo en .NET con Redis y PostgreSQL. |
| D-03 | Ruta de infraestructura **A: Azure + Terraform + Ansible** | Ruta B: AWS + Pulumi | El equipo ya cuenta con suscripción de Azure, Azure CLI y Terraform. |
| D-04 | Pruebas de rendimiento con **k6** | JMeter | Los escenarios son código versionado, los umbrales (`thresholds`) se declaran en el script y k6 devuelve código de error si se incumplen, por lo que el pipeline bloquea el despliegue automáticamente. |
| D-05 | Descuento de inventario **atómico** (`UPDATE ... WHERE stock >= cantidad`) | `SELECT ... FOR UPDATE`; bloqueo distribuido en Redis | Una sola sentencia bloquea la fila y valida el stock; no requiere coordinar procesos externos. |
| D-06 | **Idempotency-Key** obligatoria en `POST /api/orders` | Detección por contenido del pedido | Permite reenviar solicitudes desde la cola offline sin duplicar pedidos. La llave se registra dentro de la transacción para serializar envíos simultáneos. |
| D-07 | **Patrón Outbox** para publicar eventos | Publicar directo en RabbitMQ desde la API | El pedido y su evento se guardan en la misma transacción. Si RabbitMQ está caído, los eventos esperan en `outbox_messages` y se publican al recuperarse: no se pierde información. |
| D-08 | Consumidores **idempotentes** (`processed_messages`) + **3 reintentos** con espera exponencial + **DLQ** | Reintentos infinitos | RabbitMQ garantiza entrega "al menos una vez", así que un mensaje puede llegar duplicado. Los mensajes que fallan 3 veces van a la DLQ para análisis. |
| D-09 | Si **Redis** cae: catálogo y login siguen funcionando (cache en modo BYPASS, sesión en modo degradado); el carrito responde 503 | Detener la aplicación | Prioriza la disponibilidad de la venta. Riesgo residual aceptado: un token cerrado con logout podría reutilizarse mientras Redis esté caído. |
| D-10 | Los **precios siempre se leen de PostgreSQL** | Aceptar el precio enviado por el cliente | Mitiga la manipulación de precios (riesgo de seguridad del proyecto). |
| D-11 | `Inventory:Strategy=Unsafe` disponible solo por configuración | — | Permite reproducir la sobreventa para el escenario **NO-GO** (release `v1.0.0-rc1`) sin mantener otra rama de código. |
| D-12 | Cupones en una **migración posterior** | Incluirlos desde el inicio | El proyecto exige agregar cupones al final y ejecutar pruebas de regresión. |
