using FlashStore.Domain.Orders;

namespace FlashStore.UnitTests;

public class PricingCalculatorTests
{
    [Fact]
    public void Calculate_SumaLineasConPreciosDeLaBaseDeDatos()
    {
        var totals = PricingCalculator.Calculate(
        [
            new PricedLine(1, 1, 3999.00m),
            new PricedLine(2, 2, 449.00m)
        ]);

        Assert.Equal(4897.00m, totals.Subtotal);
        Assert.Equal(0m, totals.Discount);
        Assert.Equal(4897.00m, totals.Total);
    }

    [Fact]
    public void Calculate_AplicaDescuento()
    {
        var totals = PricingCalculator.Calculate([new PricedLine(1, 2, 100m)], discount: 50m);

        Assert.Equal(200m, totals.Subtotal);
        Assert.Equal(50m, totals.Discount);
        Assert.Equal(150m, totals.Total);
    }

    [Fact]
    public void Calculate_ElDescuentoNuncaDejaElTotalNegativo()
    {
        var totals = PricingCalculator.Calculate([new PricedLine(1, 1, 100m)], discount: 500m);

        Assert.Equal(100m, totals.Discount);
        Assert.Equal(0m, totals.Total);
    }

    [Fact]
    public void Calculate_RedondeaADosDecimales()
    {
        var line = new PricedLine(1, 3, 0.335m);

        Assert.Equal(1.01m, line.LineTotal);
    }

    [Fact]
    public void Calculate_RechazaPedidoSinLineas()
    {
        Assert.Throws<ArgumentException>(() => PricingCalculator.Calculate([]));
    }

    [Fact]
    public void Calculate_RechazaPrecioCero()
    {
        Assert.Throws<ArgumentException>(() => PricingCalculator.Calculate([new PricedLine(1, 1, 0m)]));
    }

    [Fact]
    public void Calculate_RechazaDescuentoNegativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PricingCalculator.Calculate([new PricedLine(1, 1, 10m)], discount: -1m));
    }
}
