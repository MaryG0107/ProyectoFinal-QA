using FlashStore.Domain.Payments;

namespace FlashStore.UnitTests;

public class PaymentSimulatorTests
{
    [Fact]
    public void Simulate_TarjetaDePruebaAprobada()
    {
        var result = PaymentSimulator.Simulate("4111 1111 1111 1111");

        Assert.True(result.IsValidCard);
        Assert.Equal(PaymentDecision.Approved, result.Decision);
        Assert.Equal("1111", result.Last4);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Simulate_TarjetaDePruebaRechazada()
    {
        var result = PaymentSimulator.Simulate(PaymentSimulator.RejectedTestCard);

        Assert.True(result.IsValidCard);
        Assert.Equal(PaymentDecision.Rejected, result.Decision);
        Assert.Equal("0002", result.Last4);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("4111111111111112")]
    [InlineData("4111-1111-1111-111a")]
    [InlineData("41111111111111111111")]
    public void Simulate_RechazaNumerosInvalidos(string? cardNumber)
    {
        var result = PaymentSimulator.Simulate(cardNumber);

        Assert.False(result.IsValidCard);
        Assert.Equal(string.Empty, result.Last4);
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4000000000000002", true)]
    [InlineData("5555555555554444", true)]
    [InlineData("4111111111111112", false)]
    public void PassesLuhn_ValidaDigitoVerificador(string digits, bool expected)
    {
        Assert.Equal(expected, PaymentSimulator.PassesLuhn(digits));
    }
}
