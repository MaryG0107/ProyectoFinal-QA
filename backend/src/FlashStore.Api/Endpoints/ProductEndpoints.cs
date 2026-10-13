using Dapper;
using FlashStore.Api.Common;
using FlashStore.Domain.Caching;
using FlashStore.Infrastructure.Caching;
using Npgsql;

namespace FlashStore.Api.Endpoints;

public sealed class ProductDto
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool IsFeatured { get; set; }

    public bool IsActive { get; set; }
}

public sealed class CategoryDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public static class ProductEndpoints
{
    private const int MaxSearchLength = 100;

    private const string SelectProducts =
        """
        SELECT p.id, p.category_id, c.name AS category_name, p.name, p.description,
               p.price, p.stock, p.is_featured, p.is_active
        FROM products p
        JOIN categories c ON c.id = p.category_id
        """;

    public sealed record ProductRequest(
        int CategoryId,
        string? Name,
        string? Description,
        decimal Price,
        int Stock,
        bool IsFeatured,
        bool IsActive = true);

    public sealed record StockRequest(int Stock);

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", GetCategoriesAsync).WithTags("Catálogo").AllowAnonymous();

        var group = app.MapGroup("/api/products").WithTags("Catálogo");

        group.MapGet("", GetProductsAsync).AllowAnonymous();
        group.MapGet("/featured", GetFeaturedAsync).AllowAnonymous();
        group.MapGet("/{id:int}", GetProductAsync).AllowAnonymous();

        group.MapPost("", CreateProductAsync).RequireAuthorization("admin");
        group.MapPut("/{id:int}", UpdateProductAsync).RequireAuthorization("admin");
        group.MapPatch("/{id:int}/stock", UpdateStockAsync).RequireAuthorization("admin");

