using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using FlashStore.Domain.Caching;
using FlashStore.Domain.Events;
using FlashStore.Infrastructure.Caching;
using FlashStore.Infrastructure.Messaging;
using FlashStore.Infrastructure.Outbox;
using Npgsql;

namespace FlashStore.Worker.Handlers;

public sealed record IncomingMessage(Guid MessageId, string Type, string Body);

public interface IMessageHandler
{
    /// <summary>Cola de RabbitMQ que atiende este consumidor.</summary>
    string Queue { get; }

    /// <summary>Nombre usado en processed_messages para detectar mensajes duplicados.</summary>
    string ConsumerName { get; }

    Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken);
}

public static class ProcessedMessages
{
    /// <summary>
    /// Marca el mensaje como procesado por este consumidor. Devuelve false si ya se había
    /// procesado (mensaje duplicado): en ese caso el consumidor no repite el efecto.
    /// </summary>
    public static async Task<bool> TryMarkAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken)
    {
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO processed_messages (message_id, consumer)
            VALUES (@MessageId, @Consumer)
            ON CONFLICT DO NOTHING
            """,
            new { MessageId = messageId, Consumer = consumer },
            transaction,
            cancellationToken: cancellationToken));

        return inserted == 1;
    }
}

/// <summary>PaymentConfirmed → PAID, PaymentRejected → PAYMENT_REJECTED.</summary>
public sealed class OrderStatusHandler : IMessageHandler
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<OrderStatusHandler> _logger;

    public OrderStatusHandler(NpgsqlDataSource dataSource, ILogger<OrderStatusHandler> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public string Queue => RabbitMqTopology.OrdersQueue;

    public string ConsumerName => "order-status";

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        Guid orderId;
        string newStatus;

        switch (message.Type)
        {
            case EventTypes.PaymentConfirmed:
                orderId = EventSerializer.Deserialize<PaymentConfirmedEvent>(message.Body).OrderId;
                newStatus = "PAID";
                break;

            case EventTypes.PaymentRejected:
                orderId = EventSerializer.Deserialize<PaymentRejectedEvent>(message.Body).OrderId;
                newStatus = "PAYMENT_REJECTED";
                break;

            default:
                return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ProcessedMessages.TryMarkAsync(connection, transaction, message.MessageId, ConsumerName, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Mensaje duplicado {MessageId} ignorado por {Consumer}", message.MessageId, ConsumerName);
            return;
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE orders SET status = @Status, updated_at = now() WHERE id = @OrderId AND status = 'PENDING_PAYMENT'",
            new { Status = newStatus, OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Pedido {OrderId} → {Status} ({Updated} fila)", orderId, newStatus, updated);
    }
}

/// <summary>
/// PaymentRejected u OrderCancelled → devuelve el stock de las líneas del pedido,
/// invalida el cache de esos productos y publica InventoryUpdated (por outbox).
/// </summary>
public sealed class InventoryRestockHandler : IMessageHandler
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RedisCache _cache;
    private readonly ILogger<InventoryRestockHandler> _logger;

    public InventoryRestockHandler(NpgsqlDataSource dataSource, RedisCache cache, ILogger<InventoryRestockHandler> logger)
    {
        _dataSource = dataSource;
        _cache = cache;
        _logger = logger;
    }

    public string Queue => RabbitMqTopology.InventoryQueue;

    public string ConsumerName => "inventory-restock";

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        Guid orderId;
        int userId;
        string reason;

        switch (message.Type)
        {
            case EventTypes.PaymentRejected:
                var rejected = EventSerializer.Deserialize<PaymentRejectedEvent>(message.Body);
                (orderId, userId, reason) = (rejected.OrderId, rejected.UserId, $"Pago rechazado: {rejected.Reason}");
                break;

            case EventTypes.OrderCancelled:
                var cancelled = EventSerializer.Deserialize<OrderCancelledEvent>(message.Body);
                (orderId, userId, reason) = (cancelled.OrderId, cancelled.UserId, cancelled.Reason);
                break;

            default:
                return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ProcessedMessages.TryMarkAsync(connection, transaction, message.MessageId, ConsumerName, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Mensaje duplicado {MessageId} ignorado: el stock NO se devuelve dos veces", message.MessageId);
            return;
        }

        var lines = (await connection.QueryAsync<LineRow>(new CommandDefinition(
            "SELECT product_id, quantity, unit_price FROM order_items WHERE order_id = @OrderId ORDER BY product_id",
            new { OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE products SET stock = stock + @Quantity, updated_at = now() WHERE id = @ProductId",
                new { line.Quantity, line.ProductId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await OutboxWriter.AddAsync(connection, transaction, EventTypes.InventoryUpdated,
            new InventoryUpdatedEvent(
                orderId,
                userId,
                lines.Select(l => new EventLine(l.ProductId, l.Quantity, l.UnitPrice)).ToList(),
                reason));

        await transaction.CommitAsync(cancellationToken);

        await _cache.InvalidateAsync(CacheKeys.ForProductChange(lines.Select(l => l.ProductId)));

        _logger.LogInformation("Stock devuelto para el pedido {OrderId}: {Lines} productos", orderId, lines.Count);
    }

    private sealed class LineRow
    {
        public int ProductId { get; set; }

        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }
    }
}

/// <summary>Escucha todos los eventos (#) y registra una notificación para el cliente.</summary>
public sealed class NotificationHandler : IMessageHandler
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NotificationHandler> _logger;

    public NotificationHandler(NpgsqlDataSource dataSource, ILogger<NotificationHandler> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public string Queue => RabbitMqTopology.NotificationsQueue;

    public string ConsumerName => "notifications";

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message.Body);
        var root = document.RootElement;

        var userId = root.GetProperty("userId").GetInt32();
        var orderId = root.GetProperty("orderId").GetGuid();
        var shortId = orderId.ToString("N")[..8].ToUpperInvariant();

        var text = message.Type switch
        {
            EventTypes.OrderCreated =>
                $"Tu pedido {shortId} fue creado por Q{root.GetProperty("total").GetDecimal().ToString("N2", CultureInfo.InvariantCulture)}. Pendiente de pago.",
            EventTypes.InventoryReserved => $"Reservamos el inventario de tu pedido {shortId}.",
            EventTypes.PaymentConfirmed => $"Pago aprobado para tu pedido {shortId}. ¡Gracias por tu compra!",
            EventTypes.PaymentRejected => $"El pago de tu pedido {shortId} fue rechazado: {root.GetProperty("reason").GetString()}",
            EventTypes.OrderCancelled => $"Tu pedido {shortId} fue cancelado.",
            EventTypes.InventoryUpdated => $"Se liberó el inventario del pedido {shortId}.",
            _ => $"Evento {message.Type} del pedido {shortId}."
        };

        if (text.Length > 300)
        {
            text = text[..300];
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ProcessedMessages.TryMarkAsync(connection, transaction, message.MessageId, ConsumerName, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Mensaje duplicado {MessageId} ignorado: no se repite la notificación", message.MessageId);
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO notifications (user_id, event_type, message) VALUES (@UserId, @EventType, @Message)",
            new { UserId = userId, EventType = message.Type, Message = text },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Notificación para el usuario {UserId}: {Text}", userId, text);
    }
}
