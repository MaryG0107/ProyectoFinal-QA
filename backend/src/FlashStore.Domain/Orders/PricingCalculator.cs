namespace FlashStore.Domain.Orders;

/// <summary>Línea de pedido con el precio unitario tomado de la base de datos.</summary>
public sealed record PricedLine(int ProductId, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
}

public sealed record OrderTotals(decimal Subtotal, decimal Discount, decimal Total);

public static class PricingCalculator
{
    /// <summary>
    /// Calcula subtotal, descuento y total. El descuento nunca puede dejar el total en negativo.
    /// El parámetro de descuento queda listo para la fase de cupones.
    /// </summary>
    public static OrderTotals Calculate(IEnumerable<PricedLine> lines, decimal discount = 0m)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var list = lines.ToList();

        if (list.Count == 0)
        {
            throw new ArgumentException("El pedido debe tener al menos una línea.", nameof(lines));
        }

        if (list.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Todas las cantidades deben ser mayores que cero.", nameof(lines));
        }

        if (list.Any(l => l.UnitPrice <= 0))
        {
            throw new ArgumentException("Todos los precios deben ser mayores que cero.", nameof(lines));
        }

        if (discount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "El descuento no puede ser negativo.");
        }

        var subtotal = list.Sum(l => l.LineTotal);
        var appliedDiscount = decimal.Round(Math.Min(discount, subtotal), 2, MidpointRounding.AwayFromZero);

        return new OrderTotals(subtotal, appliedDiscount, subtotal - appliedDiscount);
    }
}
