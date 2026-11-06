# FlashStore – Cliente móvil

El cliente móvil es la **misma PWA** de `frontend/`, instalada en Android desde Chrome.
No existe un código aparte: una sola aplicación sirve para web, desktop y móvil.

## Instalar en un celular o emulador Android

1. Levantar FlashStore (`docker compose up -d --build`) y abrir el puerto 3000.
2. En el emulador, redirigir el puerto para que `localhost` funcione
   (el Service Worker solo se activa en `https` o en `localhost`):

   ```bash
   adb reverse tcp:3000 tcp:3000
   ```

3. Abrir Chrome en el emulador → `http://localhost:3000`.
4. Menú ⋮ → **Instalar aplicación** (o **Agregar a la pantalla principal**).
5. La app aparece con el ícono de FlashStore y abre en pantalla completa.

## Pruebas con Maestro

Los flujos de Maestro (Fase 6) estarán en `tests/maestro/`. La PWA incluye
atributos `data-testid` y etiquetas accesibles (`aria-label`) para que Maestro
y Cypress encuentren los elementos de forma estable.

| Flujo planificado | Qué valida |
|---|---|
| Login correcto e incorrecto | Autenticación y mensaje de error |
| Búsqueda en el catálogo | Filtro por texto |
| Agregar al carrito | Carrito temporal en Redis |
| Compra en línea + pago | Pedido, pago simulado y cambio asíncrono a "Pagado" |
| Compra sin conexión (modo avión) | Cola offline: pedido guardado y enviado al reconectar |
| Pago rechazado | Tarjeta 4000 0000 0000 0002 |
| Producto agotado | Mensaje de stock insuficiente |
