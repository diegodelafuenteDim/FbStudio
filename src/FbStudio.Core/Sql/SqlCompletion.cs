namespace FbStudio.Core.Sql;

public enum CompletionKind
{
    Keyword,
    Table,
    Column,
    Procedure,
    Function,
    Package,
    Generator,
    Exception,
}

public sealed record CompletionItem(string Text, CompletionKind Kind, string? Detail = null);

/// <summary>La ayuda de parámetros: qué procedimiento o función se está llamando y en qué parámetro está el cursor.</summary>
public sealed record SignatureHelp(RoutineSignature Signature, int Argument, int OpenParen);

/// <summary>
/// Autocompletado (F-33): palabras clave, tablas/vistas, procedimientos, funciones, paquetes, generadores y
/// excepciones; columnas después de «alias.» y también sin alias, de lo que está en el FROM. Las columnas de una
/// subconsulta (<c>FROM (SELECT …) x</c>) y de un WITH salen de su lista de SELECT; las de un procedimiento
/// seleccionable, de sus parámetros de salida. Mira solo la sentencia donde está el cursor.
/// </summary>
public static class SqlCompletion
{
    public static readonly string[] Keywords =
    [
        "SELECT", "FIRST", "SKIP", "DISTINCT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE",
        "CONTAINING", "STARTING", "WITH", "IS", "NULL", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "ON", "GROUP",
        "BY", "HAVING", "ORDER", "ASC", "DESC", "ROWS", "TO", "UNION", "ALL", "AS", "CASE", "WHEN", "THEN", "ELSE", "END",
        "CAST", "COALESCE", "COUNT", "SUM", "MIN", "MAX", "AVG", "EXTRACT", "CURRENT_DATE", "CURRENT_TIMESTAMP",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE", "USING", "MATCHED", "RETURNING", "PLAN",
        "EXECUTE", "PROCEDURE", "BLOCK", "COMMIT", "ROLLBACK",
        "RECURSIVE", "GEN_ID", "NEXT", "VALUE", "FOR", "IIF", "NULLIF", "DATEADD", "DATEDIFF", "SUBSTRING", "TRIM", "UPPER",
        "LOWER", "LIST", "EXCEPTION", "FUNCTION", "PACKAGE", "SEQUENCE", "GENERATOR",
    ];

    private static readonly HashSet<string> KeywordSet = new(Keywords, StringComparer.OrdinalIgnoreCase);

    /// <summary>Palabras después de las que va un objeto (tabla, vista o procedimiento).</summary>
    private static readonly HashSet<string> ObjectContext = new(StringComparer.OrdinalIgnoreCase) { "FROM", "JOIN", "UPDATE", "INTO", "TABLE" };

