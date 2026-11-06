using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>
/// flashstore sale simulate: N clientes distintos intentan comprar el mismo producto en el mismo instante.
/// Es el escenario crítico del proyecto ("las últimas 5 unidades"). Código 0 = sin sobreventa (GO).
/// </summary>
public static class SaleCommands
{
    public const int SeededCustomers = 300;

    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        if (args.At(1) != "simulate")
        {
            throw new UsageException("Uso: flashstore sale simulate [--buyers 300] [--product 1] [--quantity 1] [--start-user 1] [--concurrency 50]");
        }

        var buyers = args.GetInt("buyers", SeededCustomers, 1, SeededCustomers);
        var productId = args.GetInt("product", 1, 1, int.MaxValue);
        var quantity = args.GetInt("quantity", 1, 1, 10);
        var startUser = args.GetInt("start-user", 1, 1, SeededCustomers);
        var concurrency = args.GetInt("concurrency", 50, 1, 300);

        if (startUser + buyers - 1 > SeededCustomers)
        {
            throw new UsageException($"Solo existen {SeededCustomers} clientes de prueba: --start-user + --buyers - 1 no puede pasar de {SeededCustomers}.");
        }

        using var api = new ApiClient(context.ApiUrl, maxConnections: buyers + 10);
        var containers = new Containers(context, new Shell(context));

        var health = await api.GetHealthAsync() ?? throw new CommandFailedException($"La API no responde en {context.ApiUrl}.");
        Output.Title($"Venta relámpago: {buyers} compradores, producto {productId}, {quantity} unidad(es) c/u");
        Output.Info($"Estrategia de inventario del servidor: {health.InventoryStrategy}");
        if (health.InventoryStrategy != "Atomic")
        {
            Output.Warn("Modo Unsafe activo: se espera sobreventa (escenario NO-GO de la release rc1).");
        }

        var stockBefore = await ReadStockAsync(api, containers, productId);
        Output.Info($"Stock inicial: {stockBefore}");

        // 1) Inicio de sesión de cada comprador (en lotes para no saturar la API).
        var tokens = new ConcurrentBag<string>();
        var rateLimited = 0;
        using (var gate = new SemaphoreSlim(concurrency))
        {
            var logins = Enumerable.Range(startUser, buyers).Select(async n =>
            {
                await gate.WaitAsync();
                try
                {
                    var token = await api.LoginAsync($"cliente{n}@flashstore.test", context.CustomerPassword);
                    if (token is not null)
                    {
                        tokens.Add(token);
                    }
                }
                catch (ApiException ex) when (ex.Status == HttpStatusCode.TooManyRequests)
                {
                    Interlocked.Increment(ref rateLimited);
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(logins);
        }

        Output.Info($"Sesiones iniciadas: {tokens.Count}/{buyers}");
        if (rateLimited > 0)
        {
            Output.Warn($"{rateLimited} inicios de sesión rechazados (429). Suba LOGIN_RATE_LIMIT=1000 en .env y ejecute: flashstore env up");
        }

        // 2) Todos los pedidos salen a la vez: cada tarea espera la misma "señal de salida".
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new ConcurrentBag<int>();
        var latencies = new ConcurrentBag<double>();

        var orders = tokens.Select(async token =>
        {
            await start.Task;
            var watch = Stopwatch.StartNew();
            try
            {
                var response = await api.CreateOrderAsync(token, productId, quantity, Guid.NewGuid().ToString());
                statuses.Add((int)response.Status);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                statuses.Add(0);
            }
            finally
            {
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
        }).ToList();

        var total = Stopwatch.StartNew();
        start.SetResult();
        await Task.WhenAll(orders);
        total.Stop();

        await Task.Delay(500);
        var stockAfter = await ReadStockAsync(api, containers, productId);

        var tally = new SaleTally(
            StockBefore: stockBefore,
            StockAfter: stockAfter,
            Quantity: quantity,
            Buyers: buyers,
            LoggedIn: tokens.Count,
            Created: statuses.Count(s => s == 201),
            OutOfStock: statuses.Count(s => s == 409),
            RateLimited: rateLimited,
            ServerErrors: statuses.Count(s => s >= 500),
            OtherErrors: statuses.Count(s => s is not 201 and not 409 and < 500));

        var verdict = Verdicts.EvaluateSale(tally);
        var p95 = Percentile(latencies, 0.95);

        Output.Title("Resultado");
        Output.PrintTable(["Indicador", "Valor"],
        [
            ["Pedidos creados (201)", tally.Created.ToString()],
            ["Rechazados por stock (409)", tally.OutOfStock.ToString()],
            ["Errores 5xx", tally.ServerErrors.ToString()],
            ["Otros errores", tally.OtherErrors.ToString()],
            ["Stock inicial -> final", $"{stockBefore} -> {stockAfter}"],
            ["Duración total", $"{total.Elapsed.TotalSeconds:N2} s"],
            ["p95 POST /api/orders", $"{p95:N0} ms"]
        ]);

        foreach (var finding in verdict.Findings)
        {
            Output.Warn(finding);
        }

        var result = new CheckResult("sale-simulate", verdict.Passed,
            verdict.Oversold
                ? $"SOBREVENTA: {tally.Created} pedidos con stock {stockBefore}."
                : $"{tally.Created} pedidos creados con stock {stockBefore}; {tally.OutOfStock} rechazados; sin sobreventa.",
            DateTimeOffset.Now,
            new Dictionary<string, object?>
            {
                ["inventoryStrategy"] = health.InventoryStrategy,
                ["buyers"] = buyers,
                ["loggedIn"] = tally.LoggedIn,
                ["productId"] = productId,
                ["quantity"] = quantity,
                ["stockBefore"] = stockBefore,
                ["stockAfter"] = stockAfter,
                ["created"] = tally.Created,
                ["outOfStock"] = tally.OutOfStock,
                ["serverErrors"] = tally.ServerErrors,
                ["otherErrors"] = tally.OtherErrors,
                ["p95Ms"] = Math.Round(p95),
                ["oversold"] = verdict.Oversold
            });
        var path = ResultStore.Save(context.ReportsDir, result);

        if (verdict.Passed)
        {
            Output.Ok($"Sin sobreventa. Resultado guardado en {path}");
            return 0;
        }

        Output.Fail($"{result.Summary} Resultado guardado en {path}");
        return 1;
    }

    /// <summary>Lee el stock desde PostgreSQL (la verdad); si Docker no es accesible, desde la API.</summary>
    private static async Task<int> ReadStockAsync(ApiClient api, Containers containers, int productId)
    {
        try
        {
            var rows = await containers.QueryAsync($"SELECT stock FROM products WHERE id = {productId}");
            if (rows.Count > 0 && int.TryParse(rows[0][0], out var stock))
            {
                return stock;
            }
        }
        catch (CommandFailedException)
        {
            // Sin acceso a Docker (por ejemplo, otra máquina): se usa la API.
        }

        var product = await api.GetProductAsync(productId) ?? throw new CommandFailedException($"El producto {productId} no existe.");
        return product.Stock;
    }

    internal static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
