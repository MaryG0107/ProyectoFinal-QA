using System.Text.Json;

namespace FlashStore.Domain.Events;

/// <summary>Nombres de eventos y su routing key en el exchange "flashstore.events".</summary>
public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string InventoryReserved = "InventoryReserved";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string PaymentRejected = "PaymentRejected";
    public const string OrderCancelled = "OrderCancelled";
    public const string InventoryUpdated = "InventoryUpdated";

    public static string RoutingKeyFor(string eventType) => eventType switch
    {
        OrderCreated => "order.created",
        InventoryReserved => "inventory.reserved",
        PaymentConfirmed => "payment.confirmed",
        PaymentRejected => "payment.rejected",
        OrderCancelled => "order.cancelled",
        InventoryUpdated => "inventory.updated",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Tipo de evento desconocido.")
    };
}

public sealed record EventLine(int ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderCreatedEvent(Guid OrderId, int UserId, decimal Total, IReadOnlyList<EventLine> Items);

public sealed record InventoryReservedEvent(Guid OrderId, int UserId, IReadOnlyList<EventLine> Items);

public sealed record PaymentConfirmedEvent(Guid OrderId, int UserId, Guid PaymentId, decimal Amount);

public sealed record PaymentRejectedEvent(Guid OrderId, int UserId, Guid PaymentId, decimal Amount, string Reason);

public sealed record OrderCancelledEvent(Guid OrderId, int UserId, string Reason);

public sealed record InventoryUpdatedEvent(Guid OrderId, int UserId, IReadOnlyList<EventLine> Items, string Reason);

/// <summary>Serialización JSON común (camelCase) para todos los eventos.</summary>
public static class EventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"No se pudo deserializar el evento {typeof(T).Name}.");
}
