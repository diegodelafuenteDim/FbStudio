using System.Text.RegularExpressions;
using FbStudio.Core.Trace;

namespace FbStudio.Core.Sql;

/// <summary>
/// El SQL de otros programas sin los valores (D-114): lo que sale por el conector MCP desde el monitoreo o el trace. Los
/// textos y los números pasan a «?», las listas IN a «IN (?)» y se quitan los comentarios. Los nombres de tablas y
/// columnas quedan: son estructura.
/// </summary>
public static partial class ValueRedactor
{
    /// <param name="maxLength">Más largo que esto se corta con «…» (0: sin límite).</param>
    public static string Statement(string? sql, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "";
        // Primero los textos (un «--» o un «/*» dentro de un texto no es comentario), después los comentarios y al final
        // números, espacios e IN, con la misma regla que agrupa las consultas del trace.
        var text = StringLiteral().Replace(sql, "?");
        text = BlockComment().Replace(text, " ");
        text = LineComment().Replace(text, " ");
        text = TraceAnalysis.Key(text, unifyValues: true);
        return maxLength > 0 && text.Length > maxLength ? text[..maxLength].TrimEnd() + "…" : text;
    }

    [GeneratedRegex(@"'(?:[^']|'')*'?")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"/\*.*?(\*/|$)", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineComment();
}
