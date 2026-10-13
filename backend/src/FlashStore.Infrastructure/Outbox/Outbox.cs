using System.Data;
using System.Text;
using Dapper;
using FlashStore.Domain.Events;
using FlashStore.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;

namespace FlashStore.Infrastructure.Outbox;

/// <summary>
/// Escribe eventos en la tabla outbox_messages DENTRO de la misma transacción del cambio de negocio.
/// Así un pedido nunca queda guardado sin su evento (ni un evento sin su pedido).
/// </summary>
public static class OutboxWriter
{
    public static Task AddAsync<T>(IDbConnection connection, IDbTransaction transaction, string eventType, T payload) =>
        connection.ExecuteAsync(
            """
            INSERT INTO outbox_messages (id, type, routing_key, payload)
            VALUES (@Id, @Type, @RoutingKey, CAST(@Payload AS jsonb))
            """,
            new
            {
                Id = Guid.NewGuid(),
                Type = eventType,
                RoutingKey = EventTypes.RoutingKeyFor(eventType),
                Payload = EventSerializer.Serialize(payload)
            },
            transaction);
}

/// <summary>Publica en RabbitMQ los eventos pendientes de la tabla outbox.</summary>
public sealed class OutboxDispatcher
{
    private const int BatchSize = 50;

    private readonly NpgsqlDataSource _dataSource;
    private readonly RabbitMqConnectionProvider _rabbit;

    public OutboxDispatcher(NpgsqlDataSource dataSource, RabbitMqConnectionProvider rabbit)
    {
        _dataSource = dataSource;
        _rabbit = rabbit;
    }

    /// <summary>
    /// Toma hasta 50 eventos pendientes con FOR UPDATE SKIP LOCKED (varios procesos pueden
    /// despachar al mismo tiempo sin publicar dos veces el mismo evento), los publica con
    /// confirmación del broker y los marca como publicados.
    /// </summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var pending = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(
            """
            SELECT id, type, routing_key, payload::text AS payload
            FROM outbox_messages
            WHERE published_at IS NULL
            ORDER BY created_at
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """,
            new { BatchSize },
            transaction,
            cancellationToken: cancellationToken))).ToList();

        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        using (var channel = _rabbit.GetConnection().CreateModel())
        {
            RabbitMqTopology.Declare(channel);
            channel.ConfirmSelect();

            foreach (var message in pending)
            {
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.MessageId = message.Id.ToString();
                properties.Type = message.Type;
                properties.ContentType = "application/json";
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                channel.BasicPublish(
                    exchange: RabbitMqTopology.Exchange,
                    routingKey: message.RoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload));
            }

            // Si el broker no confirma, lanza excepción y la transacción se revierte:
            // los eventos quedan pendientes y se reintentan en el siguiente ciclo.
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));
        }

        foreach (var message in pending)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE outbox_messages SET published_at = now() WHERE id = @Id",
                new { message.Id },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return pending.Count;
    }

    private sealed class OutboxRow
    {
        public Guid Id { get; set; }

        public string Type { get; set; } = string.Empty;

        public string RoutingKey { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;
    }
}

/// <summary>Proceso en segundo plano que despacha el outbox cada 2 segundos.</summary>
public sealed class OutboxBackgroundService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private readonly OutboxDispatcher _dispatcher;
    private readonly ILogger<OutboxBackgroundService> _logger;

    public OutboxBackgroundService(OutboxDispatcher dispatcher, ILogger<OutboxBackgroundService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await _dispatcher.DispatchBatchAsync(stoppingToken);

                if (published > 0)
                {
                    _logger.LogInformation("Outbox: {Count} eventos publicados en RabbitMQ", published);
                    continue;
                }

                await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox: no se pudo publicar (¿RabbitMQ caído?). Los eventos quedan pendientes y se reintentará");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}
