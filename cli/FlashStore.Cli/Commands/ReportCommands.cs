using System.Text;
using FlashStore.Cli.Core;
using static FlashStore.Cli.Core.Verdicts;

namespace FlashStore.Cli.Commands;

/// <summary>
/// flashstore report go-no-go [--strict] [--security]: reúne los resultados guardados por los demás comandos y decide GO o NO-GO.
/// Genera reports/go-no-go.md y reports/go-no-go.json. Código 0 = GO, 1 = NO-GO (Jenkins detiene el despliegue).
/// </summary>
public static class ReportCommands
{
    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        if (args.At(1) != "go-no-go")
        {
            throw new UsageException("Uso: flashstore report go-no-go [--strict] [--security]");
        }

        var strict = args.Has("strict");

        // Jenkins (pipeline de seguridad) usa --security para que el criterio de seguridad sea obligatorio.
        var requireSecurity = strict || args.Has("security");

        // La salud se vuelve a medir en este momento; lo demás sale de reports/cli/*.json.
        await HealthCommand.RunAsync(ParsedArgs.Parse(["health"]), context);
        var health = ResultStore.Load(context.ReportsDir, "health");
        var strategy = health?.Details?.GetValueOrDefault("inventoryStrategy")?.ToString();

        var criteria = new List<GoNoGoCriterion>
        {
            new("Salud", "API, PostgreSQL, Redis y RabbitMQ en línea", true, health?.Passed, health?.Summary ?? "Sin datos"),
            new("Inventario", "Estrategia Atomic (sin sobreventa por diseño)", true,
                strategy is null ? null : strategy == "Atomic", strategy is null ? "Sin datos" : $"Estrategia {strategy}"),
            FromFile(context, "sale-simulate", "Venta relámpago", "300 compradores por 5 unidades: 0 sobreventa", true),
            FromFile(context, "test-unit", "Pruebas unitarias", "dotnet test sin fallas", true),
            FromFile(context, "test-api", "Pruebas de API", "Colección de Insomnia sin fallas", true),
            FromFile(context, "test-e2e", "Pruebas E2E", "Cypress sin fallas", strict),
            FromFile(context, "test-load", "Rendimiento", "Umbrales de k6 cumplidos", strict),
            FromFile(context, "queues", "Mensajería", "0 mensajes en las DLQ", strict),
            FromFile(context, "security", "Seguridad",
                "gitleaks, dependencias, trivy, checkov y ZAP sin hallazgos altos o críticos", requireSecurity)
        };

        var decision = Decide(criteria);
        var markdown = BuildMarkdown(decision, context, strict);

        var reportsRoot = Path.Combine(context.LocalRoot, "reports");
        Directory.CreateDirectory(reportsRoot);
        var mdPath = Path.Combine(reportsRoot, "go-no-go.md");
        await File.WriteAllTextAsync(mdPath, markdown, new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(reportsRoot, "go-no-go.json"), ResultStore.Serialize(decision), new UTF8Encoding(false));

        Output.Title("Decisión GO/NO-GO");
        Output.PrintTable(["Criterio", "Obligatorio", "Resultado", "Detalle"],
            criteria.Select(c => (IReadOnlyList<string>)[c.Name, c.Required ? "sí" : "no", Label(c.Passed), c.Detail]));

        if (decision.Go)
        {
            Output.Write("\n  >>> GO: la versión cumple los criterios de salida. <<<\n", ConsoleColor.Green);
        }
        else
        {
            Output.Write("\n  >>> NO-GO: hay criterios sin cumplir. <<<\n", ConsoleColor.Red);
        }

        Output.Info($"Reporte: {mdPath}");
        return decision.Go ? 0 : 1;
    }

    private static GoNoGoCriterion FromFile(CliContext context, string file, string name, string description, bool required)
    {
        var result = ResultStore.Load(context.ReportsDir, file);
        return result is null
            ? new GoNoGoCriterion(name, description, required, null, "Sin ejecutar")
            : new GoNoGoCriterion(name, description, required, result.Passed, $"{result.Summary} ({result.ExecutedAt:yyyy-MM-dd HH:mm})");
    }

    private static string Label(bool? passed) => passed switch
    {
        true => "PASA",
        false => "FALLA",
        null => "PENDIENTE"
    };

    internal static string BuildMarkdown(GoNoGoDecision decision, CliContext context, bool strict)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Reporte GO/NO-GO – FlashStore");
        sb.AppendLine();
        sb.AppendLine($"- **Fecha:** {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        sb.AppendLine($"- **API evaluada:** {context.ApiUrl}");
        sb.AppendLine($"- **Modo:** {(strict ? "estricto (todos los criterios obligatorios)" : "estándar")}");
        sb.AppendLine($"- **Decisión:** {(decision.Go ? "✅ GO" : "❌ NO-GO")}");
        sb.AppendLine();
        sb.AppendLine("| Criterio | Qué se exige | Obligatorio | Resultado | Detalle |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var c in decision.Criteria)
        {
            sb.AppendLine($"| {c.Name} | {c.Description} | {(c.Required ? "Sí" : "No")} | {Label(c.Passed)} | {c.Detail.Replace("|", "/")} |");
        }

        sb.AppendLine();
        sb.AppendLine("Regla: GO solo si todos los criterios obligatorios **pasan** y ningún criterio ejecutado **falla**.");
        return sb.ToString();
    }
}