    public static (int Start, IReadOnlyList<CompletionItem> Items) Suggest(string text, int caret, MetadataCatalog catalog)
    {
        var start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var prefix = text[start..caret];
        bool Matches(string s) => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        var statement = StatementAround(text, caret);

        if (start > 0 && text[start - 1] == '.')
        {
            var qStart = start - 1;
            while (qStart > 0 && IsWordChar(text[qStart - 1])) qStart--;
            var qualifier = text[qStart..(start - 1)];
            // Un alias o una tabla (también una subconsulta, un WITH o un procedimiento)…
            if ((Sources(statement.Text, catalog).GetValueOrDefault(qualifier) ?? CatalogSource(qualifier, catalog)) is { } source)
                return (start, source.Columns.Where(Matches).Select(c => new CompletionItem(c, CompletionKind.Column, source.Detail)).ToList());
            // …o un paquete: lo que tiene adentro.
            if (catalog.PackageMembers.TryGetValue(qualifier, out var members))
                return (start, members.Where(Matches).Select(m => new CompletionItem(m,
                    catalog.Signatures.TryGetValue($"{qualifier}.{m}", out var s) && s.IsFunction ? CompletionKind.Function : CompletionKind.Procedure,
                    qualifier.ToUpperInvariant())).ToList());
            return (start, []);
        }

        var previous = PreviousWord(text, start);
        var before = text[..start].TrimEnd();
        IEnumerable<CompletionItem> Of(IEnumerable<string> names, CompletionKind kind, string? detail = null) =>
            names.Where(Matches).Select(n => new CompletionItem(n, kind, detail));
        IEnumerable<CompletionItem> procedures = Of(catalog.Procedures, CompletionKind.Procedure);
        IEnumerable<CompletionItem> functions = Of(catalog.Functions, CompletionKind.Function);
        IEnumerable<CompletionItem> packages = Of(catalog.Packages, CompletionKind.Package);
        var keywords = Of(Keywords, CompletionKind.Keyword);

        // Lugares donde va un solo tipo de objeto.
        if (before.EndsWith("GEN_ID(", StringComparison.OrdinalIgnoreCase) || previous is "SEQUENCE" or "GENERATOR"
            || (previous == "FOR" && PreviousWord(text, before.Length - 3) == "VALUE"))
            return (start, Of(catalog.Generators, CompletionKind.Generator).ToList());
        if (previous == "EXCEPTION") return (start, Of(catalog.Exceptions, CompletionKind.Exception).ToList());
        if (previous == "PROCEDURE") return (start, procedures.Concat(packages).ToList());
        if (previous == "FUNCTION") return (start, functions.Concat(packages).ToList());
        if (previous == "PACKAGE") return (start, packages.ToList());

        var ctes = Ctes(statement.Text, catalog).Keys;
        var objects = Of(ctes, CompletionKind.Table, "WITH")
            .Concat(catalog.Tables.Where(Matches).Select(t => new CompletionItem(t, CompletionKind.Table, $"{catalog.Columns[t].Count} columnas")));

        IEnumerable<CompletionItem> items;
        if (previous is not null && ObjectContext.Contains(previous))
        {
            // Después de FROM, JOIN, UPDATE, INTO… lo que se busca es un objeto (procedimientos, solo para leer de ellos).
            items = previous is "UPDATE" or "INTO" or "TABLE"
                ? objects.Concat(keywords)
                : objects.Concat(procedures).Concat(packages).Concat(keywords);
        }
        else
        {
            // En el resto, primero las columnas de lo que está en el FROM (sin necesidad de escribir el alias).
            IEnumerable<CompletionItem> columns = prefix.Length == 0
                ? []
                : Sources(statement.Text, catalog).Values.DistinctBy(s => s.Detail)
                    .SelectMany(s => s.Columns.Where(Matches).Select(c => new CompletionItem(c, CompletionKind.Column, s.Detail)));
            items = columns.Concat(keywords).Concat(functions).Concat(packages).Concat(objects).Concat(procedures);
        }
        return (start, items.DistinctBy(i => i.Text, StringComparer.OrdinalIgnoreCase).Take(500).ToList());
    }

