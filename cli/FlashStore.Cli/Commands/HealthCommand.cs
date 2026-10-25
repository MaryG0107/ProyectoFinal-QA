using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>flashstore health: estado de la API y sus dependencias. Código 0 solo si todo está "up".</summary>
public static class HealthCommand
{
    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        using var api = new ApiClient(context.ApiUrl);
        var health = await api.GetHealthAsync();

        if (health is null)
        {
            var failed = new CheckResult("health", false, $"La API no responde en {context.ApiUrl}.", DateTimeOffset.Now);
            ResultStore.Save(context.ReportsDir, failed);
            Output.Fail(failed.Summary);
            return 1;
        }

        var healthy = health.Status == "healthy";
        var result = new CheckResult("health", healthy,
            $"API {health.Status}: postgres={health.Postgres}, redis={health.Redis}, rabbitmq={health.Rabbitmq}, inventario={health.InventoryStrategy}",
            DateTimeOffset.Now,
            new Dictionary<string, object?> { ["inventoryStrategy"] = health.InventoryStrategy, ["status"] = health.Status });
        ResultStore.Save(context.ReportsDir, result);

        if (context.Json)
        {
            Console.WriteLine(ResultStore.Serialize(health));
            return healthy ? 0 : 1;
        }

        Output.Title($"Salud de FlashStore ({context.ApiUrl})");
        Output.PrintTable(["Componente", "Estado"],
        [
            ["API", health.Status],
            ["PostgreSQL", health.Postgres],
            ["Redis", health.Redis],
            ["RabbitMQ", health.Rabbitmq],
            ["Estrategia de inventario", health.InventoryStrategy]
        ]);

        if (health.InventoryStrategy != "Atomic")
        {
            Output.Warn("Inventario en modo Unsafe: hay sobreventa intencional (escenario NO-GO).");
        }

        if (healthy)
        {
            Output.Ok("Todo en línea.");
        }
        else
        {
            Output.Fail("Hay componentes caídos.");
        }

        return healthy ? 0 : 1;
    }
}
