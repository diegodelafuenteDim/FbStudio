using System.ComponentModel;
using FbStudio.Core.Metadata;
using System.Diagnostics;
using FbStudio.Core.Profiles;
using FbStudio.Workspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FbStudio.Mcp;

/// <summary>
/// Lo que el conector necesita de FbStudio. Las herramientas no reciben nada que ejecute SQL o lea filas: la garantía
/// de D-114 es que esa puerta no existe acá.
/// </summary>
/// <param name="Profiles">Los registros (se leen en cada pedido: si la persona habilita uno, aparece sin reiniciar).</param>
/// <param name="Areas">Las áreas de un registro.</param>
/// <param name="Password">La contraseña guardada de un registro (del llavero del sistema), o null.</param>
/// <param name="Log">Dónde queda cada llamada (D-117); null, en ningún lado (pruebas).</param>
/// <param name="ResponseLimit">Largo máximo de cada respuesta (opción de FbStudio).</param>
/// <param name="ShowClients">En el monitoreo, mostrar usuario, equipo y programa de cada conexión (opción de FbStudio).</param>
public sealed record ConnectorContext(
    Func<IReadOnlyList<ConnectionProfile>> Profiles,
    Func<ConnectionProfile, IReadOnlyList<SchemaArea>> Areas,
    Func<Guid, string?> Password,
    McpCallLog? Log = null,
    int ResponseLimit = ConnectorText.DefaultLimit,
    bool ShowClients = true);

/// <summary>
/// Las herramientas del conector (D-114). Nombres y descripciones en inglés: los lee el modelo. Todas son de solo
/// lectura y no salen de las bases registradas. Agregar una obliga a tocar <c>ProtocolTests</c> y el diseño.
/// </summary>
public static class FbStudioTools
{
    /// <summary>Cuánto puede tardar un pedido antes de cancelarse.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>La estadística completa de unas tablas recorre todas sus páginas: tiene más tiempo.</summary>
    public static readonly TimeSpan StatisticsTimeout = TimeSpan.FromMinutes(2);

    private const string DatabaseParam = "Database name exactly as returned by list_databases.";

    public static IReadOnlyList<McpServerTool> Create(ConnectorContext context, DatabaseCatalog catalog)
    {
        var t = new StructureTools(catalog);
        var m = new MonitorTools(catalog, context.ShowClients);
        return
        [
            Tool("list_databases", "List databases",
                "Lists the Firebird databases the user enabled for assistants in FbStudio, with their environment " +
                "(Development, Test or Production) and their areas (named groups of tables in big databases). " +
                "Use the database name exactly as listed in the other tools.",
                (CancellationToken ct) => Run(context, null, "list_databases", null, null,
                    _ => Task.FromResult(DatabaseList.Describe(context.Profiles(), context.Areas)), ct)),

            Tool("get_schema", "Get schema",
                "Lists the objects of a database: tables by default, or the ones of an area, of some kinds or matching a name " +
                "pattern. Big databases come in pages of " + StructureTools.PageSize + "; use offset for the next one. " +
                "Starts with how many objects of each kind there are and the area names.",
                ([Description(DatabaseParam)] string database,
                 [Description("Optional area name (see list_databases): only its objects.")] string? area = null,
                 [Description("Optional kinds, comma separated: table, view, procedure, function, package, trigger, generator, exception, domain, index, role.")] string? kind = null,
                 [Description("Optional name pattern with * and ?, e.g. LAB_* .")] string? name = null,
                 [Description("Where the page starts (0 for the first one).")] int offset = 0,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "get_schema", database, Args(("area", area), ("kind", kind), ("name", name), ("offset", offset == 0 ? null : offset.ToString())),
                        c => t.GetSchemaAsync(database, area, kind, name, offset, c), ct)),

            Tool("describe_object", "Describe object",
                "Returns the full DDL of an object as FbStudio generates it: a table with its columns, keys, indexes, triggers, " +
                "comments and logical foreign keys (written in column comments as #FK_%TABLE,%COLUMN#), or the source code of a " +
                "view, procedure, function, trigger or package.",
                ([Description(DatabaseParam)] string database,
                 [Description("Object name (table, view, procedure, function, package, trigger, generator, exception, domain, index or role).")] string name,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "describe_object", database, Args(("name", name)), c => t.DescribeObjectAsync(database, name, c), ct)),

            Tool("search_metadata", "Search metadata",
                "Finds where a text appears in a database's metadata: object and column names, comments and the source code of " +
                "views, procedures, functions, triggers and packages. It does not search table data.",
                ([Description(DatabaseParam)] string database,
                 [Description("Text to look for (not case sensitive).")] string text,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "search_metadata", database, Args(("text", text)), c => t.SearchMetadataAsync(database, text, c), ct)),

            Tool("get_dependencies", "Get dependencies",
                "Lists what an object uses and what uses it (tables read by a procedure, procedures that call another, " +
                "foreign keys, triggers of a table...). Useful to measure the impact of a change.",
                ([Description(DatabaseParam)] string database,
                 [Description("Object name.")] string name,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "get_dependencies", database, Args(("name", name)), c => t.GetDependenciesAsync(database, name, c), ct)),

            Tool("find_column_usage", "Find column usage",
                "Lists the views, procedures, functions, triggers, indexes and constraints that use a column of a table.",
                ([Description(DatabaseParam)] string database,
                 [Description("Table or view name.")] string table,
                 [Description("Column name.")] string column,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "find_column_usage", database, Args(("table", table), ("column", column)),
                        c => t.FindColumnUsageAsync(database, table, column, c), ct)),

            Tool("find_join_path", "Find join path",
                "Finds the shortest chain of relationships between two tables (declared foreign keys and logical ones from " +
                "column comments, avoiding tables that relate to almost everything) and writes the JOINs.",
                ([Description(DatabaseParam)] string database,
                 [Description("First table.")] string from,
                 [Description("Second table.")] string to,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "find_join_path", database, Args(("from", from), ("to", to)), c => t.FindJoinPathAsync(database, from, to, c), ct)),

            Tool("explain_query", "Explain query",
                "Returns the execution plan Firebird would use for one SELECT, WITH, INSERT, UPDATE, DELETE, MERGE or EXECUTE BLOCK " +
                "statement. The statement is only prepared, never executed: no rows are read or changed.",
                ([Description(DatabaseParam)] string database,
                 [Description("One SQL statement.")] string sql,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "explain_query", database, sql, c => t.ExplainQueryAsync(database, sql, c), ct)),

            Tool("get_database_info", "Get database info",
                "Returns the Firebird version and server architecture, ODS, dialect, page size, charset, cache, sweep interval, " +
                "forced writes, read-only flag and nbackup state of a database. Never server paths.",
                ([Description(DatabaseParam)] string database, CancellationToken ct = default) =>
                    Run(context, catalog, "get_database_info", database, null, c => t.GetDatabaseInfoAsync(database, c), ct)),

            Tool("get_monitor_snapshot", "Get monitor snapshot",
                "Takes a snapshot of the database activity (MON$ tables): transaction counters and the oldest active transaction " +
                "(OAT) with how old it is and who holds it, the oldest open transactions, the statements running or fetching rows " +
                "(with their SQL, values replaced by ?), statements waiting on locked records, and connections by program. " +
                "Use it to diagnose slowness, locks or garbage piling up. It never kills connections or cancels statements.",
                ([Description(DatabaseParam)] string database, CancellationToken ct = default) =>
                    Run(context, catalog, "get_monitor_snapshot", database, null, c => m.GetMonitorSnapshotAsync(database, c), ct)),

            Tool("get_statistics", "Get statistics",
                "Returns the database header statistics (page size, ODS, transaction counters and gaps, sweep interval, " +
                "attributes). With table names, also their records, record versions, data pages and fill, and their indexes " +
                "(depth, nodes, duplicates), flagging too many old versions, too deep indexes and poor selectivity. Each table " +
                "reads all its pages, so pass only the ones you need (up to " + MonitorTools.MaxTables + ").",
                ([Description(DatabaseParam)] string database,
                 [Description("Optional table names, comma separated. Without them, only the header (instant).")] string? tables = null,
                 CancellationToken ct = default) =>
                    Run(context, catalog, "get_statistics", database, Args(("tables", tables)), c => m.GetStatisticsAsync(database, tables, c), ct,
                        string.IsNullOrWhiteSpace(tables) ? Timeout : StatisticsTimeout)),
        ];
    }

