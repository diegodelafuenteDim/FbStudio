using System.Collections.Immutable;
using System.Text;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Data;

public sealed record SqlParameterValue(string Name, object? Value);

/// <summary>Una sentencia con sus parámetros (@p0, @p1…): los valores nunca se pegan en el texto del SQL.</summary>
public sealed record SqlCommandText(string Sql, IReadOnlyList<SqlParameterValue> Parameters);

/// <summary>
/// Lectura de una tabla o vista para la pestaña «Datos» (F-3): de a páginas, con el filtro y el orden resueltos en el
/// servidor (WHERE / ORDER BY), no sobre lo que ya se trajo. Cada página va en su propia transacción corta de solo
/// lectura, así hojear una tabla no frena la OAT.
/// </summary>
/// <param name="Columns">Columnas de la tabla, en orden (de RDB$RELATION_FIELDS).</param>
public sealed record TableQuery(string Table, IReadOnlyList<DbColumn> Columns, bool IsView)
{
    /// <summary>Filtros de la fila «Filtro» de la grilla, por índice de columna.</summary>
    public ImmutableDictionary<int, ColumnFilter> Filters { get; init; } = ImmutableDictionary<int, ColumnFilter>.Empty;

    /// <summary>Condición SQL libre que escribe el usuario («WHERE adicional»).</summary>
    public string? ExtraWhere { get; init; }

    public int? SortColumn { get; init; }
    public SortDirection SortDirection { get; init; }

    /// <summary>Columnas de la clave primaria: ordenan por defecto (paginado estable) e identifican la fila al editar.</summary>
    public IReadOnlyList<int> KeyColumns => Columns.Select((c, i) => (c, i)).Where(x => x.c.IsPrimaryKey).Select(x => x.i).ToList();

    /// <param name="offset">Cuántas filas se trajeron ya.</param>
    /// <param name="after">
    /// La última fila traída (sus valores, en el orden de <see cref="Columns"/>). Si el orden lo permite, la página
    /// siguiente se pide «después de esa fila» (WHERE por la clave) en vez de «filas N a M»: con ROWS el servidor
    /// recorre y descarta todas las anteriores, y cuanto más se baja más tarda; así usa el índice y tarda lo mismo
    /// en cualquier página. Tampoco se saltean ni repiten filas si entretanto se borraron algunas.
    /// </param>
    public SqlCommandText Page(int offset, int count, IReadOnlyList<object?>? after = null)
    {
        var parameters = new List<SqlParameterValue>();
        var sql = new StringBuilder("SELECT ")
            .Append(string.Join(", ", Columns.Select(c => SqlIdentifier.Quote(c.Name))))
            .Append("\nFROM ").Append(SqlIdentifier.Quote(Table));
        var seek = after is not null && SeekColumns() is { } columns ? SeekCondition(columns, after, parameters) : null;
        AppendWhere(sql, parameters, seek);
        if (OrderBy() is { } order) sql.Append("\nORDER BY ").Append(order);
        if (seek is not null) sql.Append("\nROWS ").Append(count);
        else sql.Append("\nROWS ").Append(offset + 1).Append(" TO ").Append(offset + count);
        return new SqlCommandText(sql.ToString(), parameters);
    }

    /// <summary>
    /// Las columnas del orden, si se puede pedir «después de la última fila»: hace falta la clave primaria (única y sin
    /// NULL) y que la columna elegida para ordenar no admita NULL (un NULL no se compara con «mayor que»). Si no,
    /// null y se usa ROWS.
    /// </summary>
    public IReadOnlyList<(int Column, bool Descending)>? SeekColumns()
    {
        var keys = KeyColumns;
        if (keys.Count == 0) return null;
        var list = new List<(int Column, bool Descending)>();
        if (SortColumn is { } s && s < Columns.Count && SortDirection != SortDirection.None)
        {
            if (!Columns[s].NotNull && !keys.Contains(s)) return null;
            list.Add((s, SortDirection == SortDirection.Descending));
        }
        list.AddRange(keys.Where(k => k != SortColumn || SortDirection == SortDirection.None).Select(k => (k, false)));
        return list;
    }

