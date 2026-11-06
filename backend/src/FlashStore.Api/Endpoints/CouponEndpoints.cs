using Dapper;
using FlashStore.Api.Common;
using FlashStore.Domain.Orders;
using Npgsql;

namespace FlashStore.Api.Endpoints;

public static class CouponEndpoints
{
    private const decimal MaxSubtotal = 1_000_000m;

    public sealed record ValidateCouponRequest(string? Code, decimal Subtotal);

    public sealed class CouponRow
    {
        public string Code { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal DiscountPercent { get; set; }

        public decimal? MaxDiscount { get; set; }

        public decimal MinSubtotal { get; set; }

        public DateTime? ValidFrom { get; set; }

        public DateTime? ValidUntil { get; set; }

        public bool IsActive { get; set; }

        public int? UsageLimit { get; set; }

        public int TimesUsed { get; set; }
    }

    public static IEndpointRouteBuilder MapCouponEndpoints(this IEndpointRouteBuilder app)
    {
        // Requiere sesión: evita que un anónimo pruebe códigos al azar (enumeración de cupones).
        app.MapPost("/api/coupons/validate", ValidateAsync).WithTags("Cupones").RequireAuthorization();
        return app;
    }

    /// <summary>
    /// Valida un cupón contra el subtotal del carrito y devuelve el descuento calculado en el servidor.
    /// 200 = válido · 400 = datos mal formados · 404 = no existe · 422 = existe pero no aplica.
    /// </summary>
    private static async Task<IResult> ValidateAsync(ValidateCouponRequest request, NpgsqlDataSource dataSource)
    {
        var code = CouponRules.Normalize(request.Code);

        if (!CouponRules.IsWellFormed(code))
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest,
                $"El código de cupón debe tener de {CouponRules.MinCodeLength} a {CouponRules.MaxCodeLength} letras, números o guiones.");
        }

        if (request.Subtotal <= 0 || request.Subtotal > MaxSubtotal)
        {
            return ApiResults.Error(StatusCodes.Status400BadRequest, "El subtotal debe ser mayor que cero y menor que Q1,000,000.");
        }

        await using var connection = await dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<CouponRow>(
            """
            SELECT code, description, discount_percent, max_discount, min_subtotal,
                   valid_from, valid_until, is_active, usage_limit, times_used
            FROM coupons
            WHERE code = @Code
            """,
            new { Code = code });

        var coupon = row is null
            ? null
            : new Coupon(row.Code, row.DiscountPercent, row.MaxDiscount, row.MinSubtotal,
                ToUtc(row.ValidFrom), ToUtc(row.ValidUntil), row.IsActive, row.UsageLimit, row.TimesUsed);

        var evaluation = CouponRules.Evaluate(coupon, request.Subtotal, DateTime.UtcNow);

        return evaluation.Status switch
        {
            CouponStatus.Valid => Results.Ok(new
            {
                code,
                valid = true,
                description = row!.Description,
                discountPercent = row.DiscountPercent,
                subtotal = request.Subtotal,
                discount = evaluation.Discount,
                total = request.Subtotal - evaluation.Discount,
                message = evaluation.Message
            }),
            CouponStatus.NotFound => ApiResults.Error(StatusCodes.Status404NotFound, evaluation.Message),
            _ => ApiResults.Error(StatusCodes.Status422UnprocessableEntity, evaluation.Message)
        };
    }

    private static DateTime? ToUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value.ToUniversalTime(), DateTimeKind.Utc);
}
