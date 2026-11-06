using FlashStore.Cli.Core;

namespace FlashStore.Cli.Commands;

/// <summary>
/// flashstore test unit | api | e2e | load: ejecuta cada suite con su herramienta y guarda el resultado para el GO/NO-GO.
/// Las suites corren en la máquina local, contra la API indicada con --api (y la PWA con --web).
/// </summary>
public static class TestCommands
{
    public static async Task<int> RunAsync(ParsedArgs args, CliContext context)
    {
        var root = context.LocalRoot;
        var webUrl = (args.Get("web") ?? Environment.GetEnvironmentVariable("FLASHSTORE_WEB") ?? "http://localhost:3000").TrimEnd('/');

        return args.At(1) switch
        {
            "unit" => await RunSuiteAsync(context, "test-unit", "Pruebas unitarias (.NET)", "dotnet",
                ["test", "FlashStore.sln", "--logger", "trx;LogFileName=unit.trx", "--results-directory", Path.Combine("reports", "unit")],
                root, null),

            "api" => await RunSuiteAsync(context, "test-api", "Colección de Insomnia (inso)", "inso",
                ["run", "collection", "wrk_flashstore_qa",
                 "--workingDir", Path.Combine("tests", "api", "flashstore-insomnia.json"),
                 "--env", "env_flashstore_local",
                 "--env-var", $"base_url={context.ApiUrl}",
                 "--reporter", "spec", "--ci"],
                root, null),

            "e2e" => await RunE2eAsync(context, root, webUrl),

            "load" => await RunLoadAsync(args, context, root),

            _ => throw new UsageException("Uso: flashstore test unit | api | e2e [--web URL] | load [--reset]")
        };
    }

    private static async Task<int> RunE2eAsync(CliContext context, string root, string webUrl)
    {
        var cypressDir = Path.Combine(root, "tests", "cypress");
        if (!File.Exists(Path.Combine(cypressDir, "package.json")))
        {
            Output.Warn("Cypress todavía no está en el repositorio (tests/cypress/package.json no existe).");
            return 1;
        }

        if (!Directory.Exists(Path.Combine(cypressDir, "node_modules")))
        {
            Output.Info("Instalando dependencias de Cypress (npm ci)...");
            var install = await Shell.RunAsync("npm", ["ci"], cypressDir, capture: false, stdinFile: null);
            if (!install.Ok)
            {
                return 1;
            }
        }

        return await RunSuiteAsync(context, "test-e2e", "Pruebas E2E (Cypress)", "npm", ["run", "cy:run"], cypressDir,
            new Dictionary<string, string> { ["CYPRESS_BASE_URL"] = webUrl });
    }

    private static async Task<int> RunLoadAsync(ParsedArgs args, CliContext context, string root)
    {
        if (args.Has("reset"))
        {
            var reset = ParsedArgs.Parse(["db", "reset-flash-sale", "--yes"]);
            if (await DbCommands.RunAsync(reset, context) != 0)
            {
                return 1;
            }
        }
        else
        {
            Output.Info("Recuerde: el producto 1 debe tener 5 unidades (use --reset) y LOGIN_RATE_LIMIT=1000 en .env.");
        }

        Directory.CreateDirectory(Path.Combine(root, "reports", "performance"));
        return await RunSuiteAsync(context, "test-load", "Venta relámpago con k6", "k6",
            ["run", "-e", $"BASE_URL={context.ApiUrl}",
             "--summary-export", Path.Combine("reports", "performance", "flash-sale.json"),
             Path.Combine("tests", "performance", "flash-sale.js")],
            root, null);
    }

    private static async Task<int> RunSuiteAsync(
        CliContext context,
        string resultName,
        string title,
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        Output.Title(title);
        Output.Info($"> {program} {string.Join(' ', arguments)}");

        var started = DateTimeOffset.Now;
        var run = await Shell.RunAsync(program, arguments, workingDirectory, capture: false, stdinFile: null, environment);

        if (run.ExitCode == 127)
        {
            Output.Fail(run.Error);
            return 1;
        }

        var passed = run.Ok;
        var elapsed = DateTimeOffset.Now - started;
        ResultStore.Save(context.ReportsDir, new CheckResult(resultName, passed,
            passed ? $"{title}: aprobadas ({elapsed.TotalSeconds:N0} s)." : $"{title}: con fallas (código {run.ExitCode}).",
            DateTimeOffset.Now,
            new Dictionary<string, object?> { ["exitCode"] = run.ExitCode, ["seconds"] = Math.Round(elapsed.TotalSeconds) }));

        if (passed)
        {
            Output.Ok($"{title}: aprobadas.");
            return 0;
        }

        Output.Fail($"{title}: con fallas (código {run.ExitCode}).");
        return 1;
    }
}
