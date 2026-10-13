namespace FlashStore.Domain.Caching;

/// <summary>Nombres de llaves de Redis y tiempos de vida (TTL) usados en toda la solución.</summary>
public static class CacheKeys
{
    public const string ProductList = "products:list";
    public const string Featured = "products:featured";
    public const string Categories = "categories";

    public static readonly TimeSpan ProductListTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ProductTtl = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan FeaturedTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CategoriesTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CartTtl = TimeSpan.FromMinutes(30);

    public static string Product(int productId) => $"product:{productId}";

    public static string Cart(int userId) => $"cart:{userId}";

    public static string Session(string tokenId) => $"session:{tokenId}";

    /// <summary>Llaves que deben borrarse cuando cambia el precio, stock o estado de un producto.</summary>
    public static string[] ForProductChange(IEnumerable<int> productIds) =>
        productIds.Distinct().Select(Product).Append(ProductList).Append(Featured).ToArray();
}
