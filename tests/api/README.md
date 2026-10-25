# Colección de Insomnia – API de FlashStore

Pruebas de la API REST: **59 casos** (28 positivos `[+]` y 31 negativos `[-]`), incluidas
pruebas de **autorización** (IDOR, roles, token alterado o cerrado) y de reglas de negocio
(idempotencia, precio manipulado, doble pago, cantidades límite).

Cada solicitud tiene un *after-response script* con sus aserciones (`insomnia.test`), por lo
que el resultado es verde o rojo sin revisar a mano.

## Requisitos

- FlashStore levantado: `docker compose up -d` (API en http://localhost:5000).
- Insomnia 10 o superior.

## Importar

1. Insomnia → **Import** → **File** → `tests/api/flashstore-insomnia.json`.
2. Abrir la colección **FlashStore API - QA**.
3. Arriba a la izquierda, en el selector de ambiente, elegir **Local (Docker)**.

## Ejecutar todas las pruebas

1. Clic derecho sobre la colección (o el menú `⋯`) → **Run** (Collection Runner).
2. Dejar marcadas todas las carpetas, **en el orden 00 → 08** (las solicitudes dependen del
   token y del pedido que crean las anteriores).
3. **Run**. Resultado esperado: **todas las pruebas en verde**.

> **Límite de inicios de sesión:** la API acepta 10 por minuto por IP y la colección hace 5.
> Si se corre dos veces seguidas puede aparecer `429`: esperar un minuto.

## Datos que usa

| Variable | Valor | Para qué |
|---|---|---|
| `cliente_email` | `cliente10@flashstore.test` | Cliente principal |
| `otro_email` | `cliente11@flashstore.test` | Segundo cliente (pruebas IDOR) |
| `admin_email` | `admin@flashstore.test` | Administrador |
| Producto | **2** (stock alto) | No gasta las 5 unidades del producto 1, reservadas para k6 |

Cada corrida crea 2 pedidos (uno pagado y uno cancelado), 1 usuario `qa.<fecha>@flashstore.test`
y 1 producto **inactivo** (no aparece en el catálogo).

## Matriz de casos

| ID | Módulo | Caso | Solicitud | Código esperado |
|---|---|---|---|---|
| API-01 | Salud | Salud del sistema | `GET /health` | 200 |
| API-02 | Autenticación | [+] Login cliente | `POST /api/auth/login` | 200 |
| API-03 | Autenticación | [+] Login segundo cliente | `POST /api/auth/login` | 200 |
| API-04 | Autenticación | [+] Login administrador | `POST /api/auth/login` | 200 |
| API-05 | Autenticación | [-] Login contraseña incorrecta | `POST /api/auth/login` | 401 |
| API-06 | Autenticación | [-] Login correo inexistente | `POST /api/auth/login` | 401 |
| API-07 | Autenticación | [+] Usuario actual (me) | `GET /api/auth/me` | 200 |
| API-08 | Autenticación | [+] Registro ignora rol admin (asignación masiva) | `POST /api/auth/register` | 201 |
| API-09 | Autenticación | [-] Registro correo duplicado | `POST /api/auth/register` | 409 |
| API-10 | Autenticación | [-] Registro correo inválido | `POST /api/auth/register` | 400 |
| API-11 | Autenticación | [-] Registro contraseña débil | `POST /api/auth/register` | 400 |
| API-12 | Catálogo | [+] Listar productos | `GET /api/products` | 200 |
| API-13 | Catálogo | [+] Listar productos (cache HIT) | `GET /api/products` | 200 |
| API-14 | Catálogo | [+] Buscar "laptop" | `GET /api/products?search=laptop` | 200 |
| API-15 | Catálogo | [-] Búsqueda de más de 100 caracteres | `GET /api/products?search=aaa…(101)` | 400 |
| API-16 | Catálogo | [+] Detalle producto 2 | `GET /api/products/2` | 200 |
| API-17 | Catálogo | [-] Producto inexistente | `GET /api/products/99999` | 404 |
| API-18 | Catálogo | [+] Destacados | `GET /api/products/featured` | 200 |
| API-19 | Catálogo | [+] Categorías | `GET /api/categories` | 200 |
| API-20 | Carrito | [+] Vaciar carrito | `DELETE /api/cart` | 204 |
| API-21 | Carrito | [+] Agregar producto 2 | `POST /api/cart/items` | 200 |
| API-22 | Carrito | [-] Agregar cantidad 0 | `POST /api/cart/items` | 400 |
| API-23 | Carrito | [-] Agregar más de 10 unidades | `POST /api/cart/items` | 400 |
| API-24 | Carrito | [-] Agregar producto inexistente | `POST /api/cart/items` | 404 |
| API-25 | Carrito | [+] Ver carrito | `GET /api/cart` | 200 |
| API-26 | Carrito | [+] Quitar producto 2 | `DELETE /api/cart/items/2` | 200 |
| API-27 | Pedidos | [-] Crear pedido sin Idempotency-Key | `POST /api/orders` | 400 |
| API-28 | Pedidos | [-] Crear pedido sin productos | `POST /api/orders` | 400 |
| API-29 | Pedidos | [-] Crear pedido con 11 unidades | `POST /api/orders` | 400 |
| API-30 | Pedidos | [-] Crear pedido producto inexistente | `POST /api/orders` | 404 |
| API-31 | Pedidos | [+] Crear pedido (precio manipulado se ignora) | `POST /api/orders` | 201 |
| API-32 | Pedidos | [+] Reenviar misma Idempotency-Key (sin duplicar) | `POST /api/orders` | 200 |
| API-33 | Pedidos | [+] Listar mis pedidos | `GET /api/orders` | 200 |
| API-34 | Pedidos | [+] Ver mi pedido | `GET /api/orders/{{ _.order_id }}` | 200 |
| API-35 | Pedidos | [-] Ver pedido inexistente | `GET /api/orders/00000000-0000-0000-0000-000000000001` | 404 |
| API-36 | Pedidos | [+] Crear pedido para cancelar | `POST /api/orders` | 201 |
| API-37 | Pedidos | [+] Cancelar pedido pendiente | `POST /api/orders/{{ _.order_cancel_id }}/cancel` | 202 |
| API-38 | Pedidos | [-] Cancelar pedido ya cancelado | `POST /api/orders/{{ _.order_cancel_id }}/cancel` | 409 |
| API-39 | Pagos | [-] Pagar con tarjeta inválida (Luhn) | `POST /api/payments` | 400 |
| API-40 | Pagos | [-] Pagar sin orderId | `POST /api/payments` | 400 |
| API-41 | Pagos | [+] Pagar con tarjeta aprobada | `POST /api/payments` | 202 |
| API-42 | Pagos | [-] Pagar dos veces el mismo pedido | `POST /api/payments` | 409 |
| API-43 | Autorización y seguridad | [-] Pedidos sin token | `GET /api/orders` | 401 |
| API-44 | Autorización y seguridad | [-] Token alterado | `GET /api/orders` | 401 |
| API-45 | Autorización y seguridad | [-] Otro cliente ve mi pedido (IDOR) | `GET /api/orders/{{ _.order_id }}` | 403 |
| API-46 | Autorización y seguridad | [-] Otro cliente paga mi pedido | `POST /api/payments` | 403 |
| API-47 | Autorización y seguridad | [-] Otro cliente cancela mi pedido | `POST /api/orders/{{ _.order_id }}/cancel` | 403 |
| API-48 | Autorización y seguridad | [-] Cliente crea producto (solo admin) | `POST /api/products` | 403 |
| API-49 | Autorización y seguridad | [-] Cliente consulta outbox (solo admin) | `GET /api/admin/outbox` | 403 |
| API-50 | Autorización y seguridad | [+] Admin ve pedido de un cliente | `GET /api/orders/{{ _.order_id }}` | 200 |
| API-51 | Autorización y seguridad | [+] Admin consulta outbox | `GET /api/admin/outbox` | 200 |
| API-52 | Autorización y seguridad | [+] Respuesta con cabeceras de seguridad | `GET /api/products/2` | 200 |
| API-53 | Administración de productos | [-] Admin crea producto con precio 0 | `POST /api/products` | 400 |
| API-54 | Administración de productos | [-] Admin crea producto con categoría inexistente | `POST /api/products` | 400 |
| API-55 | Administración de productos | [+] Admin crea producto (inactivo) | `POST /api/products` | 201 |
| API-56 | Administración de productos | [-] Admin pone stock negativo | `PATCH /api/products/{{ _.nuevo_producto_id }}/stock` | 400 |
| API-57 | Administración de productos | [+] Admin actualiza stock | `PATCH /api/products/{{ _.nuevo_producto_id }}/stock` | 204 |
| API-58 | Cierre de sesión | [+] Logout segundo cliente | `POST /api/auth/logout` | 204 |
| API-59 | Cierre de sesión | [-] Token cerrado ya no sirve | `GET /api/auth/me` | 401 |
