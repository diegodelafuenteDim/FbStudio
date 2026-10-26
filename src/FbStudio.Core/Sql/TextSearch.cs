using System.Text.RegularExpressions;

namespace FbStudio.Core.Sql;

/// <param name="MatchCase">Distinguir mayúsculas.</param>
/// <param name="WholeWord">Solo la palabra entera (un nombre de Firebird: letras, números, «_» y «$»).</param>
/// <param name="Regex">El texto es una expresión regular (en el reemplazo, $1… son sus grupos).</param>
public sealed record TextSearchOptions(bool MatchCase = false, bool WholeWord = false, bool Regex = false);

public readonly record struct TextMatch(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>Buscar y reemplazar en el editor: todas las coincidencias, la siguiente o la anterior, y reemplazar.</summary>
public static class TextSearch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    /// <summary>La expresión que busca <paramref name="pattern"/> con esas opciones; null y el motivo si no es válida.</summary>
    public static Regex? Build(string pattern, TextSearchOptions options, out string? error)
    {
        error = null;
        if (pattern.Length == 0) return null;
        var core = options.Regex ? pattern : System.Text.RegularExpressions.Regex.Escape(pattern);
        if (options.WholeWord) core = $@"(?<![\w$])(?:{core})(?![\w$])";
        try
        {
            return new Regex(core, (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant | RegexOptions.Multiline, Timeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Todas las coincidencias, en orden (sin las vacías, que una expresión como «a*» daría en cada lugar).</summary>
    public static IReadOnlyList<TextMatch> FindAll(string text, string pattern, TextSearchOptions options, out string? error)
    {
        if (Build(pattern, options, out error) is not { } regex) return [];
        try
        {
            return regex.Matches(text).Where(m => m.Length > 0).Select(m => new TextMatch(m.Index, m.Length)).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            error = "timeout";
            return [];
        }
    }

    /// <summary>La siguiente coincidencia que empieza después de <paramref name="offset"/> (o la primera, dando la vuelta).</summary>
    public static TextMatch? Next(IReadOnlyList<TextMatch> matches, int offset)
    {
        if (matches.Count == 0) return null;
        foreach (var m in matches) if (m.Start > offset) return m;
        return matches[0];
    }

    /// <summary>La anterior a <paramref name="offset"/> (o la última, dando la vuelta).</summary>
    public static TextMatch? Previous(IReadOnlyList<TextMatch> matches, int offset)
    {
        if (matches.Count == 0) return null;
        for (var i = matches.Count - 1; i >= 0; i--) if (matches[i].Start < offset) return matches[i];
        return matches[^1];
    }

    /// <summary>Lo que va en lugar de una coincidencia: el texto tal cual o, con expresión regular, con sus $1, $2….</summary>
    public static string Replacement(string text, TextMatch match, string pattern, string replace, TextSearchOptions options)
    {
        if (!options.Regex || Build(pattern, options, out _) is not { } regex) return replace;
        var m = regex.Match(text, match.Start);
        return m.Success && m.Index == match.Start ? m.Result(replace) : replace;
    }
}
