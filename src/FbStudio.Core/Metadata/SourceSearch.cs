namespace FbStudio.Core.Metadata;

/// <summary>
/// Líneas de una fuente PSQL que contienen un texto. La numeración es la de la fuente tal como la escribe
/// <see cref="DdlWriter"/> (sin saltos de línea al principio ni espacios al final), que siempre va al final del DDL:
/// línea en el DDL = líneas del DDL − <see cref="LineCount"/> + línea.
/// </summary>
public static class SourceSearch
{
    public static string Normalize(string source) => source.TrimStart('\r', '\n').TrimEnd().ReplaceLineEndings("\n");

    public static int LineCount(string source) => Normalize(source).Count(c => c == '\n') + 1;

    public static IEnumerable<(int Line, string Text)> MatchingLines(string source, string term, int max = int.MaxValue)
    {
        if (term.Length == 0) yield break;
        var lines = Normalize(source).Split('\n');
        var found = 0;
        for (var i = 0; i < lines.Length && found < max; i++)
        {
            if (!lines[i].Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            found++;
            yield return (i + 1, Snippet(lines[i].Trim()));
        }
    }

    /// <summary>Recorta líneas largas para mostrarlas en una lista.</summary>
    public static string Snippet(string text, int max = 220) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
