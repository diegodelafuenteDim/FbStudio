using FbStudio.Core.Metadata;
using FbStudio.Core.Resources;

namespace FbStudio.Firebird;

public enum SearchArea
{
    Name,
    Source,
    Column,
    Description,
}

/// <param name="Detail">Columna encontrada o dato extra.</param>
/// <param name="Line">Línea en el DDL que se ve al abrir el objeto (no en la fuente guardada); null si no es una coincidencia
/// en la fuente o no se puede calcular (paquetes).</param>
public sealed record MetadataSearchHit(DbObjectKind Kind, string Name, SearchArea Area, string? Detail, int? Line, string Snippet);

public sealed record MetadataSearchOptions(bool Sources = true, bool Names = true, bool Columns = true, bool Descriptions = true, int MaxLinesPerObject = 20);

public sealed partial class MetadataExplorer
{
    private const string NotSystemFlag = "COALESCE(RDB$SYSTEM_FLAG, 0) = 0";

    /// <summary>
    /// Busca un texto (sin distinguir mayúsculas) en fuentes PSQL, nombres, columnas y descripciones. El filtro lo hace
    /// el servidor con CONTAINING: solo viajan las fuentes que coinciden, y acá se ubican las líneas.
    /// </summary>
    public Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(string text, MetadataSearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new MetadataSearchOptions();
        var term = text.Trim();
        if (term.Length == 0) return Task.FromResult<IReadOnlyList<MetadataSearchHit>>([]);

        return WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            var hits = new List<MetadataSearchHit>();
            var t = ("@t", (object?)term);

            if (options.Names)
            {
                (DbObjectKind Kind, string Sql)[] names =
                [
                    (DbObjectKind.Table, $"SELECT TRIM(RDB$RELATION_NAME), IIF(RDB$VIEW_BLR IS NULL, 0, 1) FROM RDB$RELATIONS WHERE {NotSystemFlag} AND RDB$RELATION_NAME CONTAINING @t"),
                    (DbObjectKind.Procedure, $"SELECT TRIM(RDB$PROCEDURE_NAME), 0 FROM RDB$PROCEDURES WHERE {NotSystemFlag} AND RDB$PACKAGE_NAME IS NULL AND RDB$PROCEDURE_NAME CONTAINING @t"),
                    (DbObjectKind.Function, $"SELECT TRIM(RDB$FUNCTION_NAME), 0 FROM RDB$FUNCTIONS WHERE {NotSystemFlag} AND RDB$PACKAGE_NAME IS NULL AND RDB$FUNCTION_NAME CONTAINING @t"),
                    (DbObjectKind.Package, $"SELECT TRIM(RDB$PACKAGE_NAME), 0 FROM RDB$PACKAGES WHERE {NotSystemFlag} AND RDB$PACKAGE_NAME CONTAINING @t"),
                    (DbObjectKind.Trigger, $"SELECT TRIM(RDB$TRIGGER_NAME), 0 FROM RDB$TRIGGERS WHERE {NotSystemFlag} AND RDB$TRIGGER_NAME CONTAINING @t"),
                    (DbObjectKind.Generator, $"SELECT TRIM(RDB$GENERATOR_NAME), 0 FROM RDB$GENERATORS WHERE {NotSystemFlag} AND RDB$GENERATOR_NAME CONTAINING @t"),
                    (DbObjectKind.Exception, $"SELECT TRIM(RDB$EXCEPTION_NAME), 0 FROM RDB$EXCEPTIONS WHERE {NotSystemFlag} AND RDB$EXCEPTION_NAME CONTAINING @t"),
                    (DbObjectKind.Domain, $"SELECT TRIM(RDB$FIELD_NAME), 0 FROM RDB$FIELDS WHERE {NotSystemFlag} AND RDB$FIELD_NAME NOT STARTING WITH 'RDB$' AND RDB$FIELD_NAME CONTAINING @t"),
                    (DbObjectKind.Index, $"SELECT TRIM(RDB$INDEX_NAME), 0 FROM RDB$INDICES WHERE {NotSystemFlag} AND RDB$INDEX_NAME CONTAINING @t"),
                    (DbObjectKind.Role, $"SELECT TRIM(RDB$ROLE_NAME), 0 FROM RDB$ROLES WHERE {NotSystemFlag} AND RDB$ROLE_NAME CONTAINING @t"),
                ];
                foreach (var (kind, sql) in names)
                {
                    try
                    {
                        hits.AddRange(await q.ListAsync(sql, r =>
                        {
                            var k = kind == DbObjectKind.Table && Int(r, 1) == 1 ? DbObjectKind.View : kind;
                            return new MetadataSearchHit(k, Str(r, 0) ?? "", SearchArea.Name, null, null, $"{DbObjectKinds.Singular(k)} {Str(r, 0)}");
                        }, t));
                    }
                    catch (FirebirdSql.Data.FirebirdClient.FbException) when (kind is DbObjectKind.Package or DbObjectKind.Function)
                    {
                        // Firebird 2.5: no hay paquetes ni funciones PSQL.
                    }
                }
            }

            if (options.Sources)
            {
                // Cada consulta trae, además de la fuente, lo que hace falta para saber cuántas líneas de encabezado le pone
                // DdlWriter (parámetros, columnas): así la línea que se muestra es la del editor.
                (DbObjectKind Kind, string Sql)[] sources =
                [
                    (DbObjectKind.Procedure, $"""
                        SELECT TRIM(p.RDB$PROCEDURE_NAME), p.RDB$PROCEDURE_SOURCE,
                               (SELECT COUNT(*) FROM RDB$PROCEDURE_PARAMETERS pp WHERE pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME AND pp.RDB$PACKAGE_NAME IS NULL AND pp.RDB$PARAMETER_TYPE = 0),
                               (SELECT COUNT(*) FROM RDB$PROCEDURE_PARAMETERS pp WHERE pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME AND pp.RDB$PACKAGE_NAME IS NULL AND pp.RDB$PARAMETER_TYPE = 1)
                        FROM RDB$PROCEDURES p WHERE COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0 AND p.RDB$PACKAGE_NAME IS NULL AND p.RDB$PROCEDURE_SOURCE CONTAINING @t
                        """),
                    (DbObjectKind.Trigger, $"SELECT TRIM(RDB$TRIGGER_NAME), RDB$TRIGGER_SOURCE, 0, 0 FROM RDB$TRIGGERS WHERE {NotSystemFlag} AND RDB$TRIGGER_SOURCE CONTAINING @t"),
                    (DbObjectKind.View, $"""
                        SELECT TRIM(r.RDB$RELATION_NAME), r.RDB$VIEW_SOURCE,
                               (SELECT COUNT(*) FROM RDB$RELATION_FIELDS rf WHERE rf.RDB$RELATION_NAME = r.RDB$RELATION_NAME), 0
                        FROM RDB$RELATIONS r WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND r.RDB$VIEW_BLR IS NOT NULL AND r.RDB$VIEW_SOURCE CONTAINING @t
                        """),
                    (DbObjectKind.Function, $"""
                        SELECT TRIM(f.RDB$FUNCTION_NAME), f.RDB$FUNCTION_SOURCE,
                               (SELECT COUNT(*) FROM RDB$FUNCTION_ARGUMENTS a WHERE a.RDB$FUNCTION_NAME = f.RDB$FUNCTION_NAME AND a.RDB$PACKAGE_NAME IS NULL
                                       AND a.RDB$ARGUMENT_POSITION <> COALESCE(f.RDB$RETURN_ARGUMENT, 0)), 0
                        FROM RDB$FUNCTIONS f WHERE COALESCE(f.RDB$SYSTEM_FLAG, 0) = 0 AND f.RDB$PACKAGE_NAME IS NULL AND f.RDB$FUNCTION_SOURCE CONTAINING @t
                        """),
                    (DbObjectKind.Package, $"""
                        SELECT TRIM(RDB$PACKAGE_NAME), COALESCE(RDB$PACKAGE_HEADER_SOURCE, '') || ASCII_CHAR(10) || COALESCE(RDB$PACKAGE_BODY_SOURCE, ''), 0, 0
                        FROM RDB$PACKAGES WHERE {NotSystemFlag} AND (RDB$PACKAGE_HEADER_SOURCE CONTAINING @t OR RDB$PACKAGE_BODY_SOURCE CONTAINING @t)
                        """),
                ];
                foreach (var (kind, sql) in sources)
                {
                    List<(string Name, string Source, int A, int B)> matches;
                    try
                    {
                        matches = await q.ListAsync(sql, r => (Str(r, 0) ?? "", Text(r, 1) ?? "", Int(r, 2) ?? 0, Int(r, 3) ?? 0), t);
                    }
                    catch (FirebirdSql.Data.FirebirdClient.FbException) when (kind is DbObjectKind.Package or DbObjectKind.Function)
                    {
                        continue;
                    }
                    foreach (var (name, source, a, b) in matches)
                    {
                        // En los paquetes el encabezado y el cuerpo van separados en el DDL: la línea se busca por texto.
                        int? header = kind switch
                        {
                            DbObjectKind.Procedure => DdlLayout.ProcedureHeader(a, b),
                            DbObjectKind.Function => DdlLayout.FunctionHeader(a),
                            DbObjectKind.View => DdlLayout.ViewHeader(a),
                            DbObjectKind.Trigger => DdlLayout.TriggerHeader(source),
                            _ => null,
                        };
                        var lines = SourceSearch.MatchingLines(source, term, options.MaxLinesPerObject).ToList();
                        if (lines.Count == 0)
                            hits.Add(new MetadataSearchHit(kind, name, SearchArea.Source, null, null, CoreStrings.SearchMatchesInSource));
                        foreach (var (line, snippet) in lines)
                            hits.Add(new MetadataSearchHit(kind, name, SearchArea.Source, null, header + line, snippet));
                    }
                }
            }

            if (options.Columns)
            {
                hits.AddRange(await q.ListAsync($"""
                    SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), IIF(r.RDB$VIEW_BLR IS NULL, 0, 1)
                    FROM RDB$RELATION_FIELDS rf
                    JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
                    WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND rf.RDB$FIELD_NAME CONTAINING @t
                    """, r => new MetadataSearchHit(Int(r, 2) == 1 ? DbObjectKind.View : DbObjectKind.Table, Str(r, 0) ?? "", SearchArea.Column,
                        Str(r, 1), null, $"columna {Str(r, 1)}"), t));
            }

            if (options.Descriptions)
            {
                // Como VARCHAR (no blob) para no hacer un viaje al servidor por fila.
                hits.AddRange(await q.ListAsync($"""
                    SELECT TRIM(r.RDB$RELATION_NAME), IIF(r.RDB$VIEW_BLR IS NULL, 0, 1), CAST(SUBSTRING(r.RDB$DESCRIPTION FROM 1 FOR 500) AS VARCHAR(500))
                    FROM RDB$RELATIONS r WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND r.RDB$DESCRIPTION CONTAINING @t
                    """, r => new MetadataSearchHit(Int(r, 1) == 1 ? DbObjectKind.View : DbObjectKind.Table, Str(r, 0) ?? "", SearchArea.Description,
                        null, null, SourceSearch.Snippet(Str(r, 2) ?? "")), t));
                hits.AddRange(await q.ListAsync($"""
                    SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), IIF(r.RDB$VIEW_BLR IS NULL, 0, 1),
                           CAST(SUBSTRING(rf.RDB$DESCRIPTION FROM 1 FOR 500) AS VARCHAR(500))
                    FROM RDB$RELATION_FIELDS rf
                    JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
                    WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND rf.RDB$DESCRIPTION CONTAINING @t
                    """, r => new MetadataSearchHit(Int(r, 2) == 1 ? DbObjectKind.View : DbObjectKind.Table, Str(r, 0) ?? "", SearchArea.Description,
                        Str(r, 1), null, $"{Str(r, 1)}: {SourceSearch.Snippet(Str(r, 3) ?? "")}"), t));
                hits.AddRange(await q.ListAsync($"""
                    SELECT TRIM(RDB$PROCEDURE_NAME), CAST(SUBSTRING(RDB$DESCRIPTION FROM 1 FOR 500) AS VARCHAR(500))
                    FROM RDB$PROCEDURES WHERE {NotSystemFlag} AND RDB$PACKAGE_NAME IS NULL AND RDB$DESCRIPTION CONTAINING @t
                    """, r => new MetadataSearchHit(DbObjectKind.Procedure, Str(r, 0) ?? "", SearchArea.Description, null, null,
                        SourceSearch.Snippet(Str(r, 1) ?? "")), t));
            }

            return (IReadOnlyList<MetadataSearchHit>)hits
                .OrderBy(h => h.Area).ThenBy(h => h.Kind).ThenBy(h => h.Name, StringComparer.Ordinal).ThenBy(h => h.Line ?? 0)
                .ToList();
        }, ct);
    }
}
