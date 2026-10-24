using System.Globalization;
using System.Text;
using FbStudio.Core.Export;
using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Data;

/// <summary>Un UPDATE, DELETE o INSERT listo para ejecutar, con su texto legible (con los valores) para el historial.</summary>
public sealed record ChangeStatement(string RowKey, bool IsDelete, SqlCommandText Command, string Readable)
{
    /// <summary>Fila nueva: el comando trae RETURNING con todas las columnas (la clave que puso el servidor, los valores por defecto).</summary>
    public bool IsInsert { get; init; }
}

/// <summary>
/// Cambios pendientes de la pestaña «Datos»: nada toca la base (ni bloquea registros) hasta «Aplicar», que ejecuta todo
/// en una transacción corta. Las filas se identifican por su clave primaria, así los cambios sobreviven a cambiar el
/// orden, el filtro o traer más páginas. Cada UPDATE exige que las columnas editadas sigan valiendo lo que se leyó
/// (control optimista): si otro las cambió entretanto, el UPDATE no encuentra la fila y se deshace todo.
/// </summary>
public sealed class TableChanges(IReadOnlyList<DbColumn> columns)
{
    private readonly Dictionary<string, RowChange> _rows = new(StringComparer.Ordinal);
    private readonly int[] _keys = columns.Select((c, i) => (c, i)).Where(x => x.c.IsPrimaryKey).Select(x => x.i).ToArray();

    // Filas nuevas: todavía no tienen clave (la puede poner un trigger o una identity), se reconocen por su arreglo.
    private readonly Dictionary<object?[], RowChange> _new = new(ReferenceEqualityComparer.Instance);
    private readonly List<object?[]> _newOrder = [];
    private int _newSequence;

    public sealed class RowChange(object?[] original)
    {
        /// <summary>Clave interna de una fila nueva («\u0002» + número); null en las filas leídas.</summary>
        public string? NewKey { get; init; }

        /// <summary>La fila tal como se leyó (para el control optimista y para deshacer).</summary>
        public object?[] Original { get; } = original;
        public Dictionary<int, object?> Values { get; } = [];
        public bool Deleted { get; set; }
    }

    public IReadOnlyList<DbColumn> Columns => columns;

    /// <summary>Sin clave primaria no hay cómo decir qué fila cambiar: la tabla se ve pero no se edita.</summary>
    public bool HasKey => _keys.Length > 0;

    public int Count => _rows.Count + _new.Count;
    public int UpdateCount => _rows.Values.Count(r => !r.Deleted && r.Values.Count > 0);
    public int DeleteCount => _rows.Values.Count(r => r.Deleted);
    public int InsertCount => _new.Count;

    /// <summary>Las filas nuevas, en el orden en que se agregaron.</summary>
    public IReadOnlyList<object?[]> NewRows => _newOrder;

    public bool IsNew(object?[] row) => _new.ContainsKey(row);

    /// <summary>Fila nueva vacía (todo NULL): lo que no se complete queda afuera del INSERT (valor por defecto, trigger, identity).</summary>
    public object?[] AddNew()
    {
        var row = Enumerable.Repeat<object?>(DBNull.Value, columns.Count).ToArray();
        _new[row] = new RowChange((object?[])row.Clone()) { NewKey = "\u0002" + ++_newSequence };
        _newOrder.Add(row);
        return row;
    }

    /// <summary>Saca una fila nueva (borrarla o deshacerla antes de aplicar). Devuelve false si no era nueva.</summary>
    public bool RemoveNew(object?[] row)
    {
        if (!_new.Remove(row)) return false;
        _newOrder.Remove(row);
        return true;
    }

    /// <summary>La fila nueva que corresponde a una clave interna (para devolverle lo que trajo el RETURNING).</summary>
    public object?[]? NewRowFor(string rowKey) => _new.FirstOrDefault(n => n.Value.NewKey == rowKey).Key;

