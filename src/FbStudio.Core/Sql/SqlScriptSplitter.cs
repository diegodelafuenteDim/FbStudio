namespace FbStudio.Core.Sql;

public readonly record struct StatementSpan(int Start, int Length, string Text);

/// <summary>
/// Parte un texto en sentencias por el terminador de nivel superior (';' por defecto), fuera de strings, comentarios
/// y paréntesis. Entiende:
/// <list type="bullet">
/// <item>SET TERM (scripts de isql): cambia el terminador y no se devuelve como sentencia;</item>
/// <item>bloques PSQL sin SET TERM (CREATE PROCEDURE/TRIGGER/FUNCTION/PACKAGE, EXECUTE BLOCK): los ';' de las
/// declaraciones y de adentro del BEGIN … END no cortan la sentencia.</item>
/// </list>
/// </summary>
public static class SqlScriptSplitter
{
    private static readonly HashSet<string> PsqlObjects = new(StringComparer.OrdinalIgnoreCase) { "PROCEDURE", "TRIGGER", "FUNCTION", "PACKAGE" };

    public static IReadOnlyList<StatementSpan> Split(string sql)
    {
        var result = new List<StatementSpan>();
        var tokens = SqlLexer.Tokenize(sql).ToList();
        var terminator = ";";
        var start = 0;
        var statement = new StatementState();

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            // SET TERM <nuevo> <actual>
            if (statement.TokenCount == 0 && t.IsWord("SET") && i + 1 < tokens.Count && tokens[i + 1].IsWord("TERM"))
            {
                var from = tokens[i + 1].End;
                var end = sql.IndexOf(terminator, from, StringComparison.Ordinal);
                var newTerminator = (end < 0 ? sql[from..] : sql[from..end]).Trim();
                if (newTerminator.Length > 0)
                {
                    start = end < 0 ? sql.Length : end + terminator.Length;
                    terminator = newTerminator;
                    while (i + 1 < tokens.Count && tokens[i + 1].Start < start) i++;
                    continue;
                }
            }

            if (t.Kind == SqlTokenKind.Symbol && statement.ParenDepth == 0 && IsTerminatorAt(sql, t.Start, terminator)
                && !(terminator == ";" && statement.InsidePsqlBody)) // con SET TERM manda el terminador explícito
            {
                if (statement.TokenCount > 0) Add(result, sql, start, t.Start);
                start = t.Start + terminator.Length;
                while (i + 1 < tokens.Count && tokens[i + 1].Start < start) i++;
                statement = new StatementState();
                continue;
            }

            statement.Feed(t);
        }
        if (statement.TokenCount > 0) Add(result, sql, start, sql.Length);
        return result;
    }

    /// <summary>La sentencia que contiene al cursor (o la anterior más cercana si el cursor quedó entre sentencias).</summary>
    public static StatementSpan? AtCaret(string sql, int caret)
    {
        var all = Split(sql);
        if (all.Count == 0) return null;
        foreach (var s in all)
            if (caret >= s.Start && caret <= s.Start + s.Length + 1) return s;
        return all.LastOrDefault(s => s.Start <= caret) is { Length: > 0 } before ? before : all[0];
    }

    private static bool IsTerminatorAt(string sql, int index, string terminator) =>
        string.CompareOrdinal(sql, index, terminator, 0, terminator.Length) == 0;

    private static void Add(List<StatementSpan> result, string sql, int from, int to)
    {
        var text = sql[from..to];
        var trimmedStart = from + (text.Length - text.TrimStart().Length);
        var trimmed = text.Trim();
        if (trimmed.Length > 0) result.Add(new StatementSpan(trimmedStart, trimmed.Length, trimmed));
    }

    /// <summary>Estado de la sentencia en curso: paréntesis y, si es PSQL, en qué parte del cuerpo está.</summary>
    private sealed class StatementState
    {
        private readonly List<string> _leading = new(4);
        private bool _psql;
        private bool _inBody;      // pasó el AS del encabezado
        private bool _blockSeen;   // ya abrió el BEGIN principal
        private int _blockDepth;   // BEGIN/CASE … END

        public int TokenCount { get; private set; }
        public int ParenDepth { get; private set; }

        /// <summary>En las declaraciones o dentro del bloque principal: los ';' son parte de la sentencia.</summary>
        public bool InsidePsqlBody => _inBody && (!_blockSeen || _blockDepth > 0);

        public void Feed(SqlToken t)
        {
            TokenCount++;
            if (t.Kind == SqlTokenKind.Symbol)
            {
                if (t.Text == "(") ParenDepth++;
                else if (t.Text == ")") ParenDepth = Math.Max(0, ParenDepth - 1);
                return;
            }
            if (t.Kind != SqlTokenKind.Word) return;

            if (_leading.Count < 4)
            {
                _leading.Add(t.Text.ToUpperInvariant());
                _psql = _psql || IsPsqlHeader(_leading);
            }
            if (!_psql || ParenDepth > 0) return;

            if (!_inBody)
            {
                if (t.IsWord("AS")) _inBody = true;
                return;
            }
            if (t.IsWord("BEGIN") || t.IsWord("CASE"))
            {
                if (t.IsWord("BEGIN")) _blockSeen = true;
                _blockDepth++;
            }
            else if (t.IsWord("END"))
            {
                _blockDepth = Math.Max(0, _blockDepth - 1);
            }
        }

        private static bool IsPsqlHeader(List<string> words)
        {
            if (words is ["EXECUTE", "BLOCK", ..]) return true;
            var i = 0;
            while (i < words.Count && words[i] is "CREATE" or "OR" or "ALTER" or "RECREATE") i++;
            return i > 0 && i < words.Count && PsqlObjects.Contains(words[i]);
        }
    }
}
