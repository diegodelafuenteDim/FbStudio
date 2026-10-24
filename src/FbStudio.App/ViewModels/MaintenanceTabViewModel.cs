using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

public enum MaintenanceTask
{
    Backup,
    Restore,
    EmptyCopy,
    Validate,
    Sweep,
    NBackup,
    Properties,
}

/// <summary>Una tabla de la lista «copiar con datos» de la base vacía.</summary>
public sealed partial class TableChoice(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] private bool _isChecked;
}

/// <summary>Una confirmación fuerte: hay que escribir <see cref="Expected"/> para habilitar el botón.</summary>
public sealed record TypedConfirm(string Title, IReadOnlyList<string> Reasons, string Expected, string RunText);

/// <summary>
/// Pestaña «Mantenimiento» de una base: backup, restore, base vacía desde el script, validación en línea y sweep.
/// <list type="bullet">
/// <item>Las rutas son del servidor (como las ve Firebird), no de esta PC.</item>
/// <item>El backup y el restore los hace el servidor: si FbStudio se cierra, siguen. Cancelar solo deja de mirar;
/// «Comprobar estado» dice después si terminaron.</item>
/// <item>El restore va siempre a un archivo nuevo: la base del perfil no se pisa nunca; reemplazar otro archivo pide
/// escribir su nombre.</item>
/// <item>Con un registro de solo lectura no se ofrece restore, base vacía ni sweep; el sweep en producción pide escribir
/// el nombre de la base.</item>
/// </list>
/// </summary>
public sealed partial class MaintenanceTabViewModel : EditorTabViewModel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly StringBuilder _pending = new();
    private CancellationTokenSource? _cts;
    private DateTime? _backupStartedAt;
    private List<TableChoice> _allTables = [];
    private bool _flushQueued;
    private bool _disposed;

    public MaintenanceTabViewModel(MainWindowViewModel owner, ConnectionProfile profile) : base(owner, profile, Strings.MaintTabTitle)
    {
        Status = Strings.MaintIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.DatabaseCogOutline;

    /// <summary>Tamaños de página para el restore (0 = el mismo que tenía la base).</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [0, 4096, 8192, 16384, 32768];

    public IReadOnlyList<string> PageSizeLabels { get; } = PageSizes.Select(p => p == 0 ? Strings.MaintPageSizeSame : p.ToString("N0")).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTask), nameof(IsBackup), nameof(IsRestore), nameof(IsEmptyCopy), nameof(IsValidate), nameof(IsSweep),
        nameof(IsNBackup), nameof(IsProperties), nameof(IsBlockedReadOnly), nameof(RunText), nameof(CanCheckState))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CheckStateCommand))]
    private int _selectedTaskIndex;

    public MaintenanceTask CurrentTask => (MaintenanceTask)Math.Clamp(SelectedTaskIndex, 0, 6);
    public bool IsBackup => CurrentTask == MaintenanceTask.Backup;
    public bool IsRestore => CurrentTask == MaintenanceTask.Restore;
    public bool IsEmptyCopy => CurrentTask == MaintenanceTask.EmptyCopy;
    public bool IsValidate => CurrentTask == MaintenanceTask.Validate;
    public bool IsSweep => CurrentTask == MaintenanceTask.Sweep;

    /// <summary>nbackup: por ahora solo se mira el estado y el historial (F-29, paso 1); no hay nada que ejecutar.</summary>
    public bool IsNBackup => CurrentTask == MaintenanceTask.NBackup;

    /// <summary>Intervalo de sweep, escrituras forzadas, solo lectura y caché (F-34).</summary>
    public bool IsProperties => CurrentTask == MaintenanceTask.Properties;

    /// <summary>Como estaban al leerlas (null hasta elegir la tarea).</summary>
    private DatabaseSettings? _properties;

    [ObservableProperty] private decimal? _propSweepInterval;
    [ObservableProperty] private decimal? _propPageBuffers;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsForcedWritesOff))] private bool _propForcedWrites;
    [ObservableProperty] private bool _propReadOnly;
    [ObservableProperty] private bool _propertiesLoaded;

    public bool IsForcedWritesOff => PropertiesLoaded && !PropForcedWrites;

    partial void OnPropertiesLoadedChanged(bool value) => OnPropertyChanged(nameof(IsForcedWritesOff));

    /// <summary>Restore, base vacía y sweep no se ofrecen con un registro de solo lectura.</summary>
    public bool IsBlockedReadOnly => Profile?.ReadOnly == true && CurrentTask is MaintenanceTask.Restore or MaintenanceTask.EmptyCopy or MaintenanceTask.Sweep
        or MaintenanceTask.Properties;

    public string RunText => CurrentTask switch
    {
        MaintenanceTask.Backup => Strings.MaintRunBackup,
        MaintenanceTask.Restore => Strings.MaintRunRestore,
        MaintenanceTask.EmptyCopy => Strings.MaintRunEmptyCopy,
        MaintenanceTask.Validate => Strings.MaintRunValidate,
        MaintenanceTask.Properties => Strings.MaintRunProperties,
        _ => Strings.MaintRunSweep,
    };

    /// <summary>«Base clinica · página de 8.192 · WIN1252»: la base como la nombra el registro, nunca su ruta (D-54).</summary>
    [ObservableProperty] private string _databaseInfo = "";

    /// <summary>Dónde está la base en el servidor: solo para taparla en lo que se muestra.</summary>
    private string? _serverPath;

    [ObservableProperty] private string _backupFile = "";
    [ObservableProperty] private bool _backupMetadataOnly;
    [ObservableProperty] private bool _backupNoGarbageCollect;
    [ObservableProperty] private bool _backupIgnoreChecksums;
    [ObservableProperty] private bool _backupNonTransportable;

    /// <summary>Tablas que van sin sus datos, escritas a mano («LOG_ACCESOS, AUDITORIA»).</summary>
    [ObservableProperty] private string _backupSkipData = "";

    [ObservableProperty] private string _restoreSource = "";
    [ObservableProperty] private string _restoreTarget = "";
    [ObservableProperty] private bool _restoreReplace;
    [ObservableProperty] private bool _restoreMetadataOnly;
    [ObservableProperty] private bool _restoreDeactivateIndexes;
    [ObservableProperty] private int _restorePageSizeIndex;
    [ObservableProperty] private bool _restoreNoValidity;
    [ObservableProperty] private bool _restoreOneAtATime;
    [ObservableProperty] private bool _restoreNoShadow;
    [ObservableProperty] private string _restoreSkipData = "";

    /// <summary>Validar solo estas tablas (vacío: toda la base).</summary>
    [ObservableProperty] private string _validateTables = "";

    [ObservableProperty] private string _emptyTarget = "";
    [ObservableProperty] private bool _emptyGeneratorValues = true;

    /// <summary>Las tablas que se ven con el filtro.</summary>
    [ObservableProperty] private IReadOnlyList<TableChoice> _visibleTables = [];

    [ObservableProperty] private string _tableFilter = "";

    /// <summary>«3 elegidas (se agregan solas las que referencian sus FK)».</summary>
    [ObservableProperty] private string _chosenText = "";

    [ObservableProperty] private bool _tablesLoading;

    /// <summary>La base que dejó el último restore o base vacía (para registrarla).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegisterNew))]
    [NotifyCanExecuteChangedFor(nameof(RegisterNewCommand))]
    private string? _newDatabasePath;

    public bool CanRegisterNew => NewDatabasePath is not null && !IsRunning;

    public bool CanCheckState => IsBackup || IsRestore || IsNBackup || IsProperties;

    /// <summary>Lo último leído de nbackup (null hasta elegir la tarea).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NBackupWarning), nameof(HasNBackupWarning), nameof(NBackupStateText), nameof(NBackupDeltaText),
        nameof(NBackupHistoryTitle), nameof(HasNBackupHistory), nameof(NBackupHistory))]
    private NBackupStatus? _nBackup;

    public string? NBackupWarning => NBackup is { } n ? BackupStates.Warning(n.State) : null;
    public bool HasNBackupWarning => NBackupWarning is not null;
    public string NBackupStateText => NBackup is { State: BackupState.Normal } ? Strings.MaintNBackupStateNormal : "";
    public string NBackupDeltaText => NBackup is null ? "" : NBackup.CustomDeltaFile ? Strings.MaintNBackupDeltaCustom : Strings.MaintNBackupDeltaDefault;
    public IReadOnlyList<NBackupEntry> NBackupHistory => NBackup?.History ?? [];
    public bool HasNBackupHistory => NBackupHistory.Count > 0;
    public string NBackupHistoryTitle => NBackup is null ? "" : HasNBackupHistory
        ? string.Format(Strings.MaintNBackupHistory, NBackupHistory.Count)
        : Strings.MaintNBackupNoHistory;

    public IReadOnlyList<TableChoice> AllTables => _allTables;

    /// <summary>La última tarea lanzada (para las pruebas).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    partial void OnSelectedTaskIndexChanged(int value)
    {
        if (CurrentTask == MaintenanceTask.EmptyCopy && _allTables.Count == 0 && !TablesLoading) _ = LoadTablesAsync();
        if (IsNBackup && NBackup is null && !IsRunning) _ = CheckStateAsync();
        if (IsProperties && _properties is null && !IsRunning) _ = CheckStateAsync();
    }

    partial void OnTableFilterChanged(string value) => ApplyFilter();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsRunning))
        {
            RunCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            CheckStateCommand.NotifyCanExecuteChanged();
            RegisterNewCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanRegisterNew));
        }
    }

    /// <summary>
    /// Al abrir: página y charset de la base. Las rutas no se proponen: la carpeta de la base en el servidor no se muestra
    /// (D-54), así que los archivos se escriben con la ruta completa o, para una base nueva, con un alias.
    /// </summary>
    public async Task LoadAsync()
    {
        if (Profile is not { } profile || Password() is not { } password) return;
        try
        {
            var info = await DatabaseServices.FileInfoAsync(profile, password);
            if (_disposed) return;
            _serverPath = info.Path;
            DatabaseInfo = string.Format(Strings.MaintDatabaseInfo, profile.Database, info.PageSize, info.Charset);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.MaintFailed, First(ex));
        }
    }

    /// <summary>Carpeta (con su separador) y nombre sin extensión de una ruta del servidor (Windows o Linux).</summary>
    internal static (string Folder, string Name) Split(string path)
    {
        var cut = path.LastIndexOfAny(['/', '\\']) + 1;
        var file = path[cut..];
        var dot = file.LastIndexOf('.');
        return (path[..cut], dot > 0 ? file[..dot] : file);
    }

    public async Task LoadTablesAsync()
    {
        if (Profile is not { } profile || Password() is not { } password) return;
        TablesLoading = true;
        try
        {
            await using var explorer = new MetadataExplorer(profile, password);
            var tables = await explorer.LoadObjectsAsync(DbObjectKind.Table);
            if (_disposed) return;
            _allTables = tables.Select(t => new TableChoice(t.Name)).ToList();
            foreach (var t in _allTables) t.PropertyChanged += (_, _) => UpdateChosen();
            OnPropertyChanged(nameof(AllTables));
            ApplyFilter();
            UpdateChosen();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.MaintFailed, First(ex));
        }
        finally
        {
            TablesLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var filter = TableFilter.Trim();
        VisibleTables = filter.Length == 0 ? _allTables : _allTables.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void UpdateChosen()
    {
        var count = _allTables.Count(t => t.IsChecked);
        ChosenText = count == 0 ? Strings.MaintNoTablesChosen : string.Format(Strings.MaintTablesChosen, count);
    }

    [RelayCommand]
    private void ClearTables()
    {
        foreach (var t in _allTables) t.IsChecked = false;
    }

    private bool CanRun() => !IsRunning && !IsBlockedReadOnly && !IsNBackup && Profile is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task RunAsync()
    {
        if (IsRunning) return Working;
        return Working = CurrentTask switch
        {
            MaintenanceTask.Backup => Do(BackupCoreAsync),
            MaintenanceTask.Restore => Do(RestoreCoreAsync),
            MaintenanceTask.EmptyCopy => Do(EmptyCopyCoreAsync),
            MaintenanceTask.Validate => Do(ValidateCoreAsync),
            MaintenanceTask.Properties => Do(PropertiesCoreAsync),
            _ => Do(SweepCoreAsync),
        };
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _cts?.Cancel();

    private bool CanCheck() => !IsRunning && CanCheckState;

    /// <summary>¿Terminó el backup (alguno de este usuario) o en qué quedó la base del restore?</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    public Task CheckStateAsync() => Working = Do(async (profile, password, ct) =>
    {
        if (IsProperties)
        {
            ShowProperties(profile, await DatabaseProperties.ReadAsync(profile, password, ct));
            return;
        }
        if (IsNBackup)
        {
            NBackup = await NBackupInfo.ReadAsync(profile, password, ct);
            Report(string.Format(Strings.MaintNBackupRead, NBackupWarning ?? NBackupStateText) + " " + NBackupDeltaText + " " + NBackupHistoryTitle + ".");
            return;
        }
        if (IsBackup)
        {
            var running = await DatabaseServices.BackupRunningAsync(profile, password, _backupStartedAt, ct);
            Report(running ? string.Format(Strings.MaintBackupRunningNow, profile.User) : string.Format(Strings.MaintBackupNotRunning, profile.User));
            return;
        }
        var target = RestoreTarget.Trim();
        var state = await DatabaseServices.RestoreStateAsync(profile, password, target, ct);
        Report(StateText(state, target));
        if (state.Status == RestoreStatus.Online) NewDatabasePath = target;
    });

    /// <summary>Registra la base nueva como la de esta pestaña (producción pasa a prueba) y la elige en el árbol.</summary>
    [RelayCommand(CanExecute = nameof(CanRegisterNew))]
    public void RegisterNew()
    {
        if (Profile is not { } profile || NewDatabasePath is not { } path) return;
        var copy = Owner.RegisterCopy(profile, path);
        Status = string.Format(Strings.MaintRegistered, copy.Name, ProfileEnvironments.Label(copy.Environment));
    }

    private async Task Do(Func<ConnectionProfile, string, CancellationToken, Task> work)
    {
        if (_disposed || Profile is not { } profile) return;
        if (Password() is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            await work(profile, password, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Report(CurrentTask switch
            {
                MaintenanceTask.Backup => Strings.MaintBackupStopWatching,
                MaintenanceTask.Restore => Strings.MaintRestoreStopWatching,
                MaintenanceTask.EmptyCopy => Strings.MaintEmptyCopyCancelled,
                _ => Strings.MaintCancelled,
            });
        }
        catch (Exception ex)
        {
            Report(string.Format(Strings.MaintFailed, First(ex)));
            Log(ex.Message.Trim());
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>La extensión de un archivo de base de Firebird (.fdb, .gdb, .ib): no sirve de destino de un backup.</summary>
    internal static bool IsDatabaseFile(string path) =>
        Path.GetExtension(path.Trim()).ToLowerInvariant() is ".fdb" or ".gdb" or ".ib" or ".ibs" or ".fdb2";

    private async Task BackupCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        var file = BackupFile.Trim();
        if (file.Length == 0) { Status = Strings.MaintPathMissing; return; }
        // gbak escribe el archivo con la cuenta del servicio de Firebird: un destino equivocado pisaría una base.
        if (IsDatabaseFile(file) || string.Equals(file, profile.Database.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Status = Strings.MaintBackupTargetIsDatabase;
            return;
        }
        var clock = Stopwatch.StartNew();
        Section(string.Format(Strings.MaintBackupStart, profile.Name, file));
        var options = new BackupOptions(BackupMetadataOnly, BackupNoGarbageCollect, BackupIgnoreChecksums, BackupNonTransportable, SqlNames.Parse(BackupSkipData));
        LogOptions(
            (options.MetadataOnly, Strings.MaintBackupMetadataOnly), (options.NoGarbageCollect, Strings.MaintBackupNoGc),
            (options.IgnoreChecksums, Strings.MaintBackupIgnoreChecksums), (options.NonTransportable, Strings.MaintBackupNonTransportable),
            (options.SkipData is { Count: > 0 }, Strings.MaintSkipData + " " + string.Join(", ", options.SkipData ?? [])));
        Status = Strings.MaintBackupRunning;
        _backupStartedAt = await DatabaseServices.ServerTimeAsync(profile, password, ct);
        await DatabaseServices.BackupAsync(profile, password, file, options, Log, ct);
        // FirebirdClient puede volver antes de que el servidor termine: se espera a que se vaya su conexión.
        if (await DatabaseServices.BackupRunningAsync(profile, password, _backupStartedAt, ct))
        {
            Log(Strings.MaintBackupStillRunning);
            while (await DatabaseServices.BackupRunningAsync(profile, password, _backupStartedAt, ct)) await Task.Delay(PollInterval, ct);
        }
        RestoreSource = file;
        Report(string.Format(Strings.MaintBackupDone, file, clock.Elapsed.TotalSeconds));
    }

    private async Task RestoreCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        var source = RestoreSource.Trim();
        var target = RestoreTarget.Trim();
        if (source.Length == 0 || target.Length == 0) { Status = Strings.MaintPathMissing; return; }
        if (RestoreReplace)
        {
            var (_, name) = Split(target);
            var reasons = new List<string> { string.Format(Strings.MaintReplaceConfirm, target) };
            if (!await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.MaintReplaceTitle, reasons, name, Strings.MaintRunRestore))) return;
        }
        var clock = Stopwatch.StartNew();
        Section(string.Format(Strings.MaintRestoreStart, source, target));
        Status = Strings.MaintRestoreRunning;
        var pageSize = PageSizes[Math.Clamp(RestorePageSizeIndex, 0, PageSizes.Count - 1)];
        var options = new RestoreOptions(RestoreReplace, RestoreMetadataOnly, pageSize > 0 ? pageSize : null, RestoreDeactivateIndexes,
            RestoreNoValidity, RestoreOneAtATime, RestoreNoShadow, SqlNames.Parse(RestoreSkipData));
        LogOptions(
            (options.MetadataOnly, Strings.MaintRestoreMetadataOnly), (options.DeactivateIndexes, Strings.MaintRestoreDeactivateIndexes),
            (options.NoValidity, Strings.MaintRestoreNoValidity), (options.OneAtATime, Strings.MaintRestoreOneAtATime),
            (options.NoShadow, Strings.MaintRestoreNoShadow),
            (options.SkipData is { Count: > 0 }, Strings.MaintSkipData + " " + string.Join(", ", options.SkipData ?? [])));
        await DatabaseServices.RestoreAsync(profile, password, source, target, options, Log, ct);

        // El cliente puede volver antes: se mira el encabezado de la base nueva hasta que quede en línea.
        var missing = 0;
        var lastNote = Stopwatch.StartNew();
        while (true)
        {
            var state = await DatabaseServices.RestoreStateAsync(profile, password, target, ct);
            if (state.Status == RestoreStatus.Online) break;
            if (state.Status == RestoreStatus.Missing && ++missing >= 3)
            {
                Report(string.Format(Strings.MaintRestoreFailedMissing, target));
                return;
            }
            if (state.Status == RestoreStatus.InProgress && lastNote.Elapsed > TimeSpan.FromMinutes(1))
            {
                Log(string.Format(Strings.MaintRestoreStillRunning, clock.Elapsed));
                lastNote.Restart();
            }
            await Task.Delay(PollInterval, ct);
        }
        NewDatabasePath = target;
        Report(string.Format(Strings.MaintRestoreDone, target, clock.Elapsed));
    }

    private async Task EmptyCopyCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        var target = EmptyTarget.Trim();
        if (target.Length == 0) { Status = Strings.MaintPathMissing; return; }
        var tables = _allTables.Where(t => t.IsChecked).Select(t => t.Name).ToList();
        Section(string.Format(Strings.MaintEmptyCopyStart, profile.Name, target));
        Status = Strings.MaintEmptyCopyRunning;
        var result = await DatabaseServices.CreateEmptyCopyAsync(profile, password, target, new EmptyCopyOptions(EmptyGeneratorValues, tables), Log, ct);
        NewDatabasePath = target;
        var rows = result.Tables.Where(t => t.Error is null).Sum(t => t.Rows);
        var failedTables = result.Tables.Count(t => t.Error is not null);
        Report(result.Errors.Count == 0 && failedTables == 0
            ? string.Format(Strings.MaintEmptyCopyDone, target, result.Tables.Count, rows, result.Elapsed.TotalSeconds)
            : string.Format(Strings.MaintEmptyCopyDoneWithErrors, target, result.Errors.Count, failedTables, result.Elapsed.TotalSeconds));
    }

    private async Task ValidateCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        var tables = SqlNames.Parse(ValidateTables);
        var list = string.Join(", ", tables);
        var reason = tables.Count == 0
            ? string.Format(Strings.MaintValidateProduction, profile.Name)
            : string.Format(Strings.MaintValidateProductionTables, profile.Name, list);
        if (profile.IsProduction
            && !await Owner.ConfirmActionAsync(new ConfirmPrompt([reason], Strings.MaintRunValidate, Strings.Cancel, Strings.MaintValidateTitle))) return;
        Section(tables.Count == 0 ? string.Format(Strings.MaintValidateStart, profile.Name) : string.Format(Strings.MaintValidateStartTables, profile.Name, list));
        Status = Strings.MaintValidateRunning;
        var result = await DatabaseServices.ValidateAsync(profile, password, Log, ct, tables);
        Report(result.Errors switch
        {
            null => Strings.MaintValidateIncomplete,
            0 when result.Warnings is null or 0 => string.Format(Strings.MaintValidateOk, result.Elapsed.TotalSeconds),
            _ => string.Format(Strings.MaintValidateProblems, result.Errors, result.Warnings ?? 0),
        });
    }

    /// <summary>Anota en el registro las opciones elegidas (las que no se tocaron no se nombran).</summary>
    private void LogOptions(params (bool On, string Label)[] options)
    {
        var chosen = options.Where(o => o.On).Select(o => o.Label.TrimEnd(':')).ToList();
        if (chosen.Count > 0) Log(string.Format(Strings.MaintOptionsUsed, string.Join(" · ", chosen)));
    }

    private void ShowProperties(ConnectionProfile profile, DatabaseSettings settings)
    {
        _properties = settings;
        PropSweepInterval = settings.SweepInterval;
        PropPageBuffers = settings.PageBuffers;
        PropForcedWrites = settings.ForcedWrites;
        PropReadOnly = settings.ReadOnly;
        PropertiesLoaded = true;
        Report(string.Format(Strings.MaintPropsRead, profile.Name, SweepText(settings.SweepInterval), YesNo(settings.ForcedWrites),
            YesNo(settings.ReadOnly), CacheText(settings.PageBuffers)));
    }

    private static string YesNo(bool value) => value ? Strings.MaintPropsYes : Strings.MaintPropsNo;
    private static string SweepText(int interval) => interval == 0 ? Strings.MaintPropsOff : interval.ToString("N0");
    private static string CacheText(int pages) => pages == 0 ? Strings.MaintPropsServer : pages.ToString("N0");

    /// <summary>
    /// Aplica lo que cambió. Pregunta siempre, con cada cambio; en producción, apagar las escrituras forzadas o pasar a
    /// solo lectura hay que escribir el nombre de la base.
    /// </summary>
    private async Task PropertiesCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        if (_properties is not { } before) return;
        var after = new DatabaseSettings((int)Math.Max(0, PropSweepInterval ?? before.SweepInterval), PropForcedWrites, PropReadOnly,
            (int)Math.Max(0, PropPageBuffers ?? before.PageBuffers));
        if (after == before)
        {
            Status = Strings.MaintPropsUnchanged;
            return;
        }
        var reasons = new List<string> { string.Format(Strings.MaintPropsConfirm, profile.Name) };
        if (after.SweepInterval != before.SweepInterval)
            reasons.Add(string.Format(Strings.MaintPropsChange, Strings.MaintPropSweepShort, SweepText(before.SweepInterval), SweepText(after.SweepInterval)));
        if (after.ForcedWrites != before.ForcedWrites)
            reasons.Add(string.Format(Strings.MaintPropsChange, Strings.MaintPropForcedWritesShort, YesNo(before.ForcedWrites), YesNo(after.ForcedWrites)));
        if (after.PageBuffers != before.PageBuffers)
            reasons.Add(string.Format(Strings.MaintPropsChange, Strings.MaintPropPageBuffersShort, CacheText(before.PageBuffers), CacheText(after.PageBuffers)));
        if (after.ReadOnly != before.ReadOnly)
            reasons.Add(string.Format(Strings.MaintPropsChange, Strings.MaintPropReadOnlyShort, YesNo(before.ReadOnly), YesNo(after.ReadOnly)));
        if (!after.ForcedWrites && before.ForcedWrites) reasons.Add(Strings.MaintPropForcedWritesOff);
        if (after.ReadOnly && !before.ReadOnly) reasons.Add(Strings.MaintPropsReadOnlyWarn);
        var risky = (!after.ForcedWrites && before.ForcedWrites) || (after.ReadOnly && !before.ReadOnly);
        if (profile.IsProduction)
        {
            reasons.Add(Strings.MaintPropsProduction);
            if (risky && !await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.MaintPropsTitle, reasons, profile.Name, Strings.MaintRunProperties))) return;
        }
        if (!(profile.IsProduction && risky)
            && !await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, Strings.MaintRunProperties, Strings.Cancel, Strings.MaintPropsTitle))) return;
        Section(string.Join(" · ", reasons.Skip(1).Take(4)));
        try
        {
            await DatabaseProperties.ApplyAsync(profile, password, before, after, ct);
        }
        catch (Exception ex) when (after.ReadOnly && !before.ReadOnly && ex is not OperationCanceledException)
        {
            Log(Strings.MaintPropsReadOnlyHint);
            throw;
        }
        ShowProperties(profile, await DatabaseProperties.ReadAsync(profile, password, ct));
        Report(string.Format(Strings.MaintPropsApplied, profile.Name));
    }

    private async Task SweepCoreAsync(ConnectionProfile profile, string password, CancellationToken ct)
    {
        var reasons = new List<string> { string.Format(Strings.MaintSweepConfirm, profile.Name) };
        if (profile.IsProduction)
        {
            reasons.Add(Strings.MaintSweepProduction);
            if (!await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.MaintSweepTitle, reasons, profile.Name, Strings.MaintRunSweep))) return;
        }
        else if (!await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, Strings.MaintRunSweep, Strings.Cancel, Strings.MaintSweepTitle))) return;
        Section(string.Format(Strings.MaintSweepStart, profile.Name));
        Status = Strings.MaintSweepRunning;
        var elapsed = await DatabaseServices.SweepAsync(profile, password, ct);
        Report(string.Format(Strings.MaintSweepDone, elapsed.TotalSeconds));
    }

    private static string StateText(RestoreState state, string target) => state.Status switch
    {
        RestoreStatus.Online => string.Format(Strings.MaintStateOnline, target),
        RestoreStatus.InProgress => string.Format(Strings.MaintStateInProgress, target),
        RestoreStatus.Missing => string.Format(Strings.MaintStateMissing, target),
        _ => string.Format(Strings.MaintStateUnknown, target, state.Detail),
    };

    private string? Password() => Profile is { } p ? Owner.ProfileStore.GetPassword(p.Id) : null;

    private string First(Exception ex) => Hide(ex.Message.Split('\n', 2)[0].Trim());

    /// <summary>Lo que dice el servidor (gbak, la validación, un error), con la base como la nombra el registro (D-54).</summary>
    private string Hide(string text) => Profile is { } p ? ServerPaths.Hide(text, _serverPath, p.Database) : text;

    /// <summary>El resultado: en el estado de la pestaña y en el registro.</summary>
    private void Report(string message)
    {
        Status = Hide(message);
        Log($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void Section(string title)
    {
        if (Document.TextLength > 0 || _pending.Length > 0) Log("");
        Log($"[{DateTime.Now:HH:mm:ss}] {title}");
    }

    /// <summary>Un renglón al registro. Llega desde cualquier hilo (la salida de gbak); se escribe de a tandas en el de la UI.</summary>
    private void Log(string line)
    {
        line = Hide(line);
        lock (_pending)
        {
            _pending.Append(line).Append('\n');
            if (_flushQueued) return;
            _flushQueued = true;
        }
        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    private void Flush()
    {
        string text;
        lock (_pending)
        {
            text = _pending.ToString();
            _pending.Clear();
            _flushQueued = false;
        }
        if (!_disposed && text.Length > 0) Document.Insert(Document.TextLength, text);
    }

    /// <summary>Lo que quedó pendiente de escribir en el registro (para las pruebas).</summary>
    internal void FlushLog() => Flush();

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
