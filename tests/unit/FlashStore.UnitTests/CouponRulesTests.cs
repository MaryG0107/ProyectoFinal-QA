using FlashStore.Domain.Orders;

namespace FlashStore.UnitTests;

public class CouponRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Coupon Flash10(
        bool active = true,
        DateTime? from = null,
        DateTime? until = null,
        int? limit = null,
        int used = 0,
        decimal minSubtotal = 100m,
        decimal? maxDiscount = null) =>
        new("FLASH10", 10m, maxDiscount, minSubtotal, from ?? Now.AddDays(-1), until ?? Now.AddDays(30), active, limit, used);

    [Fact]
    public void Evaluate_CuponValido_CalculaDiezPorCiento()
    {
        var result = CouponRules.Evaluate(Flash10(), 449.00m, Now);

        Assert.True(result.IsValid);
        Assert.Equal(44.90m, result.Discount);
    }

    [Fact]
    public void Evaluate_RespetaElDescuentoMaximo()
    {
        var coupon = new Coupon("BIENVENIDA50", 50m, 200m, 500m, null, null, true, null, 0);

        var result = CouponRules.Evaluate(coupon, 3999.00m, Now);

        Assert.True(result.IsValid);
        Assert.Equal(200m, result.Discount);
    }

    [Fact]
    public void Evaluate_CuponInexistente()
    {
        Assert.Equal(CouponStatus.NotFound, CouponRules.Evaluate(null, 100m, Now).Status);
    }

    [Theory]
    [InlineData(false, 0, CouponStatus.Inactive)]
    [InlineData(true, 1, CouponStatus.Exhausted)]
    public void Evaluate_InactivoOAgotado_NoAplica(bool active, int used, CouponStatus expected)
    {
        var result = CouponRules.Evaluate(Flash10(active: active, limit: 1, used: used), 449m, Now);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0m, result.Discount);
    }

    [Fact]
    public void Evaluate_Vencido()
    {
        var result = CouponRules.Evaluate(Flash10(from: Now.AddDays(-60), until: Now.AddSeconds(-1)), 449m, Now);

        Assert.Equal(CouponStatus.Expired, result.Status);
    }

    [Fact]
    public void Evaluate_TodaviaNoComienza()
    {
        var result = CouponRules.Evaluate(Flash10(from: Now.AddDays(1), until: Now.AddDays(30)), 449m, Now);

        Assert.Equal(CouponStatus.NotYetValid, result.Status);
    }

    [Fact]
    public void Evaluate_SubtotalMenorAlMinimo()
    {
        var result = CouponRules.Evaluate(Flash10(minSubtotal: 500m), 449m, Now);

        Assert.Equal(CouponStatus.BelowMinimum, result.Status);
    }

    [Fact]
    public void Evaluate_ElDescuentoNuncaSuperaElSubtotal()
    {
        var coupon = new Coupon("TOTAL", 100m, null, 0m, null, null, true, null, 0);

        var result = CouponRules.Evaluate(coupon, 129.00m, Now);

        Assert.Equal(129.00m, result.Discount);
    }

    [Fact]
    public void Evaluate_SubtotalCeroOSinValor_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CouponRules.Evaluate(Flash10(), 0m, Now));
    }

    [Theory]
    [InlineData(" flash10 ", "FLASH10", true)]
    [InlineData("' OR 1=1--", "' OR 1=1--", false)]
    [InlineData("<script>", "<SCRIPT>", false)]
    [InlineData("AB", "AB", false)]
    public void NormalizeEIsWellFormed_RechazanCodigosPeligrosos(string input, string normalized, bool wellFormed)
    {
        var code = CouponRules.Normalize(input);

        Assert.Equal(normalized, code);
        Assert.Equal(wellFormed, CouponRules.IsWellFormed(code));
    }
}
