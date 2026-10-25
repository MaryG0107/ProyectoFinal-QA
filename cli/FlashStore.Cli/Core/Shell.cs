using System.Diagnostics;
using System.Text;

namespace FlashStore.Cli.Core;

/// <summary>Resultado de un proceso externo.</summary>
public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Ejecuta procesos externos. Los comandos de Docker se ejecutan en la máquina local o, con --ssh, dentro de la VM.
/// Los argumentos se pasan como lista (local) o entre comillas simples (remoto) para que no se interpreten como comandos.
/// </summary>
public sealed class Shell(CliContext context)
{
    /// <summary>Ejecuta "docker ..." en la carpeta del proyecto (local o remota).</summary>
    public Task<ProcessResult> DockerAsync(IReadOnlyList<string> args, bool capture = true, string? stdinFileRelative = null)
    {
        if (context.Ssh is { } ssh)
        {
            var remote = new StringBuilder();
            remote.Append("cd ").Append(context.ComposeDir).Append(" && docker");
            foreach (var arg in args)
            {
                remote.Append(' ').Append(QuoteForPosix(arg));
            }

            if (stdinFileRelative is not null)
            {
                remote.Append(" < ").Append(QuoteForPosix(stdinFileRelative));
            }

            return RunAsync("ssh", ["-p", ssh.Port.ToString(), $"{ssh.User}@{ssh.Host}", remote.ToString()], null, capture, null);
        }

        var stdin = stdinFileRelative is null ? null : Path.Combine(context.ComposeDir, stdinFileRelative);
        return RunAsync("docker", args, context.ComposeDir, capture, stdin);
    }

    /// <summary>Ejecuta un programa en la máquina local (dotnet, inso, npm, k6...).</summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory,
        bool capture,
        string? stdinFile,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(fileName),
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
            RedirectStandardInput = stdinFile is not null
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(127, string.Empty, $"No se encontró el programa '{fileName}'. ¿Está instalado y en el PATH?");
        }

        using (process)
        {
            var outputTask = capture ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
            var errorTask = capture ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);

            if (stdinFile is not null)
            {
                await using (var input = File.OpenRead(stdinFile))
                {
                    await input.CopyToAsync(process.StandardInput.BaseStream);
                }

                process.StandardInput.Close();
            }

            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
    }

    /// <summary>Comillas simples POSIX: 'a'\''b'. Nada dentro se interpreta en el shell remoto.</summary>
    public static string QuoteForPosix(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>En Windows, npm/npx/inso se instalan como .cmd y Process.Start no los encuentra sin la extensión.</summary>
    private static string ResolveExecutable(string fileName)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(fileName))
        {
            return fileName;
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
        {
            foreach (var dir in paths)
            {
                var candidate = Path.Combine(dir.Trim(), fileName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return fileName;
    }
}
