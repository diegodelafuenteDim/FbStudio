namespace FbStudio.Core.Sql;

/// <summary>
/// Los procedimientos y funciones que usa una consulta y que pueden escribir en la base: un SELECT de un procedimiento
/// seleccionable o de una función PSQL corre lo que tengan adentro (INSERT, UPDATE…). Sirve para avisar antes de
/// correrlo dentro de una transacción de escritura abierta en producción (D-73).
/// </summary>
public static class SqlRoutines
{
    /// <summary>
    /// Por la sintaxis: «FROM X(…)» o «JOIN X(…)» (un procedimiento con parámetros) y «FROM PKG.X» o «PKG.X(…)» (de un
    /// paquete: en Firebird 3 un nombre con punto en esos lugares no puede ser otra cosa). Con el catálogo: «FROM X» si X
    /// es un procedimiento y «X(…)» si X es una función PSQL (las UDF no escriben). Sin repetir, en el orden del SQL.
    /// </summary>
    public static IReadOnlyList<string> Used(string sql, MetadataCatalog catalog)
    {
        var tokens = SqlLexer.Tokenize(sql).ToList();
        var procedures = new HashSet<string>(catalog.Procedures, StringComparer.OrdinalIgnoreCase);
        var functions = new HashSet<string>(catalog.StoredFunctions, StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        void Add(string name)
        {
            if (!found.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);
        }
        bool Is(int i, string symbol) => i < tokens.Count && tokens[i].Kind == SqlTokenKind.Symbol && tokens[i].Text == symbol;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!IsName(tokens[i])) continue;
            var name = Name(tokens[i]);
            var source = i > 0 && (tokens[i - 1].IsWord("FROM") || tokens[i - 1].IsWord("JOIN"));
            if (Is(i + 1, ".") && i + 2 < tokens.Count && IsName(tokens[i + 2]))
            {
                if (source || Is(i + 3, "(")) Add($"{name}.{Name(tokens[i + 2])}");
                i += 2; // ALIAS.COLUMNA: no es una rutina
                continue;
            }
            var call = Is(i + 1, "(");
            if (source && (call || procedures.Contains(name))) Add(name);
            else if (call && functions.Contains(name)) Add(name);
        }
        return found;
    }

    private static bool IsName(SqlToken t) => t.Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;

    private static string Name(SqlToken t) =>
        t.Kind == SqlTokenKind.QuotedIdentifier ? t.Text[1..^1].Replace("\"\"", "\"") : t.Text.ToUpperInvariant();
}
