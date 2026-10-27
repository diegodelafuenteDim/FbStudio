using FbStudio.Core.Diagrams;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;

namespace FbStudio.Mcp;

/// <summary>Un error para contarle al asistente tal cual (registro inexistente, sin contraseña, sentencia rechazada…).</summary>
public sealed class ConnectorException(string message) : Exception(message);

/// <summary>
/// La única puerta del conector a las bases (D-114): por cada registro habilitado, el explorador de metadatos de FbStudio
/// (lecturas de RDB$ y MON$ en transacciones de solo lectura, y el plan sin ejecutar). Nada de esto ejecuta SQL del
/// asistente ni lee filas de las tablas. Una conexión por base, que se cierra sola tras un rato sin uso; los pedidos a
/// una misma base van de a uno.
/// </summary>
public sealed class DatabaseCatalog : IAsyncDisposable
{
    private readonly ConnectorContext _context;
    private readonly TimeSpan _idle;
    private readonly Dictionary<Guid, DatabaseSession> _sessions = [];
    private readonly Lock _lock = new();
    private readonly Timer _sweeper;

    public DatabaseCatalog(ConnectorContext context, TimeSpan? idle = null)
    {
        _context = context;
        _idle = idle ?? TimeSpan.FromMinutes(2);
        var tick = _idle < TimeSpan.FromSeconds(30) ? _idle : TimeSpan.FromSeconds(30);
        _sweeper = new Timer(_ => _ = CloseIdleAsync(), null, tick, tick);
    }

    /// <summary>El registro habilitado con ese nombre (sin distinguir mayúsculas), o un error que dice cuáles hay.</summary>
    public ConnectionProfile Resolve(string database)
    {
        var enabled = _context.Profiles().Where(p => p.AssistantAccess).ToList();
        var name = (database ?? "").Trim();
        var profile = enabled.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                      ?? enabled.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile is not null) return profile;
        throw new ConnectorException(enabled.Count == 0
            ? "No database is enabled for assistants. " + DatabaseList.HowToEnable
            : $"There is no enabled database called \"{name}\". Enabled: {string.Join(", ", enabled.Select(p => p.Name))}.");
    }

    /// <summary>Hace <paramref name="work"/> con la sesión de esa base, de a un pedido por vez por base.</summary>
    public async Task<T> UseAsync<T>(string database, Func<DatabaseSession, Task<T>> work, CancellationToken ct)
    {
        var profile = Resolve(database);
        DatabaseSession session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(profile.Id, out session!) || session.Profile != profile)
            {
                var password = _context.Password(profile.Id)
                               ?? throw new ConnectorException($"FbStudio has no saved password for \"{profile.Name}\": open the registration in FbStudio, type the password and save it.");
                if (_sessions.Remove(profile.Id, out var old)) _ = old.DisposeAsync().AsTask();
                session = new DatabaseSession(profile, password, _context.Areas);
                _sessions[profile.Id] = session;
            }
        }
        return await session.RunAsync(work, ct);
    }

    private async Task CloseIdleAsync()
    {
        List<DatabaseSession> idle;
        lock (_lock)
        {
            idle = _sessions.Values.Where(s => s.IdleFor > _idle).ToList();
            foreach (var s in idle) _sessions.Remove(s.Profile.Id);
        }
        foreach (var s in idle) await s.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sweeper.DisposeAsync();
        List<DatabaseSession> all;
        lock (_lock)
        {
            all = [.. _sessions.Values];
            _sessions.Clear();
        }
        foreach (var s in all) await s.DisposeAsync();
    }
}

/// <summary>Lo que el conector sabe de una base mientras la usa: el explorador y lo que ya leyó (objetos, relaciones).</summary>
public sealed class DatabaseSession(ConnectionProfile profile, string password, Func<ConnectionProfile, IReadOnlyList<SchemaArea>> areas)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastUse = Environment.TickCount64;
    private IReadOnlyList<DbObject>? _objects;
    private TableUses? _uses;
    private SchemaGraph? _graph;

    public ConnectionProfile Profile { get; } = profile;
    public MetadataExplorer Explorer { get; } = new(profile, password);
    internal string Password { get; } = password;

    public TimeSpan IdleFor => _gate.CurrentCount == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastUse));

    /// <summary>Las áreas del registro (del archivo de áreas de FbStudio, que puede estar en una carpeta compartida).</summary>
    public IReadOnlyList<SchemaArea> Areas() => areas(Profile);

    /// <summary>Todos los objetos de la base (se leen una vez por sesión).</summary>
    public async Task<IReadOnlyList<DbObject>> ObjectsAsync(CancellationToken ct) => _objects ??= await Explorer.LoadAllObjectsAsync(ct);

    /// <summary>Qué tablas usa cada procedimiento, vista y trigger: para las áreas que suman lo que usa solo sus tablas.</summary>
    public async Task<TableUses> UsesAsync(CancellationToken ct) => _uses ??= await Explorer.LoadTableUsesAsync(ct);

    /// <summary>Las tablas y sus relaciones (FK y FK lógicas de los comentarios), para el camino entre dos tablas.</summary>
    public async Task<SchemaGraph> GraphAsync(CancellationToken ct) => _graph ??= await Explorer.LoadSchemaGraphAsync(ct: ct);

    internal async Task<T> RunAsync<T>(Func<DatabaseSession, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await work(this);
        }
        finally
        {
            Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await Explorer.DisposeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
