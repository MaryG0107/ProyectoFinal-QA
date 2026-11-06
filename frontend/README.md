# FlashStore – PWA (web, desktop y móvil)

Aplicación React + Vite instalable como app (Progressive Web App).

| Plataforma | Cómo se usa |
|---|---|
| Web | Navegador en `http://localhost:3000` |
| Desktop | Chrome/Edge → ícono **Instalar** en la barra de direcciones |
| Móvil | Chrome en Android → menú ⋮ → **Instalar aplicación** |

## Funciones

- Catálogo con búsqueda, filtro por categoría y estado del cache Redis (`X-Cache`).
- Login con JWT, carrito temporal (Redis), pedidos, pago simulado y notificaciones.
- **Cola offline:** sin internet el pedido se guarda en IndexedDB con su
  `Idempotency-Key` y se envía solo al volver la conexión (evento `online`,
  al abrir la app y cada 15 s). El reenvío usa la misma llave: el servidor no duplica.
- **Por seguridad, la cola nunca guarda datos de tarjeta:** el pago solo se hace en línea.
- Catálogo disponible sin conexión gracias al Service Worker (estrategia *NetworkFirst*).

## Comandos

```bash
npm install          # dependencias
npm run dev          # desarrollo en http://localhost:5173 (proxy /api → http://localhost:5000)
npm test             # pruebas unitarias de la cola offline (Vitest)
npm run lint         # ESLint
npm run build        # compilación de producción en dist/
npm audit            # análisis de dependencias
```

Si la API no está en `localhost:5000`, use `VITE_DEV_API_TARGET`:

```bash
VITE_DEV_API_TARGET=http://localhost:5000 npm run dev
```

## En Docker

`frontend/Dockerfile` compila la PWA y la sirve con Nginx sin privilegios de root.
Nginx envía `/api` y `/health` a la API (mismo origen, sin CORS) y agrega
cabeceras de seguridad (CSP, X-Frame-Options, nosniff, Referrer-Policy).

## Probar el modo sin conexión

1. Inicie sesión y agregue productos al carrito.
2. Chrome → F12 → pestaña **Network** → cambie *No throttling* por **Offline**.
3. En el carrito presione **Guardar pedido sin conexión**: aparece el aviso y el pedido en *Mis pedidos → Pendientes*.
4. Vuelva a **No throttling**: el pedido se envía solo y aparece en la tabla de pedidos.
