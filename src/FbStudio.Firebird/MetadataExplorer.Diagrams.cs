using FbStudio.Core.Diagrams;
using FbStudio.Core.Metadata;

namespace FbStudio.Firebird;

public sealed partial class MetadataExplorer
{
    // Todas las columnas de las tablas y vistas, con el comentario (por las FK lógicas «#FK_») como VARCHAR: un solo viaje.
    private const string DiagramColumnsSql = """
        SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH,
               f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0),
               IIF(r.RDB$VIEW_BLR IS NULL, 0, 1),
               IIF(rf.RDB$DESCRIPTION CONTAINING '#FK_', CAST(SUBSTRING(rf.RDB$DESCRIPTION FROM 1 FOR 1000) AS VARCHAR(1000)), NULL)
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
        WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_POSITION
        """;

    private const string DiagramPrimaryKeysSql = """
        SELECT TRIM(rc.RDB$RELATION_NAME), TRIM(s.RDB$FIELD_NAME)
        FROM RDB$RELATION_CONSTRAINTS rc
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
        WHERE rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
        """;

    // Cada FK con sus columnas y las de la clave a la que apunta, en orden.
    internal const string DiagramForeignKeysSql = """
        SELECT TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(rc.RDB$RELATION_NAME), TRIM(uq.RDB$RELATION_NAME), TRIM(s.RDB$FIELD_NAME), TRIM(us.RDB$FIELD_NAME)
        FROM RDB$RELATION_CONSTRAINTS rc
        JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
        JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
        LEFT JOIN RDB$INDEX_SEGMENTS us ON us.RDB$INDEX_NAME = uq.RDB$INDEX_NAME AND us.RDB$FIELD_POSITION = s.RDB$FIELD_POSITION
        WHERE rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
        ORDER BY rc.RDB$CONSTRAINT_NAME, s.RDB$FIELD_POSITION
        """;

    /// <summary>
    /// Las tablas y vistas con sus columnas, las FOREIGN KEY y las FK lógicas de los comentarios «#FK_», para los
    /// diagramas (F-8). Son tres consultas a RDB$, sin importar cuántas tablas tenga la base.
    /// </summary>
    public async Task<SchemaGraph> LoadSchemaGraphAsync(int hubThreshold = SchemaGraph.DefaultHubThreshold, CancellationToken ct = default)
    {
        var keys = await WithReaderAsync(DiagramPrimaryKeysSql, null, async reader =>
        {
            var set = new HashSet<(string, string)>();
            while (await reader.ReadAsync(ct)) set.Add((reader.GetString(0), reader.GetString(1)));
            return set;
        }, ct);

        var (tables, logical) = await WithReaderAsync(DiagramColumnsSql, null, async reader =>
        {
            var columns = new Dictionary<string, (bool IsView, List<DiagramColumn> Columns)>(StringComparer.Ordinal);
            var links = new List<DiagramLink>();
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                var name = reader.GetString(1);
                if (!columns.TryGetValue(table, out var entry)) columns[table] = entry = (Convert.ToInt32(reader.GetValue(9)) == 1, []);
                entry.Columns.Add(new DiagramColumn(name,
                    FieldTypeFormatter.Format(reader.GetInt16(2), Int(reader, 3), Int(reader, 4), Int(reader, 5), Int(reader, 6), Int(reader, 7)),
                    NotNull: Convert.ToInt32(reader.GetValue(8)) == 1, IsPrimaryKey: keys.Contains((table, name))));
                if (!reader.IsDBNull(10) && LogicalForeignKeys.Parse(table, name, reader.GetString(10)) is { } link) links.Add(link);
            }
            return (columns.Select(kv => new DiagramTable(kv.Key, kv.Value.IsView, kv.Value.Columns)).ToList(), links);
        }, ct);

        var physical = await WithReaderAsync(DiagramForeignKeysSql, null, async reader =>
        {
            var byName = new Dictionary<string, (string Table, string Referenced, List<string> Columns, List<string> ReferencedColumns)>(StringComparer.Ordinal);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (!byName.TryGetValue(name, out var fk)) byName[name] = fk = (reader.GetString(1), reader.GetString(2), [], []);
                fk.Columns.Add(reader.GetString(3));
                if (!reader.IsDBNull(4)) fk.ReferencedColumns.Add(reader.GetString(4));
            }
            return byName.Select(kv => new DiagramLink(kv.Key, kv.Value.Table, kv.Value.Columns, kv.Value.Referenced, kv.Value.ReferencedColumns)).ToList();
        }, ct);

        return new SchemaGraph(tables, physical.Concat(logical), hubThreshold);
    }
}
