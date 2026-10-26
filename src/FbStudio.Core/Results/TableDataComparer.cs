using System.Text;
using FbStudio.Core.Export;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Results;

public enum RowChange
{
    /// <summary>Está en el origen y no en el destino: el script la inserta.</summary>
    OnlyInSource,

    /// <summary>Está en las dos con otros valores: el script actualiza las columnas distintas.</summary>
    Different,

    /// <summary>Está solo en el destino: el script la borra solo si se pide.</summary>
    OnlyInTarget,
}

/// <param name="Key">Los valores de la clave primaria.</param>
/// <param name="Source">La fila del origen (null si no está).</param>
/// <param name="Target">La fila del destino (null si no está).</param>
/// <param name="ChangedColumns">Las columnas distintas (posiciones en <see cref="TableDataComparison.Columns"/>).</param>
public sealed record RowDifference(RowChange Change, IReadOnlyList<object?> Key, IReadOnlyList<object?>? Source, IReadOnlyList<object?>? Target,
    IReadOnlyList<int> ChangedColumns);

/// <summary>El resultado de comparar los datos de una tabla en dos bases (las columnas que están en las dos).</summary>
/// <param name="KeyColumns">Las posiciones de la clave primaria en <paramref name="Columns"/>.</param>
public sealed record TableDataComparison(
    string Table,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<int> KeyColumns,
    int SourceRows,
    int TargetRows,
    int Equal,
    IReadOnlyList<RowDifference> Differences)
{
    public int OnlyInSource => Differences.Count(d => d.Change == RowChange.OnlyInSource);
    public int Different => Differences.Count(d => d.Change == RowChange.Different);
    public int OnlyInTarget => Differences.Count(d => d.Change == RowChange.OnlyInTarget);
}

/// <summary>
/// Comparar los datos de una tabla entre dos bases: por la clave primaria, qué filas
/// faltan en el destino, cuáles tienen otros valores y cuáles sobran, y el script para que el destino quede igual al
/// origen (INSERT, UPDATE de las columnas distintas y, si se pide, DELETE), sin COMMIT: se revisa y se confirma a mano.
/// Pensado para tablas chicas (configuración, tipos, nomencladores): las filas se comparan en memoria.
/// </summary>
public static class TableDataComparer
{
    public static TableDataComparison Compare(string table, IReadOnlyList<ColumnInfo> columns, IReadOnlyList<int> keyColumns,
        IEnumerable<IReadOnlyList<object?>> sourceRows, IEnumerable<IReadOnlyList<object?>> targetRows)
    {
        if (keyColumns.Count == 0) throw new ArgumentException("Hace falta la clave primaria para comparar.", nameof(keyColumns));
        var keyComparer = new KeyComparer();
        IReadOnlyList<object?> KeyOf(IReadOnlyList<object?> row) => keyColumns.Select(k => row[k]).ToArray();
        var target = new Dictionary<IReadOnlyList<object?>, IReadOnlyList<object?>>(keyComparer);
        var targetOrder = new List<IReadOnlyList<object?>>();
        foreach (var row in targetRows)
        {
            var key = KeyOf(row);
            if (target.TryAdd(key, row)) targetOrder.Add(key);
        }
        var differences = new List<RowDifference>();
        var sourceCount = 0;
        var equal = 0;
        var seen = new HashSet<IReadOnlyList<object?>>(keyComparer);
        foreach (var row in sourceRows)
        {
            sourceCount++;
            var key = KeyOf(row);
            seen.Add(key);
            if (!target.TryGetValue(key, out var other))
            {
                differences.Add(new RowDifference(RowChange.OnlyInSource, key, row, null, []));
                continue;
            }
            var changed = Enumerable.Range(0, columns.Count).Where(i => !keyColumns.Contains(i) && !ValuesEqual(row[i], other[i])).ToList();
            if (changed.Count == 0) equal++;
            else differences.Add(new RowDifference(RowChange.Different, key, row, other, changed));
        }
        differences.AddRange(targetOrder.Where(k => !seen.Contains(k)).Select(k => new RowDifference(RowChange.OnlyInTarget, k, null, target[k], [])));
        return new TableDataComparison(table, columns, keyColumns, sourceCount, target.Count, equal, differences);
    }

