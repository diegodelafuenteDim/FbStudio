namespace FbStudio.Core.Sql;

/// <summary>Comillas dobles solo cuando hacen falta: minúsculas, espacios, símbolos o palabras reservadas (Firebird 3 a 5).</summary>
public static class SqlIdentifier
{
    // Palabras reservadas de Firebird 3.0 (las que no pueden usarse como identificador sin comillas).
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "ADD", "ADMIN", "ALL", "ALTER", "AND", "ANY", "AS", "AT", "AVG", "BEGIN", "BETWEEN", "BIGINT", "BIT_LENGTH", "BLOB",
        "BOOLEAN", "BOTH", "BY", "CASE", "CAST", "CHAR", "CHAR_LENGTH", "CHARACTER", "CHARACTER_LENGTH", "CHECK", "CLOSE",
        "COLLATE", "COLUMN", "COMMIT", "CONNECT", "CONSTRAINT", "CORR", "COUNT", "COVAR_POP", "COVAR_SAMP", "CREATE", "CROSS",
        "CURRENT", "CURRENT_CONNECTION", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "CURRENT_TRANSACTION", "CURRENT_USER", "CURSOR", "DATE", "DAY", "DEC", "DECIMAL", "DECLARE", "DEFAULT", "DELETE",
        "DELETING", "DETERMINISTIC", "DISCONNECT", "DISTINCT", "DOUBLE", "DROP", "ELSE", "END", "ESCAPE", "EXECUTE", "EXISTS",
        "EXTERNAL", "EXTRACT", "FALSE", "FETCH", "FILTER", "FLOAT", "FOR", "FOREIGN", "FROM", "FULL", "FUNCTION", "GDSCODE",
        "GLOBAL", "GRANT", "GROUP", "HAVING", "HOUR", "IN", "INDEX", "INNER", "INSENSITIVE", "INSERT", "INSERTING", "INT",
        "INTEGER", "INTO", "IS", "JOIN", "LEADING", "LEFT", "LIKE", "LONG", "LOWER", "MAX", "MERGE", "MIN", "MINUTE", "MONTH",
        "NATIONAL", "NATURAL", "NCHAR", "NO", "NOT", "NULL", "NUMERIC", "OCTET_LENGTH", "OF", "OFFSET", "ON", "ONLY", "OPEN",
        "OR", "ORDER", "OUTER", "OVER", "PARAMETER", "PLAN", "POSITION", "POST_EVENT", "PRECISION", "PRIMARY", "PROCEDURE",
        "RDB$DB_KEY", "RDB$RECORD_VERSION", "REAL", "RECORD_VERSION", "RECREATE", "RECURSIVE", "REFERENCES", "REGR_AVGX",
        "REGR_AVGY", "REGR_COUNT", "REGR_INTERCEPT", "REGR_R2", "REGR_SLOPE", "REGR_SXX", "REGR_SXY", "REGR_SYY", "RELEASE",
        "RETURN", "RETURNING_VALUES", "RETURNS", "REVOKE", "RIGHT", "ROLLBACK", "ROW", "ROW_COUNT", "ROWS", "SAVEPOINT",
        "SCROLL", "SECOND", "SELECT", "SENSITIVE", "SET", "SIMILAR", "SMALLINT", "SOME", "SQLCODE", "SQLSTATE", "START",
        "STDDEV_POP", "STDDEV_SAMP", "SUM", "TABLE", "THEN", "TIME", "TIMESTAMP", "TO", "TRAILING", "TRIGGER", "TRIM", "TRUE",
        "UNION", "UNIQUE", "UNKNOWN", "UPDATE", "UPDATING", "UPPER", "USER", "USING", "VALUE", "VALUES", "VAR_POP", "VAR_SAMP",
        "VARCHAR", "VARIABLE", "VARYING", "VIEW", "WHEN", "WHERE", "WHILE", "WITH", "YEAR",
        // Firebird 4 y 5
        "BINARY", "DECFLOAT", "INT128", "LATERAL", "LOCAL", "LOCALTIME", "LOCALTIMESTAMP", "PUBLICATION", "RESETTING",
        "TIMEZONE_HOUR", "TIMEZONE_MINUTE", "UNBOUNDED", "VARBINARY", "WINDOW", "WITHOUT",
    };

    public static bool IsReserved(string word) => Reserved.Contains(word.ToUpperInvariant());

    public static bool NeedsQuotes(string name) =>
        name.Length == 0
        || !(char.IsAsciiLetterUpper(name[0]))
        || !name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '_' or '$')
        || Reserved.Contains(name);

    public static string Quote(string name) => NeedsQuotes(name) ? "\"" + name.Replace("\"", "\"\"") + "\"" : name;

    /// <summary>Literal de texto SQL con las comillas simples duplicadas.</summary>
    public static string Literal(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>Para poner un nombre dentro de un comentario «-- …»: sin saltos de línea.</summary>
    public static string OneLine(string text) => text.ReplaceLineEndings(" ");
}
