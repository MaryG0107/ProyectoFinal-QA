using System.Globalization;
using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>flashstore product list | show &lt;id&gt; | stock &lt;id&gt; &lt;cantidad&gt;</summary>
public static class ProductCommands
{
    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        using var api = new ApiClient(context.ApiUrl);

        switch (args.At(1))
        {
            case "list":
            {
                var products = await api.GetProductsAsync();
                if (context.Json)
                {
                    Console.WriteLine(ResultStore.Serialize(products));
                    return 0;
                }

                Output.Title("Catálogo");
                Output.PrintTable(["ID", "Producto", "Categoría", "Precio (Q)", "Stock", "Destacado"],
                    products.Select(p => (IReadOnlyList<string>)
                    [
                        p.Id.ToString(), p.Name, p.CategoryName,
                        p.Price.ToString("N2", CultureInfo.InvariantCulture), p.Stock.ToString(), p.IsFeatured ? "sí" : ""
                    ]));
                return 0;
            }

            case "show":
            {
                var id = ParseId(args.At(2));
                var product = await api.GetProductAsync(id) ?? throw new CommandFailedException($"El producto {id} no existe.");
                Console.WriteLine(ResultStore.Serialize(product));
                return 0;
            }

            case "stock":
            {
                var id = ParseId(args.At(2));
                if (!int.TryParse(args.At(3), out var stock) || stock is < 0 or > 100_000)
                {
                    throw new UsageException("La cantidad debe ser un número entre 0 y 100000.");
                }

                var token = await api.LoginAsync(context.AdminEmail, context.AdminPassword)
                    ?? throw new CommandFailedException("Credenciales de administrador inválidas (FLASHSTORE_ADMIN_EMAIL / FLASHSTORE_ADMIN_PASSWORD).");

                await api.UpdateStockAsync(token, id, stock);
                Output.Ok($"Producto {id} actualizado a {stock} unidades (cache invalidada por la API).");
                return 0;
            }

            default:
                throw new UsageException("Uso: flashstore product list | show <id> | stock <id> <cantidad>");
        }
    }

    public static int ParseId(string? raw) =>
        int.TryParse(raw, out var id) && id > 0 ? id : throw new UsageException("Indique un ID de producto válido.");
}