        return app;
    }

    private static async Task<IResult> GetCategoriesAsync(HttpContext http, NpgsqlDataSource dataSource, RedisCache cache)
    {
        var (categories, status) = await cache.GetOrSetAsync(CacheKeys.Categories, CacheKeys.CategoriesTtl, async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<CategoryDto>("SELECT id, name FROM categories ORDER BY name")).ToList();
        });

        SetCacheHeader(http, status);
        return Results.Ok(categories);
    }

    /// <summary>
    /// Lista de productos con cache-aside: se cachea la lista completa (TTL 60 s) y los
    /// filtros de categoría y búsqueda se aplican en memoria. Header X-Cache: HIT, MISS o BYPASS.
    /// </summary>
    private static async Task<IResult> GetProductsAsync(
        HttpContext http,
        NpgsqlDataSource dataSource,
        RedisCache cache,
        string? search,
        int? categoryId)
    {
        if (search is { Length: > MaxSearchLength })
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, $"La búsqueda admite máximo {MaxSearchLength} caracteres.");
        }

        var (products, status) = await cache.GetOrSetAsync(CacheKeys.ProductList, CacheKeys.ProductListTtl, async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<ProductDto>($"{SelectProducts} WHERE p.is_active ORDER BY p.id")).ToList();
        });

        IEnumerable<ProductDto> result = products ?? [];

        if (categoryId is not null)
        {
            result = result.Where(p => p.CategoryId == categoryId);
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            result = result.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        SetCacheHeader(http, status);
        return Results.Ok(result.ToList());
    }

    private static async Task<IResult> GetFeaturedAsync(HttpContext http, NpgsqlDataSource dataSource, RedisCache cache)
    {
        var (products, status) = await cache.GetOrSetAsync(CacheKeys.Featured, CacheKeys.FeaturedTtl, async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<ProductDto>(
                $"{SelectProducts} WHERE p.is_active AND p.is_featured ORDER BY p.id")).ToList();
        });

        SetCacheHeader(http, status);
        return Results.Ok(products);
    }

    private static async Task<IResult> GetProductAsync(int id, HttpContext http, NpgsqlDataSource dataSource, RedisCache cache)
    {
        var (product, status) = await cache.GetOrSetAsync(CacheKeys.Product(id), CacheKeys.ProductTtl, async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<ProductDto>(
                $"{SelectProducts} WHERE p.id = @Id AND p.is_active", new { Id = id });
        });

        SetCacheHeader(http, status);

        return product is null
            ? ApiResults.Error(StatusCodes.Status404NotFound, "Producto no encontrado.")
            : Results.Ok(product);
    }

    private static async Task<IResult> CreateProductAsync(ProductRequest request, NpgsqlDataSource dataSource, RedisCache cache)
    {
        var error = Validate(request);
        if (error is not null)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, error);
        }

        await using var connection = await dataSource.OpenConnectionAsync();

        try
        {
            var id = await connection.ExecuteScalarAsync<int>(
                """
                INSERT INTO products (category_id, name, description, price, stock, is_featured, is_active)
                VALUES (@CategoryId, @Name, @Description, @Price, @Stock, @IsFeatured, @IsActive)
                RETURNING id
                """,
                new
                {
                    request.CategoryId,
                    Name = request.Name!.Trim(),
                    Description = request.Description?.Trim() ?? string.Empty,
                    request.Price,
                    request.Stock,
                    request.IsFeatured,
                    request.IsActive
                });

            await cache.InvalidateAsync(CacheKeys.ForProductChange([id]));
            return Results.Created($"/api/products/{id}", new { id });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "La categoría no existe.");
        }
    }

    private static async Task<IResult> UpdateProductAsync(int id, ProductRequest request, NpgsqlDataSource dataSource, RedisCache cache)
    {
        var error = Validate(request);
        if (error is not null)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, error);
        }

        await using var connection = await dataSource.OpenConnectionAsync();

        try
        {
            var updated = await connection.ExecuteAsync(
                """
                UPDATE products
                SET category_id = @CategoryId, name = @Name, description = @Description, price = @Price,
                    stock = @Stock, is_featured = @IsFeatured, is_active = @IsActive, updated_at = now()
                WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    request.CategoryId,
                    Name = request.Name!.Trim(),
                    Description = request.Description?.Trim() ?? string.Empty,
                    request.Price,
                    request.Stock,
                    request.IsFeatured,
                    request.IsActive
                });

            if (updated == 0)
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "Producto no encontrado.");
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "La categoría no existe.");
        }

        // Invalidación: el siguiente GET será MISS y traerá el dato nuevo de PostgreSQL.
        await cache.InvalidateAsync(CacheKeys.ForProductChange([id]));
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateStockAsync(int id, StockRequest request, NpgsqlDataSource dataSource, RedisCache cache)
    {
        if (request.Stock is < 0 or > 100_000)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "El stock debe estar entre 0 y 100000.");
        }

        await using var connection = await dataSource.OpenConnectionAsync();

        var updated = await connection.ExecuteAsync(
            "UPDATE products SET stock = @Stock, updated_at = now() WHERE id = @Id",
            new { Id = id, request.Stock });

        if (updated == 0)
        {
            return ApiResults.Error(StatusCodes.Status404NotFound, "Producto no encontrado.");
        }

        await cache.InvalidateAsync(CacheKeys.ForProductChange([id]));
        return Results.NoContent();
    }

    private static string? Validate(ProductRequest request)
    {
        if (request.CategoryId <= 0)
        {
            return "La categoría es obligatoria.";
        }

        if (!InputValidation.IsValidName(request.Name))
        {
            return "El nombre debe tener entre 2 y 150 caracteres.";
        }

        if (request.Description is { Length: > 2000 })
        {
            return "La descripción admite máximo 2000 caracteres.";
        }

        if (request.Price is <= 0 or > 1_000_000)
        {
            return "El precio debe ser mayor que 0 y menor o igual a 1,000,000.";
        }

        if (decimal.Round(request.Price, 2) != request.Price)
        {
            return "El precio admite máximo 2 decimales.";
        }

        if (request.Stock is < 0 or > 100_000)
        {
            return "El stock debe estar entre 0 y 100000.";
        }

        return null;
    }

    private static void SetCacheHeader(HttpContext http, CacheStatus status) =>
        http.Response.Headers["X-Cache"] = status.ToString().ToUpperInvariant();
}
