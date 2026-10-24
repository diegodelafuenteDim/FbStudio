namespace FbStudio.Core.Sql;

/// <summary>Un nombre del editor: el que está bajo el mouse o el cursor, y lo que tiene adelante con punto («t.ID»).</summary>
/// <param name="Name">Como lo guarda Firebird: sin comillas en mayúsculas; entre comillas, tal cual.</param>
/// <param name="Qualifier">Lo de antes del punto (un alias, una tabla o un paquete), o null.</param>
public sealed record SqlName(string Name, string? Qualifier, int Start, int Length);

/// <summary>Ctrl+clic y F12 (F-32): qué nombre hay en un lugar del texto y a qué tabla apunta un alias.</summary>
public static class SqlNavigation
{
    /// <summary>El nombre que contiene a <paramref name="offset"/> (o termina justo ahí); null en un texto, un comentario o un signo.</summary>
    public static SqlName? NameAt(string text, int offset)
    {
        var tokens = SqlLexer.Tokenize(text).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (offset < t.Start || offset > t.End) continue;
            if (t.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier))
            {
                // Justo al final de un nombre y al principio de un signo: vale el nombre.
                if (offset == t.Start && i > 0 && tokens[i - 1].End == offset && tokens[i - 1].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
                    return Build(tokens, i - 1);
                continue;
            }
            return Build(tokens, i);
        }
        return null;
    }

    private static SqlName Build(List<SqlToken> tokens, int i)
    {
        var t = tokens[i];
        string? qualifier = null;
        if (i >= 2 && tokens[i - 1] is { Kind: SqlTokenKind.Symbol, Text: "." } dot && dot.Start == tokens[i - 2].End && dot.End == t.Start
            && tokens[i - 2].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
            qualifier = Stored(tokens[i - 2]);
        return new SqlName(Stored(t), qualifier, t.Start, t.Length);
    }

    private static string Stored(SqlToken t) => t.Kind == SqlTokenKind.QuotedIdentifier
        ? t.Text[1..^1].Replace("\"\"", "\"")
        : t.Text.ToUpperInvariant();

    /// <summary>
    /// La tabla de un alias («t» en «FROM TURNOS t» o «JOIN TURNOS AS t»), mirando la sentencia donde está el nombre;
    /// null si no es un alias. <paramref name="alias"/> como lo devuelve <see cref="NameAt"/>.
    /// </summary>
    public static string? TableOfAlias(string text, int offset, string alias)
    {
        var statement = SqlScriptSplitter.AtCaret(text, offset)?.Text ?? text;
        var tokens = SqlLexer.Tokenize(statement).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (!tokens[i].IsWord("FROM") && !tokens[i].IsWord("JOIN")) continue;
            var table = tokens[i + 1];
            if (table.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)) continue;
            var j = i + 2;
            if (j < tokens.Count && tokens[j].IsWord("AS")) j++;
            if (j < tokens.Count && tokens[j].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier && Stored(tokens[j]) == alias
                && !SqlCompletion.Keywords.Contains(tokens[j].Text, StringComparer.OrdinalIgnoreCase))
                return Stored(table);
        }
        return null;
    }
}
