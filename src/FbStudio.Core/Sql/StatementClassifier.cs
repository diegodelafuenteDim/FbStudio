namespace FbStudio.Core.Sql;

public enum StatementKind
{
    Empty,
    Select,
    Dml,
    Ddl,
    Execute,
    Transaction,
    Other,
}

/// <param name="Kind">Tipo de sentencia según su primera palabra clave.</param>
/// <param name="Keyword">Primera palabra clave (o dos, p. ej. "EXECUTE BLOCK", "UPDATE OR").</param>
/// <param name="AffectsAllRows">UPDATE o DELETE sin WHERE en el nivel principal.</param>
/// <param name="ChangesSequences">
/// Usa NEXT VALUE FOR o GEN_ID con un paso distinto de 0: mueve un generador, y eso no lo deshace la transacción (ni la
/// de solo lectura).
/// </param>
public sealed record StatementInfo(StatementKind Kind, string Keyword, bool AffectsAllRows, bool ChangesSequences = false)
{
    public bool Modifies => Kind is StatementKind.Dml or StatementKind.Ddl or StatementKind.Execute;
}

public static class StatementClassifier
{
    private static readonly HashSet<string> Ddl = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "DROP", "RECREATE", "COMMENT", "GRANT", "REVOKE", "DECLARE",
    };

    public static StatementInfo Classify(string sql)
    {
        var tokens = SqlLexer.Tokenize(sql).ToList();
        if (tokens.Count == 0) return new StatementInfo(StatementKind.Empty, "", false);

        var first = tokens[0].Text.ToUpperInvariant();
        var second = tokens.Count > 1 ? tokens[1].Text.ToUpperInvariant() : "";

        var info = first switch
        {
            // En Firebird un WITH (CTE) siempre encabeza un SELECT.
            "SELECT" or "WITH" => new StatementInfo(StatementKind.Select, first, false),
            "INSERT" => new StatementInfo(StatementKind.Dml, first, false),
            "MERGE" => new StatementInfo(StatementKind.Dml, first, MergeDeletesAllMatched(tokens)),
            "UPDATE" when second == "OR" => new StatementInfo(StatementKind.Dml, "UPDATE OR INSERT", false),
            "UPDATE" or "DELETE" => new StatementInfo(StatementKind.Dml, first, !HasTopLevelWhere(tokens)),
            "EXECUTE" => new StatementInfo(StatementKind.Execute, $"EXECUTE {second}".Trim(), second == "BLOCK" && BlockHasUnfilteredDml(tokens)),
            "COMMIT" or "ROLLBACK" or "SAVEPOINT" or "RELEASE" => new StatementInfo(StatementKind.Transaction, first, false),
            "SET" when second == "TRANSACTION" => new StatementInfo(StatementKind.Transaction, "SET TRANSACTION", false),
            "SET" when second is "GENERATOR" or "STATISTICS" => new StatementInfo(StatementKind.Ddl, $"SET {second}", false),
            _ when Ddl.Contains(first) => new StatementInfo(StatementKind.Ddl, first, false),
            _ => new StatementInfo(StatementKind.Other, first, false),
        };
        return ChangesSequences(tokens) ? info with { ChangesSequences = true } : info;
    }

    /// <summary>«WHEN MATCHED THEN DELETE» sin «AND …»: borra todo lo que el ON hace coincidir (con ON 1 = 1, todo).</summary>
    private static bool MergeDeletesAllMatched(List<SqlToken> tokens)
    {
        for (var i = 0; i + 3 < tokens.Count; i++)
            if (tokens[i].IsWord("WHEN") && tokens[i + 1].IsWord("MATCHED") && tokens[i + 2].IsWord("THEN") && tokens[i + 3].IsWord("DELETE"))
                return true;
        return false;
    }

    /// <summary>Un UPDATE o DELETE dentro del bloque que no tiene WHERE (hasta su «;»).</summary>
    private static bool BlockHasUnfilteredDml(List<SqlToken> tokens)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            if (!(tokens[i].IsWord("UPDATE") || tokens[i].IsWord("DELETE"))) continue;
            var previous = tokens[i - 1];
            var startsStatement = previous.Kind == SqlTokenKind.Symbol && previous.Text == ";"
                                  || previous.IsWord("BEGIN") || previous.IsWord("DO") || previous.IsWord("THEN") || previous.IsWord("ELSE");
            if (!startsStatement || (tokens[i].IsWord("UPDATE") && i + 1 < tokens.Count && tokens[i + 1].IsWord("OR"))) continue;
            var depth = 0;
            var where = false;
            for (var j = i + 1; j < tokens.Count; j++)
            {
                var t = tokens[j];
                if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
                else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
                else if (t.Kind == SqlTokenKind.Symbol && t.Text == ";" && depth <= 0) break;
                else if (depth == 0 && t.IsWord("WHERE")) where = true;
            }
            if (!where) return true;
        }
        return false;
    }

    /// <summary>NEXT VALUE FOR, o GEN_ID(generador, paso) con un paso que no es 0.</summary>
    private static bool ChangesSequences(List<SqlToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i + 2 < tokens.Count && tokens[i].IsWord("NEXT") && tokens[i + 1].IsWord("VALUE") && tokens[i + 2].IsWord("FOR")) return true;
            if (!tokens[i].IsWord("GEN_ID") || i + 1 >= tokens.Count || tokens[i + 1].Text != "(") continue;
            // El paso: lo que va después de la primera coma y antes del paréntesis que cierra.
            var depth = 0;
            var step = new List<SqlToken>();
            var afterComma = false;
            for (var j = i + 1; j < tokens.Count; j++)
            {
                var t = tokens[j];
                if (t.Text == "(") depth++;
                else if (t.Text == ")" && --depth == 0) break;
                else if (depth == 1 && t.Text == "," && !afterComma) afterComma = true;
                else if (afterComma) step.Add(t);
            }
            if (!(step.Count == 1 && step[0].Kind == SqlTokenKind.Number && decimal.TryParse(step[0].Text,
                    System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) && n == 0))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Objeto que crea o modifica un CREATE / ALTER / RECREATE / CREATE OR ALTER: tipo ("PROCEDURE") y nombre tal como
    /// queda en el catálogo (sin comillas en mayúsculas; entre comillas, tal cual).
    /// </summary>
    public static (string ObjectType, string Name)? DdlTarget(string sql)
    {
        var tokens = SqlLexer.Tokenize(sql).Take(8).ToList();
        var i = 0;
        while (i < tokens.Count && (tokens[i].IsWord("CREATE") || tokens[i].IsWord("OR") || tokens[i].IsWord("ALTER") || tokens[i].IsWord("RECREATE"))) i++;
        if (i == 0 || i + 1 >= tokens.Count || tokens[i].Kind != SqlTokenKind.Word) return null;
        var type = tokens[i].Text.ToUpperInvariant();
        var name = tokens[i + 1];
        return name.Kind switch
        {
            SqlTokenKind.Word => (type, name.Text.ToUpperInvariant()),
            SqlTokenKind.QuotedIdentifier => (type, name.Text[1..^1].Replace("\"\"", "\"")),
            _ => null,
        };
    }

    /// <summary>Primera tabla del FROM principal de un SELECT (para proponer la tabla destino de un script INSERT).</summary>
    public static string? MainTable(string? sql)
    {
        if (sql is null) return null;
        var tokens = SqlLexer.Tokenize(sql).ToList();
        var depth = 0;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var t = tokens[i];
            if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
            else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
            else if (depth == 0 && t.IsWord("FROM"))
            {
                var next = tokens[i + 1];
                return next.Kind switch
                {
                    SqlTokenKind.Word => next.Text.ToUpperInvariant(),
                    SqlTokenKind.QuotedIdentifier => next.Text,
                    _ => null,
                };
            }
        }
        return null;
    }

    private static bool HasTopLevelWhere(List<SqlToken> tokens)
    {
        var depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
            else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
            else if (depth == 0 && t.IsWord("WHERE")) return true;
        }
        return false;
    }
}
