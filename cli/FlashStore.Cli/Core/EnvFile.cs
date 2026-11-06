namespace FlashStore.Cli.Core;

/// <summary>Lee un archivo .env (CLAVE=valor). Ignora comentarios y líneas vacías. Nunca imprime los valores.</summary>
public static class EnvFile
{
    public static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    public static Dictionary<string, string> Load(string? path) =>
        path is not null && File.Exists(path) ? Parse(File.ReadAllLines(path)) : new Dictionary<string, string>();
}
