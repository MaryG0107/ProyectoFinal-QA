namespace FlashStore.Domain.Payments;

public enum PaymentDecision
{
    Approved,
    Rejected
}

public sealed record PaymentSimulationResult(bool IsValidCard, PaymentDecision Decision, string Last4, string? Reason);

/// <summary>
/// Pasarela de pago simulada.
/// - Tarjeta 4111 1111 1111 1111 (o cualquier tarjeta válida por Luhn) → aprobada.
/// - Tarjeta 4000 0000 0000 0002 → rechazada (fondos insuficientes).
/// - Número inválido → error de validación (no se registra el pago).
/// Nunca se guarda el número completo de la tarjeta, solo los últimos 4 dígitos.
/// </summary>
public static class PaymentSimulator
{
    public const string ApprovedTestCard = "4111111111111111";
    public const string RejectedTestCard = "4000000000000002";

    public static PaymentSimulationResult Simulate(string? cardNumber)
    {
        var digits = new string((cardNumber ?? string.Empty)
            .Where(c => c != ' ' && c != '-')
            .ToArray());

        if (digits.Length is < 13 or > 19 || !digits.All(char.IsAsciiDigit) || !PassesLuhn(digits))
        {
            return new PaymentSimulationResult(false, PaymentDecision.Rejected, string.Empty, "Número de tarjeta inválido.");
        }

        var last4 = digits[^4..];

        if (digits == RejectedTestCard)
        {
            return new PaymentSimulationResult(true, PaymentDecision.Rejected, last4, "Fondos insuficientes (tarjeta de prueba de rechazo).");
        }

        return new PaymentSimulationResult(true, PaymentDecision.Approved, last4, null);
    }

    /// <summary>Algoritmo de Luhn, el mismo que usan las tarjetas reales para validar su número.</summary>
    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleDigit = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';

            if (doubleDigit)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
