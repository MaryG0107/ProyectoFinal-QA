using System.Text.RegularExpressions;

namespace FlashStore.Cli.Core;

/// <summary>
/// Configuración de una ejecución del CLI. Prioridad: opción de línea de comandos > variable de entorno > valor por defecto.
///
///   --api   / FLASHSTORE_API   URL de la API              (por defecto http://localhost:5000)
///   --ssh   / FLASHSTORE_SSH   usuario@host[:puerto]      (vacío = Docker local; ej. mary@localhost:2222 para la VM)
///   --dir   / FLASHSTORE_DIR   carpeta del proyecto donde corre Docker Compose
///                              (local: se busca docker-compose.yml hacia arriba; remoto: ~/ProyectoFinal-QA)
/// </summary>
public sealed partial class CliContext
{
    public const string DefaultApi = "http://localhost:5000";
    public const string DefaultRemoteDir = "~/ProyectoFinal-QA";

    public required string ApiUrl { get; init; }

    /// <summary>Carpeta local del repositorio (para leer .env, scripts y guardar reportes).</summary>
    public required string LocalRoot { get; init; }

    /// <summary>Carpeta donde se ejecuta Docker Compose (local o en la VM).</summary>
    public required string ComposeDir { get; init; }

    public SshTarget? Ssh { get; init; }

    public required IReadOnlyDictionary<string, string> Env { get; init; }

    public bool Json { get; init; }

    public bool IsRemote => Ssh is not null;

    public string ReportsDir => Path.Combine(LocalRoot, "reports", "cli");

    public string AdminEmail => Environment.GetEnvironmentVariable("FLASHSTORE_ADMIN_EMAIL") ?? "admin@flashstore.test";

    public string AdminPassword => Environment.GetEnvironmentVariable("FLASHSTORE_ADMIN_PASSWORD") ?? "Admin1234!";

    public string CustomerPassword => Environment.GetEnvironmentVariable("FLASHSTORE_CUSTOMER_PASSWORD") ?? "Test1234!";

    public static CliContext Create(ParsedArgs args, string currentDirectory)
    {
        var api = (args.Get("api") ?? Environment.GetEnvironmentVariable("FLASHSTORE_API") ?? DefaultApi).TrimEnd('/');
        if (!Uri.TryCreate(api, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new UsageException($"--api no es una URL válida: {api}");
        }

        var sshRaw = args.Get("ssh") ?? Environment.GetEnvironmentVariable("FLASHSTORE_SSH");
        var ssh = string.IsNullOrWhiteSpace(sshRaw) ? null : SshTarget.Parse(sshRaw);

        var localRoot = FindProjectRoot(currentDirectory) ?? currentDirectory;
        var dir = args.Get("dir") ?? Environment.GetEnvironmentVariable("FLASHSTORE_DIR");

        string composeDir;
        if (ssh is not null)
        {
            composeDir = dir ?? DefaultRemoteDir;
            if (!SafeRemotePath().IsMatch(composeDir))
            {
                throw new UsageException("--dir remoto solo admite letras, números, ~ / . _ -");
            }
        }
        else
        {
            composeDir = dir is null ? localRoot : Path.GetFullPath(dir);
        }

        return new CliContext
        {
            ApiUrl = api,
            LocalRoot = localRoot,
            ComposeDir = composeDir,
            Ssh = ssh,
            Env = EnvFile.Load(Path.Combine(localRoot, ".env")),
            Json = args.Has("json")
        };
    }

    /// <summary>Busca hacia arriba la carpeta que contiene docker-compose.yml.</summary>
    public static string? FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_./~-]+$")]
    private static partial Regex SafeRemotePath();
}

/// <summary>Destino SSH validado (evita inyectar comandos por el nombre de host o usuario).</summary>
public sealed partial record SshTarget(string User, string Host, int Port)
{
    public static SshTarget Parse(string raw)
    {
        var match = Pattern().Match(raw.Trim());
        if (!match.Success)
        {
            throw new UsageException("--ssh debe tener la forma usuario@host o usuario@host:puerto (ej. mary@localhost:2222).");
        }

        var port = match.Groups["port"].Success ? int.Parse(match.Groups["port"].Value) : 22;
        if (port is < 1 or > 65535)
        {
            throw new UsageException("El puerto SSH debe estar entre 1 y 65535.");
        }

        return new SshTarget(match.Groups["user"].Value, match.Groups["host"].Value, port);
    }

    public override string ToString() => $"{User}@{Host}:{Port}";

    [GeneratedRegex(@"^(?<user>[A-Za-z0-9._-]+)@(?<host>[A-Za-z0-9.-]+)(:(?<port>\d{1,5}))?$")]
    private static partial Regex Pattern();
}
