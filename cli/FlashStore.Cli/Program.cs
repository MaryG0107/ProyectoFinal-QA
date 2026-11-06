using System.Reflection;
using System.Text;
using FlashStore.Cli.Commands;
using FlashStore.Cli.Core;

Console.OutputEncoding = Encoding.UTF8;

var parsed = ParsedArgs.Parse(args);
var command = parsed.At(0);

if (command is null || command == "help" || (parsed.Has("help") && parsed.Positionals.Count <= 1))
{
    Console.WriteLine(HelpCommand.Text);
    return command is null && !parsed.Has("help") ? 2 : 0;
}

if (command is "version" or "--version")
{
    Console.WriteLine($"flashstore {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
    return 0;
}

try
{
    var context = CliContext.Create(parsed, Environment.CurrentDirectory);

    return command switch
    {
        "env" => await EnvCommands.RunAsync(parsed, context),
        "health" => await HealthCommand.RunAsync(parsed, context),
        "db" => await DbCommands.RunAsync(parsed, context),
        "product" => await ProductCommands.RunAsync(parsed, context),
        "order" => await OrderCommands.RunAsync(parsed, context),
        "cache" => await InfraCommands.CacheAsync(parsed, context),
        "queue" => await InfraCommands.QueueAsync(parsed, context),
        "outbox" => await InfraCommands.OutboxAsync(parsed, context),
        "chaos" => await InfraCommands.ChaosAsync(parsed, context),
        "sale" => await SaleCommands.RunAsync(parsed, context),
        "test" => await TestCommands.RunAsync(parsed, context),
        "report" => await ReportCommands.RunAsync(parsed, context),
        _ => throw new UsageException($"Comando desconocido: {command}. Use: flashstore help")
    };
}
catch (UsageException ex)
{
    Output.Fail(ex.Message);
    return 2;
}
catch (Exception ex) when (ex is CommandFailedException or ApiException or HttpRequestException or TaskCanceledException)
{
    Output.Fail(ex is HttpRequestException or TaskCanceledException
        ? $"No hay conexión con la API ({ex.Message}). ¿Está levantado FlashStore? Pruebe: flashstore health"
        : ex.Message);
    return 1;
}
