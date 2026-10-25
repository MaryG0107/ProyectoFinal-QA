using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>flashstore env up|down|status|logs|restart: maneja el ambiente con Docker Compose.</summary>
public static class EnvCommands
{
    public static readonly string[] Services = ["postgres", "redis", "rabbitmq", "api", "worker", "web"];

    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        var shell = new Shell(context);
        var where = context.IsRemote ? $"{context.Ssh} ({context.ComposeDir})" : context.ComposeDir;

        switch (args.At(1))
        {
            case "up":
            {
                Output.Title($"Levantando FlashStore en {where}");
                string[] composeArgs = args.Has("build") ? ["compose", "up", "-d", "--build"] : ["compose", "up", "-d"];
                var result = await shell.DockerAsync(composeArgs, capture: false);
                if (!result.Ok)
                {
                    Output.Fail("docker compose up terminó con error.");
                    return 1;
                }

                Output.Ok("Contenedores iniciados. Revise el estado con: flashstore health");
                return 0;
            }

            case "down":
            {
                Output.Title($"Apagando FlashStore en {where}");
                Output.Info("Los volúmenes (datos) se conservan. Para borrar datos use docker compose down -v a mano.");
                var result = await shell.DockerAsync(["compose", "down"], capture: false);
                return result.Ok ? 0 : 1;
            }

            case "status":
            {
                Output.Title($"Contenedores de FlashStore en {where}");
                var result = await shell.DockerAsync(["compose", "ps", "--format", "table {{.Name}}\t{{.Status}}\t{{.Ports}}"], capture: false);
                return result.Ok ? 0 : 1;
            }

            case "logs":
            {
                var service = RequireService(args.At(2));
                var tail = args.GetInt("tail", 50, 1, 5000).ToString();
                var result = await shell.DockerAsync(["compose", "logs", "--tail", tail, service], capture: false);
                return result.Ok ? 0 : 1;
            }

            case "restart":
            {
                var service = RequireService(args.At(2));
                Output.Title($"Reiniciando {service}");
                var result = await shell.DockerAsync(["compose", "restart", service], capture: false);
                return result.Ok ? 0 : 1;
            }

            default:
                throw new UsageException("Uso: flashstore env up [--build] | down | status | logs <servicio> [--tail N] | restart <servicio>");
        }
    }

    public static string RequireService(string? service)
    {
        if (service is null || !Services.Contains(service))
        {
            throw new UsageException($"Servicio inválido. Use uno de: {string.Join(", ", Services)}");
        }

        return service;
    }
}
