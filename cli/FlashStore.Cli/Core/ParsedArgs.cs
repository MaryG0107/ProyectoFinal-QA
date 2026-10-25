namespace FlashStore.Cli.Core;

/// <summary>
/// Argumentos ya separados en posicionales (comando, subcomando, valores) y opciones (--nombre valor / --bandera).
/// </summary>
public sealed class ParsedArgs
{
    private readonly Dictionary<string, string?> _options;

    private ParsedArgs(List<string> positionals, Dictionary<string, string?> options)
    {
        Positionals = positionals;
        _options = options;
    }

    public IReadOnlyList<string> Positionals { get; }

    /// <summary>Opciones que no aceptan valor (se leen como banderas).</summary>
    public static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "yes", "json", "help", "verbose", "strict", "follow"
    };

    public static ParsedArgs Parse(IEnumerable<string> args)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var list = args.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            var token = list[i];

            if (token is "-h" or "--help")
            {
                options["help"] = null;
                continue;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                positionals.Add(token);
                continue;
            }

            var name = token[2..];
            string? value = null;

            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (!Flags.Contains(name) && i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = list[++i];
            }

            options[name] = value;
        }

        return new ParsedArgs(positionals, options);
    }

    public string? At(int index) => index < Positionals.Count ? Positionals[index] : null;

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public int GetInt(string name, int defaultValue, int min, int max)
    {
        var raw = Get(name);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new UsageException($"--{name} debe ser un número entre {min} y {max}.");
        }

        return value;
    }
}

/// <summary>Error de uso (argumentos inválidos). Termina con código 2.</summary>
public sealed class UsageException(string message) : Exception(message);
