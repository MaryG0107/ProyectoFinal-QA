namespace FlashStore.Cli.Commands;

public static class HelpCommand
{
    public const string Text = """
        flashstore - CLI de FlashStore (Proyecto Final QA, UMG)

        USO
          flashstore <comando> [subcomando] [opciones]

        AMBIENTE
          env up [--build]               Levanta los 6 contenedores (docker compose up -d)
          env down                       Apaga el ambiente (conserva los datos)
          env status                     Estado de los contenedores
          env logs <servicio> [--tail N] Logs de postgres|redis|rabbitmq|api|worker|web
          env restart <servicio>         Reinicia un servicio
          health                         Salud de la API y dependencias (código 1 si algo está caído)

        DATOS
          db stats                       Usuarios, pedidos, pagos, outbox y stock del producto 1
          db reset-flash-sale --yes      Borra pedidos y deja el producto 1 con 5 unidades
          product list                   Catálogo con precio y stock
          product show <id>              Detalle de un producto (JSON)
          product stock <id> <cantidad>  Cambia el stock (como administrador)
          order list [--limit N] [--status PAID|PENDING_PAYMENT|PAYMENT_REJECTED|CANCELLED]
          order show <uuid>              Detalle de un pedido (como administrador)

        INFRAESTRUCTURA
          cache stats                    Llaves en Redis (catálogo, sesiones, carritos)
          cache clear-catalog            Borra la cache del catálogo (no toca sesiones ni carritos)
          queue status                   Colas de RabbitMQ con mensajes y consumidores
          queue dlq                      Solo las colas de mensajes fallidos
          outbox status                  Eventos pendientes de publicar
          chaos stop|start <redis|rabbitmq|worker>   Simula la caída de un servicio

        CALIDAD
          sale simulate [--buyers 300] [--product 1] [--quantity 1]   Venta relámpago concurrente
          test unit                      dotnet test
          test api                       Colección de Insomnia con inso
          test e2e [--web URL]           Cypress
          test load [--reset]            k6 (flash-sale.js)
          report go-no-go [--strict] [--security]   Decisión GO/NO-GO (código 0 = GO, 1 = NO-GO)

        OPCIONES GLOBALES
          --api <url>        API de FlashStore          (FLASHSTORE_API, por defecto http://localhost:5000)
          --ssh <u@host:p>   Ejecutar Docker en la VM   (FLASHSTORE_SSH, ej. mary@localhost:2222)
          --dir <ruta>       Carpeta de Docker Compose  (FLASHSTORE_DIR, remoto: ~/ProyectoFinal-QA)
          --json             Salida en JSON (health, product list)
          FLASHSTORE_SSH_OPTS  Opciones extra de ssh (ej. "-i ~/.ssh/flashstore_gcp -o BatchMode=yes")
          --help             Esta ayuda

        CÓDIGOS DE SALIDA
          0 = correcto / GO     1 = falla / NO-GO     2 = uso incorrecto

        EJEMPLOS
          flashstore health
          flashstore --ssh mary@localhost:2222 env status
          flashstore --ssh mary@localhost:2222 db reset-flash-sale --yes
          flashstore sale simulate --buyers 300
          flashstore report go-no-go
        """;
}
