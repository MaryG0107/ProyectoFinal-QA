using System.Globalization;
using Dapper;
using FlashStore.Domain.Caching;
using FlashStore.Domain.Orders;
using FlashStore.Infrastructure.Caching;
using Npgsql;
using StackExchange.Redis;

namespace FlashStore.Api.Cart;

public sealed record CartLine(int ProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal, int AvailableStock);

public sealed record CartView(int UserId, IReadOnlyList<CartLine> Items, decimal Subtotal, int? ExpiresInSeconds);

public enum CartAddStatus
{
    Added,
    Invalid,
    ProductNotFound,
    OutOfStock
}

public sealed record CartAddResult(CartAddStatus Status, string? Error = null, CartView? Cart = null);

/// <summary>Se lanza cuando Redis no está disponible: el carrito temporal vive en Redis.</summary>
public sealed class CartUnavailableException : Exception
{
    public CartUnavailableException(Exception inner)
        : base("El carrito no está disponible temporalmente.", inner)
    {
    }
}

/// <summary>
/// Carrito temporal en Redis: hash cart:{userId} → { productId: cantidad } con TTL de 30 minutos
/// que se renueva en cada modificación. Los precios se leen siempre de PostgreSQL.
/// </summary>
public sealed class CartService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly NpgsqlDataSource _dataSource;

    public CartService(IConnectionMultiplexer redis, NpgsqlDataSource dataSource)
    {
        _redis = redis;
        _dataSource = dataSource;
    }

    public async Task<Dictionary<int, int>> GetRawAsync(int userId)
    {
        var entries = await RedisCallAsync(() => _redis.GetDatabase().HashGetAllAsync(CacheKeys.Cart(userId)));
        var result = new Dictionary<int, int>();

        foreach (var entry in entries)
        {
            if (int.TryParse(entry.Name.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var productId)
                && int.TryParse(entry.Value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                && quantity > 0)
            {
                result[productId] = quantity;
            }
        }

        return result;
    }

    public async Task<CartView> GetViewAsync(int userId)
    {
        var raw = await GetRawAsync(userId);

        if (raw.Count == 0)
        {
            return new CartView(userId, [], 0m, null);
        }

        await using var connection = await _dataSource.OpenConnectionAsync();

        var products = (await connection.QueryAsync<CartProductRow>(
                "SELECT id, name, price, stock FROM products WHERE id = ANY(@Ids) AND is_active",
                new { Ids = raw.Keys.ToArray() }))
            .ToDictionary(p => p.Id);

        // Si un producto fue desactivado mientras estaba en el carrito, se retira del carrito.
        var missing = raw.Keys.Where(id => !products.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            await RedisCallAsync(() => _redis.GetDatabase().HashDeleteAsync(
                CacheKeys.Cart(userId),
                missing.Select(id => (RedisValue)id.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }

        var lines = raw
            .Where(kv => products.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var product = products[kv.Key];
                var line = new PricedLine(product.Id, kv.Value, product.Price);
                return new CartLine(product.Id, product.Name, product.Price, kv.Value, line.LineTotal, product.Stock);
            })
            .ToList();

        var ttl = await RedisCallAsync(() => _redis.GetDatabase().KeyTimeToLiveAsync(CacheKeys.Cart(userId)));

        return new CartView(
            userId,
            lines,
            lines.Sum(l => l.LineTotal),
            ttl is null ? null : (int)Math.Ceiling(ttl.Value.TotalSeconds));
    }

    public async Task<CartAddResult> AddItemAsync(int userId, int productId, int quantity)
    {
        if (productId <= 0)
        {
            return new CartAddResult(CartAddStatus.Invalid, "Identificador de producto inválido.");
        }

        if (quantity is <= 0 or > OrderRules.MaxQuantityPerItem)
        {
            return new CartAddResult(CartAddStatus.Invalid,
                $"La cantidad debe estar entre 1 y {OrderRules.MaxQuantityPerItem}.");
        }

        CartProductRow? product;
        await using (var connection = await _dataSource.OpenConnectionAsync())
        {
            product = await connection.QuerySingleOrDefaultAsync<CartProductRow>(
                "SELECT id, name, price, stock FROM products WHERE id = @Id AND is_active",
                new { Id = productId });
        }

        if (product is null)
        {
            return new CartAddResult(CartAddStatus.ProductNotFound, "Producto no encontrado.");
        }

        var key = CacheKeys.Cart(userId);
        var field = productId.ToString(CultureInfo.InvariantCulture);
        var current = await RedisCallAsync(() => _redis.GetDatabase().HashGetAsync(key, field));
        var currentQuantity = current.HasValue && int.TryParse(current.ToString(), out var parsed) ? parsed : 0;
        var newQuantity = currentQuantity + quantity;

        if (newQuantity > OrderRules.MaxQuantityPerItem)
        {
            return new CartAddResult(CartAddStatus.Invalid,
                $"La cantidad máxima por producto es {OrderRules.MaxQuantityPerItem}.");
        }

        // Validación preliminar: la garantía real contra la sobreventa está al crear el pedido.
        if (newQuantity > product.Stock)
        {
            return new CartAddResult(CartAddStatus.OutOfStock, $"Stock insuficiente. Disponible: {product.Stock}.");
        }

        await RedisCallAsync(async () =>
        {
            var db = _redis.GetDatabase();
            await db.HashSetAsync(key, field, newQuantity);
            await db.KeyExpireAsync(key, CacheKeys.CartTtl);
            return true;
        });

        return new CartAddResult(CartAddStatus.Added, Cart: await GetViewAsync(userId));
    }

    public async Task<CartView> RemoveItemAsync(int userId, int productId)
    {
        await RedisCallAsync(() => _redis.GetDatabase().HashDeleteAsync(
            CacheKeys.Cart(userId),
            productId.ToString(CultureInfo.InvariantCulture)));

        return await GetViewAsync(userId);
    }

    public Task ClearAsync(int userId) =>
        RedisCallAsync(() => _redis.GetDatabase().KeyDeleteAsync(CacheKeys.Cart(userId)));

    private static async Task<T> RedisCallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (RedisCache.IsRedisFailure(ex))
        {
            throw new CartUnavailableException(ex);
        }
    }

    private sealed class CartProductRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public int Stock { get; set; }
    }
}
