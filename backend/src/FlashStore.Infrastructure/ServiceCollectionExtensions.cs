using Dapper;
using FlashStore.Infrastructure.Caching;
using FlashStore.Infrastructure.Messaging;
using FlashStore.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace FlashStore.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra PostgreSQL, Redis, RabbitMQ y el despachador del outbox.
    /// Lo usan tanto la API como el Worker.
    /// </summary>
    public static IServiceCollection AddFlashStoreInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string clientName)
    {
        // Permite mapear columnas snake_case (unit_price) a propiedades PascalCase (UnitPrice).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var postgres = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(postgres))
        {
            throw new InvalidOperationException(
                "Falta ConnectionStrings:Postgres. Defínala con la variable de entorno ConnectionStrings__Postgres.");
        }

        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            throw new InvalidOperationException(
                "Falta ConnectionStrings:Redis. Defínala con la variable de entorno ConnectionStrings__Redis.");
        }

        services.AddSingleton(_ => NpgsqlDataSource.Create(postgres));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false; // la app arranca aunque Redis esté caído
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 2000;
            options.AsyncTimeout = 2000;
            options.ClientName = clientName;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<RedisCache>();

        services.AddSingleton(new RabbitMqSettings
        {
            HostName = configuration["RabbitMq:Host"] ?? "localhost",
            Port = int.TryParse(configuration["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = configuration["RabbitMq:User"] ?? "guest",
            Password = configuration["RabbitMq:Password"] ?? "guest",
            VirtualHost = configuration["RabbitMq:VirtualHost"] ?? "/",
            ClientName = clientName
        });

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxBackgroundService>();

        return services;
    }
}
