using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Trace;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Texto del selector de umbral: «Todas las sentencias» o «Desde 100 ms».</summary>
public static class TraceLabels
{
    public static readonly Avalonia.Data.Converters.IValueConverter Threshold =
        new Avalonia.Data.Converters.FuncValueConverter<int, string>(ms => ms <= 0 ? Strings.TraceThresholdAll : string.Format(Strings.TraceThreshold, ms));

    public static readonly Avalonia.Data.Converters.IValueConverter AutoStop =
        new Avalonia.Data.Converters.FuncValueConverter<int, string>(m => m <= 0 ? Strings.TraceAutoStopNever : string.Format(Strings.TraceAutoStop, m));
}

/// <summary>
/// Pestaña «Trace» de una base (F-24): una sesión de traza de Firebird con las sentencias que tardan más que el umbral,
/// de cualquier programa, y el análisis por consulta (las iguales juntas, ordenadas por el tiempo total). También abre
/// un log guardado o de fbtracemgr.
/// <list type="bullet">
/// <item>En producción pregunta antes; siempre se detiene sola a los minutos elegidos (por defecto 10) y al cerrar la
/// pestaña, para que no quede cargando el servidor.</item>
/// <item>Lo que llega queda solo en memoria (tiene SQL con datos) hasta que el usuario lo guarde; las rutas del servidor
/// no se muestran (D-54).</item>
/// </list>
/// </summary>
public sealed partial class TraceTabViewModel : EditorTabViewModel
{
    /// <summary>Más eventos que esto: se descartan los más viejos (el análisis los cuenta igual hasta ese momento).</summary>
    public const int MaxEvents = 50_000;
    private const int MaxLogLines = 1_000_000;

    private readonly ConcurrentQueue<string> _incoming = new();
    private readonly List<string> _log = [];
    private readonly List<TraceEvent> _events = [];
    private readonly Dictionary<object[], TraceEvent> _eventRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object[], TraceQueryGroup> _groupRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object[], TraceQueryChange> _changeRows = new(ReferenceEqualityComparer.Instance);
    private List<TraceEvent>? _baseline;
    private readonly DispatcherTimer _flush;
    private readonly DispatcherTimer _autoStop;
    private TraceParser _parser = new();
    private TraceSession? _session;
    private DateTime _startedAt;
    private bool _disposed;

    public TraceTabViewModel(MainWindowViewModel owner, ConnectionProfile profile) : base(owner, profile, Strings.TraceTabTitle)
    {
        _flush = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Flush());
        _flush.Stop();
        _autoStop = new DispatcherTimer(TimeSpan.FromMinutes(10), DispatcherPriority.Background, async (_, _) => await StopAsync(auto: true));
        _autoStop.Stop();
        Status = Strings.TraceIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.TimelineClockOutline;

    public IReadOnlyList<int> Thresholds { get; } = [0, 10, 50, 100, 250, 500, 1000, 5000];
    public IReadOnlyList<int> AutoStops { get; } = [5, 10, 30, 60, 0];

    [ObservableProperty] private int _thresholdMs = 100;
    [ObservableProperty] private int _autoStopMinutes = 10;
    [ObservableProperty] private bool _procedures = true;
    [ObservableProperty] private bool _triggers;
    [ObservableProperty] private bool _errors = true;
    [ObservableProperty] private string _filter = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private bool _isTracing;

    /// <summary>Juntar las sentencias que solo cambian en los valores («WHERE ID = 1» y «= 2»).</summary>
    [ObservableProperty] private bool _unifyValues = true;

    partial void OnUnifyValuesChanged(bool value) => Rebuild();

    /// <summary>Comparar dos períodos: el log guardado con que se compara lo de ahora (el «antes»).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBaseline))]
    [NotifyCanExecuteChangedFor(nameof(ClearBaselineCommand))]
    private string _baselineName = "";

    public bool HasBaseline => BaselineName.Length > 0;

    /// <summary>«Comparando con antes.log (35 consultas) · 4 más lentas, 2 nuevas, 1 ya no aparece».</summary>
    [ObservableProperty] private string _comparisonSummary = "";

    /// <summary>La comparación por consulta (para las pruebas y para elegir una fila).</summary>
    public IReadOnlyList<TraceQueryChange> Changes { get; private set; } = [];

    /// <summary>Cambió la comparación: la vista arma su grilla.</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>>? ComparisonChanged;