    /// <summary>
    /// Si el cursor está dentro de los paréntesis de un procedimiento o una función que el catálogo conoce, cuál es y en
    /// qué parámetro está (contando las comas de ese nivel). Dentro de una llamada que no conoce (COALESCE) mira la de afuera.
    /// </summary>
    public static SignatureHelp? SignatureAt(string text, int caret, MetadataCatalog catalog)
    {
        if (catalog.Signatures.Count == 0) return null;
        var tokens = SqlLexer.Tokenize(text[..Math.Clamp(caret, 0, text.Length)]).ToList();
        var open = new Stack<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "(" }) open.Push(i);
            else if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: ")" } && open.Count > 0) open.Pop();
            else if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: ";" }) open.Clear();
        }
        foreach (var paren in open)
        {
            if (paren == 0 || tokens[paren - 1].Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)) continue;
            var name = Unquote(tokens[paren - 1].Text);
            if (paren >= 3 && tokens[paren - 2] is { Kind: SqlTokenKind.Symbol, Text: "." } && tokens[paren - 3].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
                name = Unquote(tokens[paren - 3].Text) + "." + name;
            if (!catalog.Signatures.TryGetValue(name, out var signature)) continue;
            var depth = 0;
            var argument = 0;
            for (var i = paren + 1; i < tokens.Count; i++)
            {
                if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "(" }) depth++;
                else if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: ")" }) depth--;
                else if (depth == 0 && tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "," }) argument++;
            }
            return new SignatureHelp(signature, argument, tokens[paren].Start);
        }
        return null;
    }

    /// <summary>Tablas, vistas y procedimientos que aparecen después de FROM o JOIN.</summary>
    public static IReadOnlyList<string> TablesInText(string text)
    {
        var tokens = SqlLexer.Tokenize(text).ToList();
        var tables = new List<string>();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if ((tokens[i].IsWord("FROM") || tokens[i].IsWord("JOIN")) && tokens[i + 1].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
            {
                var name = tokens[i + 1].Text.Trim('"');
                if (!tables.Contains(name, StringComparer.OrdinalIgnoreCase)) tables.Add(name);
            }
        }
        return tables;
    }

    // ── Lo que hay en el FROM ──

    /// <summary>Columnas de un alias o un nombre, y qué se muestra al lado (la tabla, el WITH o la subconsulta).</summary>
    private sealed record Source(string Detail, IReadOnlyList<string> Columns);

    private static Source? CatalogSource(string name, MetadataCatalog catalog) =>
        catalog.Columns.TryGetValue(name, out var columns) ? new Source(name.ToUpperInvariant(), columns) : null;

    /// <summary>La sentencia donde está el cursor (para no mezclar los alias de otra).</summary>
    private static StatementSpan StatementAround(string text, int caret) =>
        SqlScriptSplitter.AtCaret(text, caret) ?? new StatementSpan(0, text.Length, text);

    /// <summary>
    /// Todo lo que aparece después de FROM y JOIN (en cualquier nivel), por su alias y por su nombre: tablas y
    /// procedimientos del catálogo, los WITH y las subconsultas con nombre.
    /// </summary>
    private static Dictionary<string, Source> Sources(string sql, MetadataCatalog catalog, int depth = 0)
    {
        var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        if (depth > 5) return sources;
        var ctes = Ctes(sql, catalog, depth);
        var tokens = SqlLexer.Tokenize(sql).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (!tokens[i].IsWord("FROM") && !tokens[i].IsWord("JOIN")) continue;
            var next = tokens[i + 1];
            Source? source = null;
            var j = i + 2;
            if (next is { Kind: SqlTokenKind.Symbol, Text: "(" })
            {
                // FROM (SELECT …) x
                var close = MatchingParen(tokens, i + 1);
                var inner = sql[(next.End)..(close < tokens.Count ? tokens[close].Start : sql.Length)];
                j = close + 1;
                var alias = AliasAt(tokens, j);
                if (alias is null) continue;
                sources[alias] = new Source(alias.ToUpperInvariant(), SelectColumns(inner, catalog, depth + 1));
                continue;
            }
            if (next.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier) || KeywordSet.Contains(next.Text) && next.Kind == SqlTokenKind.Word) continue;
            var name = Unquote(next.Text);
            // Procedimiento seleccionable: FROM PROC(arg1, arg2) alias
            if (j < tokens.Count && tokens[j] is { Kind: SqlTokenKind.Symbol, Text: "(" }) j = MatchingParen(tokens, j) + 1;
            if (ctes.TryGetValue(name, out var cte)) source = cte;
            else if (CatalogSource(name, catalog) is { } table) source = table;
            if (source is null) continue;
            sources.TryAdd(name, source);
            if (AliasAt(tokens, j) is { } a) sources[a] = source;
        }
        return sources;
    }

    /// <summary>«AS x» o «x» (si no es una palabra clave) en la posición <paramref name="j"/>.</summary>
    private static string? AliasAt(List<SqlToken> tokens, int j)
    {
        if (j < tokens.Count && tokens[j].IsWord("AS")) j++;
        if (j >= tokens.Count) return null;
        return tokens[j] switch
        {
            { Kind: SqlTokenKind.QuotedIdentifier } q => Unquote(q.Text),
            { Kind: SqlTokenKind.Word } w when !KeywordSet.Contains(w.Text) => w.Text,
            _ => null,
        };
    }

    /// <summary>Los WITH de la sentencia: «WITH x (A, B) AS (…)» o «WITH x AS (SELECT a, b …)».</summary>
    private static Dictionary<string, Source> Ctes(string sql, MetadataCatalog catalog, int depth = 0)
    {
        var ctes = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        var tokens = SqlLexer.Tokenize(sql).ToList();
        if (tokens.Count == 0 || !tokens[0].IsWord("WITH") || depth > 5) return ctes;
        var i = 1;
        if (i < tokens.Count && tokens[i].IsWord("RECURSIVE")) i++;
        while (i < tokens.Count && tokens[i].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)
        {
            var name = Unquote(tokens[i].Text);
            i++;
            List<string>? explicitColumns = null;
            if (i < tokens.Count && tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "(" })
            {
                var close = MatchingParen(tokens, i);
                explicitColumns = tokens.Skip(i + 1).Take(Math.Max(0, close - i - 1))
                    .Where(t => t.Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier).Select(t => Unquote(t.Text)).ToList();
                i = close + 1;
            }
            if (i >= tokens.Count || !tokens[i].IsWord("AS") || i + 1 >= tokens.Count || tokens[i + 1] is not { Kind: SqlTokenKind.Symbol, Text: "(" }) break;
            var open = i + 1;
            var end = MatchingParen(tokens, open);
            var inner = sql[tokens[open].End..(end < tokens.Count ? tokens[end].Start : sql.Length)];
            // El primero se agrega antes de leer su cuerpo: un WITH RECURSIVE se nombra a sí mismo.
            ctes[name] = new Source(name.ToUpperInvariant(), explicitColumns ?? []);
            if (explicitColumns is null) ctes[name] = new Source(name.ToUpperInvariant(), SelectColumns(inner, catalog, depth + 1));
            i = end + 1;
            if (i < tokens.Count && tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "," }) i++;
            else break;
        }
        return ctes;
    }

    /// <summary>
    /// Los nombres de las columnas de un SELECT: el alias («AS x» o «expr x»), el nombre de la columna («t.ID» → ID) o,
    /// con «*» y «t.*», las columnas de lo que está en su FROM. Una expresión sin alias no tiene nombre y se saltea.
    /// </summary>
    private static IReadOnlyList<string> SelectColumns(string select, MetadataCatalog catalog, int depth)
    {
        var tokens = SqlLexer.Tokenize(select).ToList();
        var start = tokens.FindIndex(t => t.IsWord("SELECT"));
        if (start < 0) return [];
        var i = start + 1;
        while (i < tokens.Count)
        {
            if ((tokens[i].IsWord("FIRST") || tokens[i].IsWord("SKIP")) && i + 1 < tokens.Count)
                i = tokens[i + 1] is { Kind: SqlTokenKind.Symbol, Text: "(" } ? MatchingParen(tokens, i + 1) + 1 : i + 2;
            else if (tokens[i].IsWord("DISTINCT") || tokens[i].IsWord("ALL")) i++;
            else break;
        }
        // Los elementos hasta el FROM de este nivel.
        var items = new List<List<SqlToken>> { new() };
        var level = 0;
        for (; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t is { Kind: SqlTokenKind.Symbol, Text: "(" }) level++;
            else if (t is { Kind: SqlTokenKind.Symbol, Text: ")" }) level--;
            else if (level == 0 && t.IsWord("FROM")) break;
            else if (level == 0 && t is { Kind: SqlTokenKind.Symbol, Text: "," })
            {
                items.Add([]);
                continue;
            }
            items[^1].Add(t);
        }
        var columns = new List<string>();
        Dictionary<string, Source>? sources = null;
        foreach (var item in items.Where(i => i.Count > 0))
        {
            var last = item[^1];
            if (last is { Kind: SqlTokenKind.Symbol, Text: "*" })
            {
                sources ??= Sources(select, catalog, depth);
                if (item.Count >= 3 && item[^2] is { Text: "." })
                {
                    if (sources.TryGetValue(Unquote(item[^3].Text), out var qualified)) columns.AddRange(qualified.Columns);
                }
                else columns.AddRange(sources.Values.DistinctBy(s => s.Detail).SelectMany(s => s.Columns));
                continue;
            }
            if (last.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier) || last.Kind == SqlTokenKind.Word && KeywordSet.Contains(last.Text) && last.Text.ToUpperInvariant() is not "VALUE") continue;
            var name = Unquote(last.Text);
            if (item.Count == 1 || (item.Count == 3 && item[1] is { Kind: SqlTokenKind.Symbol, Text: "." }) || item[^2].IsWord("AS")
                || item[^2].Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier or SqlTokenKind.Number or SqlTokenKind.String
                || item[^2] is { Kind: SqlTokenKind.Symbol, Text: ")" })
                columns.Add(name);
        }
        return columns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int MatchingParen(List<SqlToken> tokens, int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: "(" }) depth++;
            else if (tokens[i] is { Kind: SqlTokenKind.Symbol, Text: ")" } && --depth == 0) return i;
        }
        return tokens.Count; // sin cerrar (se está escribiendo): hasta el final
    }

    private static string Unquote(string name) => name.Length >= 2 && name[0] == '"' && name[^1] == '"' ? name[1..^1].Replace("\"\"", "\"") : name;

    /// <summary>La palabra anterior al cursor (hacia atrás, sin recorrer todo el texto en cada tecla).</summary>
    private static string? PreviousWord(string text, int start)
    {
        var i = start - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        var end = i + 1;
        while (i >= 0 && IsWordChar(text[i])) i--;
        return end > i + 1 ? text[(i + 1)..end].ToUpperInvariant() : null;
    }

    /// <summary>Busca "FROM|JOIN tabla [AS] alias" en el texto y devuelve la tabla del alias.</summary>
    public static string? ResolveAlias(string text, string alias)
    {
        var tokens = SqlLexer.Tokenize(text).ToList();
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (!tokens[i].IsWord("FROM") && !tokens[i].IsWord("JOIN")) continue;
            var tableToken = tokens[i + 1];
            if (tableToken.Kind is not (SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier)) continue;
            var table = tableToken.Text.Trim('"');
            var j = i + 2;
            // Procedimiento seleccionable: FROM PROC(arg1, arg2) alias
            if (j < tokens.Count && tokens[j] is { Kind: SqlTokenKind.Symbol, Text: "(" })
            {
                var depth = 0;
                for (; j < tokens.Count; j++)
                {
                    if (tokens[j] is { Kind: SqlTokenKind.Symbol, Text: "(" }) depth++;
                    else if (tokens[j] is { Kind: SqlTokenKind.Symbol, Text: ")" } && --depth == 0) { j++; break; }
                }
            }
            if (j < tokens.Count && tokens[j].IsWord("AS")) j++;
            var a = j < tokens.Count && tokens[j].Kind == SqlTokenKind.Word && !KeywordSet.Contains(tokens[j].Text) ? tokens[j].Text : null;
            if (string.Equals(a, alias, StringComparison.OrdinalIgnoreCase) || string.Equals(table, alias, StringComparison.OrdinalIgnoreCase))
                return table;
        }
        return null;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
