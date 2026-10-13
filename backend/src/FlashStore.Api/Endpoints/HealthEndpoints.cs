using Dapper;
using FlashStore.Api.Orders;
using FlashStore.Infrastructure.Messaging;
using Npgsql;
using StackExchange.Redis;

namespace FlashStore.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealthAsync).WithTags("Salud").AllowAnonymous();
        app.MapGet("/api/admin/outbox", GetOutboxStatusAsync).WithTags("Administración").RequireAuthorization("admin");
        return app;
    }

    /// <summary>
    /// healthy: todo arriba. degraded: PostgreSQL arriba pero Redis o RabbitMQ caídos
    /// (la tienda sigue vendiendo). unhealthy: PostgreSQL caído (503).
    /// </summary>
    private static async Task<IResult> GetHealthAsync(
        NpgsqlDataSource dataSource,
        IConnectionMultiplexer redis,
        RabbitMqConnectionProvider rabbit,
        OrderService orders)
    {
        var postgresUp = await CheckAsync(async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteScalarAsync<int>("SELECT 1");
        });

        var redisUp = await CheckAsync(async () => await redis.GetDatabase().PingAsync());
        var rabbitUp = await CheckAsync(() => Task.FromResult(rabbit.GetConnection()));

        var status = !postgresUp ? "unhealthy" : redisUp && rabbitUp ? "healthy" : "degraded";

        return Results.Json(new
        {
            status,
            postgres = postgresUp ? "up" : "down",
            redis = redisUp ? "up" : "down",
            rabbitmq = rabbitUp ? "up" : "down",
            inventoryStrategy = orders.Strategy.ToString()
        }, statusCode: postgresUp ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Eventos pendientes de publicar: evidencia de que nada se pierde cuando RabbitMQ está caído.</summary>
    private static async Task<IResult> GetOutboxStatusAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        var status = await connection.QuerySingleAsync<OutboxStatus>(
            """
            SELECT COUNT(*) FILTER (WHERE published_at IS NULL)     AS pending,
                   COUNT(*) FILTER (WHERE published_at IS NOT NULL) AS published,
                   MIN(created_at) FILTER (WHERE published_at IS NULL) AS oldest_pending_at
            FROM outbox_messages
            """);

        return Results.Ok(status);
    }

    private static async Task<bool> CheckAsync(Func<Task> check)
    {
        try
        {
            await check().WaitAsync(TimeSpan.FromSeconds(3));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class OutboxStatus
    {
        public long Pending { get; set; }

        public long Published { get; set; }

        public DateTime? OldestPendingAt { get; set; }
    }
}
