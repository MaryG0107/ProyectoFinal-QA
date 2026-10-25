using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>flashstore order list [--limit N] [--status S] | show &lt;id&gt;</summary>
public static class OrderCommands
{
    private static readonly string[] Statuses = ["PENDING_PAYMENT", "PAID", "PAYMENT_REJECTED", "CANCELLED"];

    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        switch (args.At(1))
        {
            case "list":
            {
                var limit = args.GetInt("limit", 20, 1, 500);
                var status = args.Get("status")?.ToUpperInvariant();
                if (status is not null && !Statuses.Contains(status))
                {
                    throw new UsageException($"--status debe ser uno de: {string.Join(", ", Statuses)}");
                }

                // status y limit ya están validados contra una lista fija: no hay texto libre en el SQL.
                var where = status is null ? string.Empty : $"WHERE o.status = '{status}'";
                var containers = new Containers(context, new Shell(context));
                var rows = await containers.QueryAsync(
                    $"""
                    SELECT left(o.id::text, 8), u.email, o.status, o.total, to_char(o.created_at, 'YYYY-MM-DD HH24:MI')
                    FROM orders o JOIN users u ON u.id = o.user_id
                    {where}
                    ORDER BY o.created_at DESC
                    LIMIT {limit}
                    """);

                Output.Title(status is null ? "Últimos pedidos" : $"Pedidos {status}");
                Output.PrintTable(["Pedido", "Cliente", "Estado", "Total (Q)", "Creado"], rows.Select(r => (IReadOnlyList<string>)r));
                Output.Info($"{rows.Count} pedido(s).");
                return 0;
            }

            case "show":
            {
                if (!Guid.TryParse(args.At(2), out var id))
                {
                    throw new UsageException("Indique el ID completo del pedido (UUID).");
                }

                using var api = new ApiClient(context.ApiUrl);
                var token = await api.LoginAsync(context.AdminEmail, context.AdminPassword)
                    ?? throw new CommandFailedException("Credenciales de administrador inválidas.");

                var response = await api.GetOrderAsync(token, id);
                if (!response.Ok)
                {
                    throw new CommandFailedException($"HTTP {(int)response.Status}: {response.Body}");
                }

                Console.WriteLine(response.Body);
                return 0;
            }

            default:
                throw new UsageException("Uso: flashstore order list [--limit N] [--status PAID] | show <uuid>");
        }
    }
}