    /// <summary>
    /// Corre una herramienta con límite de tiempo y devuelve su texto, recortado si es muy largo. Los errores salen como
    /// resultado con error, con un texto que no muestra servidor, ruta, usuario ni valores. Cada llamada queda en el
    /// registro (D-117).
    /// </summary>
    private static async Task<CallToolResult> Run(ConnectorContext context, DatabaseCatalog? catalog, string tool, string? database,
        string? arguments, Func<CancellationToken, Task<string>> work, CancellationToken ct, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var result = await RunCoreAsync(context, catalog, database, work, ct, timeout ?? Timeout);
        var text = ((TextContentBlock)result.Content[0]).Text;
        context.Log?.Add(new McpCall(DateTime.Now, tool, database?.Trim(), arguments, watch.ElapsedMilliseconds, result.IsError != true,
            text.Length, result.IsError == true ? text : null));
        return result;
    }

    private static async Task<CallToolResult> RunCoreAsync(ConnectorContext context, DatabaseCatalog? catalog, string? database,
        Func<CancellationToken, Task<string>> work, CancellationToken ct, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return Result(ConnectorText.Truncate(await work(cts.Token), context.ResponseLimit), error: false);
        }
        catch (ConnectorException ex)
        {
            return Result(ex.Message, error: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result($"The request took longer than {timeout.TotalSeconds:0} seconds and was cancelled.", error: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConnectionProfile? profile = null;
            try { if (catalog is not null && database is not null) profile = catalog.Resolve(database); } catch (ConnectorException) { }
            return Result("Firebird error: " + ConnectorText.Error(ex, profile), error: true);
        }
    }

    /// <summary>Lo que pidió el asistente, para el registro: «area=LAB · kind=table» (sin los vacíos).</summary>
    private static string? Args(params (string Name, string? Value)[] values) =>
        string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => $"{v.Name}={v.Value!.Trim()}")) is { Length: > 0 } text ? text : null;

    private static CallToolResult Result(string text, bool error) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = error,
    };

    private static McpServerTool Tool(string name, string title, [Localizable(false)] string description, Delegate method) =>
        McpServerTool.Create(method, new McpServerToolCreateOptions
        {
            Name = name,
            Title = title,
            Description = description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
        });
}
