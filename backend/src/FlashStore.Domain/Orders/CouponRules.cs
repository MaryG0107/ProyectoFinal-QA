namespace FlashStore.Domain.Orders;

/// <summary>Cupón tal como está guardado en la base de datos. Las fechas están en UTC.</summary>
public sealed record Coupon(
    string Code,
    decimal DiscountPercent,
    decimal? MaxDiscount,
    decimal MinSubtotal,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    bool IsActive,
    int? UsageLimit,
    int TimesUsed);

public enum CouponStatus
{
    Valid,
    NotFound,
    Inactive,
    NotYetValid,
    Expired,
    Exhausted,
    BelowMinimum
}

public sealed record CouponEvaluation(CouponStatus Status, decimal Discount, string Message)
{
    public bool IsValid => Status == CouponStatus.Valid;
}

/// <summary>
/// Reglas de los cupones (fase de cupones, D-12). Son funciones puras para poder probarlas sin base de datos.
/// El descuento se calcula siempre en el servidor sobre el subtotal; nunca se confía en un monto enviado por el cliente.
/// </summary>
public static class CouponRules
{
    public const int MinCodeLength = 3;

    public const int MaxCodeLength = 30;

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Solo letras, números, guion y guion bajo: rechaza intentos de inyección antes de ir a la base.</summary>
    public static bool IsWellFormed(string normalizedCode) =>
        normalizedCode.Length is >= MinCodeLength and <= MaxCodeLength
        && normalizedCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static CouponEvaluation Evaluate(Coupon? coupon, decimal subtotal, DateTime utcNow)
    {
        if (subtotal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subtotal), "El subtotal debe ser mayor que cero.");
        }

        if (coupon is null)
        {
            return new CouponEvaluation(CouponStatus.NotFound, 0m, "El cupón no existe.");
        }

        if (!coupon.IsActive)
        {
            return new CouponEvaluation(CouponStatus.Inactive, 0m, "El cupón no está activo.");
        }

        if (coupon.ValidFrom is { } from && utcNow < from)
        {
            return new CouponEvaluation(CouponStatus.NotYetValid, 0m, "La promoción todavía no ha comenzado.");
        }

        if (coupon.ValidUntil is { } until && utcNow > until)
        {
            return new CouponEvaluation(CouponStatus.Expired, 0m, "El cupón está vencido.");
        }

        if (coupon.UsageLimit is { } limit && coupon.TimesUsed >= limit)
        {
            return new CouponEvaluation(CouponStatus.Exhausted, 0m, "El cupón ya alcanzó su límite de usos.");
        }

        if (subtotal < coupon.MinSubtotal)
        {
            return new CouponEvaluation(CouponStatus.BelowMinimum, 0m,
                $"El cupón aplica a compras desde Q{coupon.MinSubtotal:0.00}.");
        }

        var discount = decimal.Round(subtotal * coupon.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);

        if (coupon.MaxDiscount is { } max)
        {
            discount = Math.Min(discount, max);
        }

        discount = Math.Min(discount, subtotal);

        return new CouponEvaluation(CouponStatus.Valid, discount, "Cupón aplicado.");
    }
}
