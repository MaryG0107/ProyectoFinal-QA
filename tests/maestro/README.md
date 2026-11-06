# Pruebas móviles con Maestro

La app móvil de FlashStore es la **PWA** (decisión D-01): el mismo código se instala en Android desde Chrome. Estos 7 flujos usan **Maestro Web**, que maneja la PWA en Chrome igual que lo haría un usuario en el teléfono.

| Flujo | Tipo | Qué valida |
|---|---|---|
| 01 · Login correcto | Crítico, smoke | El cliente inicia sesión y ve "Mis pedidos" |
| 02 · Login con contraseña incorrecta | Negativo | Mensaje "Credenciales inválidas." y sin sesión |
| 03 · Búsqueda de productos | Crítico | Buscar "audífonos" filtra el catálogo |
| 04 · Búsqueda sin resultados | Negativo | Mensaje "No encontramos productos…" |
| 05 · Agregar al carrito | Crítico | El producto aparece en el carrito |
| 06 · Compra con pago aprobado | Crítico | Checkout completo: pedido y "Pago aprobado" |
| 07 · Pago rechazado | Negativo | Tarjeta sin fondos: "Pago rechazado" |

`_login.yaml` es un subflujo que reutilizan los flujos 01, 05, 06 y 07.

## Instalar Maestro en Windows

1. Java 17 o superior: `java -version`. Si no está: `winget install EclipseAdoptium.Temurin.17.JDK` y **cerrar y abrir la terminal**.
2. Descargar `maestro.zip` de la última versión en https://github.com/mobile-dev-inc/maestro/releases y descomprimirlo en `C:\maestro`.
3. Agregar `C:\maestro\bin` al PATH (Configuración → Variables de entorno) y **cerrar y abrir la terminal**.
4. Verificar: `maestro --version`.

## Ejecutar

```powershell
# Contra la nube (IP que imprime el pipeline 1)
maestro test tests/maestro -e BASE_URL=http://IP:3000 --format junit --output reports/maestro/maestro.xml

# Contra Docker local
maestro test tests/maestro --format junit --output reports/maestro/maestro.xml
```

Las capturas (`maestro-0X-*.png`) quedan en la carpeta desde donde se ejecuta. Guárdelas en `reports/maestro/` como evidencia.
