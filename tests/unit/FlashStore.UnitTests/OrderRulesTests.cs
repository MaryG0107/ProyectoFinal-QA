using FlashStore.Domain.Orders;

namespace FlashStore.UnitTests;

public class OrderRulesTests
{
    [Fact]
    public void NormalizeItems_UneLineasRepetidasYOrdenaPorProducto()
    {
        var result = OrderRules.NormalizeItems(
        [
            new OrderItemRequest(5, 1),
            new OrderItemRequest(2, 3),
            new OrderItemRequest(5, 2)
        ]);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(new OrderItemRequest(2, 3), result.Value[0]);
        Assert.Equal(new OrderItemRequest(5, 3), result.Value[1]);
    }

    [Fact]
    public void NormalizeItems_RechazaListaNula()
    {
        var result = OrderRules.NormalizeItems(null);

        Assert.False(result.IsValid);
        Assert.Equal("La orden debe incluir al menos un producto.", result.Error);
    }

    [Fact]
    public void NormalizeItems_RechazaListaVacia()
    {
        var result = OrderRules.NormalizeItems([]);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NormalizeItems_RechazaCantidadCeroONegativa(int quantity)
    {
        var result = OrderRules.NormalizeItems([new OrderItemRequest(1, quantity)]);

        Assert.False(result.IsValid);
        Assert.Equal("La cantidad debe ser mayor que cero.", result.Error);
    }

    [Fact]
    public void NormalizeItems_RechazaProductoInvalido()
    {
        var result = OrderRules.NormalizeItems([new OrderItemRequest(0, 1)]);

        Assert.False(result.IsValid);
        Assert.Equal("Identificador de producto inválido.", result.Error);
    }

    [Fact]
    public void NormalizeItems_RechazaCantidadMayorAlMaximo()
    {
        var result = OrderRules.NormalizeItems([new OrderItemRequest(1, OrderRules.MaxQuantityPerItem + 1)]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NormalizeItems_RechazaSumaDeLineasRepetidasMayorAlMaximo()
    {
        var result = OrderRules.NormalizeItems(
        [
            new OrderItemRequest(1, 6),
            new OrderItemRequest(1, 6)
        ]);

        Assert.False(result.IsValid);
        Assert.Equal($"La cantidad máxima por producto es {OrderRules.MaxQuantityPerItem}.", result.Error);
    }

    [Fact]
    public void NormalizeItems_EvitaDesbordamientoConCantidadesEnormes()
    {
        var result = OrderRules.NormalizeItems(
        [
            new OrderItemRequest(1, int.MaxValue),
            new OrderItemRequest(1, int.MaxValue)
        ]);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("3f2b9c1e-7a4d-4e8b-9f1a-2c3d4e5f6a7b", true)]
    [InlineData("pedido_123", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("llave con espacios", false)]
    [InlineData("<script>alert(1)</script>", false)]
    public void IsValidIdempotencyKey_ValidaFormato(string? key, bool expected)
    {
        Assert.Equal(expected, OrderRules.IsValidIdempotencyKey(key));
    }

    [Fact]
    public void IsValidIdempotencyKey_RechazaLlavesDemasiadoLargas()
    {
        Assert.False(OrderRules.IsValidIdempotencyKey(new string('a', OrderRules.MaxIdempotencyKeyLength + 1)));
    }
}
