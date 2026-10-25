using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlashStore.Cli.Core;

public sealed record HealthInfo(string Status, string Postgres, string Redis, string Rabbitmq, string InventoryStrategy);

public sealed record ProductInfo(int Id, int CategoryId, string CategoryName, string Name, decimal Price, int Stock, bool IsFeatured, bool IsActive);

public sealed record OutboxInfo(long Pending, long Published, DateTime? OldestPendingAt);

public sealed record ApiResponse(HttpStatusCode Status, string Body)
{
    public bool Ok => (int)Status is >= 200 and < 300;

    public T? As<T>() => string.IsNullOrWhiteSpace(Body) ? default : JsonSerializer.Deserialize<T>(Body, ApiClient.JsonOptions);
}

/// <summary>Cliente HTTP de la API de FlashStore. Los precios y el stock siempre se consultan al servidor.</summary>
public sealed class ApiClient : IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient(string baseUrl, int maxConnections = 64)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnections,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<HealthInfo?> GetHealthAsync()
    {
        try
        {
            var response = await SendAsync(HttpMethod.Get, "health");
            return response.Body.Length > 0 ? response.As<HealthInfo>() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<List<ProductInfo>> GetProductsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "api/products");
        EnsureOk(response, "consultar productos");
        return response.As<List<ProductInfo>>() ?? [];
    }

    public async Task<ProductInfo?> GetProductAsync(int id)
    {
        var response = await SendAsync(HttpMethod.Get, $"api/products/{id}");
        if (response.Status == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureOk(response, $"consultar el producto {id}");
        return response.As<ProductInfo>();
    }

    /// <summary>Devuelve el token o null si las credenciales no son válidas. Lanza ApiException ante 429 u otros errores.</summary>
    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await SendAsync(HttpMethod.Post, "api/auth/login", body: new { email, password });

        if (response.Status == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        EnsureOk(response, $"iniciar sesión con {email}");
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("token").GetString();
    }

    public Task<ApiResponse> CreateOrderAsync(string token, int productId, int quantity, string idempotencyKey) =>
        SendAsync(HttpMethod.Post, "api/orders", token,
            new { items = new[] { new { productId, quantity } } },
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey });

    public Task<ApiResponse> GetOrderAsync(string token, Guid id) => SendAsync(HttpMethod.Get, $"api/orders/{id}", token);

    public async Task UpdateStockAsync(string adminToken, int productId, int stock)
    {
        var response = await SendAsync(HttpMethod.Patch, $"api/products/{productId}/stock", adminToken, new { stock });
        EnsureOk(response, $"actualizar el stock del producto {productId}");
    }

    public async Task<OutboxInfo?> GetOutboxAsync(string adminToken)
    {
        var response = await SendAsync(HttpMethod.Get, "api/admin/outbox", adminToken);
        EnsureOk(response, "consultar el outbox");
        return response.As<OutboxInfo>();
    }

    public async Task<ApiResponse> SendAsync(
        HttpMethod method,
        string path,
        string? token = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _http.SendAsync(request);
        return new ApiResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static void EnsureOk(ApiResponse response, string action)
    {
        if (response.Ok)
        {
            return;
        }

        var message = response.Status == HttpStatusCode.TooManyRequests
            ? "la API rechazó la solicitud por límite de inicios de sesión (429). Espere un minuto o suba LOGIN_RATE_LIMIT en .env."
            : $"HTTP {(int)response.Status}: {Truncate(response.Body)}";

        throw new ApiException($"No se pudo {action}: {message}", response.Status);
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "...";

    public void Dispose() => _http.Dispose();
}

public sealed class ApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