    /// <summary>(a &gt; @s0) OR (a = @s1 AND b &gt; @s2) …, con «&lt;» en las columnas descendentes.</summary>
    private string SeekCondition(IReadOnlyList<(int Column, bool Descending)> order, IReadOnlyList<object?> after, List<SqlParameterValue> parameters)
    {
        string P(object? value)
        {
            var name = "@s" + parameters.Count;
            parameters.Add(new SqlParameterValue(name, value is DBNull ? null : value));
            return name;
        }
        var alternatives = new List<string>();
        for (var i = 0; i < order.Count; i++)
        {
            var parts = new List<string>();
            for (var j = 0; j < i; j++) parts.Add($"{SqlIdentifier.Quote(Columns[order[j].Column].Name)} = {P(after[order[j].Column])}");
            var (column, descending) = order[i];
            parts.Add($"{SqlIdentifier.Quote(Columns[column].Name)} {(descending ? "<" : ">")} {P(after[column])}");
            alternatives.Add("(" + string.Join(" AND ", parts) + ")");
        }
        return alternatives.Count == 1 ? alternatives[0] : "(" + string.Join(" OR ", alternatives) + ")";
    }

    /// <summary>
    /// Los valores de una columna con cuántas filas tiene cada uno, los más repetidos primero, con los demás filtros
    /// (no el de esa columna: es el que se está eligiendo). Las columnas con fecha y hora, por día. Recorre la tabla
    /// filtrada: se pide a mano.
    /// </summary>
    public SqlCommandText Distinct(int column, int limit)
    {
        var parameters = new List<SqlParameterValue>();
        var col = SqlIdentifier.Quote(Columns[column].Name);
        // Con fecha y hora, por día: por minuto casi no se repiten y la lista no sirve para elegir.
        var expression = Columns[column].Type == "TIMESTAMP" ? $"CAST({col} AS DATE)" : col;
        var sql = new StringBuilder($"SELECT FIRST {limit} {expression}, COUNT(*) FROM ").Append(SqlIdentifier.Quote(Table));
        (this with { Filters = Filters.Remove(column) }).AppendWhere(sql, parameters);
        sql.Append("\nGROUP BY 1\nORDER BY 2 DESC, 1");
        return new SqlCommandText(sql.ToString(), parameters);
    }

    public SqlCommandText Count()
    {
        var parameters = new List<SqlParameterValue>();
        var sql = new StringBuilder("SELECT COUNT(*) FROM ").Append(SqlIdentifier.Quote(Table));
        AppendWhere(sql, parameters);
        return new SqlCommandText(sql.ToString(), parameters);
    }

