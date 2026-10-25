namespace FlashStore.Cli.Core;

/// <summary>Acceso a PostgreSQL, Redis y RabbitMQ a través de "docker exec" (no expone puertos ni contraseñas extra).</summary>
public sealed class Containers(CliContext context, Shell shell)
{
    public const string Postgres = "flashstore-postgres";
    public const string Redis = "flashstore-redis";
    public const string RabbitMq = "flashstore-rabbitmq";

    private string DbUser => context.Env.GetValueOrDefault("POSTGRES_USER", "flashstore");

    private string DbName => context.Env.GetValueOrDefault("POSTGRES_DB", "flashstore");

    /// <summary>Ejecuta una consulta SQL fija (nunca concatenar texto del usuario) y devuelve las filas separadas por '|'.</summary>
    public async Task<List<string[]>> QueryAsync(string sql)
    {
        var result = await shell.DockerAsync(["exec", Postgres, "psql", "-U", DbUser, "-d", DbName, "-At", "-F", "|", "-v", "ON_ERROR_STOP=1", "-c", sql]);
        EnsureOk(result, "consultar PostgreSQL");

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|'))
            .ToList();
    }

    public async Task<ProcessResult> RunSqlFileAsync(string relativePath)
    {
        var result = await shell.DockerAsync(["exec", "-i", Postgres, "psql", "-U", DbUser, "-d", DbName, "-v", "ON_ERROR_STOP=1"],
            stdinFileRelative: relativePath);
        EnsureOk(result, $"ejecutar {relativePath}");
        return result;
    }

    public async Task<string> RedisAsync(params string[] args)
    {
        var result = await shell.DockerAsync(["exec", Redis, "redis-cli", .. args]);
        EnsureOk(result, "consultar Redis");
        return result.Output.Trim();
    }

    /// <summary>Colas de RabbitMQ: nombre, mensajes listos y consumidores.</summary>
    public async Task<List<(string Name, long Messages, int Consumers)>> QueuesAsync()
    {
        var result = await shell.DockerAsync(["exec", RabbitMq, "rabbitmqctl", "-q", "list_queues", "name", "messages", "consumers"]);
        EnsureOk(result, "consultar RabbitMQ");

        var queues = new List<(string, long, int)>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && long.TryParse(parts[1], out var messages) && int.TryParse(parts[2], out var consumers))
            {
                queues.Add((parts[0], messages, consumers));
            }
        }

        return queues.OrderBy(q => q.Item1, StringComparer.Ordinal).ToList();
    }

    private static void EnsureOk(ProcessResult result, string action)
    {
        if (!result.Ok)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new CommandFailedException($"No se pudo {action}: {detail.Trim()}");
        }
    }
}

/// <summary>Falla de un comando (servicio caído, contenedor inexistente...). Termina con código 1.</summary>
public sealed class CommandFailedException(string message) : Exception(message);
