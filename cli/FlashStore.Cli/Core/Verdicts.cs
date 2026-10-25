namespace FlashStore.Cli.Core;

/// <summary>Conteo de la venta relámpago simulada.</summary>
public sealed record SaleTally(
    int StockBefore,
    int StockAfter,
    int Quantity,
    int Buyers,
    int LoggedIn,
    int Created,
    int OutOfStock,
    int RateLimited,
    int ServerErrors,
    int OtherErrors);

public sealed record SaleVerdict(bool Passed, bool Oversold, IReadOnlyList<string> Findings);

/// <summary>Reglas de evaluación puras (sin red ni procesos) para poder probarlas con pruebas unitarias.</summary>
public static class Verdicts
{
    /// <summary>
    /// GO si: no hubo sobreventa (unidades vendidas &lt;= stock inicial y stock final &gt;= 0),
    /// el stock final cuadra con lo vendido, no hubo errores 5xx y todos los compradores iniciaron sesión.
    /// </summary>
    public static SaleVerdict EvaluateSale(SaleTally t)
    {
        var findings = new List<string>();
        var unitsSold = t.Created * t.Quantity;
        var oversold = unitsSold > t.StockBefore || t.StockAfter < 0;

        if (oversold)
        {
            findings.Add($"SOBREVENTA: se crearon {t.Created} pedidos ({unitsSold} unidades) con solo {t.StockBefore} en stock.");
        }

        if (t.StockAfter >= 0 && t.StockBefore - unitsSold != t.StockAfter)
        {
            findings.Add($"El stock final ({t.StockAfter}) no cuadra con lo vendido ({t.StockBefore} - {unitsSold}).");
        }

        if (t.ServerErrors > 0)
        {
            findings.Add($"{t.ServerErrors} respuestas 5xx del servidor.");
        }

        if (t.LoggedIn < t.Buyers)
        {
            findings.Add($"Solo {t.LoggedIn} de {t.Buyers} compradores iniciaron sesión ({t.RateLimited} rechazados por límite 429).");
        }

        var expectedSold = Math.Min(t.StockBefore / Math.Max(t.Quantity, 1), t.LoggedIn);
        if (!oversold && t.Created < expectedSold)
        {
            findings.Add($"Se vendieron menos unidades de las posibles ({t.Created} de {expectedSold}).");
        }

        // Una corrida incompleta (compradores sin sesión) no sirve como evidencia de GO.
        var passed = !oversold && t.ServerErrors == 0 && t.LoggedIn == t.Buyers && t.StockAfter == t.StockBefore - unitsSold;
        return new SaleVerdict(passed, oversold, findings);
    }

    public sealed record GoNoGoCriterion(string Name, string Description, bool Required, bool? Passed, string Detail);

    public sealed record GoNoGoDecision(bool Go, IReadOnlyList<GoNoGoCriterion> Criteria);

    /// <summary>GO solo si todos los criterios obligatorios pasaron y ninguno de los ejecutados falló.</summary>
    public static GoNoGoDecision Decide(IReadOnlyList<GoNoGoCriterion> criteria)
    {
        var go = criteria.All(c => c.Passed == true || (!c.Required && c.Passed is null));
        return new GoNoGoDecision(go, criteria);
    }
}