    /// <summary>
    /// El orden elegido y, para que las páginas no se pisen, la clave primaria. Sin clave: las tablas por RDB$DB_KEY
    /// (orden físico, estable mientras nadie inserte) y las vistas sin orden.
    /// </summary>
    private string? OrderBy()
    {
        var parts = new List<string>();
        var keys = KeyColumns;
        var sorted = SortColumn is { } s && s < Columns.Count && SortDirection != SortDirection.None;
        if (sorted) parts.Add(SqlIdentifier.Quote(Columns[SortColumn!.Value].Name) + (SortDirection == SortDirection.Descending ? " DESC" : ""));
        parts.AddRange(keys.Where(k => !sorted || k != SortColumn).Select(k => SqlIdentifier.Quote(Columns[k].Name)));
        if (keys.Count == 0 && !IsView) parts.Add("RDB$DB_KEY");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private void AppendWhere(StringBuilder sql, List<SqlParameterValue> parameters, string? seek = null)
    {
        var conditions = new List<string>();
        if (seek is not null) conditions.Add(seek);
        if (!string.IsNullOrWhiteSpace(ExtraWhere)) conditions.Add("(" + ExtraWhere.Trim().TrimEnd(';') + ")");
        foreach (var (column, filter) in Filters.OrderBy(f => f.Key))
            if (column < Columns.Count && filter.Condition is { } condition)
                conditions.Add(FilterSql.ToSql(condition, Columns[column], parameters));
        if (conditions.Count > 0) sql.Append("\nWHERE ").Append(string.Join("\n  AND ", conditions));
    }
}

/// <summary>
/// Los filtros de la grilla como SQL, con la misma lógica que en el cliente: texto que contiene (CONTAINING, sin
/// distinguir mayúsculas) o no, comodines * y %, comparaciones, entre (BETWEEN), listas (IN / OR), fechas («ese día»,
/// rangos, «este mes»), horas, BOOLEAN y null / !null. A diferencia del cliente, los acentos se comparan según la
/// collation de la columna.
/// </summary>
public static class FilterSql
{
    public static string ToSql(FilterCondition condition, DbColumn column, List<SqlParameterValue> parameters)
    {
        var col = SqlIdentifier.Quote(column.Name);
        string P(object? value)
        {
            var name = "@f" + parameters.Count;
            parameters.Add(new SqlParameterValue(name, value));
            return name;
        }
        string Op(string op) => op == "" ? "=" : op;
        var asText = column.Type == "BOOLEAN" ? $"CAST({col} AS VARCHAR(5))" : col;
        var dateOnly = column.Type == "DATE";

        return condition switch
        {
            NullCondition { Negated: false } => $"{col} IS NULL",
            NullCondition => $"{col} IS NOT NULL",
            NumberCondition n => $"{col} {Op(n.Op)} {P(n.Value)}",
            NumberRangeCondition r => $"{col} BETWEEN {P(r.From)} AND {P(r.To)}",
            // Un día entero en una columna con hora: de 0:00 a 0:00 del siguiente («hasta el 5» incluye todo el 5).
            DateCondition { DayOnly: true, Op: "" or "=" } d when !dateOnly => $"({col} >= {P(d.Value.Date)} AND {col} < {P(d.Value.Date.AddDays(1))})",
            DateCondition { DayOnly: true, Op: "<>" } d when !dateOnly => $"({col} < {P(d.Value.Date)} OR {col} >= {P(d.Value.Date.AddDays(1))})",
            DateCondition { DayOnly: true, Op: "<=" } d when !dateOnly => $"{col} < {P(d.Value.Date.AddDays(1))}",
            DateCondition { DayOnly: true, Op: ">" } d when !dateOnly => $"{col} >= {P(d.Value.Date.AddDays(1))}",
            DateCondition d => $"{col} {Op(d.Op)} {P(dateOnly ? d.Value.Date : d.Value)}",
            DateRangeCondition r => $"({col} >= {P(dateOnly ? r.From.Date : r.From)} AND {col} {(r.ToExclusive ? "<" : "<=")} {P(dateOnly ? r.To.Date : r.To)})",
            TimeCondition t => $"{col} {Op(t.Op)} {P(t.Value)}",
            TimeRangeCondition r => $"{col} BETWEEN {P(r.From)} AND {P(r.To)}",
            TextCompareCondition t => $"UPPER({asText}) {Op(t.Op)} UPPER({P(t.Value)})",
            TextLikeCondition l => $"UPPER({asText}) LIKE UPPER({P(LikePattern(l.Pattern))}) ESCAPE '\\'",
            TextContainsCondition c => $"{asText} CONTAINING {P(c.Value)}",
            TextNotContainsCondition n => $"({col} IS NULL OR NOT ({asText} CONTAINING {P(n.Value)}))",
            BoolCondition b => $"{col} = {(b.Value ? "TRUE" : "FALSE")}",
            AnyCondition { Items: var items } when items.All(i => i is NumberCondition { Op: "" or "=" }) =>
                $"{col} IN ({string.Join(", ", items.Select(i => P(((NumberCondition)i).Value)))})",
            AnyCondition a => "(" + string.Join(" OR ", a.Items.Select(i => ToSql(i, column, parameters))) + ")",
            _ => "1 = 1",
        };
    }

    /// <summary>* y % son comodines; el _ y la barra se escapan para que valgan literalmente (como en la grilla).</summary>
    private static string LikePattern(string pattern)
    {
        var sb = new StringBuilder();
        foreach (var ch in pattern)
            sb.Append(ch switch
            {
                '*' or '%' => "%",
                '_' => "\\_",
                '\\' => "\\\\",
                _ => ch.ToString(),
            });
        return sb.ToString();
    }
}
