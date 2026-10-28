# Pruebas E2E con Cypress – FlashStore

Pruebas de extremo a extremo de la PWA. Corren contra la aplicación levantada con Docker.

## Requisitos

- FlashStore corriendo: `docker compose up -d` en la raíz del repositorio (PWA en http://localhost:3000).
- Node.js 22 o superior.

## Instalación (una sola vez)

```powershell
cd tests/cypress
npm ci
```

## Ejecución

| Comando | Qué hace |
|---|---|
| `npm run cy:open` | Abre la interfaz de Cypress para ver las pruebas paso a paso |
| `npm run cy:run` | Corre todas las pruebas en consola (lo que usará Jenkins) |
| `npm run cy:smoke` | Corre solo el smoke: catálogo, login y compra |

Para otro ambiente: `$env:CYPRESS_BASE_URL="http://otra-url"; npm run cy:run`

## Casos

| Archivo | Casos |
|---|---|
| `01-catalogo.cy.js` | Catálogo carga, búsqueda, sin resultados, pide login al agregar, cabeceras CSP |
| `02-login.cy.js` | Contraseña incorrecta, login correcto y cierre de sesión |
| `03-compra.cy.js` | Carrito → pedido → pago aprobado (estado *Pagado* vía Worker) y pago rechazado |
| `04-offline.cy.js` | Pedido sin conexión queda en cola y se envía una sola vez al reconectar |

## Notas

- Las compras usan el **producto 2** (stock alto) para no gastar las 5 unidades del producto 1,
  reservadas para la prueba de concurrencia con k6.
- Usuarios: `cliente1` (login), `cliente2` (compra), `cliente3` (offline), clave `Test1234!`.
- La API permite 10 inicios de sesión por minuto por IP; por eso se usa `cy.session`.
  Si aparece *Demasiados intentos*, espere un minuto y vuelva a correr.
- Las capturas de los fallos quedan en `tests/cypress/screenshots/` (ignoradas por Git).
