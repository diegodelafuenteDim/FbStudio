using System.Text.Json;

namespace FbStudio.Workspace;

/// <summary>Consulta de monitoreo armada por el usuario (F-1): un SELECT sobre MON$ con su nombre.</summary>
/// <param name="AttachmentColumn">Columna con el número de conexión (para «Cortar conexión»); vacía = se detecta sola.</param>
/// <param name="StatementColumn">Columna con el número de sentencia (para «Cancelar sentencia»).</param>
/// <param name="SqlColumn">Columna con el texto SQL (se muestra abajo, entero).</param>
public sealed record CustomMonitorQuery(Guid Id, string Name, string Sql, string? AttachmentColumn = null, string? StatementColumn = null, string? SqlColumn = null);

/// <summary>
/// Consultas propias del monitoreo en %APPDATA%\FbStudio\monitor-queries.json (valen para todas las bases). Se pueden
/// exportar a un archivo e importar, para compartirlas en el equipo. Sin carpeta, viven solo en memoria (pruebas).
/// </summary>
public sealed class MonitorQueryStore(string? folder)
{
    private List<CustomMonitorQuery>? _memory;

    public string? FilePath => folder is null ? null : Path.Combine(folder, "monitor-queries.json");

    /// <summary>Cambió la lista (alta, edición, baja o importación).</summary>
    public event Action? Changed;

    public IReadOnlyList<CustomMonitorQuery> Load()
    {
        if (FilePath is null) return _memory ??= [];
        if (!File.Exists(FilePath)) return [];
        return Read(FilePath);
    }

    public void Upsert(CustomMonitorQuery query)
    {
        var all = Load().ToList();
        var i = all.FindIndex(q => q.Id == query.Id);
        if (i >= 0) all[i] = query; else all.Add(query);
        Save(all);
    }

    public void Delete(Guid id) => Save(Load().Where(q => q.Id != id).ToList());

    public void Export(string path, IEnumerable<CustomMonitorQuery> queries) =>
        File.WriteAllText(path, JsonSerializer.Serialize(queries.ToList(), WorkspaceJsonContext.Default.ListCustomMonitorQuery));

    /// <summary>Suma las consultas del archivo; una con el mismo Id reemplaza a la que había. Devuelve cuántas trajo.</summary>
    public int Import(string path)
    {
        var incoming = Read(path);
        var all = Load().ToList();
        foreach (var q in incoming)
        {
            var i = all.FindIndex(x => x.Id == q.Id);
            if (i >= 0) all[i] = q; else all.Add(q);
        }
        Save(all);
        return incoming.Count;
    }

    private static List<CustomMonitorQuery> Read(string path) =>
        (JsonSerializer.Deserialize(File.ReadAllText(path), WorkspaceJsonContext.Default.ListCustomMonitorQuery) ?? [])
        .Where(q => q is not null && !string.IsNullOrWhiteSpace(q.Name) && !string.IsNullOrWhiteSpace(q.Sql) && IsSingleSelect(q.Sql))
        .ToList();

    /// <summary>Lo mismo que pide el editor de consultas: una sola sentencia y de lectura (se corre cada pocos segundos).</summary>
    public static bool IsSingleSelect(string sql)
    {
        var statements = Core.Sql.SqlScriptSplitter.Split(sql.Trim());
        return statements.Count == 1 && Core.Sql.StatementClassifier.Classify(statements[0].Text) is
            { Kind: Core.Sql.StatementKind.Select, ChangesSequences: false };
    }

    private void Save(List<CustomMonitorQuery> all)
    {
        all = all.OrderBy(q => q.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (FilePath is null)
        {
            _memory = all;
        }
        else
        {
            PrivateFiles.EnsureFolder(folder!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, WorkspaceJsonContext.Default.ListCustomMonitorQuery));
            PrivateFiles.Restrict(tmp);
            File.Move(tmp, FilePath, overwrite: true);
        }
        Changed?.Invoke();
    }
}
