using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Monitoring;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Firebird;
using FbStudio.Workspace;
using Material.Icons;
using C = FbStudio.Core.Resources.CoreStrings;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Pestaña «Monitoreo» de una base (F-1): los contadores de transacciones (OIT, OAT, OST, la próxima y la brecha),
/// las conexiones, las transacciones y las sentencias en curso, con refresco automático mientras la pestaña está a la
/// vista. Cada refresco es una transacción nueva que se confirma enseguida. Permite cortar una conexión o cancelar
/// una sentencia, siempre preguntando antes.
/// </summary>
/// <summary>Texto del selector de refresco: «A mano» o «Cada 5 s».</summary>
public static class MonitorIntervals
{
    public static readonly Avalonia.Data.Converters.IValueConverter Label =
        new Avalonia.Data.Converters.FuncValueConverter<int, string>(s => s <= 0 ? Strings.MonManual : string.Format(Strings.MonEvery, s));
}

/// <summary>Una fila de la lista de consultas: un título de grupo («Predefinidas», «Propias (20)») o una consulta.</summary>
public sealed record MonitorQueryItem(bool IsHeader, string Caption, MonitorPreset? Preset)
{
    public Avalonia.Media.FontWeight Weight => IsHeader ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
    public double Indent => IsHeader ? 0 : 14;
    public MaterialIconKind Icon => IsHeader ? MaterialIconKind.FolderOutline : Preset?.IsCustom == true ? MaterialIconKind.FileDocumentEditOutline : MaterialIconKind.FileDocumentOutline;
}

public sealed partial class MonitorTabViewModel : EditorTabViewModel
{
    private readonly DispatcherTimer _timer;
    private MonitorSession? _monitor;
    private bool _refreshing;
    private string? _shownPreset;
    private bool _disposed;
    private MonitorColumnMap _columns = MonitorColumnMap.None;

