using System.Text;

namespace FbStudio.Core.Sql;

/// <param name="UppercaseKeywords">Palabras clave y funciones de Firebird en mayúsculas; si no, quedan como se escribieron.</param>
/// <param name="IndentSize">Espacios por nivel.</param>
/// <param name="MaxLineLength">Una lista (columnas del SELECT, ORDER BY, SET…) que entra en este ancho queda en un renglón.</param>
public sealed record SqlFormatOptions(bool UppercaseKeywords = true, int IndentSize = 2, int MaxLineLength = 100);

/// <summary>
/// Formatea las consultas de un texto (F-32): cada cláusula (SELECT, FROM, JOIN, WHERE, GROUP BY…) en su renglón, las
/// condiciones del WHERE una por renglón, las listas largas un elemento por renglón, un CASE largo con un WHEN por
/// renglón y las subconsultas sangradas. Toca SELECT, WITH, INSERT, UPDATE, DELETE, MERGE y UPDATE OR INSERT, y los
/// bloques PSQL (procedimientos, funciones, triggers y EXECUTE BLOCK, ver <see cref="PsqlFormatter"/>); el resto del DDL,
/// SET TERM y lo que hay entre sentencias queda tal cual. Los textos, los identificadores entre comillas y los
/// comentarios no se cambian.
/// </summary>
public static class SqlFormatter
{
    // Palabras que se pasan a mayúsculas: las de SQL de Firebird 3 y sus funciones.
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "ASCENDING", "AT", "AVG", "BEGIN", "BETWEEN", "BIGINT", "BLOB", "BLOCK",
        "BOOLEAN", "BOTH", "BY", "CASE", "CAST", "CHAR", "CHARACTER", "COLLATE", "COMMIT", "CONTAINING", "COUNT", "CROSS",
        "CURRENT", "CURRENT_CONNECTION", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "CURRENT_TRANSACTION", "CURRENT_USER", "DATE", "DAY", "DECIMAL", "DEFAULT", "DELETE", "DESC", "DESCENDING", "DISTINCT",
        "DOUBLE", "ELSE", "END", "ESCAPE", "EXECUTE", "EXISTS", "EXTRACT", "FALSE", "FETCH", "FIRST", "FLOAT", "FOR", "FROM",
        "FULL", "GROUP", "HAVING", "HOUR", "IN", "INNER", "INSERT", "INTEGER", "INTO", "IS", "JOIN", "LAST", "LEADING", "LEFT",
        "LIKE", "LOCK", "MATCHED", "MATCHING", "MAX", "MERGE", "MIN", "MINUTE", "MONTH", "NATURAL", "NEXT", "NOT", "NULL",
        "NULLS", "NUMERIC", "OF", "OFFSET", "ON", "ONLY", "OR", "ORDER", "OUTER", "OVER", "PARTITION", "PLAN", "PRECISION",
        "PROCEDURE", "RECURSIVE", "RETURNING", "RIGHT", "ROW", "ROWS", "SECOND", "SELECT", "SET", "SIMILAR", "SINGULAR", "SKIP",
        "SMALLINT", "SOME", "STARTING", "SUM", "THEN", "TIME", "TIMESTAMP", "TO", "TRAILING", "TRUE", "UNION", "UNKNOWN",
        "UPDATE", "USING", "VALUES", "VARCHAR", "WEEK", "WEEKDAY", "WHEN", "WHERE", "WITH", "YEAR", "YEARDAY",
        "ABS", "ASCII_CHAR", "ASCII_VAL", "BIT_LENGTH", "CEIL", "CEILING", "CHAR_LENGTH", "CHARACTER_LENGTH", "COALESCE",
        "DATEADD", "DATEDIFF", "DECODE", "DENSE_RANK", "FLOOR", "GEN_ID", "GEN_UUID", "HASH", "IIF", "LAG", "LEAD", "LIST",
        "LOWER", "LPAD", "MAXVALUE", "MINVALUE", "MOD", "NULLIF", "OCTET_LENGTH", "OVERLAY", "PLACING", "POSITION", "POWER",
        "RAND", "RANK", "REPLACE", "REVERSE", "ROUND", "ROW_NUMBER", "RPAD", "SIGN", "SQRT", "SUBSTRING", "TRIM", "TRUNC",
        "UPPER", "UUID_TO_CHAR", "CHAR_TO_UUID", "RDB$GET_CONTEXT", "RDB$SET_CONTEXT", "RDB$DB_KEY",
        // PSQL
        "SUSPEND", "EXIT", "EXCEPTION", "LEAVE", "CONTINUE", "DECLARE", "VARIABLE", "CURSOR", "OPEN", "CLOSE", "RETURN",
        "POST_EVENT", "STATEMENT", "IF", "WHILE", "DO", "AUTONOMOUS", "TRANSACTION", "NEW", "OLD", "INSERTING", "UPDATING",
        "DELETING", "ROW_COUNT", "SQLCODE", "GDSCODE", "SQLSTATE", "RETURNING_VALUES",
    };

    /// <summary>Una palabra clave o función de Firebird (la que se pasa a mayúsculas).</summary>
    internal static bool IsKeyword(string word) => Keywords.Contains(word);

    // Después de estas palabras va un espacio antes de «(»; después de un nombre de función, no.
    private static readonly HashSet<string> SpaceBeforeParen = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IN", "EXISTS", "SINGULAR", "VALUES", "AS", "ON", "USING", "FROM", "JOIN", "SELECT", "WHERE", "THEN",
        "ELSE", "WHEN", "INTO", "MATCHING", "RETURNING", "BY", "SET", "ALL", "ANY", "SOME", "UNION", "HAVING", "IS", "BETWEEN",
        "LIKE", "CONTAINING", "STARTING", "CASE", "PLAN", "WITH", "DISTINCT", "OVER",
    };

    private static readonly HashSet<string> JoinWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "NATURAL", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "JOIN",
    };

    private static readonly HashSet<string> TwoCharOperators = ["<>", "!=", "^=", "~=", "<=", ">=", "!<", "!>", "||"];

    /// <summary>Formatea las sentencias de consulta del texto y deja el resto como estaba.</summary>
    public static string Format(string sql, SqlFormatOptions? options = null)
    {
        options ??= new SqlFormatOptions();
        var result = new StringBuilder(sql);
        foreach (var span in SqlScriptSplitter.Split(sql).Reverse())
        {
            var formatted = StatementClassifier.Classify(span.Text).Kind switch
            {
                StatementKind.Select or StatementKind.Dml => FormatStatement(span.Text, options),
                StatementKind.Ddl or StatementKind.Execute => PsqlFormatter.Format(span.Text, options),
                _ => null,
            };
            if (formatted is not null) result.Remove(span.Start, span.Length).Insert(span.Start, formatted);
        }
        return result.ToString();
    }

    /// <summary>Una sola sentencia (sin el terminador).</summary>
    public static string FormatStatement(string statement, SqlFormatOptions? options = null)
    {
        options ??= new SqlFormatOptions();
        var nodes = Parse(Tokens(statement, options));
        return new Writer(options).Statement(nodes, 0).TrimEnd();
    }

    // ── Tokens ──

    /// <param name="SpaceBefore">En el original había espacio o renglón antes (decide «f(x)» o «T (A, B)»).</param>
    private sealed record Tok(SqlTokenKind Kind, string Text, bool SpaceBefore)
    {
        public bool IsWord(string w) => Kind == SqlTokenKind.Word && Text.Equals(w, StringComparison.OrdinalIgnoreCase);
        public bool IsSymbol(string s) => Kind == SqlTokenKind.Symbol && Text == s;
        public bool IsLineComment => Kind == SqlTokenKind.Comment && Text.StartsWith("--", StringComparison.Ordinal);
    }

    private static List<Tok> Tokens(string sql, SqlFormatOptions options)
    {
        var raw = SqlLexer.Tokenize(sql, comments: true).ToList();
        var tokens = new List<Tok>(raw.Count);
        var previousEnd = 0;
        for (var i = 0; i < raw.Count; i++)
        {
            var t = raw[i];
            var space = t.Start > previousEnd;
            // Operadores de dos signos: el lexer los da de a uno («<» «>»); se juntan si estaban pegados.
            if (t.Kind == SqlTokenKind.Symbol && i + 1 < raw.Count && raw[i + 1] is { Kind: SqlTokenKind.Symbol } n && n.Start == t.End
                && TwoCharOperators.Contains(t.Text + n.Text))
            {
                tokens.Add(new Tok(SqlTokenKind.Symbol, t.Text + n.Text, space));
                previousEnd = n.End;
                i++;
                continue;
            }
            // Signo de un número o de una columna («= -1», «(-X)», «THEN -1»): queda pegado a lo que sigue.
            if (t.Kind == SqlTokenKind.Symbol && t.Text is "-" or "+" && i + 1 < raw.Count && raw[i + 1] is { Kind: SqlTokenKind.Number or SqlTokenKind.Word } next
                && next.Start == t.End && IsOperandStart(tokens.LastOrDefault()))
            {
                tokens.Add(new Tok(next.Kind, t.Text + Case(next, options), space));
                previousEnd = next.End;
                i++;
                continue;
            }
            tokens.Add(new Tok(t.Kind, Case(t, options), space));
            previousEnd = t.End;
        }
        return tokens;
    }

    private static string Case(SqlToken t, SqlFormatOptions options) =>
        t.Kind == SqlTokenKind.Word && options.UppercaseKeywords && Keywords.Contains(t.Text) ? t.Text.ToUpperInvariant() : t.Text;

    /// <summary>Después de esto, un «-» es el signo de lo que sigue y no una resta.</summary>
    private static bool IsOperandStart(Tok? previous) => previous switch
    {
        null => true,
        { Kind: SqlTokenKind.Symbol } s => s.Text is not ")",
        { Kind: SqlTokenKind.Word } w => Keywords.Contains(w.Text) && w.Text.ToUpperInvariant() is not ("NULL" or "TRUE" or "FALSE" or "UNKNOWN"
            or "CURRENT_DATE" or "CURRENT_TIME" or "CURRENT_TIMESTAMP" or "CURRENT_USER" or "CURRENT_ROLE" or "CURRENT_CONNECTION"
            or "CURRENT_TRANSACTION" or "END"),
        _ => false,
    };

    // ── Árbol de paréntesis ──

    private abstract record Node;

    private sealed record Leaf(Tok Token) : Node;

    /// <summary>«( … )»; es una subconsulta si adentro empieza con SELECT o WITH.</summary>
    private sealed record Group(List<Node> Children, bool SpaceBefore) : Node
    {
        public bool IsSubquery => Children.OfType<Leaf>().FirstOrDefault(l => l.Token.Kind != SqlTokenKind.Comment) is { } first
                                  && (first.Token.IsWord("SELECT") || first.Token.IsWord("WITH"));
    }

    private static List<Node> Parse(List<Tok> tokens)
    {
        var stack = new Stack<(List<Node> Nodes, bool Space)>();
        var current = new List<Node>();
        foreach (var t in tokens)
        {
            if (t.IsSymbol("("))
            {
                stack.Push((current, t.SpaceBefore));
                current = [];
            }
            else if (t.IsSymbol(")") && stack.Count > 0)
            {
                var (parent, space) = stack.Pop();
                parent.Add(new Group(current, space));
                current = parent;
            }
            else current.Add(new Leaf(t));
        }
        // Paréntesis sin cerrar: lo que quedó adentro va igual, sin perder nada.
        while (stack.Count > 0)
        {
            var (parent, space) = stack.Pop();
            parent.Add(new Leaf(new Tok(SqlTokenKind.Symbol, "(", space)));
            parent.AddRange(current);
            current = parent;
        }
        return current;
    }

    // ── Escritura ──

    private enum ClauseKind
    {
        /// <summary>SELECT, GROUP BY, ORDER BY, SET, RETURNING, FROM, WITH: lista separada por comas.</summary>
        List,

        /// <summary>WHERE y HAVING: una condición por renglón (AND / OR).</summary>
        Conditions,

        /// <summary>Lo demás (JOIN, INSERT INTO, VALUES, UNION…): todo en el renglón.</summary>
        Plain,
    }

    private sealed record Clause(List<Tok> Header, List<Node> Content, ClauseKind Kind);

    private sealed class Writer(SqlFormatOptions options)
    {
        private string Indent(int level) => new(' ', level * options.IndentSize);

        /// <summary>Una sentencia (o subconsulta) con sus cláusulas a partir de <paramref name="level"/>.</summary>
        public string Statement(List<Node> nodes, int level)
        {
            var sb = new StringBuilder();
            foreach (var clause in Clauses(nodes))
            {
                var indent = Indent(level);
                var header = string.Join(" ", clause.Header.Select(t => t.Text));
                if (header.Length == 0)
                {
                    // Comentarios antes de la primera cláusula: tal cual, en su renglón.
                    sb.Append(indent).Append(Item(clause.Content, level)).Append('\n');
                    continue;
                }
                if (clause.Content.Count == 0)
                {
                    sb.Append(indent).Append(header).Append('\n');
                    continue;
                }
                switch (clause.Kind)
                {
                    case ClauseKind.List:
                    {
                        var items = SplitTopLevel(clause.Content);
                        if (Inline(clause.Content) is { } inline && indent.Length + header.Length + 1 + inline.Length <= options.MaxLineLength)
                            sb.Append(indent).Append(header).Append(' ').Append(inline).Append('\n');
                        else if (items.Count == 1)
                            sb.Append(indent).Append(header).Append(' ').Append(Item(items[0], level)).Append('\n');
                        else
                        {
                            sb.Append(indent).Append(header).Append('\n');
                            for (var i = 0; i < items.Count; i++)
                                sb.Append(Indent(level + 1)).Append(Item(items[i], level + 1)).Append(i < items.Count - 1 ? "," : "").Append('\n');
                        }
                        break;
                    }
                    case ClauseKind.Conditions:
                    {
                        var parts = Conditions(clause.Content);
                        sb.Append(indent).Append(header).Append(' ').Append(Item(parts[0].Nodes, level)).Append('\n');
                        foreach (var (op, conditionNodes) in parts.Skip(1))
                            sb.Append(Indent(level + 1)).Append(op).Append(' ').Append(Item(conditionNodes, level + 1)).Append('\n');
                        break;
                    }
                    default:
                        sb.Append(indent).Append(header).Append(' ').Append(Item(clause.Content, level)).Append('\n');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Parte la sentencia en cláusulas, solo en su propio nivel (lo de adentro de paréntesis no cuenta).</summary>
        private static List<Clause> Clauses(List<Node> nodes)
        {
            var clauses = new List<Clause>();
            var header = new List<Tok>();
            var content = new List<Node>();
            var kind = ClauseKind.Plain;
            var caseDepth = 0;
            Tok? previous = null;
            Tok? beforePrevious = null;
            var isMerge = nodes.OfType<Leaf>().FirstOrDefault(l => l.Token.Kind != SqlTokenKind.Comment)?.Token.IsWord("MERGE") == true;

            void Close()
            {
                if (header.Count > 0 || content.Count > 0) clauses.Add(new Clause(header, content, kind));
                header = [];
                content = [];
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not Leaf { Token: var t })
                {
                    content.Add(nodes[i]);
                    continue;
                }
                if (t.IsWord("CASE")) caseDepth++;
                else if (t.IsWord("END") && caseDepth > 0) caseDepth--;

                var first = nodes.Take(i).All(n => n is Leaf { Token.Kind: SqlTokenKind.Comment });
                if (t.Kind != SqlTokenKind.Word || caseDepth > 0 || !StartsClause(t, previous, beforePrevious, first, isMerge))
                {
                    content.Add(nodes[i]);
                    if (t.Kind != SqlTokenKind.Comment) (beforePrevious, previous) = (previous, t);
                    continue;
                }

                Close();
                // Lo que va con la palabra en el encabezado: «GROUP BY», «LEFT OUTER JOIN», «INSERT INTO», «SELECT FIRST 10 DISTINCT»…
                header.Add(t);
                var j = i + 1;
                bool Next(params string[] words) => j < nodes.Count && nodes[j] is Leaf { Token: var n } && words.Any(n.IsWord);
                void Take() => header.Add(((Leaf)nodes[j++]).Token);
                var word = t.Text.ToUpperInvariant();
                switch (word)
                {
                    case "GROUP" or "ORDER":
                        while (Next("BY")) Take();
                        break;
                    case "UNION":
                        while (Next("ALL", "DISTINCT")) Take();
                        break;
                    case "INSERT" or "MERGE":
                        while (Next("INTO")) Take();
                        break;
                    case "DELETE":
                        while (Next("FROM")) Take();
                        break;
                    case "UPDATE" when Next("OR"):
                        while (Next("OR", "INSERT", "INTO")) Take();
                        break;
                    case "WITH":
                        while (Next("RECURSIVE")) Take();
                        break;
                    case "WHEN":
                        while (Next("NOT", "MATCHED", "BY", "TARGET", "SOURCE", "THEN")) Take();
                        break;
                    case "FOR":
                        while (Next("UPDATE")) Take();
                        break;
                    case "SELECT":
                        // SELECT FIRST n SKIP m DISTINCT
                        while (j < nodes.Count)
                        {
                            if (Next("FIRST", "SKIP") && j + 1 < nodes.Count)
                            {
                                Take();
                                header.Add(nodes[j] is Leaf { Token: var count } ? count : new Tok(SqlTokenKind.Word, "(" + Inline(((Group)nodes[j]).Children) + ")", true));
                                j++;
                            }
                            else if (Next("DISTINCT", "ALL")) Take();
                            else break;
                        }
                        break;
                    default:
                        if (JoinWords.Contains(word))
                            while (j < nodes.Count && nodes[j] is Leaf { Token: { Kind: SqlTokenKind.Word } n } && JoinWords.Contains(n.Text)) Take();
                        break;
                }
                kind = word switch
                {
                    "SELECT" or "GROUP" or "ORDER" or "SET" or "RETURNING" or "FROM" or "WITH" or "INTO" => ClauseKind.List,
                    "WHERE" or "HAVING" => ClauseKind.Conditions,
                    _ => ClauseKind.Plain,
                };
                (beforePrevious, previous) = (header.Count > 1 ? header[^2] : previous, header[^1]);
                i = j - 1;
            }
            Close();
            return clauses;
        }

        /// <summary>¿Esta palabra empieza una cláusula (en el nivel de la sentencia)?</summary>
        private static bool StartsClause(Tok t, Tok? previous, Tok? beforePrevious, bool first, bool isMerge)
        {
            var word = t.Text.ToUpperInvariant();
            return word switch
            {
                "SELECT" or "WHERE" or "HAVING" or "UNION" or "VALUES" or "RETURNING" or "PLAN" or "MATCHING" or "SET" or "ROWS"
                    or "OFFSET" or "FETCH" or "GROUP" or "ORDER" or "DELETE" or "MERGE" => true,
                // SELECT … INTO :a, :b de PSQL (INSERT INTO y MERGE INTO ya se lo llevaron en el encabezado).
                "INTO" => previous is not null,
                // DELETE FROM y «IS [NOT] DISTINCT FROM» no cortan; FROM dentro de funciones va en paréntesis y no llega acá.
                "FROM" => !(previous is { } p && (p.IsWord("DELETE") || (p.IsWord("DISTINCT") && beforePrevious is { } b && (b.IsWord("IS") || b.IsWord("NOT"))))),
                "INSERT" => !(previous is { } p && p.IsWord("OR")),
                "UPDATE" => !(previous is { } p && (p.IsWord("FOR") || p.IsWord("OR"))),
                "WITH" => first,                         // al final es «WITH LOCK»
                "USING" or "WHEN" => isMerge,            // en un JOIN, USING va con la tabla; WHEN fuera de un CASE es de MERGE
                "FOR" => previous is not null,           // FOR UPDATE [WITH LOCK]
                // «LEFT OUTER JOIN»: empieza en la primera palabra; OUTER y JOIN siguen en el mismo renglón.
                _ => JoinWords.Contains(word) && !(previous is { Kind: SqlTokenKind.Word } p && JoinWords.Contains(p.Text) && !p.IsWord("JOIN")),
            };
        }

        /// <summary>Las condiciones de un WHERE: «a = 1», («AND», «b BETWEEN 1 AND 2»)… sin partir el AND de un BETWEEN.</summary>
        private static List<(string Op, List<Node> Nodes)> Conditions(List<Node> content)
        {
            var parts = new List<(string, List<Node>)>();
            var current = new List<Node>();
            var op = "";
            var between = 0;
            var caseDepth = 0;
            foreach (var node in content)
            {
                if (node is Leaf { Token: var t })
                {
                    if (t.IsWord("BETWEEN")) between++;
                    else if (t.IsWord("CASE")) caseDepth++;
                    else if (t.IsWord("END") && caseDepth > 0) caseDepth--;
                    else if ((t.IsWord("AND") || t.IsWord("OR")) && caseDepth == 0)
                    {
                        if (t.IsWord("AND") && between > 0) between--;
                        else if (current.Count > 0)
                        {
                            parts.Add((op, current));
                            (op, current) = (t.Text, []);
                            continue;
                        }
                    }
                }
                current.Add(node);
            }
            parts.Add((op, current));
            return parts;
        }

        /// <summary>Los elementos de una lista: separados por las comas de su nivel (no las de adentro de un CASE o de paréntesis).</summary>
        private static List<List<Node>> SplitTopLevel(List<Node> nodes)
        {
            var items = new List<List<Node>> { new() };
            var caseDepth = 0;
            foreach (var node in nodes)
            {
                if (node is Leaf { Token: var t })
                {
                    if (t.IsWord("CASE")) caseDepth++;
                    else if (t.IsWord("END") && caseDepth > 0) caseDepth--;
                    else if (caseDepth == 0 && t.IsSymbol(","))
                    {
                        items.Add([]);
                        continue;
                    }
                }
                items[^1].Add(node);
            }
            return items.Where(i => i.Count > 0).ToList();
        }

        /// <summary>Todo en un renglón; null si no se puede (una subconsulta o un comentario de línea adentro).</summary>
        private static string? Inline(List<Node> nodes)
        {
            var sb = new StringBuilder();
            Node? previous = null;
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case Group { IsSubquery: true }:
                        return null;
                    case Group g:
                        if (Inline(g.Children) is not { } inner) return null;
                        if (previous is not null && SpaceBeforeGroup(previous, g)) sb.Append(' ');
                        sb.Append('(').Append(inner).Append(')');
                        break;
                    case Leaf { Token: var t }:
                        if (t.IsLineComment) return null;
                        if (previous is not null && SpaceBetween(previous, t)) sb.Append(' ');
                        sb.Append(t.Text);
                        break;
                }
                previous = node;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Un elemento (una columna, una condición, lo que sigue a un JOIN) que empieza en un renglón de nivel
        /// <paramref name="level"/>: en el renglón, salvo las subconsultas (sangradas, con el «)» a ese nivel) y los
        /// comentarios de línea (lo que sigue va al renglón siguiente).
        /// </summary>
        private string Item(List<Node> nodes, int level)
        {
            if (Inline(nodes) is { } inline)
                return level * options.IndentSize + inline.Length > options.MaxLineLength && CaseItem(nodes, level) is { } split ? split : inline;
            var sb = new StringBuilder();
            Node? previous = null;
            var newLine = false;
            foreach (var node in nodes)
            {
                if (newLine) sb.Append(Indent(level + 1));
                switch (node)
                {
                    case Group { IsSubquery: true } g:
                        if (previous is not null && !newLine && SpaceBeforeGroup(previous, g)) sb.Append(' ');
                        sb.Append("(\n").Append(Statement(g.Children, level + 1)).Append(Indent(level)).Append(')');
                        break;
                    case Group g:
                        if (previous is not null && !newLine && SpaceBeforeGroup(previous, g)) sb.Append(' ');
                        sb.Append('(').Append(Item(g.Children, level)).Append(')');
                        break;
                    case Leaf { Token: var t }:
                        if (previous is not null && !newLine && SpaceBetween(previous, t)) sb.Append(' ');
                        sb.Append(t.Text);
                        break;
                }
                newLine = node is Leaf { Token.IsLineComment: true };
                if (newLine) sb.Append('\n');
                previous = node;
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Un elemento largo con un CASE en su nivel: lo de antes, «CASE», un WHEN (y el ELSE) por renglón un nivel
        /// adentro, y «END» con lo que sigue (p. ej. el alias). null si no tiene un CASE en su nivel.
        /// </summary>
        private string? CaseItem(List<Node> nodes, int level)
        {
            var start = nodes.FindIndex(n => n is Leaf { Token: var t } && t.IsWord("CASE"));
            if (start < 0) return null;
            var depth = 0;
            var end = -1;
            for (var i = start; i < nodes.Count && end < 0; i++)
            {
                if (nodes[i] is not Leaf { Token: var t }) continue;
                if (t.IsWord("CASE")) depth++;
                else if (t.IsWord("END") && --depth == 0) end = i;
            }
            if (end < 0) return null;
            // Lo de adentro: el operando de un CASE simple y después cada WHEN y el ELSE (sin partir los CASE de adentro).
            var parts = new List<List<Node>> { new() };
            depth = 0;
            for (var i = start + 1; i < end; i++)
            {
                if (nodes[i] is Leaf { Token: var t })
                {
                    if (t.IsWord("CASE")) depth++;
                    else if (t.IsWord("END")) depth--;
                    else if (depth == 0 && (t.IsWord("WHEN") || t.IsWord("ELSE")) && parts[^1].Count > 0) parts.Add([]);
                }
                parts[^1].Add(nodes[i]);
            }
            var operand = parts[0] is [Leaf { Token: var w }, ..] && (w.IsWord("WHEN") || w.IsWord("ELSE")) ? null : parts[0];
            var sb = new StringBuilder();
            var before = nodes[..start];
            if (before.Count > 0) sb.Append(Item(before, level)).Append(' ');
            sb.Append(((Leaf)nodes[start]).Token.Text);
            if (operand is not null) sb.Append(' ').Append(Item(operand, level));
            foreach (var part in operand is null ? parts : parts.Skip(1))
                sb.Append('\n').Append(Indent(level + 1)).Append(Item(part, level + 1));
            sb.Append('\n').Append(Indent(level)).Append(((Leaf)nodes[end]).Token.Text);
            var after = nodes[(end + 1)..];
            if (after.Count > 0) sb.Append(after[0] is Leaf { Token: var a } && (a.IsSymbol(",") || a.IsSymbol(")")) ? "" : " ").Append(Item(after, level));
            return sb.ToString();
        }

        private static bool SpaceBeforeGroup(Node previous, Group group) => previous switch
        {
            Leaf { Token: { Kind: SqlTokenKind.Word } w } => SpaceBeforeParen.Contains(w.Text) || group.SpaceBefore,
            Leaf { Token: { Kind: SqlTokenKind.QuotedIdentifier } } => group.SpaceBefore,
            Leaf { Token: var t } when t.IsSymbol(".") => false,
            _ => true,
        };

        private static bool SpaceBetween(Node previous, Tok next)
        {
            if (next.IsSymbol(",") || next.IsSymbol(".") || next.IsSymbol(";")) return false;
            if (previous is not Leaf { Token: var p }) return true;
            if (p.IsSymbol(".")) return false;
            // Parámetros «:nombre» pegados como se escribieron.
            return !(p.IsSymbol(":") && !next.SpaceBefore);
        }
    }
}
