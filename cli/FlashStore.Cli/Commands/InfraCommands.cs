using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>Cache (Redis), colas (RabbitMQ), outbox y pruebas de caos.</summary>
public static class InfraCommands
{
    private static readonly string[] CatalogPatterns = ["product:*", "products:*", "categories*"];

    private static readonly string[] ChaosServices = ["redis", "rabbitmq", "worker"];

    public static async Task<int> CacheAsync(ParsedArgs args, CliContext context)
    {
        var containers = new Containers(context, new Shell(context));

        switch (args.At(1))
        {
            case "stats":
            {
                var rows = new List<IReadOnlyList<string>> { new[] { "Total de llaves", await containers.RedisAsync("DBSIZE") } };
                foreach (var (label, pattern) in new[] { ("Catálogo", "product*"), ("Sesiones", "session:*"), ("Carritos", "cart:*") })
                {
                    var keys = await ScanAsync(containers, pattern);
                    rows.Add([label, keys.Count.ToString()]);
                }

                Output.Title("Redis");
                Output.PrintTable(["Indicador", "Valor"], rows);
                return 0;
            }

            case "clear-catalog":
            {
                var keys = new List<string>();
                foreach (var pattern in CatalogPatterns)
                {
                    keys.AddRange(await ScanAsync(containers, pattern));
                }

                foreach (var batch in keys.Distinct().Chunk(100))
                {
                    await containers.RedisAsync(["DEL", .. batch]);
                }

                Output.Ok($"{keys.Count} llave(s) del catálogo borradas. Sesiones y carritos se conservan.");
                return 0;
            }

            default:
                throw new UsageException("Uso: flashstore cache stats | clear-catalog");
        }
    }

    public static async Task<int> QueueAsync(ParsedArgs args, CliContext context)
    {
        var mode = args.At(1);
        if (mode is not ("status" or "dlq"))
        {
            throw new UsageException("Uso: flashstore queue status | dlq");
        }

        var containers = new Containers(context, new Shell(context));
        var queues = await containers.QueuesAsync();
        var shown = mode == "dlq" ? queues.Where(q => q.Name.EndsWith(".dlq", StringComparison.Ordinal)).ToList() : queues;
        var dlqMessages = queues.Where(q => q.Name.EndsWith(".dlq", StringComparison.Ordinal)).Sum(q => q.Messages);

        Output.Title(mode == "dlq" ? "Colas de mensajes fallidos (DLQ)" : "Colas de RabbitMQ");
        Output.PrintTable(["Cola", "Mensajes", "Consumidores"],
            shown.Select(q => (IReadOnlyList<string>)[q.Name, q.Messages.ToString(), q.Consumers.ToString()]));

        var passed = dlqMessages == 0;
        ResultStore.Save(context.ReportsDir, new CheckResult("queues", passed,
            $"{queues.Count} colas, {dlqMessages} mensaje(s) en DLQ.", DateTimeOffset.Now,
            new Dictionary<string, object?> { ["dlqMessages"] = dlqMessages }));

        if (passed)
        {
            Output.Ok("Sin mensajes en las DLQ.");
        }
        else
        {
            Output.Warn($"{dlqMessages} mensaje(s) en DLQ: un consumidor falló 3 veces. Revise: flashstore env logs worker");
        }

        return 0;
    }

    public static async Task<int> OutboxAsync(ParsedArgs args, CliContext context)
    {
        if (args.At(1) != "status")
        {
            throw new UsageException("Uso: flashstore outbox status");
        }

        using var api = new ApiClient(context.ApiUrl);
        var token = await api.LoginAsync(context.AdminEmail, context.AdminPassword)
            ?? throw new CommandFailedException("Credenciales de administrador inválidas.");
        var outbox = await api.GetOutboxAsync(token) ?? throw new CommandFailedException("Respuesta vacía del outbox.");

        Output.Title("Outbox (eventos por publicar en RabbitMQ)");
        Output.PrintTable(["Indicador", "Valor"],
        [
            ["Pendientes", outbox.Pending.ToString()],
            ["Publicados", outbox.Published.ToString()],
            ["Pendiente más antiguo", outbox.OldestPendingAt?.ToString("u") ?? "-"]
        ]);
        return 0;
    }

    public static async Task<int> ChaosAsync(ParsedArgs args, CliContext context)
    {
        var action = args.At(1);
        var service = args.At(2);

        if (action is not ("stop" or "start") || service is null || !ChaosServices.Contains(service))
        {
            throw new UsageException($"Uso: flashstore chaos stop|start <{string.Join("|", ChaosServices)}>");
        }

        var shell = new Shell(context);
        var result = await shell.DockerAsync(["compose", action, service], capture: false);
        if (!result.Ok)
        {
            return 1;
        }

        if (action == "stop")
        {
            Output.Warn($"{service} detenido. Comportamiento esperado:");
            Output.Info(service switch
            {
                "redis" => "catálogo y login siguen (X-Cache: BYPASS); el carrito responde 503.",
                "rabbitmq" => "los pedidos se siguen creando; los eventos esperan en el outbox (flashstore outbox status).",
                _ => "los mensajes se acumulan en las colas (flashstore queue status) y se procesan al volver."
            });
        }
        else
        {
            Output.Ok($"{service} iniciado. Verifique con: flashstore health");
        }

        return 0;
    }

    private static async Task<List<string>> ScanAsync(Containers containers, string pattern)
    {
        var output = await containers.RedisAsync("--scan", "--pattern", pattern);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