    public MonitorTabViewModel(MainWindowViewModel owner, ConnectionProfile? profile) : base(owner, profile, Strings.MonitorTabTitle)
    {
        _selectedPreset = MonitorQueries.Default; // abre siempre en «Transacciones»
        RebuildItems();
        owner.MonitorQueries.Changed += OnQueriesChanged;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(_interval), DispatcherPriority.Background, async (_, _) => await TickAsync());
        _timer.Stop(); // arranca cuando la pestaña está a la vista
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IsSelected)) UpdateTimer(); };
        Status = Strings.MonitorIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.MonitorDashboard;

    /// <summary>Las que vienen con FbStudio y, debajo, las propias.</summary>
    public IReadOnlyList<MonitorPreset> Presets { get; private set; } = [];

    /// <summary>La lista de la izquierda: títulos de grupo y consultas.</summary>
    [ObservableProperty] private IReadOnlyList<MonitorQueryItem> _items = [];

    [ObservableProperty] private MonitorQueryItem? _selectedItem;

    partial void OnSelectedItemChanged(MonitorQueryItem? value)
    {
        if (value is { Preset: { } preset }) SelectedPreset = preset;
        else if (value is { IsHeader: true }) SelectedItem = Items.FirstOrDefault(i => i.Preset?.Key == SelectedPreset.Key); // los títulos no se eligen
    }

    private void RebuildItems()
    {
        var custom = Owner.MonitorQueries.Load()
            .Select(q => new MonitorPreset("custom:" + q.Id, q.Name, q.Sql)
            {
                AttachmentColumn = q.AttachmentColumn, StatementColumn = q.StatementColumn, SqlColumn = q.SqlColumn, CustomId = q.Id,
            }).ToList();
        Presets = [.. MonitorQueries.Presets, .. custom];
        var items = new List<MonitorQueryItem> { new(true, Strings.MonPredefined, null) };
        items.AddRange(MonitorQueries.Presets.Select(p => new MonitorQueryItem(false, p.Name, p)));
        items.Add(new MonitorQueryItem(true, string.Format(Strings.MonCustomGroup, custom.Count), null));
        items.AddRange(custom.Select(p => new MonitorQueryItem(false, p.Name, p)));
        Items = items;
        var current = Presets.FirstOrDefault(p => p.Key == SelectedPreset.Key) ?? Presets.First(p => p.Key == MonitorQueries.DefaultKey);
        if (!ReferenceEquals(current, SelectedPreset)) SelectedPreset = current;
        SelectedItem = Items.FirstOrDefault(i => i.Preset?.Key == current.Key);
        EditQueryCommand.NotifyCanExecuteChanged();
        DeleteQueryCommand.NotifyCanExecuteChanged();
    }

    private void OnQueriesChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildItems);

    /// <summary>Cada cuántos segundos refresca (0 = a mano).</summary>
    public IReadOnlyList<int> Intervals { get; } = [0, 2, 5, 10, 30];

    [ObservableProperty] private MonitorPreset _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassicNote), nameof(HasClassicNote))]
    private int _interval = 5;

    /// <summary>Si el intervalo lo eligió el usuario (en Classic solo se lo espacia si no lo tocó).</summary>
    private bool _intervalChosen;

    /// <summary>Classic, SuperClassic o SuperServer (F-16): se averigua en el primer refresco.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerModeText), nameof(ServerModeTip), nameof(ClassicNote), nameof(HasClassicNote))]
    private ServerModeEvidence? _modeEvidence;

    private bool _serverModeAsked;

    private bool IsClassic => ModeEvidence?.Mode == ServerMode.Classic;

    public string ServerModeText => ModeEvidence is { } m ? ServerModes.Label(m.Mode) : "—";

    public string? ServerModeTip => ModeEvidence is { } m
        ? string.Join(Environment.NewLine, new[]
        {
            ServerModes.Description(m.Mode),
            m.Mode == ServerMode.Unknown ? null : m.Reason,
            IsClassic ? string.Format(Strings.MonClassicRefresh, ServerModes.ClassicRefreshSeconds) : null,
        }.Where(s => s is not null))
        : null;

    /// <summary>En Classic cada refresco le pide los datos a cada proceso: se avisa si el intervalo es corto.</summary>
    public string? ClassicNote => IsClassic && Interval > 0 && Interval < ServerModes.ClassicRefreshSeconds
        ? string.Format(Strings.MonClassicNote, Interval, ServerModes.ClassicRefreshSeconds)
        : null;

    public bool HasClassicNote => ClassicNote is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextText), nameof(OitText), nameof(OatText), nameof(OstText), nameof(GapText), nameof(OatAgeText),
        nameof(CountsText), nameof(OatWarning), nameof(HasOatWarning), nameof(VisibilityWarning), nameof(HasVisibilityWarning),
        nameof(IsOatWarn), nameof(IsOatDanger), nameof(OatHolderText), nameof(OatInfo), nameof(HasOatInfo),
        nameof(WaitingText), nameof(IsLockWarn), nameof(IsLockDanger), nameof(BackupWarning), nameof(HasBackupWarning))]
    private MonitorSummary? _summary;

    [ObservableProperty] private string _lastRefresh = "";

    /// <summary>Fila elegida en la grilla (la vista la avisa).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSql), nameof(HasSelectedSql))]
    [NotifyCanExecuteChangedFor(nameof(KillCommand), nameof(CancelStatementCommand))]
    private object[]? _selectedRow;

    public AvaloniaList<object[]> Results { get; private set; } = [];
    public ColumnInfo[] ResultColumns { get; private set; } = [];

    /// <summary>Llegó un resultado: la vista lo muestra (conservando orden, filtros y fila elegida si es la misma consulta).</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>, bool>? ResultsChanged;

    private static string N(long n) => n.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    public string NextText => Summary is { } s ? N(s.NextTransaction) : "—";
    public string OitText => Summary is { } s ? N(s.OldestInteresting) : "—";
    public string OatText => Summary is { } s ? N(s.OldestActive) : "—";
    public string OstText => Summary is { } s ? N(s.OldestSnapshot) : "—";
    public string GapText => Summary is { } s ? N(s.Gap) : "—";

    public string OatAgeText => Summary?.OldestActiveAge is { } age ? Age(age) : Summary is null ? "—" : Strings.MonOatNotVisible;

    public string CountsText => Summary is { } s ? string.Format(Strings.MonCounts, s.Attachments, s.Transactions, s.RunningStatements) : "";

    public bool HasOatWarning => OatWarning is not null;

    /// <summary>OAT abierta más que los umbrales de Opciones (ámbar, rojo); un backup o un sweep no alarma.</summary>
    public bool IsOatWarn => !OatExpected && Summary?.OldestActiveAge is { } age && age > OatWarnAfter && age <= OatDangerAfter;
    public bool IsOatDanger => !OatExpected && Summary?.OldestActiveAge is { } age && age > OatDangerAfter;

    private TimeSpan OatWarnAfter => TimeSpan.FromMinutes(Owner.Options.OatWarnMinutes);
    private TimeSpan OatDangerAfter => TimeSpan.FromMinutes(Owner.Options.OatDangerMinutes);

    /// <summary>La OAT es de un backup o un sweep y la opción dice no alarmar: se informa sin color.</summary>
    private bool OatExpected => Summary is { OatIsMaintenance: true } && Owner.Options.OatIgnoreMaintenance;

    /// <summary>Quién tiene la OAT: «ETL_NOCHE · 192.0.2.33 · PC-LAB-03 · python.exe» (el equipo, si el programa lo informa).</summary>
    public string OatHolderText => Summary is { } s
        ? string.Join(" · ", new[] { s.OatUser, s.OatIp, s.OatHost, ProcessName(s.OatProcess) }.Where(x => !string.IsNullOrEmpty(x)))
        : "";

    private static string? ProcessName(string? process) =>
        string.IsNullOrWhiteSpace(process) ? null : process.Trim().Replace('\\', '/').Split('/')[^1];

    /// <summary>La OAT abierta hace mucho frena la recolección de basura: se avisa quién la tiene.</summary>
    public string? OatWarning => Summary is { } s && !OatExpected && s.OldestActiveAge is { } age && age > OatWarnAfter
        ? string.Format(Strings.MonOatWarning, N(s.OldestActive), Age(age), N(s.Gap))
          + (OatHolderText.Length > 0 ? " " + string.Format(Strings.MonOatHolder, OatHolderText) : "")
        : null;

    /// <summary>La OAT vieja es de un backup o un sweep: se dice, sin alarma.</summary>
    public string? OatInfo => Summary is { } s && OatExpected && s.OldestActiveAge is { } age && age > OatWarnAfter
        ? string.Format(Strings.MonOatMaintenance, N(s.OldestActive), Age(age), OatHolderText)
        : null;

    public bool HasOatInfo => OatInfo is not null;

    /// <summary>Esperando un registro bloqueado: «2 · la más larga 45 s».</summary>
    public string WaitingText => Summary is { } s
        ? s.WaitingStatements == 0 ? "0" : string.Format(Strings.MonWaitingText, s.WaitingStatements, Age(TimeSpan.FromSeconds(s.LongestWaitSeconds ?? 0)))
        : "—";

    public bool IsLockWarn => Summary is { WaitingStatements: > 0 } s && TimeSpan.FromSeconds(s.LongestWaitSeconds ?? 0) < MonitorQueries.LongWait;
    public bool IsLockDanger => Summary is { WaitingStatements: > 0 } s && TimeSpan.FromSeconds(s.LongestWaitSeconds ?? 0) >= MonitorQueries.LongWait;

    /// <summary>Clic en una tarjeta: la consulta que explica ese número.</summary>
    public void ShowPreset(string key)
    {
        if (Presets.FirstOrDefault(p => p.Key == key) is { } preset) SelectedPreset = preset;
    }

    public bool HasVisibilityWarning => VisibilityWarning is not null;

    /// <summary>La base quedó en modo backup de nbackup (F-29): el delta crece mientras dure.</summary>
    public string? BackupWarning => Summary is { } s ? BackupStates.Warning(s.BackupState) : null;
    public bool HasBackupWarning => BackupWarning is not null;
    public string? VisibilityWarning => Summary is { SeesEveryone: false } ? Strings.MonOnlyYours : null;

    public string? SelectedSql => SelectedRow is { } row && _columns.Sql is { } i && i < row.Length && row[i] is string sql ? sql.Trim() : null;
    public bool HasSelectedSql => SelectedSql is not null;

    private static string Age(TimeSpan t) =>
        t.TotalMinutes < 1 ? string.Format(Strings.MonAgeSeconds, (int)t.TotalSeconds)
        : t.TotalHours < 1 ? string.Format(Strings.MonAgeMinutes, (int)t.TotalMinutes)
        : t.TotalDays < 1 ? string.Format(Strings.MonAgeHours, (int)t.TotalHours, t.Minutes)
        : string.Format(Strings.MonAgeDays, (int)t.TotalDays, t.Hours);

    partial void OnSelectedPresetChanged(MonitorPreset value)
    {
        SelectedRow = null;
        if (SelectedItem?.Preset?.Key != value.Key) SelectedItem = Items.FirstOrDefault(i => i.Preset?.Key == value.Key);
        EditQueryCommand.NotifyCanExecuteChanged();
        DeleteQueryCommand.NotifyCanExecuteChanged();
        _ = RefreshAsync();
    }

    partial void OnIntervalChanged(int value)
    {
        _intervalChosen = true;
        UpdateTimer();
    }

    /// <summary>
    /// La primera vez, averigua el tipo de servidor; en Classic el refresco automático pasa a cada 10 s, salvo que el
    /// usuario ya haya elegido otro intervalo.
    /// </summary>
    private async Task DetectServerModeAsync(MonitorSession monitor)
    {
        if (_serverModeAsked) return;
        _serverModeAsked = true;
        try { ModeEvidence = await monitor.ServerModeAsync(); }
        catch (Exception) { /* el tipo es informativo: sin él, el monitoreo sigue igual */ }
        if (IsClassic && !_intervalChosen && Interval is > 0 and < ServerModes.ClassicRefreshSeconds)
        {
            Interval = ServerModes.ClassicRefreshSeconds;
            _intervalChosen = false;
        }
    }

    private void UpdateTimer()
    {
        _timer.Stop();
        if (_disposed || Interval <= 0 || !IsSelected) return;
        _timer.Interval = TimeSpan.FromSeconds(Interval);
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (!_refreshing && IsSelected) await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_refreshing || _disposed || Profile is not { } profile) return;
        _refreshing = true;
        try
        {
            var monitor = Monitor(profile);
            var preset = SelectedPreset;
            Summary = await monitor.SummaryAsync();
            await DetectServerModeAsync(monitor);
            var result = await monitor.QueryAsync(preset.Sql);
            var sameQuery = _shownPreset == preset.Key;
            _shownPreset = preset.Key;
            ResultColumns = result.Columns;
            _columns = MonitorColumns.Resolve(preset, result.Columns);
            MarkOat(result);
            Results = new AvaloniaList<object[]>(result.Rows);
            ResultsChanged?.Invoke(ResultColumns, Results, sameQuery);
            LastRefresh = string.Format(Strings.MonRefreshedAt, DateTime.Now);
            Status = string.Format(Strings.MonStatus, preset.Name, result.Rows.Count) + (Interval > 0 ? string.Format(Strings.MonAutoRefresh, Interval) : "");
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.MonFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// El aviso «es la OAT» / «tiene la OAT» llega del servidor sin el símbolo (en una conexión ISO8859_1 el ⚠ vuelve
    /// como «?»): se lo agrega acá, al mostrar.
    /// </summary>
    private static void MarkOat(MonitorResult result)
    {
        var note = Array.FindIndex(result.Columns, c => c.Name == C.MonColNote);
        if (note < 0) return;
        foreach (var row in result.Rows)
            if (row[note] is string text && (text.Trim() == C.MonIsOat || text.Trim() == C.MonHasOat))
                row[note] = "⚠ " + text.Trim();
    }

    private long? SelectedId(int? ordinal) =>
        SelectedRow is { } row && ordinal is { } i && i < row.Length && row[i] is not (null or DBNull) ? Convert.ToInt64(row[i]) : null;

    /// <summary>
    /// Valor de la fila elegida en la primera columna que se llame como alguno de los nombres (para armar los mensajes:
    /// sirve también con las consultas propias, que traen USER_NAME, PROCESS, "Remote Address"…).
    /// </summary>
    private string? Column(params string[] headers)
    {
        var i = MonitorColumns.Find(ResultColumns, null, headers);
        return SelectedRow is { } row && i is { } c && c < row.Length && row[c] is not (null or DBNull) ? Convert.ToString(row[c])?.Trim() : null;
    }

    /// <summary>
    /// Si la conexión es de esta PC: por el nombre del equipo o, si la consulta no lo trae (las predefinidas muestran la
    /// dirección, «IP/puerto»), por la IP.
    /// </summary>
    internal static bool IsThisMachine(string? host, string? address)
    {
        if (string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
        var ip = address?.Split('/')[0].Trim();
        if (string.IsNullOrEmpty(ip)) return false;
        if (ip is "127.0.0.1" or "::1" or "localhost") return true;
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.ToString() == ip);
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return false;
        }
    }

    private string? UserColumn() => Column(C.MonColUser, "USER_NAME", "MON$USER", "USER", "USUARIO");
    private string? HostColumn() => Column(C.MonColHost, "MON$REMOTE_HOST", "REMOTE_HOST");
    private string? IpColumn() => Column(C.MonColIp, "IP", C.MonColAddress, "MON$REMOTE_ADDRESS", "REMOTE_ADDRESS");
    private string? ProcessColumn() => Column(C.MonColProcess, "MON$REMOTE_PROCESS", "REMOTE_PROCESS", "PROCESS", "PROGRAMA");

    private bool CanKill() =>
        Profile is { ReadOnly: false } && SelectedId(_columns.Attachment) is { } id && id != Summary?.OwnAttachment;

    private bool CanCancelStatement() => Profile is { ReadOnly: false } && SelectedId(_columns.Statement) is not null;

    /// <summary>Corta la conexión elegida (su transacción abierta se deshace). Pregunta antes, con los datos de la conexión.</summary>
    [RelayCommand(CanExecute = nameof(CanKill))]
    private async Task KillAsync()
    {
        if (Profile is not { } profile || SelectedId(_columns.Attachment) is not { } id) return;
        // Los datos de la conexión, del servidor: una consulta propia (o importada) podría mostrar otros al lado del número.
        AttachmentWho? real = null;
        try { real = await Monitor(profile).AttachmentAsync(id); }
        catch (Exception) { /* si no se pueden leer, quedan los de la grilla */ }
        var (user, address, host, process) = real is { } r
            ? (r.User, r.Address, r.Host, r.Process)
            : (UserColumn(), IpColumn(), HostColumn(), ProcessColumn());
        var who = string.Join(" · ", new[] { user, address, host, process }.Where(s => !string.IsNullOrEmpty(s)));
        var reasons = new List<string> { string.Format(Strings.MonKillConfirm, id, who) };
        if (process?.StartsWith("FbStudio", StringComparison.Ordinal) == true && IsThisMachine(host, address))
            reasons.Add(Strings.MonKillFbStudio);
        if (profile.IsProduction) reasons.Add(string.Format(Strings.MonKillProduction, profile.Name));
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, Strings.MonKill, Strings.MonDontKill, Strings.MonKillTitle)))
            return;
        string message;
        try
        {
            message = await Monitor(profile).KillAttachmentAsync(id) ? string.Format(Strings.MonKilled, id) : string.Format(Strings.MonKillNotFound, id);
        }
        catch (Exception ex)
        {
            message = string.Format(Strings.MonFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        await RefreshAsync();
        Status = message; // después del refresco, que si no lo pisa
    }

    /// <summary>Cancela la sentencia elegida sin cortar su conexión (a quien la ejecutaba le llega un error).</summary>
    [RelayCommand(CanExecute = nameof(CanCancelStatement))]
    private async Task CancelStatementAsync()
    {
        if (Profile is not { } profile || SelectedId(_columns.Statement) is not { } id) return;
        var sql = SelectedSql is { } s ? (s.Length > 300 ? s[..300] + "…" : s) : "";
        var reasons = new List<string> { string.Format(Strings.MonCancelConfirm, id, UserColumn() ?? "?", sql) };
        if (profile.IsProduction) reasons.Add(string.Format(Strings.MonKillProduction, profile.Name));
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, Strings.MonCancelStatement, Strings.MonDontCancel, Strings.MonCancelTitle)))
            return;
        string message;
        try
        {
            message = await Monitor(profile).CancelStatementAsync(id) ? string.Format(Strings.MonCancelled, id) : string.Format(Strings.MonCancelNotFound, id);
        }
        catch (Exception ex)
        {
            message = string.Format(Strings.MonFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        await RefreshAsync();
        Status = message; // después del refresco, que si no lo pisa
    }

    // ---------- consultas propias ----------

    /// <summary>Nueva consulta propia, en blanco o copiando la elegida (para partir de una predefinida).</summary>
    [RelayCommand]
    private async Task NewQueryAsync(bool copySelected = false)
    {
        var editor = copySelected
            ? new MonitorQueryEditorViewModel(null, CloneName(SelectedPreset.Name), SelectedPreset.Sql, TestQueryAsync)
            {
                AttachmentColumn = SelectedPreset.AttachmentColumn ?? "",
                StatementColumn = SelectedPreset.StatementColumn ?? "",
                SqlColumn = SelectedPreset.SqlColumn ?? "",
            }
            : new MonitorQueryEditorViewModel(null, "", Strings.MonNewQuerySql, TestQueryAsync);
        await EditAndSaveAsync(editor);
    }

    /// <summary>«X (copia)», o «X (copia 2)» si ya hay una.</summary>
    private string CloneName(string name)
    {
        var names = Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = string.Format(Strings.MonCopyOf, name);
        for (var n = 2; names.Contains(candidate); n++) candidate = string.Format(Strings.CopyNameN, name, n);
        return candidate;
    }

    private bool CanEditQuery() => SelectedPreset.IsCustom;

    [RelayCommand(CanExecute = nameof(CanEditQuery))]
    private async Task EditQueryAsync()
    {
        if (SelectedPreset.CustomId is not { } id || Owner.MonitorQueries.Load().FirstOrDefault(q => q.Id == id) is not { } query) return;
        await EditAndSaveAsync(new MonitorQueryEditorViewModel(query, query.Name, query.Sql, TestQueryAsync));
    }

    private async Task EditAndSaveAsync(MonitorQueryEditorViewModel editor)
    {
        if (!await Owner.EditMonitorQueryAsync(editor)) return;
        var query = editor.ToQuery();
        Owner.MonitorQueries.Upsert(query);
        RebuildItems();
        SelectedPreset = Presets.First(p => p.CustomId == query.Id);
        Status = string.Format(Strings.MonQuerySaved, query.Name);
    }

    [RelayCommand(CanExecute = nameof(CanEditQuery))]
    private async Task DeleteQueryAsync()
    {
        if (SelectedPreset.CustomId is not { } id) return;
        var name = SelectedPreset.Name;
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.MonDeleteQueryConfirm, name)], Strings.ProfilesDelete, Strings.Cancel, Strings.MonDeleteQueryTitle)))
            return;
        Owner.MonitorQueries.Delete(id);
        SelectedPreset = MonitorQueries.Default;
        RebuildItems();
        Status = string.Format(Strings.MonQueryDeleted, name);
    }

    /// <summary>Para compartir en el equipo: exporta las consultas propias a un archivo.</summary>
    public int ExportQueries(string path)
    {
        var all = Owner.MonitorQueries.Load();
        Owner.MonitorQueries.Export(path, all);
        Status = string.Format(Strings.MonExported, all.Count, path);
        return all.Count;
    }

    public int ImportQueries(string path)
    {
        try
        {
            var count = Owner.MonitorQueries.Import(path);
            RebuildItems();
            Status = string.Format(Strings.MonImported, count, path);
            return count;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.MonImportFailed, ex.Message.Split('\n', 2)[0].Trim());
            return 0;
        }
    }

    /// <summary>«Probar» del editor: corre el SQL con la conexión del monitoreo (en una transacción de solo lectura).</summary>
    private Task<MonitorResult> TestQueryAsync(string sql) =>
        Profile is { } profile ? Monitor(profile).QueryAsync(sql) : throw new InvalidOperationException(Strings.MonitorPickDatabase);

    private MonitorSession Monitor(ConnectionProfile profile)
    {
        if (_monitor is not null && _monitor.Profile.Id == profile.Id) return _monitor;
        var password = Owner.ProfileStore.GetPassword(profile.Id)
                       ?? throw new InvalidOperationException(string.Format(Strings.NoSavedPassword, profile.Name));
        ModeEvidence = null;
        _serverModeAsked = false;
        return _monitor = new MonitorSession(profile, password);
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _timer.Stop();
        Owner.MonitorQueries.Changed -= OnQueriesChanged;
        if (_monitor is not null) await _monitor.DisposeAsync();
        _monitor = null;
        await base.DisposeAsync();
    }
}
