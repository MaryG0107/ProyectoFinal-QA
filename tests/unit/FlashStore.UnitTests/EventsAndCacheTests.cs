using FlashStore.Domain.Caching;
using FlashStore.Domain.Events;

namespace FlashStore.UnitTests;

public class EventsAndCacheTests
{
    [Theory]
    [InlineData(EventTypes.OrderCreated, "order.created")]
    [InlineData(EventTypes.InventoryReserved, "inventory.reserved")]
    [InlineData(EventTypes.PaymentConfirmed, "payment.confirmed")]
    [InlineData(EventTypes.PaymentRejected, "payment.rejected")]
    [InlineData(EventTypes.OrderCancelled, "order.cancelled")]
    [InlineData(EventTypes.InventoryUpdated, "inventory.updated")]
    public void RoutingKeyFor_DevuelveLaLlaveCorrecta(string eventType, string expected)
    {
        Assert.Equal(expected, EventTypes.RoutingKeyFor(eventType));
    }

    [Fact]
    public void RoutingKeyFor_RechazaEventoDesconocido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventTypes.RoutingKeyFor("EventoInventado"));
    }

    [Fact]
    public void EventSerializer_UsaCamelCaseYConservaLosDatos()
    {
        var original = new OrderCreatedEvent(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            7,
            3999.00m,
            [new EventLine(1, 1, 3999.00m)]);

        var json = EventSerializer.Serialize(original);
        var copy = EventSerializer.Deserialize<OrderCreatedEvent>(json);

        Assert.Contains("\"orderId\"", json);
        Assert.Contains("\"userId\":7", json);
        Assert.Equal(original.OrderId, copy.OrderId);
        Assert.Equal(original.Total, copy.Total);
        Assert.Equal(original.Items[0], copy.Items[0]);
    }

    [Fact]
    public void ForProductChange_IncluyeProductoListaYDestacadosSinDuplicados()
    {
        var keys = CacheKeys.ForProductChange([1, 1, 2]);

        Assert.Equal(new[] { "product:1", "product:2", "products:list", "products:featured" }, keys);
    }

    [Fact]
    public void Llaves_TienenElFormatoEsperado()
    {
        Assert.Equal("cart:15", CacheKeys.Cart(15));
        Assert.Equal("session:abc", CacheKeys.Session("abc"));
        Assert.Equal("product:9", CacheKeys.Product(9));
    }
}
