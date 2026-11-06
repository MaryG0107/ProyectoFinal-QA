using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlashStore.Cli.Core;

/// <summary>Resultado de una verificación. Se guarda en reports/cli/{name}.json para el reporte GO/NO-GO.</summary>
public sealed record CheckResult(
    string Name,
    bool Passed,
    string Summary,
    DateTimeOffset ExecutedAt,
    Dictionary<string, object?>? Details = null);

public static class ResultStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Save(string reportsDir, CheckResult result)
    {
        Directory.CreateDirectory(reportsDir);
        var path = Path.Combine(reportsDir, result.Name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, Options));
        return path;
    }

    public static CheckResult? Load(string reportsDir, string name)
    {
        var path = Path.Combine(reportsDir, name + ".json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CheckResult>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