    /// <summary>
    /// «Comparar con un log…»: otro período (un log guardado antes, de FbStudio o de fbtracemgr) contra lo de ahora,
    /// consulta por consulta: cuáles están más lentas, más rápidas, cuáles son nuevas y cuáles ya no aparecen.
    /// </summary>
    public void CompareWith(string path)
    {
        try
        {
            _baseline = TraceParser.Parse(File.ReadLines(path)).Where(e => e.Kind is not ("TRACE_INIT" or "TRACE_FINI")).ToList();
            BaselineName = Path.GetFileName(path);
            Rebuild();
            Status = ComparisonSummary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Status = string.Format(Strings.TraceFailed, ex.Message);
        }
    }

    private bool CanClearBaseline() => HasBaseline;

    [RelayCommand(CanExecute = nameof(CanClearBaseline))]
    private void ClearBaseline()
    {
        _baseline = null;
        BaselineName = "";
        Rebuild();
    }

    /// <summary>El detalle de lo elegido en una grilla: SQL, plan, parámetros, tablas.</summary>
    [ObservableProperty] private string _detail = "";

    /// <summary>
    /// Sin SYSDBA puede que se vean solo las conexiones propias: el trace se inicia sin rol (Firebird 3 no acepta el rol en
    /// el servicio de trazas), así que RDB$ADMIN no cuenta.
    /// </summary>
    public bool SeesOnlyOwn => Profile is { } p && !p.User.Trim().Equals("SYSDBA", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<TraceEvent> Events => _events;
    public IReadOnlyList<TraceQueryGroup> Groups { get; private set; } = [];

    /// <summary>Llegaron eventos: la vista actualiza las dos grillas (análisis y eventos) conservando lo que se ve.</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>, ColumnInfo[], AvaloniaList<object[]>>? GridsChanged;

    /// <summary>El número de la sesión en el servidor (para las pruebas).</summary>
    internal int? SessionId => _session?.SessionId;

    /// <summary>Para las pruebas: cortar antes que a los minutos elegidos.</summary>
    internal TimeSpan? AutoStopAfter { get; set; }

    /// <summary>La última operación (para las pruebas).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    private bool CanStart() => !IsTracing && !_disposed && Profile is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public Task StartAsync() => Working = StartCoreAsync();

    private async Task StartCoreAsync()
    {
        if (Profile is not { } profile) return;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        if (profile.IsProduction)
        {
            var stops = AutoStopMinutes > 0 ? string.Format(Strings.TraceStopsAfter, AutoStopMinutes) : Strings.TraceStopsManual;
            if (!await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.TraceProduction, profile.Name, ThresholdMs, stops)],
                    Strings.TraceStart, Strings.Cancel, Strings.TraceProductionTitle))) return;
        }
        IsTracing = true;
        var session = new TraceSession(profile, password);
        try
        {
            var filter = string.IsNullOrWhiteSpace(Filter) ? null : "%" + TraceSession.NamePattern(Filter) + "%";
            await session.StartAsync(new TraceOptions(ThresholdMs, Procedures, Triggers, Errors, IncludeFilter: filter), _incoming.Enqueue);
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            IsTracing = false;
            Status = string.Format(Strings.TraceStartFailed, ex.Message.Split('\n', 2)[0].Trim());
            return;
        }
        if (_disposed)
        {
            await session.DisposeAsync();
            return;
        }
        _session = session;
        _startedAt = DateTime.Now;
        _flush.Start();
        _ = WatchAsync(session);
        if (AutoStopMinutes > 0)
        {
            _autoStop.Interval = AutoStopAfter ?? TimeSpan.FromMinutes(AutoStopMinutes);
            _autoStop.Start();
        }
        UpdateStatus();
    }

    /// <summary>
    /// Si la sesión termina sin que el usuario la detenga (la cortó el servidor, se cayó la conexión), la pestaña lo dice
    /// con el motivo que dio el servidor, en vez de seguir mostrándola activa.
    /// </summary>
    private async Task WatchAsync(TraceSession session)
    {
        var reason = await session.Ended;
        if (_disposed || !ReferenceEquals(_session, session)) return; // la detuvo el usuario o el corte automático
        _session = null;
        _autoStop.Stop();
        _flush.Stop();
        Flush();
        if (_parser.Flush() is { } last) Add(last);
        Rebuild();
        IsTracing = false;
        Status = string.Format(Strings.TraceEndedByServer, _events.Count, reason ?? Strings.TraceNoReason);
    }

    private bool CanStop() => IsTracing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    public Task StopAsync() => Working = StopAsync(auto: false);

    private async Task StopAsync(bool auto)
    {
        _autoStop.Stop();
        if (_session is not { } session) return;
        _session = null;
        await session.StopAsync();
        _flush.Stop();
        Flush();
        if (_parser.Flush() is { } last) Add(last);
        Rebuild();
        IsTracing = false;
        Status = auto ? string.Format(Strings.TraceAutoStopped, AutoStopMinutes, _events.Count) : string.Format(Strings.TraceStopped, _events.Count);
    }

    [RelayCommand]
    public void Clear()
    {
        _events.Clear();
        _log.Clear();
        _parser = new TraceParser();
        Detail = "";
        Rebuild();
        Status = IsTracing ? Status : Strings.TraceIntro;
    }

    /// <summary>Un log guardado (de FbStudio o de fbtracemgr): se lee y se analiza igual que lo que llega en vivo.</summary>
    public void OpenLog(string path)
    {
        if (IsTracing) return;
        try
        {
            _events.Clear();
            _log.Clear();
            foreach (var e in TraceParser.Parse(File.ReadLines(path))) Add(e);
            _parser = new TraceParser();
            Detail = "";
            Rebuild();
            Status = string.Format(Strings.TraceLogOpened, _events.Count, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Status = string.Format(Strings.TraceFailed, ex.Message);
        }
    }

    /// <summary>Lo que llegó en esta sesión, tal cual (sin la ruta de la base), para analizarlo después o mandarlo.</summary>
    public bool SaveLog(string path)
    {
        if (_log.Count == 0)
        {
            Status = Strings.TraceNothingToSave;
            return false;
        }
        File.WriteAllLines(path, _log);
        Status = string.Format(Strings.TraceSaved, path, _log.Count);
        return true;
    }

    public bool HasLog => _log.Count > 0;

    /// <summary>Pasa lo que llegó del servidor al parser y actualiza las grillas (en el hilo de la interfaz, de a tandas).</summary>
    private void Flush()
    {
        var any = false;
        while (_incoming.TryDequeue(out var line))
        {
            if (_log.Count < MaxLogLines) _log.Add(line);
            TraceEvent? e;
            try { e = _parser.Feed(line); }
            catch (Exception)
            {
                // Un renglón que no se entiende no frena la lectura: se descarta ese evento y se sigue con el próximo.
                _parser = new TraceParser();
                continue;
            }
            if (e is not null)
            {
                Add(e);
                any = true;
            }
        }
        if (any) Rebuild();
        if (IsTracing) UpdateStatus();
    }

    private void Add(TraceEvent e)
    {
        // Lo que no aporta al análisis ni a la lista: el inicio y el fin de la sesión.
        if (e.Kind is "TRACE_INIT" or "TRACE_FINI") return;
        _events.Add(e);
        if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
    }

    private void UpdateStatus()
    {
        if (Profile is not { } profile) return;
        Status = AutoStopMinutes > 0
            ? string.Format(Strings.TraceRunning, profile.Name, _startedAt, _events.Count, _startedAt.AddMinutes(AutoStopMinutes))
            : string.Format(Strings.TraceRunningNoLimit, profile.Name, _startedAt, _events.Count);
    }

    private static object V(long? n) => n is { } v ? v : DBNull.Value;

    private static string OneLine(string? sql, int max = 300)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(sql ?? "", @"\s+", " ").Trim();
        return text.Length > max ? text[..max] + "…" : text;
    }

    private static string Warning(TraceEvent e) => string.Join(" · ", new[]
    {
        e.Failed ? Strings.TraceWarnFailed : null,
        e.Error,
        e.HasFullScan ? string.Format(Strings.TraceWarnFullScan, string.Join(", ", e.Tables.Where(t => t.FullScan).Select(t => t.Table))) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Arma las dos grillas: el análisis por consulta y la lista de eventos (lo último arriba).</summary>
    private void Rebuild()
    {
        Groups = TraceAnalysis.Group(_events, UnifyValues);
        _groupRows.Clear();
        ColumnInfo[] groupColumns =
        [
            new(0, Strings.TraceColCount, "BIGINT"), new(1, Strings.TraceColTotalMs, "BIGINT"), new(2, Strings.TraceColAvgMs, "NUMERIC"),
            new(3, Strings.TraceColMaxMs, "BIGINT"), new(4, Strings.TraceColQuery, "VARCHAR"), new(5, Strings.TraceColReads, "BIGINT"),
            new(6, Strings.TraceColFetches, "BIGINT"), new(7, Strings.TraceColWrites, "BIGINT"), new(8, Strings.TraceColRecords, "BIGINT"),
            new(9, Strings.TraceColFailed, "BIGINT"), new(10, Strings.TraceColWarning, "VARCHAR"),
        ];
        var groupRows = new AvaloniaList<object[]>();
        foreach (var g in Groups)
        {
            var row = new object[]
            {
                (long)g.Count, g.TotalMs, Math.Round((decimal)g.AverageMs, 1), g.MaxMs, OneLine(g.Key), g.Reads, g.Fetches, g.Writes, g.Records,
                (long)g.Failed, g.FullScan ? Warning(g.Slowest) : g.Failed > 0 ? Strings.TraceWarnFailed : "",
            };
            _groupRows[row] = g;
            groupRows.Add(row);
        }

        _eventRows.Clear();
        ColumnInfo[] eventColumns =
        [
            new(0, Strings.TraceColTime, "TIMESTAMP"), new(1, Strings.TraceColMs, "BIGINT"), new(2, Strings.TraceColSql, "VARCHAR"),
            new(3, Strings.TraceColReads, "BIGINT"), new(4, Strings.TraceColFetches, "BIGINT"), new(5, Strings.TraceColWrites, "BIGINT"),
            new(6, Strings.TraceColRecords, "BIGINT"), new(7, Strings.TraceColUser, "VARCHAR"), new(8, Strings.TraceColAddress, "VARCHAR"),
            new(9, Strings.TraceColProcess, "VARCHAR"), new(10, Strings.TraceColWarning, "VARCHAR"),
        ];
        var eventRows = new AvaloniaList<object[]>();
        for (var i = _events.Count - 1; i >= 0; i--)
        {
            var e = _events[i];
            var row = new object[]
            {
                e.Time, V(e.Milliseconds), OneLine(e.Sql ?? e.Kind), V(e.Reads), V(e.Fetches), V(e.Writes), V(e.Records),
                (object?)e.User ?? DBNull.Value, (object?)e.Address ?? DBNull.Value, (object?)e.Process ?? DBNull.Value, Warning(e),
            };
            _eventRows[row] = e;
            eventRows.Add(row);
        }
        GridsChanged?.Invoke(groupColumns, groupRows, eventColumns, eventRows);
        RebuildComparison();
    }

    /// <summary>La grilla de la comparación con el log de antes (vacía si no hay con qué comparar).</summary>
    private void RebuildComparison()
    {
        _changeRows.Clear();
        Changes = _baseline is null ? [] : TraceComparison.Compare(TraceAnalysis.Group(_baseline, UnifyValues), Groups);
        ColumnInfo[] columns =
        [
            new(0, Strings.TraceColChange, "VARCHAR"), new(1, Strings.TraceColQuery, "VARCHAR"), new(2, Strings.TraceColCountBefore, "BIGINT"),
            new(3, Strings.TraceColCountNow, "BIGINT"), new(4, Strings.TraceColAvgBefore, "NUMERIC"), new(5, Strings.TraceColAvgNow, "NUMERIC"),
            new(6, Strings.TraceColAvgChange, "NUMERIC"), new(7, Strings.TraceColTotalBefore, "BIGINT"), new(8, Strings.TraceColTotalNow, "BIGINT"),
            new(9, Strings.TraceColTotalDifference, "BIGINT"), new(10, Strings.TraceColReadsBefore, "NUMERIC"), new(11, Strings.TraceColReadsNow, "NUMERIC"),
        ];
        var rows = new AvaloniaList<object[]>();
        foreach (var c in Changes)
        {
            var row = new object[]
            {
                ChangeLabel(c.Kind), OneLine(c.Key), Count(c.Before), Count(c.After), Avg(c.Before?.AverageMs), Avg(c.After?.AverageMs),
                c.AverageChange is { } change ? Math.Round((decimal)change * 100, 0) : DBNull.Value,
                c.Before is { } b ? b.TotalMs : DBNull.Value, c.After is { } a ? a.TotalMs : DBNull.Value, c.TotalDifference,
                ReadsPerRun(c.Before), ReadsPerRun(c.After),
            };
            _changeRows[row] = c;
            rows.Add(row);
        }
        ComparisonSummary = _baseline is null ? "" : string.Format(Strings.TraceComparisonSummary, BaselineName, Changes.Count(c => c.Before is not null),
            Changes.Count(c => c.After is not null), Changes.Count(c => c.Kind == TraceChangeKind.Slower), Changes.Count(c => c.Kind == TraceChangeKind.Faster),
            Changes.Count(c => c.Kind == TraceChangeKind.New), Changes.Count(c => c.Kind == TraceChangeKind.Gone));
        ComparisonChanged?.Invoke(columns, rows);

        static object Count(TraceQueryGroup? g) => g is null ? DBNull.Value : (long)g.Count;
        static object Avg(double? ms) => ms is { } v ? Math.Round((decimal)v, 1) : DBNull.Value;
        static object ReadsPerRun(TraceQueryGroup? g) => g is { Count: > 0 } x ? Math.Round((decimal)x.Reads / x.Count, 0) : DBNull.Value;
    }

    public static string ChangeLabel(TraceChangeKind kind) => kind switch
    {
        TraceChangeKind.New => Strings.TraceChangeNew,
        TraceChangeKind.Gone => Strings.TraceChangeGone,
        TraceChangeKind.Slower => Strings.TraceChangeSlower,
        TraceChangeKind.Faster => Strings.TraceChangeFaster,
        _ => Strings.TraceChangeSame,
    };

    /// <summary>Fila elegida en la comparación: los números de antes y de ahora, y la ejecución más lenta de ahora (o de antes).</summary>
    public void SelectChange(object[]? row)
    {
        if (row is null || !_changeRows.TryGetValue(row, out var c)) return;
        string Line(string label, TraceQueryGroup? g) => g is null
            ? string.Format(Strings.TraceDetailPeriodMissing, label)
            : string.Format(CultureInfo.CurrentCulture, Strings.TraceDetailPeriod, label, g.Count, g.TotalMs, g.AverageMs, g.MaxMs);
        Detail = ChangeLabel(c.Kind) + Environment.NewLine + Line(Strings.TraceBefore, c.Before) + Environment.NewLine + Line(Strings.TraceNow, c.After)
                 + Environment.NewLine + Describe((c.After ?? c.Before)!.Slowest);
    }

    /// <summary>Fila elegida en el análisis: el resumen y la ejecución más lenta.</summary>
    public void SelectGroup(object[]? row)
    {
        if (row is null || !_groupRows.TryGetValue(row, out var g)) return;
        Detail = string.Format(CultureInfo.CurrentCulture, Strings.TraceDetailGroup, g.Count, g.TotalMs, g.AverageMs, g.MaxMs)
                 + Environment.NewLine + Describe(g.Slowest);
    }

    /// <summary>Fila elegida en los eventos.</summary>
    public void SelectEvent(object[]? row)
    {
        if (row is not null && _eventRows.TryGetValue(row, out var e)) Detail = Describe(e);
    }

    /// <summary>El evento entero en texto: quién, cuánto, el SQL, el plan, los parámetros y las tablas.</summary>
    public static string Describe(TraceEvent e)
    {
        var b = new StringBuilder();
        b.AppendLine(string.Format(CultureInfo.CurrentCulture, Strings.TraceDetailHeader, e.Time, e.Kind, e.Attachment?.ToString() ?? "—",
            e.User ?? "—", e.Address ?? "—", e.Process ?? "—"));
        if (e.Milliseconds is { } ms)
            b.AppendLine(string.Format(CultureInfo.CurrentCulture, Strings.TraceDetailPerf, ms, e.Reads ?? 0, e.Fetches ?? 0, e.Writes ?? 0, e.Records ?? 0));
        if (e.Error is { } error) b.AppendLine().AppendLine(Strings.TraceDetailError).AppendLine(error);
        if (e.Sql is { } sql) b.AppendLine().AppendLine(sql);
        if (e.Plan is { } plan) b.AppendLine().AppendLine(Strings.TraceDetailPlan).AppendLine(plan);
        if (e.Parameters is { } parameters) b.AppendLine().AppendLine(Strings.TraceDetailParams).AppendLine(parameters);
        if (e.Tables.Count > 0)
        {
            b.AppendLine().AppendLine(Strings.TraceDetailTables);
            foreach (var t in e.Tables)
                b.AppendLine(string.Format(CultureInfo.CurrentCulture, Strings.TraceDetailTable, t.Table, t.Natural, t.Index, t.Update, t.Insert, t.Delete));
        }
        return b.ToString().TrimEnd();
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _flush.Stop();
        _autoStop.Stop();
        if (_session is { } session)
        {
            _session = null;
            await session.DisposeAsync();
        }
        await base.DisposeAsync();
    }
}
