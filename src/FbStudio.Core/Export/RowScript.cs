using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Export;

/// <summary>A qué tabla van las filas copiadas como INSERT o UPDATE.</summary>
/// <param name="Table">La tabla, como la escribió el usuario o como está en RDB$ (sin comillas se pasa a mayúsculas).</param>
/// <param name="KeyColumns">La clave primaria (para el WHERE del UPDATE); null si no se conoce.</param>
/// <param name="SkipColumns">Columnas del resultado que no se escriben: calculadas, o que no son de la tabla.</param>
/// <param name="TableGuessed">No se reconoció la tabla: va un nombre para cambiar al pegar.</param>
public sealed record RowScriptTarget(string Table, IReadOnlyList<string>? KeyColumns, IReadOnlySet<string> SkipColumns, bool TableGuessed = false);

/// <summary>
/// Filas elegidas en la grilla como sentencias para pegar («Copiar como INSERT / UPDATE»): una por
/// fila, con los mismos literales que el script INSERT (fechas como TIMESTAMP '…', blobs como x'…'), sin COMMIT.
/// </summary>
public static class RowScript
{
    /// <summary>El nombre de la tabla para el SQL: sin comillas, en mayúsculas (como lo guarda Firebird).</summary>
    public static string QuoteTable(string table)
    {
        var t = table.Trim();
        return t.StartsWith('"') ? t : SqlIdentifier.Quote(t.ToUpperInvariant());
    }

    /// <param name="columns">Las columnas en el orden en que van; cada fila trae sus valores en ese orden.</param>
    public static string Insert(RowScriptTarget target, IReadOnlyList<ColumnInfo> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var written = Written(target, columns, keys: null);
        var prefix = $"INSERT INTO {QuoteTable(target.Table)} ({string.Join(", ", written.Select(i => SqlIdentifier.Quote(columns[i].Name)))}) VALUES (";
        return string.Concat(rows.Select(row =>
            prefix + string.Join(", ", written.Select(i => SqlLiteral.Format(row[i], columns[i].DataTypeName))) + ");" + Environment.NewLine));
    }

    /// <summary>UPDATE … SET (todo lo que no es clave) WHERE (la clave). Hace falta que el resultado traiga toda la clave.</summary>
    public static string Update(RowScriptTarget target, IReadOnlyList<ColumnInfo> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var keyNames = target.KeyColumns is { Count: > 0 } k ? k : throw new InvalidOperationException("Sin clave primaria no se arma el UPDATE.");
        var keys = keyNames.Select(name => columns.ToList().FindIndex(c => c.Name == name)).ToList();
        if (keys.Contains(-1)) throw new InvalidOperationException("El resultado no trae toda la clave primaria.");
        var set = Written(target, columns, keys);
        if (set.Count == 0) throw new InvalidOperationException("No hay columnas para actualizar.");
        var table = QuoteTable(target.Table);
        return string.Concat(rows.Select(row =>
            $"UPDATE {table} SET {string.Join(", ", set.Select(i => $"{SqlIdentifier.Quote(columns[i].Name)} = {SqlLiteral.Format(row[i], columns[i].DataTypeName)}"))}"
            + $" WHERE {string.Join(" AND ", keys.Select(i => SqlIdentifier.Quote(columns[i].Name) + (CellFormatter.IsNull(row[i]) ? " IS NULL" : " = " + SqlLiteral.Format(row[i], columns[i].DataTypeName))))};"
            + Environment.NewLine));
    }

    /// <summary>Las columnas que se escriben: sin las salteadas, sin repetidas y (en el UPDATE) sin la clave.</summary>
    private static List<int> Written(RowScriptTarget target, IReadOnlyList<ColumnInfo> columns, IReadOnlyList<int>? keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<int>();
        for (var i = 0; i < columns.Count; i++)
        {
            var name = columns[i].Name;
            if (target.SkipColumns.Contains(name) || keys?.Contains(i) == true || !seen.Add(name)) continue;
            list.Add(i);
        }
        return list;
    }
}
