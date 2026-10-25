using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>flashstore db stats | reset-flash-sale --yes</summary>
public static class DbCommands
{
    public const string ResetScript = "tests/performance/reset-flash-sale.sql";

    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        var shell = new Shell(context);
        var containers = new Containers(context, shell);

        switch (args.At(1))
        {
            case "stats":
            {
                var rows = await containers.QueryAsync(
                    """
                    SELECT 'Usuarios', count(*) FROM users
                    UNION ALL SELECT 'Productos', count(*) FROM products
                    UNION ALL SELECT 'Pedidos', count(*) FROM orders
                    UNION ALL SELECT 'Pedidos pagados', count(*) FROM orders WHERE status = 'PAID'
                    UNION ALL SELECT 'Pagos', count(*) FROM payments
                    UNION ALL SELECT 'Notificaciones', count(*) FROM notifications
                    UNION ALL SELECT 'Outbox pendiente', count(*) FROM outbox_messages WHERE published_at IS NULL
                    UNION ALL SELECT 'Stock producto 1', COALESCE((SELECT stock FROM products WHERE id = 1), 0)
                    """);

                Output.Title("Datos en PostgreSQL");
                Output.PrintTable(["Indicador", "Valor"], rows.Select(r => (IReadOnlyList<string>)r));
                return 0;
            }

            case "reset-flash-sale":
            {
                if (!args.Has("yes"))
                {
                    Output.Warn("Esto BORRA todos los pedidos, pagos y notificaciones, y deja el producto 1 con 5 unidades.");
                    Output.Info("Para confirmar, repita el comando con --yes");
                    return 2;
                }

                Output.Title("Reiniciando la venta relámpago");
                await containers.RunSqlFileAsync(ResetScript);
                await containers.RedisAsync("DEL", "product:1", "products:list", "products:featured");

                var stock = await containers.QueryAsync("SELECT stock FROM products WHERE id = 1");
                Output.Ok($"Pedidos borrados y producto 1 con stock {stock.FirstOrDefault()?[0]}. Cache del catálogo limpiada.");
                return 0;
            }

            default:
                throw new UsageException("Uso: flashstore db stats | reset-flash-sale --yes");
        }
    }
}
