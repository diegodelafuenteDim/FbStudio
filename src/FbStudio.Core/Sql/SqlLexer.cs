namespace FbStudio.Core.Sql;

public enum SqlTokenKind
{
    Word,
    QuotedIdentifier,
    String,
    Number,
    Symbol,

    /// <summary>Solo con <see cref="SqlLexer.Tokenize(string, bool)"/> y comments = true: «-- …» o «/* … */».</summary>
    Comment,
}

public readonly record struct SqlToken(SqlTokenKind Kind, string Text, int Start, int Length)
{
    public int End => Start + Length;

    public bool IsWord(string word) => Kind == SqlTokenKind.Word && Text.Equals(word, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lexer mínimo de SQL de Firebird: ignora comentarios (-- y /* */), respeta strings con '' escapado
/// e identificadores entre comillas dobles. Alcanza para clasificar y partir sentencias.
/// </summary>
public static class SqlLexer
{
    public static IEnumerable<SqlToken> Tokenize(string sql) => Tokenize(sql, comments: false);

    /// <param name="comments">Devolver también los comentarios (el formateador los necesita; el resto los ignora).</param>
    public static IEnumerable<SqlToken> Tokenize(string sql, bool comments)
    {
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            var start = i;
            if (c == '-' && Peek(sql, i + 1) == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                if (comments) yield return new SqlToken(SqlTokenKind.Comment, sql[start..i].TrimEnd('\r'), start, i - start);
                continue;
            }
            if (c == '/' && Peek(sql, i + 1) == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? sql.Length : close + 2;
                if (comments) yield return new SqlToken(SqlTokenKind.Comment, sql[start..i], start, i - start);
                continue;
            }

            if ((c is 'q' or 'Q') && Peek(sql, i + 1) == '\'' && i + 2 < sql.Length
                && (i == 0 || !(char.IsLetterOrDigit(sql[i - 1]) || sql[i - 1] is '_' or '$')))
            {
                var close = sql[i + 2] switch { '(' => ')', '[' => ']', '{' => '}', '<' => '>', var d => d };
                var end = sql.IndexOf(close + "'", i + 3, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                yield return new SqlToken(SqlTokenKind.String, sql[start..i], start, i - start);
                continue;
            }
            if (c is '\'' or '"')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == c)
                    {
                        if (Peek(sql, i + 1) == c) { i += 2; continue; } // '' o "" escapado
                        i++;
                        break;
                    }
                    i++;
                }
                yield return new SqlToken(c == '\'' ? SqlTokenKind.String : SqlTokenKind.QuotedIdentifier, sql[start..i], start, i - start);
                continue;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                yield return new SqlToken(SqlTokenKind.Word, sql[start..i], start, i - start);
                continue;
            }
            if (char.IsDigit(c))
            {
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                yield return new SqlToken(SqlTokenKind.Number, sql[start..i], start, i - start);
                continue;
            }
            i++;
            yield return new SqlToken(SqlTokenKind.Symbol, c.ToString(), start, 1);
        }
    }

    private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';
}