    /// <summary>
    /// Igualdad de valores como la ve la base: NULL con NULL, binarios byte a byte, textos sin los espacios del final (un
    /// CHAR de otro largo en la otra base) y números por su valor aunque vengan de tipos distintos (INTEGER y BIGINT).
    /// </summary>
    public static bool ValuesEqual(object? a, object? b)
    {
        var aNull = a is null or DBNull;
        var bNull = b is null or DBNull;
        if (aNull || bNull) return aNull && bNull;
        return (a!, b!) switch
        {
            (byte[] x, byte[] y) => x.AsSpan().SequenceEqual(y),
            (string x, string y) => string.Equals(x.TrimEnd(), y.TrimEnd(), StringComparison.Ordinal),
            _ when IsNumber(a!) && IsNumber(b!) => ToDecimal(a!) is { } x && ToDecimal(b!) is { } y ? x == y : a!.Equals(b),
            _ => a!.Equals(b),
        };
    }

    private static bool IsNumber(object v) => v is sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double;

    private static decimal? ToDecimal(object v)
    {
        try
        {
            return Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>Lo que se muestra de la clave: «12» o «12 · 3».</summary>
    public static string KeyText(RowDifference difference) =>
        string.Join(" · ", difference.Key.Select(v => CellFormatter.Format(v, "")));

    /// <summary>«OBS: 'a' → 'b'» de cada columna distinta.</summary>
    public static IReadOnlyList<string> ChangeDetails(TableDataComparison comparison, RowDifference difference) =>
        difference.ChangedColumns.Select(i =>
        {
            var column = comparison.Columns[i];
            return $"{column.Name}: {Short(difference.Target![i], column)} → {Short(difference.Source![i], column)}";
        }).ToList();

    private static string Short(object? value, ColumnInfo column)
    {
        var text = SqlLiteral.Format(value, column.DataTypeName);
        return text.Length > 60 ? text[..57] + "…" : text;
    }

    /// <summary>
    /// El script para que el destino quede igual al origen: INSERT de las que faltan, UPDATE de las columnas distintas y,
    /// con <paramref name="deleteExtra"/>, DELETE de las que sobran (si no, solo un comentario con cuántas son). Sin COMMIT.
    /// </summary>
    public static string Script(TableDataComparison comparison, bool deleteExtra, string header)
    {
        var sb = new StringBuilder(header);
        var table = SqlIdentifier.Quote(comparison.Table);
        var columns = comparison.Columns;
        string Where(RowDifference d) => string.Join(" AND ", comparison.KeyColumns.Select((k, i) =>
            $"{SqlIdentifier.Quote(columns[k].Name)} = {SqlLiteral.Format(d.Key[i], columns[k].DataTypeName)}"));
        var names = string.Join(", ", columns.Select(c => SqlIdentifier.Quote(c.Name)));
        foreach (var d in comparison.Differences)
        {
            switch (d.Change)
            {
                case RowChange.OnlyInSource:
                    sb.Append($"INSERT INTO {table} ({names}) VALUES ({string.Join(", ", columns.Select((c, i) => SqlLiteral.Format(d.Source![i], c.DataTypeName)))});\n");
                    break;
                case RowChange.Different:
                    sb.Append($"UPDATE {table} SET {string.Join(", ", d.ChangedColumns.Select(i => $"{SqlIdentifier.Quote(columns[i].Name)} = {SqlLiteral.Format(d.Source![i], columns[i].DataTypeName)}"))} WHERE {Where(d)};\n");
                    break;
                case RowChange.OnlyInTarget when deleteExtra:
                    sb.Append($"DELETE FROM {table} WHERE {Where(d)};\n");
                    break;
            }
        }
        if (!deleteExtra && comparison.OnlyInTarget > 0)
            sb.Append(string.Format(Resources.CoreStrings.DataCompareExtraNotDeleted, comparison.OnlyInTarget)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Compara claves con <see cref="ValuesEqual"/> (y un hash coherente: textos sin espacios finales, números por valor).</summary>
    private sealed class KeyComparer : IEqualityComparer<IReadOnlyList<object?>>
    {
        public bool Equals(IReadOnlyList<object?>? x, IReadOnlyList<object?>? y) =>
            x is not null && y is not null && x.Count == y.Count && x.Zip(y).All(p => ValuesEqual(p.First, p.Second));

        public int GetHashCode(IReadOnlyList<object?> key)
        {
            var hash = new HashCode();
            foreach (var v in key)
            {
                hash.Add(v switch
                {
                    null or DBNull => 0,
                    string s => s.TrimEnd().GetHashCode(StringComparison.Ordinal),
                    byte[] b => b.Length,
                    _ when IsNumber(v) => ToDecimal(v)?.GetHashCode() ?? v.GetHashCode(),
                    _ => v.GetHashCode(),
                });
            }
            return hash.ToHashCode();
        }
    }
}
