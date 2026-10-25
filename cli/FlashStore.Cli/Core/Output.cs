using System.Text;

namespace FlashStore.Cli.Core;

/// <summary>Salida en consola: títulos, estados con color y tablas alineadas.</summary>
public static class Output
{
    public static void Title(string text)
    {
        Console.WriteLine();
        Write(text, ConsoleColor.Cyan);
        Console.WriteLine(new string('-', Math.Min(text.Length, 70)));
    }

    public static void Ok(string text) => Write("OK    " + text, ConsoleColor.Green);

    public static void Warn(string text) => Write("AVISO " + text, ConsoleColor.Yellow);

    public static void Fail(string text) => Write("FALLA " + text, ConsoleColor.Red);

    public static void Info(string text) => Console.WriteLine("      " + text);

    public static void Write(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var data = rows.ToList();
        var widths = headers.Select(h => h.Length).ToArray();

        foreach (var row in data)
        {
            for (var i = 0; i < headers.Count && i < row.Count; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i]))).TrimEnd());
        sb.AppendLine(string.Join("  ", widths.Select(w => new string('-', w))));

        foreach (var row in data)
        {
            sb.AppendLine(string.Join("  ", headers.Select((_, i) => (i < row.Count ? row[i] : string.Empty).PadRight(widths[i]))).TrimEnd());
        }

        return sb.ToString();
    }

    public static void PrintTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) =>
        Console.Write(Table(headers, rows));
}