    public string KeyOf(object?[] row) =>
        _new.TryGetValue(row, out var created) ? created.NewKey! :
        string.Join("\u001F", _keys.Select(k => row[k] switch
        {
            null or DBNull => "\u0000",
            string s => s.TrimEnd(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var v => v.ToString(),
        }));

    public bool TryGetValue(object?[] row, int column, out object? value)
    {
        value = null;
        if (_new.TryGetValue(row, out var created)) return created.Values.TryGetValue(column, out value);
        return _rows.TryGetValue(KeyOf(row), out var change) && change.Values.TryGetValue(column, out value);
    }

    public bool IsDeleted(object?[] row) => !_new.ContainsKey(row) && _rows.TryGetValue(KeyOf(row), out var change) && change.Deleted;

    public bool IsChanged(object?[] row) => _new.ContainsKey(row) || _rows.ContainsKey(KeyOf(row));

    /// <summary>Cambia una celda; si vuelve a valer lo que se leyó, deja de ser un cambio.</summary>
    public void Set(object?[] row, int column, object? value)
    {
        if (_new.TryGetValue(row, out var created))
        {
            // En una fila nueva, vacío = no mandar la columna (así vale el DEFAULT, el trigger o la identity).
            if (value is null or DBNull) created.Values.Remove(column);
            else created.Values[column] = value;
            return;
        }
        var key = KeyOf(row);
        if (!_rows.TryGetValue(key, out var change)) _rows[key] = change = new RowChange((object?[])row.Clone());
        if (CellValueParser.SameValue(change.Original[column], value)) change.Values.Remove(column);
        else change.Values[column] = value;
        if (change.Values.Count == 0 && !change.Deleted) _rows.Remove(key);
    }

    public void ToggleDeleted(object?[] row)
    {
        if (RemoveNew(row)) return; // una fila nueva que se borra antes de aplicar, simplemente no se agrega
        var key = KeyOf(row);
        if (!_rows.TryGetValue(key, out var change)) _rows[key] = change = new RowChange((object?[])row.Clone());
        change.Deleted = !change.Deleted;
        if (!change.Deleted && change.Values.Count == 0) _rows.Remove(key);
    }

    public void Revert(object?[] row)
    {
        if (!RemoveNew(row)) _rows.Remove(KeyOf(row));
    }

    public void Clear()
    {
        _rows.Clear();
        _new.Clear();
        _newOrder.Clear();
    }

    public IReadOnlyDictionary<string, RowChange> Rows => _rows;

    /// <summary>
    /// Los DELETE primero (una fila borrada no se actualiza), después los UPDATE y al final los INSERT (así se puede
    /// borrar una fila y volver a cargar su clave). <paramref name="returning"/>: los INSERT devuelven la fila tal
    /// como quedó (la clave que puso el servidor); en las vistas no se pide.
    /// </summary>
    public IReadOnlyList<ChangeStatement> BuildStatements(string table, bool returning = true)
    {
        if (!HasKey && _rows.Count > 0) throw new InvalidOperationException(Resources.CoreStrings.EditNoPrimaryKey);
        var list = new List<ChangeStatement>();
        foreach (var (key, change) in _rows.Where(r => r.Value.Deleted))
            list.Add(Delete(table, key, change));
        foreach (var (key, change) in _rows.Where(r => !r.Value.Deleted && r.Value.Values.Count > 0))
            list.Add(Update(table, key, change));
        foreach (var row in _newOrder)
            list.Add(Insert(table, _new[row], returning));
        return list;
    }

    private ChangeStatement Insert(string table, RowChange change, bool returning)
    {
        var parameters = new List<SqlParameterValue>();
        var sql = new StringBuilder("INSERT INTO ").Append(SqlIdentifier.Quote(table));
        var readable = new StringBuilder(sql.ToString());
        var set = change.Values.OrderBy(v => v.Key).ToList();
        if (set.Count == 0)
        {
            sql.Append(" DEFAULT VALUES");
            readable.Append(" DEFAULT VALUES");
        }
        else
        {
            var names = string.Join(", ", set.Select(v => SqlIdentifier.Quote(columns[v.Key].Name)));
            sql.Append(" (").Append(names).Append(") VALUES (");
            readable.Append(" (").Append(names).Append(") VALUES (");
            for (var i = 0; i < set.Count; i++)
            {
                var p = "@v" + parameters.Count;
                parameters.Add(new SqlParameterValue(p, set[i].Value));
                sql.Append(i == 0 ? "" : ", ").Append(p);
                readable.Append(i == 0 ? "" : ", ").Append(SqlLiteral.Format(set[i].Value, LiteralType(columns[set[i].Key])));
            }
            sql.Append(')');
            readable.Append(')');
        }
        // El RETURNING no va en el texto legible: en una pestaña de consulta daría un resultado que no hace falta.
        if (returning) sql.Append(" RETURNING ").Append(string.Join(", ", columns.Select(c => SqlIdentifier.Quote(c.Name))));
        return new ChangeStatement(change.NewKey!, false, new SqlCommandText(sql.ToString(), parameters), readable.Append(';').ToString())
        {
            IsInsert = true,
        };
    }

    private ChangeStatement Delete(string table, string key, RowChange change)
    {
        var parameters = new List<SqlParameterValue>();
        var sql = new StringBuilder("DELETE FROM ").Append(SqlIdentifier.Quote(table));
        var readable = new StringBuilder(sql.ToString());
        AppendKeyWhere(sql, readable, change, parameters);
        return new ChangeStatement(key, true, new SqlCommandText(sql.ToString(), parameters), readable.Append(';').ToString());
    }

    private ChangeStatement Update(string table, string key, RowChange change)
    {
        var parameters = new List<SqlParameterValue>();
        var sql = new StringBuilder("UPDATE ").Append(SqlIdentifier.Quote(table)).Append(" SET ");
        var readable = new StringBuilder(sql.ToString());
        var first = true;
        foreach (var (column, value) in change.Values.OrderBy(v => v.Key))
        {
            var c = columns[column];
            var p = "@v" + parameters.Count;
            parameters.Add(new SqlParameterValue(p, value));
            var sep = first ? "" : ", ";
            sql.Append(sep).Append(SqlIdentifier.Quote(c.Name)).Append(" = ").Append(p);
            readable.Append(sep).Append(SqlIdentifier.Quote(c.Name)).Append(" = ").Append(SqlLiteral.Format(value, LiteralType(c)));
            first = false;
        }
        AppendKeyWhere(sql, readable, change, parameters);
        // Control optimista sobre lo editado (salvo blobs y flotantes, que no se comparan bien por igualdad).
        foreach (var column in change.Values.Keys.Order().Where(c => !_keys.Contains(c) && Comparable(columns[c])))
        {
            var p = "@o" + parameters.Count;
            parameters.Add(new SqlParameterValue(p, Normalize(change.Original[column])));
            sql.Append(" AND ").Append(SqlIdentifier.Quote(columns[column].Name)).Append(" IS NOT DISTINCT FROM ").Append(p);
            readable.Append(" AND ").Append(SqlIdentifier.Quote(columns[column].Name)).Append(" IS NOT DISTINCT FROM ")
                .Append(SqlLiteral.Format(Normalize(change.Original[column]), LiteralType(columns[column])));
        }
        return new ChangeStatement(key, false, new SqlCommandText(sql.ToString(), parameters), readable.Append(';').ToString());
    }

    private void AppendKeyWhere(StringBuilder sql, StringBuilder readable, RowChange change, List<SqlParameterValue> parameters)
    {
        for (var i = 0; i < _keys.Length; i++)
        {
            var c = columns[_keys[i]];
            var value = Normalize(change.Original[_keys[i]]);
            var p = "@k" + parameters.Count;
            parameters.Add(new SqlParameterValue(p, value));
            var word = i == 0 ? " WHERE " : " AND ";
            sql.Append(word).Append(SqlIdentifier.Quote(c.Name)).Append(" = ").Append(p);
            readable.Append(word).Append(SqlIdentifier.Quote(c.Name)).Append(" = ").Append(SqlLiteral.Format(value, LiteralType(c)));
        }
    }

    /// <summary>
    /// El script con los valores, tal como se va a ejecutar (para verlo antes de aplicar, copiarlo o llevarlo a una
    /// pestaña de consulta). Las sentencias son las mismas, con los valores escritos en lugar de parámetros.
    /// </summary>
    /// <param name="withCommit">Termina con «COMMIT;» (para un script que se copia o se lleva a una consulta); sin eso queda para confirmarlo a mano.</param>
    public static string Script(string table, IReadOnlyList<ChangeStatement> statements, bool withCommit = false)
    {
        var updates = statements.Count(s => !s.IsDelete && !s.IsInsert);
        var deletes = statements.Count(s => s.IsDelete);
        var inserts = statements.Count(s => s.IsInsert);
        var lines = new List<string>
        {
            "-- " + string.Format(Resources.CoreStrings.DataScriptHeader, table, updates, deletes, inserts),
            "-- " + Resources.CoreStrings.DataScriptCheck,
        };
        lines.AddRange(statements.Select(s => s.Readable));
        if (withCommit) lines.Add("COMMIT;");
        return string.Join("\n", lines);
    }

    /// <summary>Texto legible de la clave de una fila («ID = 5»), para los mensajes.</summary>
    public string Describe(string rowKey) =>
        rowKey.StartsWith('\u0002') ? string.Format(Resources.CoreStrings.EditNewRow, rowKey[1..])
        : _rows.TryGetValue(rowKey, out var change)
            ? string.Join(", ", _keys.Select(k => $"{columns[k].Name} = {SqlLiteral.Format(Normalize(change.Original[k]), LiteralType(columns[k]))}"))
            : rowKey;

    private static object? Normalize(object? value) => value is DBNull ? null : value;

    private static bool Comparable(DbColumn c) => !c.Type.StartsWith("BLOB", StringComparison.Ordinal) && c.Type is not ("DOUBLE PRECISION" or "FLOAT");

    private static string LiteralType(DbColumn c) =>
        c.Type.StartsWith("CHAR(", StringComparison.Ordinal) ? "CHAR"
        : c.Type == "DATE" ? "DATE"
        : c.Type.StartsWith("TIME", StringComparison.Ordinal) && !c.Type.StartsWith("TIMESTAMP", StringComparison.Ordinal) ? "TIME"
        : c.Type;
}
