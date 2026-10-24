using System.Text.RegularExpressions;

namespace FbStudio.Core.Sql;

/// <summary>
/// Posición (1-based) que Firebird informa en los errores de compilación: "Token unknown - line 5, column 3" o
/// "At line 8, column 15". Es relativa al texto de la sentencia enviada.
/// </summary>
public readonly record struct FirebirdErrorPosition(int Line, int Column)
{
    private static readonly Regex Pattern = new(@"line\s+(\d+),\s*(?:col|column|char)\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static FirebirdErrorPosition? Parse(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        var m = Pattern.Match(message);
        return m.Success && int.TryParse(m.Groups[1].Value, out var line) && int.TryParse(m.Groups[2].Value, out var column)
            ? new FirebirdErrorPosition(line, column)
            : null;
    }

    /// <summary>Pasa la posición de la sentencia a la del texto completo del editor (la sentencia empieza en <paramref name="statementStart"/>).</summary>
    public FirebirdErrorPosition InDocument(string document, int statementStart)
    {
        var before = document.AsSpan(0, Math.Clamp(statementStart, 0, document.Length));
        var linesBefore = before.Count('\n');
        var lineStart = before.LastIndexOf('\n') + 1;
        var columnOffset = Line == 1 ? statementStart - lineStart : 0;
        return new FirebirdErrorPosition(Line + linesBefore, Column + columnOffset);
    }
}
