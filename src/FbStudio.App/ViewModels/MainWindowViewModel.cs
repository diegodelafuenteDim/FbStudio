using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FbStudio.Core.Metadata;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FbStudio.Firebird;
using FbStudio.Workspace;

namespace FbStudio.App.ViewModels;

/// <summary>
/// La ventana: árbol de bases, pestañas de editor (cada una con su base y su sesión) e historial compartido.
/// La franja de color, la barra de herramientas y la barra de estado siguen a la pestaña elegida.
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _profiles;
    private readonly HistoryStore? _history;
    private readonly SettingsStore? _settings;
    private AppSettings _unsavedSettings = new(); // sin SettingsStore (pruebas): las opciones viven en memoria
    private readonly Dictionary<Guid, (Task<MetadataCatalog> Load, DateTime Started)> _catalogs = [];
    private readonly Dictionary<Guid, Task<Core.Diagrams.SchemaGraph>> _graphs = [];
    private int _queryNumber;

    public MainWindowViewModel(ProfileStore profiles, HistoryStore? history = null, SettingsStore? settings = null, MonitorQueryStore? monitorQueries = null,
        GridLayoutStore? gridLayouts = null, AreaStore? areas = null, SessionStore? session = null)
    {
        _session = session;
        MonitorQueries = monitorQueries ?? new MonitorQueryStore(null);
        GridLayouts = gridLayouts ?? new GridLayoutStore(null);
        AreaStore = areas ?? new AreaStore(null);
        _profiles = profiles;
        _history = history;
        _settings = settings;
        _rowLimit = Options.DefaultRowLimit;
        Tree = new DatabaseTreeViewModel(profiles) { AreasFor = AreaStore.Areas, FolderColorFor = FolderColor };
        AreaStore.Changed += Tree.RefreshAreas;
        if (settings?.LoadError is { } problem) Tree.Status = problem;
        else if (!profiles.SavesPasswords) Tree.Status = Strings.PasswordsNotSaved; // Linux sin llavero del escritorio
        Tree.DatabaseActivated += Activate;
        Tree.ConnectionsChanged += RefreshOtherConnections;
        Tree.ServerInfoLoaded += id =>
        {
            if (SelectedProfile?.Id != id) return;
            OnPropertyChanged(nameof(Banner));
            OnPropertyChanged(nameof(BannerTip));
        };
        Tree.ObjectRequested += (profile, obj) => _ = OpenObjectAsync(profile, obj);
        // Lo último que se tocó manda (D-88): elegir algo de una base en el árbol hace que la consulta nueva vaya a esa base.
        Tree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DatabaseTreeViewModel.SelectedNode) && Tree.SelectedNode?.DatabaseNode is not null) _treeIsNewer = true;
        };
        // Sin pestañas hasta que se elige una base: una consulta sin base no sirve para nada.
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(ShowStartHint));
        };
        ReloadProfiles();
        RefreshHistory();
    }

    public ObservableCollection<EditorTabViewModel> Tabs { get; } = [];

    /// <summary>Orden y anchos de las columnas de la pestaña Datos, por base y tabla.</summary>
    public GridLayoutStore GridLayouts { get; }

    /// <summary>Áreas de cada base (F-9), en su archivo (el de esta PC o uno compartido).</summary>
    public AreaStore AreaStore { get; }

    /// <summary>Hay alguna pestaña abierta (al arrancar no hay: se abre al elegir una base).</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Sin pestañas y con bases registradas: la vista indica cómo empezar.</summary>
    public bool ShowStartHint => !HasTabs && !Tree.IsEmpty;

    /// <summary>Un aviso en la barra de estado: en la pestaña elegida o, si no hay ninguna, en la del árbol.</summary>
    public void Notify(string message)
    {
        if (SelectedTab is { } tab) tab.Status = message;
        else Tree.Status = message;
    }
    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    public IReadOnlyList<int> RowLimits { get; } = [1_000, 10_000, 100_000, 1_000_000];
    public ProfileStore ProfileStore => _profiles;
    /// <summary>Árbol de bases registradas (la base de la pestaña se elige ahí).</summary>
    public DatabaseTreeViewModel Tree { get; }

    /// <summary>La vista muestra el diálogo de confirmación (producción, UPDATE/DELETE sin WHERE).</summary>
    public Func<SafetyDecision, Task<bool>> ConfirmAsync { get; set; } = _ => Task.FromResult(false);

    /// <summary>La vista pregunta antes de cerrar una pestaña con una transacción abierta.</summary>
    public Func<string, Task<bool>> ConfirmCloseAsync { get; set; } = _ => Task.FromResult(true);

    /// <summary>Consultas propias del monitoreo (valen para todas las bases).</summary>
    public MonitorQueryStore MonitorQueries { get; }

    /// <summary>La vista muestra los comandos de «Aplicar» (pestaña Datos) antes de ejecutarlos.</summary>
    public Func<ScriptPreview, Task<ScriptPreviewResult>> PreviewScriptAsync { get; set; } = p => Task.FromResult(new ScriptPreviewResult(ScriptPreviewChoice.Cancel, p.WithCommit));

    /// <summary>La vista abre el editor de una consulta propia del monitoreo; true si se guardó.</summary>
    public Func<MonitorQueryEditorViewModel, Task<bool>> EditMonitorQueryAsync { get; set; } = _ => Task.FromResult(false);

    /// <summary>La vista pide los valores de los parámetros de una consulta (F-25); null si se cancela.</summary>
    public Func<IReadOnlyList<ParameterEntry>, Task<IReadOnlyList<ParameterEntry>?>> AskParametersAsync { get; set; } =
        _ => Task.FromResult<IReadOnlyList<ParameterEntry>?>(null);

    /// <summary>Lo último que se escribió para cada parámetro con nombre (mientras FbStudio está abierto).</summary>
    public Dictionary<string, ParameterEntry> ParameterMemory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pide los valores de los parámetros (con lo último que se usó para cada nombre) y los convierte: fechas y números
    /// como se escriben en la PC, NULL si se marcó. Null si se cancela.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> RequestParametersAsync(IReadOnlyList<string> names)
    {
        var initial = names.Select(n => !n.StartsWith('#') && ParameterMemory.TryGetValue(n, out var e) ? e with { Name = n } : new ParameterEntry(n, "", false)).ToList();
        if (await AskParametersAsync(initial) is not { } answers) return null;
        foreach (var a in answers.Where(a => !a.Name.StartsWith('#'))) ParameterMemory[a.Name] = a;
        return answers.ToDictionary(a => a.Name, a => SqlParameters.ToValue(a.Text, a.IsNull, System.Globalization.CultureInfo.CurrentCulture),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>La vista pregunta antes de una acción con consecuencias (cortar una conexión, cancelar una sentencia).</summary>
    public Func<ConfirmPrompt, Task<bool>> ConfirmActionAsync { get; set; } = _ => Task.FromResult(false);

    /// <summary>Confirmación fuerte (reemplazar una base, sweep en producción): hay que escribir un nombre para seguir.</summary>
    public Func<TypedConfirm, Task<bool>> ConfirmTypedAsync { get; set; } = _ => Task.FromResult(false);

    /// <summary>La vista pide los datos de un usuario nuevo o cambiado (F-28); null si se cancela.</summary>
    public Func<UserDialogRequest, Task<Core.Security.UserEdit?>> EditUserAsync { get; set; } = _ => Task.FromResult<Core.Security.UserEdit?>(null);

    /// <summary>La vista pide un texto; null si se cancela.</summary>
    public Func<TextPrompt, Task<string?>> AskTextAsync { get; set; } = _ => Task.FromResult<string?>(null);

    /// <summary>
    /// Mantenimiento de la base (backup, restore, base vacía, validación, sweep): una pestaña por base; si ya está
    /// abierta, la elige. Sin base, usa la de la pestaña elegida.
    /// </summary>
    public MaintenanceTabViewModel? OpenMaintenance(ConnectionProfile? profile = null, MaintenanceTask? task = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.MaintPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<MaintenanceTabViewModel>().FirstOrDefault(m => m.Profile?.Id == profile.Id);
        if (tab is null)
        {
            tab = new MaintenanceTabViewModel(this, profile) { Kind = EditorTabKind.Maintenance };
            Tabs.Add(tab);
            _ = tab.LoadAsync();
        }
        if (task is { } t && !tab.IsRunning) tab.SelectedTaskIndex = (int)t;
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Registra una base que salió de otra (restore, base vacía): mismo servidor, usuario, contraseña y carpeta; una
    /// copia de producción queda como prueba. El nombre lleva el del archivo.
    /// </summary>
    public ConnectionProfile RegisterCopy(ConnectionProfile profile, string database)
    {
        var names = _profiles.Load().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = $"{profile.Name} · {MaintenanceTabViewModel.Split(database).Name}";
        for (var n = 2; names.Contains(name); n++) name = $"{profile.Name} · {MaintenanceTabViewModel.Split(database).Name} ({n})";
        var copy = profile with
        {
            Id = Guid.NewGuid(),
            Name = name,
            Database = database,
            Environment = profile.IsProduction ? ProfileEnvironment.Prueba : profile.Environment,
        };
        _profiles.Upsert(copy, _profiles.GetPassword(profile.Id));
        ReloadProfiles();
        return copy;
    }

    /// <summary>La pestaña de trace de una base (F-24): una por base; si ya está abierta, se elige.</summary>
    public TraceTabViewModel? OpenTrace(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.TracePickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<TraceTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id);
        if (tab is null)
        {
            tab = new TraceTabViewModel(this, profile) { Kind = EditorTabKind.Trace };
            Tabs.Add(tab);
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Usuarios y permisos de una base (F-28): una pestaña por base; si ya está abierta, se elige.</summary>
    public SecurityTabViewModel? OpenSecurity(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.SecPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<SecurityTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id);
        if (tab is null)
        {
            tab = new SecurityTabViewModel(this, profile) { Kind = EditorTabKind.Security };
            Tabs.Add(tab);
            _ = tab.LoadAsync();
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Áreas de una base (F-9): una pestaña por base; si ya está abierta, se elige.</summary>
    public AreasTabViewModel? OpenAreas(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.AreasPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<AreasTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id);
        if (tab is null)
        {
            tab = new AreasTabViewModel(this, profile) { Kind = EditorTabKind.Areas };
            Tabs.Add(tab);
            tab.Loading = tab.LoadAsync();
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Diagrama de una tabla y sus vecinas, o de un área (F-8): una pestaña por base y tabla o área; si ya está abierta,
    /// se elige.
    /// </summary>
    public DiagramTabViewModel? OpenDiagram(ConnectionProfile? profile, DiagramSource source)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.DiagramPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<DiagramTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id && t.Source.Name == source.Name
                                                                         && t.Source.IsArea == source.IsArea && t.Source.Area == source.Area);
        if (tab is null)
        {
            tab = new DiagramTabViewModel(this, profile, source) { Kind = EditorTabKind.Diagram };
            Tabs.Add(tab);
            tab.Loading = tab.LoadAsync();
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Abre un diagrama guardado (.fbsdiagram) en una base: se vuelve a leer su estructura y se acomoda como quedó. Si ya
    /// estaba abierto el mismo diagrama, se abre en una pestaña aparte.
    /// </summary>
    public DiagramTabViewModel? OpenDiagramFile(string path, ConnectionProfile? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.DiagramPickDatabase);
            return null;
        }
        Core.Diagrams.DiagramFile file;
        try
        {
            file = Workspace.DiagramFiles.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify(string.Format(Strings.DiagramFileInvalid, Path.GetFileName(path)));
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = new DiagramTabViewModel(this, profile, new DiagramSource(file.Source, file.Area), file) { Kind = EditorTabKind.Diagram };
        Tabs.Add(tab);
        tab.Loading = tab.LoadAsync();
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Los objetos de una base que caen en un área (con sus reglas, y lo que usa solo sus tablas si el área lo pide):
    /// para limitar el script, la comparación o las estadísticas. <paramref name="area"/> puede ser de otra base (al
    /// comparar, las reglas del origen se aplican al destino).
    /// </summary>
    public async Task<IReadOnlySet<string>> AreaMembersAsync(ConnectionProfile profile, SchemaArea area)
    {
        var objects = await WithExplorerAsync(profile, e => e.LoadAllObjectsAsync());
        var uses = area.IncludeDependents ? await WithExplorerAsync(profile, e => e.LoadTableUsesAsync()) : null;
        return new AreaMembership([area], objects, uses).Members(0).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Guarda un cambio del registro hecho desde una pestaña (p. ej. el archivo de áreas) y rearma lo que lo usa.</summary>
    public void UpdateProfile(ConnectionProfile profile)
    {
        _profiles.Upsert(profile, null);
        ReloadProfiles();
    }

    /// <summary>Importar un CSV o un Excel a una tabla (F-26): una pestaña por base y tabla; si ya está abierta, se elige.</summary>
    /// <summary>«Datos de prueba…» de una tabla: una pestaña por base y tabla (si ya está abierta, se elige).</summary>
    /// <summary>«Comparar datos con otra base…» de una tabla del árbol (reutiliza la de esa tabla y base si ya está abierta).</summary>
    public DataCompareTabViewModel OpenDataCompare(ConnectionProfile source, string table)
    {
        source = Profiles.FirstOrDefault(p => p.Id == source.Id) ?? source;
        var tab = Tabs.OfType<DataCompareTabViewModel>().FirstOrDefault(t => t.Source.Id == source.Id && t.Table == table);
        if (tab is null)
        {
            tab = new DataCompareTabViewModel(this, source, table) { Kind = EditorTabKind.DataCompare };
            Tabs.Add(tab);
        }
        SelectedTab = tab;
        return tab;
    }

    public TestDataTabViewModel OpenTestData(ConnectionProfile profile, string table)
    {
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<TestDataTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id && t.Table == table);
        if (tab is null)
        {
            tab = new TestDataTabViewModel(this, profile, table) { Kind = EditorTabKind.TestData };
            Tabs.Add(tab);
            _ = tab.LoadAsync();
        }
        SelectedTab = tab;
        return tab;
    }

    /// <param name="table">null: importar a una tabla nueva (se arma desde el archivo).</param>
    public ImportTabViewModel OpenImport(ConnectionProfile profile, string? table)
    {
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = Tabs.OfType<ImportTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile.Id
            && (table is null ? t.IsNewTable && !t.IsImporting : !t.IsNewTable && t.Table == table));
        if (tab is null)
        {
            tab = new ImportTabViewModel(this, profile, table) { Kind = EditorTabKind.Import };
            Tabs.Add(tab);
            if (table is not null) _ = tab.LoadTableAsync();
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Monitoreo de la base (F-1): una pestaña por base; si ya está abierta, la elige. Sin base, usa la de la pestaña
    /// elegida.
    /// </summary>
    public MonitorTabViewModel? OpenMonitor(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.MonitorPickDatabase);
            return null;
        }
        if (Tabs.OfType<MonitorTabViewModel>().FirstOrDefault(m => m.Profile?.Id == profile.Id) is { } existing)
        {
            SelectedTab = existing;
            return existing;
        }
        var tab = new MonitorTabViewModel(this, profile) { Kind = EditorTabKind.Monitor };
        Tabs.Add(tab);
        SelectedTab = tab;
        _ = tab.RefreshAsync();
        return tab;
    }

    /// <summary>
    /// Estadísticas de la base (gstat): una pestaña por base; si ya está abierta, la elige y vuelve a leer el
    /// encabezado. Al abrirse lee el encabezado (una página, instantáneo). Sin base, usa la de la pestaña elegida.
    /// </summary>
    public StatisticsTabViewModel? OpenStatistics(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.StatsPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        if (Tabs.OfType<StatisticsTabViewModel>().FirstOrDefault(s => s.Profile?.Id == profile.Id) is { } existing)
        {
            SelectedTab = existing;
            _ = existing.ReadHeaderAsync();
            return existing;
        }
        var tab = new StatisticsTabViewModel(this, profile) { Kind = EditorTabKind.Statistics };
        Tabs.Add(tab);
        SelectedTab = tab;
        _ = tab.ReadHeaderAsync();
        return tab;
    }

    /// <summary>«Script de la base…»: una pestaña nueva que lee la base y arma el script con lo elegido.</summary>
    public SchemaScriptTabViewModel? OpenSchemaScript(ConnectionProfile? profile, Core.Metadata.SchemaScriptOptions options)
    {
        profile ??= SelectedProfile;
        if (profile is null)
        {
            Notify(Strings.SchemaScriptPickDatabase);
            return null;
        }
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        var tab = new SchemaScriptTabViewModel(this, profile, options) { Kind = EditorTabKind.Script };
        Tabs.Add(tab);
        SelectedTab = tab;
        _ = tab.GenerateAsync();
        return tab;
    }

    /// <summary>«Comparar bases…»: una pestaña que lee las dos bases y lista las diferencias (la pestaña es del destino).</summary>
    public CompareTabViewModel OpenCompare(ConnectionProfile source, ConnectionProfile target, Core.Metadata.SchemaCompareOptions options)
    {
        source = Profiles.FirstOrDefault(p => p.Id == source.Id) ?? source;
        target = Profiles.FirstOrDefault(p => p.Id == target.Id) ?? target;
        var tab = new CompareTabViewModel(this, source, target, options) { Kind = EditorTabKind.Compare };
        Tabs.Add(tab);
        SelectedTab = tab;
        _ = tab.CompareAsync();
        return tab;
    }

    /// <summary>Un texto en una pestaña de consulta sin base (se elige con doble clic en el árbol dónde correrlo).</summary>
    public EditorTabViewModel OpenScriptWithoutDatabase(string title, string text)
    {
        var tab = new EditorTabViewModel(this, null, title, text);
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    /// <summary>La vista pregunta antes de descartar cambios sin compilar (cerrar o recargar una pestaña de objeto).</summary>
    public Func<DiscardPrompt, Task<bool>> ConfirmDiscardAsync { get; set; } = _ => Task.FromResult(true);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfile), nameof(Banner), nameof(BannerTip), nameof(IsProduction), nameof(IsTest), nameof(IsDevelopment), nameof(IsReadOnlyProfile))]
    private EditorTabViewModel? _selectedTab;

    [ObservableProperty] private int _rowLimit;

    /// <summary>Opciones del sistema vigentes.</summary>
    public AppSettings Options => _settings?.Current ?? _unsavedSettings;

    /// <summary>Menú Consulta → «Seguir ante errores en scripts»: la misma opción de Opciones → Consultas.</summary>
    public bool ScriptContinueOnError
    {
        get => !Options.ScriptStopOnError;
        set
        {
            if (value != ScriptContinueOnError) SaveSettings(Options with { ScriptStopOnError = !value });
        }
    }

    /// <summary>Espera ante un registro bloqueado para las transacciones de escritura que se abran.</summary>
    public TimeSpan LockTimeout => TimeSpan.FromSeconds(Options.LockTimeoutSeconds);

    /// <summary>Después de «Aceptar» en Opciones: las vistas aplican el tema y la configuración del editor.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>
    /// Guarda las opciones y aplica lo que no depende de la vista: el límite de filas (solo si cambió el valor por
    /// defecto, para no pisar el elegido en la barra) y la retención del historial (purga en el momento).
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
        var before = Options;
        settings = settings.Normalized();
        if (_settings is not null) _settings.Save(settings);
        else _unsavedSettings = settings;
        if (settings.DefaultRowLimit != before.DefaultRowLimit) RowLimit = settings.DefaultRowLimit;
        if (settings.Language != before.Language)
            Tree.Status = UiLanguage.Text(nameof(Strings.LanguageAppliesOnRestart), UiLanguage.CultureFor(settings.Language));
        OnPropertyChanged(nameof(ScriptContinueOnError));
        if (settings.HistoryRetentionDays != before.HistoryRetentionDays && _history is not null)
        {
            _history.Purge(DateTimeOffset.UtcNow - TimeSpan.FromDays(settings.HistoryRetentionDays));
            RefreshHistory();
        }
        SettingsChanged?.Invoke(settings);
    }

    [ObservableProperty] private IReadOnlyList<HistoryEntry> _historyItems = [];
    [ObservableProperty] private string _historyFilter = "";
    [ObservableProperty] private bool _historyOnlyThisProfile = true;

    partial void OnHistoryFilterChanged(string value) => RefreshHistory();

    partial void OnHistoryOnlyThisProfileChanged(bool value) => RefreshHistory();

    /// <summary>Base de la pestaña elegida.</summary>
    public ConnectionProfile? SelectedProfile => SelectedTab?.Profile;

    public bool IsProduction => SelectedProfile?.IsProduction == true;
    public bool IsTest => SelectedProfile?.Environment == ProfileEnvironment.Prueba;
    public bool IsDevelopment => SelectedProfile?.Environment == ProfileEnvironment.Desarrollo;
    public bool IsReadOnlyProfile => SelectedProfile?.ReadOnly == true;

    public string Banner => SelectedProfile switch
    {
        null => Tree.IsEmpty ? Strings.BannerNoDatabases : SelectedTab is null ? Strings.BannerNoTabs : Strings.BannerNoDatabaseInTab,
        { IsProduction: true } p => $"{Strings.BannerProduction} · {p.Display}{(p.ReadOnly ? Strings.BannerReadOnly : "")}{Wire(p)}",
        var p => $"{ProfileEnvironments.Label(p.Environment)} · {p.Display}{(p.ReadOnly ? Strings.BannerReadOnly : "")}{Wire(p)}",
    };

    /// <summary>« · conexión cifrada» o « · ⚠ sin cifrar», cuando la base ya se conectó en el árbol y el servidor lo dijo.</summary>
    private string Wire(ConnectionProfile p) => Tree.ServerFor(p.Id)?.WireEncrypted switch
    {
        true => Strings.BannerEncrypted,
        false => Strings.BannerNotEncrypted,
        null => "",
    };

    /// <summary>
    /// Si al arrancar hay que mostrar las novedades (D-85): la primera vez que se abre una versión que tiene su sección en
    /// el registro de cambios. En una instalación nueva (nada anotado y ninguna base registrada) no: no hay nada «nuevo».
    /// Deja anotada la versión, así se muestra una sola vez.
    /// </summary>
    public bool ShouldShowWhatsNew(string version, IReadOnlyList<Core.ChangeLogVersion> changeLog)
    {
        var last = Options.LastSeenVersion;
        if (last == version) return false;
        var upgrade = last is not null || Profiles.Count > 0;
        SaveSettings(Options with { LastSeenVersion = version });
        return upgrade && changeLog.Any(v => v.Version == version && v.English.Count + v.Spanish.Count > 0);
    }

    /// <summary>Las otras bases conectadas, para la franja (F-21): la de la pestaña sigue siendo el aviso de dónde se ejecuta.</summary>
    public ObservableCollection<ConnectedChip> OtherConnections { get; } = [];

    public bool HasOtherConnections => OtherConnections.Count > 0;

    private void RefreshOtherConnections()
    {
        var others = Profiles.Where(p => Tree.IsConnected(p.Id) && p.Id != SelectedProfile?.Id).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (others.Select(p => (p.Id, p.Name, p.Environment)).SequenceEqual(OtherConnections.Select(c => (c.Profile.Id, c.Profile.Name, c.Profile.Environment)))) return;
        OtherConnections.Clear();
        foreach (var p in others) OtherConnections.Add(new ConnectedChip(p, GoToConnection));
        OnPropertyChanged(nameof(HasOtherConnections));
    }

    /// <summary>Lleva a la pestaña de esa base (la primera) o, si no tiene, abre una consulta en ella; la actual no cambia de base.</summary>
    public void GoToConnection(ConnectionProfile profile)
    {
        if (Tabs.FirstOrDefault(t => t.Profile?.Id == profile.Id) is { } tab) SelectedTab = tab;
        else NewQueryTab(profile).Status = string.Format(Strings.TabNewOnDatabase, profile.Name);
    }

    /// <summary>Por qué importa y cómo exigir el cifrado, cuando la conexión va sin cifrar.</summary>
    public string? BannerTip => SelectedProfile is { } p && Tree.ServerFor(p.Id)?.WireEncrypted == false ? Strings.BannerNotEncryptedTip : null;

    partial void OnSelectedTabChanged(EditorTabViewModel? oldValue, EditorTabViewModel? newValue)
    {
        _treeIsNewer = false;
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            oldValue.PropertyChanged -= OnTabPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.PropertyChanged += OnTabPropertyChanged;
        }
        Tree.MarkActive(newValue?.Profile?.Id);
        RefreshOtherConnections();
        if (HistoryOnlyThisProfile) RefreshHistory();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorTabViewModel.Profile)) return;
        NotifyProfileDependents();
        Tree.MarkActive(SelectedProfile?.Id);
        if (HistoryOnlyThisProfile) RefreshHistory();
    }

    private void NotifyProfileDependents()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(BannerTip));
        RefreshOtherConnections();
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(IsTest));
        OnPropertyChanged(nameof(IsDevelopment));
        OnPropertyChanged(nameof(IsReadOnlyProfile));
        OnPropertyChanged(nameof(ShowStartHint));
    }

    /// <summary>
    /// Copia de un registro (mismo servidor, base, usuario, rol, contraseña y opciones) con un nombre nuevo, para
    /// cambiarle lo que haga falta: otra base del mismo servidor, otro rol, otro usuario.
    /// </summary>
    public ConnectionProfile CloneProfile(ConnectionProfile profile)
    {
        var names = _profiles.Load().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = string.Format(Strings.CopyName, profile.Name);
        for (var n = 2; names.Contains(name); n++) name = string.Format(Strings.CopyNameN, profile.Name, n);
        var copy = profile with { Id = Guid.NewGuid(), Name = name };
        _profiles.Upsert(copy, _profiles.GetPassword(profile.Id));
        ReloadProfiles();
        return copy;
    }

    /// <summary>
    /// «Exportar registros…» (F-22): los elegidos, con o sin usuario, con frase con sus contraseñas cifradas y, si se
    /// pide, con las áreas de cada base.
    /// </summary>
    public void ExportProfiles(string path, IReadOnlyList<Guid> ids, bool includeUser, string? passphrase, bool includeAreas = false)
    {
        var chosen = _profiles.Load().Where(p => ids.Contains(p.Id)).ToList();
        var withAreas = 0;
        ProfileBackup.Export(path, chosen, includeUser, _profiles.GetPassword, passphrase, includeAreas
            ? p =>
            {
                var file = AreaStore.Load(p).File;
                if (file.Areas.Count > 0) withAreas++;
                return file;
            }
            : null);
        var withPassword = passphrase is not null && includeUser ? chosen.Count(p => _profiles.GetPassword(p.Id) is not null) : 0;
        Notify((withPassword > 0
                   ? string.Format(Strings.BackupExportedWithPasswords, chosen.Count, withPassword, path)
                   : string.Format(Strings.BackupExported, chosen.Count, path))
               + (withAreas > 0 ? " " + string.Format(Strings.BackupExportedAreas, withAreas) : ""));
    }

    /// <summary>Qué pasaría al importar: nuevos y ya registrados.</summary>
    public IReadOnlyList<ProfileImportItem> PlanImport(ProfileBackupContent content) => ProfileBackup.Plan(_profiles.Load(), content);

    /// <summary>«Importar registros…»: guarda los del plan (contraseñas al llavero de este equipo) y rearma el árbol.</summary>
    public ProfileImportResult ImportProfiles(IReadOnlyList<ProfileImportItem> plan, bool updateExisting, string? user, string? password)
    {
        var result = ProfileBackup.Apply(_profiles, plan, updateExisting, user, password, AreaStore);
        ReloadProfiles();
        Notify(string.Format(Strings.BackupImported, result.Added, result.Updated, result.Skipped)
               + (result.Invalid > 0 ? " " + string.Format(Strings.BackupInvalid, result.Invalid) : "")
               + (result.PasswordsForgotten > 0 ? " " + string.Format(Strings.BackupPasswordsForgotten, result.PasswordsForgotten) : "")
               + (result.KeptProtection > 0 ? " " + string.Format(Strings.BackupKeptProtection, result.KeptProtection) : "")
               + (result.AreasImported > 0 ? " " + string.Format(Strings.BackupAreasImported, result.AreasImported) : "")
               + (result.AreasKept > 0 ? " " + string.Format(Strings.BackupAreasKept, result.AreasKept) : "")
               + (result.AreasFailed > 0 ? " " + string.Format(Strings.BackupAreasFailed, result.AreasFailed) : ""));
        return result;
    }

    /// <summary>Carpetas propias que ya tienen alguna base (para elegir una existente).</summary>
    public IReadOnlyList<string> Folders => ProfileFolders.All(_profiles.Load());

    /// <summary>
    /// Pone la base en una carpeta propia (existente o nueva, p. ej. «Clinica»); vacía, vuelve al grupo de su ambiente.
    /// Si ya hay una carpeta con ese nombre (sin importar mayúsculas), se usa esa.
    /// </summary>
    public void MoveToFolder(ConnectionProfile profile, string? folder)
    {
        var all = _profiles.Load();
        var current = all.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        folder = ProfileFolders.Normalize(folder, all);
        _profiles.Upsert(current with { Folder = folder }, null);
        ReloadProfiles();
        Tree.Status = folder is null
            ? string.Format(Strings.FolderMovedToEnvironment, current.Name, ProfileEnvironments.Label(current.Environment))
            : string.Format(Strings.FolderMoved, current.Name, folder);
    }

    /// <summary>
    /// «Nueva carpeta…»: crea la carpeta con las bases elegidas (una carpeta existe mientras tenga alguna base). Si ya
    /// hay una con ese nombre, las bases se suman a esa.
    /// </summary>
    public void CreateFolder(string name, IEnumerable<Guid> profileIds)
    {
        var ids = profileIds.ToHashSet();
        var all = _profiles.Load();
        var folder = ProfileFolders.Normalize(name, all);
        if (folder is null || ids.Count == 0) return;
        _profiles.Save(all.Select(p => ids.Contains(p.Id) ? p with { Folder = folder } : p));
        ReloadProfiles();
        Tree.Status = string.Format(Strings.FolderCreated, folder, all.Count(p => ids.Contains(p.Id)));
    }

    /// <summary>
    /// Renombra (o mueve: «Clinica/Producción» → «Producción» la saca a la raíz) una carpeta propia con todo lo que
    /// tiene adentro. Si la ruta nueva ya existe, se juntan; vacía, sus bases vuelven al grupo de su ambiente. No se
    /// puede meter una carpeta adentro de sí misma.
    /// </summary>
    public bool RenameFolder(string folder, string? newName)
    {
        var all = _profiles.Load();
        newName = ProfileFolders.Normalize(newName, all.Where(p => !ProfileFolders.IsInside(p.Folder, folder)));
        if (newName is not null && ProfileFolders.IsInside(newName, folder) && !ProfileFolders.Same(newName, folder))
        {
            Tree.Status = string.Format(Strings.FolderIntoItself, folder);
            return false;
        }
        var cleanOld = ProfileFolders.Clean(folder)!;
        _profiles.Save(all.Select(p => ProfileFolders.IsInside(p.Folder, folder)
            ? p with { Folder = newName is null ? null : newName + ProfileFolders.Clean(p.Folder)![cleanOld.Length..] }
            : p));
        // Los colores de la carpeta y de las que tiene adentro la acompañan (sin carpeta, se van).
        if (Options.FolderColors is { } folderColors && folderColors.Keys.Any(k => ProfileFolders.IsInside(k, folder)))
        {
            var colors = folderColors.Where(kv => !ProfileFolders.IsInside(kv.Key, folder)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (newName is not null)
                foreach (var (key, hex) in folderColors.Where(kv => ProfileFolders.IsInside(kv.Key, folder)))
                    colors[newName + ProfileFolders.Clean(key)![cleanOld.Length..]] = hex;
            SaveSettings(Options with { FolderColors = colors });
        }
        ReloadProfiles();
        Tree.Status = newName is null ? string.Format(Strings.FolderRemoved, folder) : string.Format(Strings.FolderRenamed, folder, newName);
        return true;
    }

    /// <summary>El color propio de una carpeta (F-20); null: sin color propio.</summary>
    public string? FolderColor(string folder) =>
        ProfileFolders.Clean(folder) is { } key && (Options.FolderColors ?? []).FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) is { Value: { } hex }
            ? hex
            : null;

    /// <summary>Pone (o saca, con null) el color propio de una carpeta: solo uno de la paleta de las carpetas, sin rojos.</summary>
    public void SetFolderColor(string folder, string? hex)
    {
        if (ProfileFolders.Clean(folder) is not { } key || (hex is not null && !ColorPalette.IsAllowed(null, hex))) return;
        var colors = (Options.FolderColors ?? []).Where(kv => !string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (hex is not null) colors[key] = hex;
        SaveSettings(Options with { FolderColors = colors });
        ReloadProfiles();
    }

    /// <summary>¿Se puede soltar lo arrastrado ahí? (para el cursor mientras se arrastra en el árbol).</summary>
    public bool CanDrop(TreeNode dragged, TreeNode? target) => DropDestination(dragged, target).Ok;

    /// <summary>
    /// Arrastrar en el árbol: una base a una carpeta (o sobre otra base: a su carpeta; o a un lugar vacío o al grupo de
    /// su ambiente: sin carpeta) y una carpeta adentro de otra (o a un lugar vacío: a la raíz).
    /// </summary>
    public bool Drop(TreeNode dragged, TreeNode? target)
    {
        var (ok, folder, error) = DropDestination(dragged, target);
        if (!ok)
        {
            if (error is not null) Tree.Status = error;
            return false;
        }
        if (dragged is { Kind: TreeNodeKind.Database, Profile: { } profile })
        {
            if (ProfileFolders.Same(profile.Folder, folder) || (ProfileFolders.Clean(profile.Folder) is null && folder is null)) return false;
            MoveToFolder(profile, folder);
            return true;
        }
        return RenameFolder(dragged.FolderPath!, folder);
    }

    private (bool Ok, string? Folder, string? Error) DropDestination(TreeNode dragged, TreeNode? target)
    {
        // Soltar sobre una tabla, una columna o una categoría vale como soltar sobre su base.
        if (target is { Kind: not (TreeNodeKind.Folder or TreeNodeKind.Database) }) target = target.DatabaseNode;
        if (dragged is { Kind: TreeNodeKind.Database, Profile: { } profile })
        {
            string? folder;
            ProfileEnvironment? group = null;
            switch (target)
            {
                case null: folder = null; break;
                case { IsCustomFolder: true, FolderPath: { } path }: folder = path; break;
                case { GroupEnvironment: { } environment }: folder = null; group = environment; break;
                case { Kind: TreeNodeKind.Database, Profile: { } other }:
                    folder = ProfileFolders.Clean(other.Folder);
                    if (folder is null) group = other.Environment;
                    break;
                default: return (false, null, null);
            }
            // Sin carpeta, cada base va al grupo de su ambiente: no se «mueve» a otro grupo (el ambiente es del registro).
            if (folder is null && group is { } g && g != profile.Environment)
                return (false, null, string.Format(Strings.FolderOtherEnvironment, profile.Name, ProfileEnvironments.Label(profile.Environment)));
            return (true, folder, null);
        }
        if (dragged is { IsCustomFolder: true, FolderPath: { } dragging })
        {
            var parent = target switch
            {
                null or { GroupEnvironment: not null } => null,
                { IsCustomFolder: true, FolderPath: { } path } => path,
                { Kind: TreeNodeKind.Database, Profile: { } other } => ProfileFolders.Clean(other.Folder),
                _ => null,
            };
            if (parent is not null && ProfileFolders.IsInside(parent, dragging))
                return (false, null, string.Format(Strings.FolderIntoItself, dragging));
            var destination = parent is null ? ProfileFolders.Name(dragging) : parent + ProfileFolders.Separator + ProfileFolders.Name(dragging);
            return ProfileFolders.Same(destination, dragging) ? (false, null, null) : (true, destination, null);
        }
        return (false, null, null);
    }

    /// <summary>
    /// Relee los registros (después de registrar, editar o quitar bases). Las pestañas toman la versión nueva de su
    /// base, salvo las que tienen una transacción abierta o una ejecución en curso.
    /// </summary>
    public void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _profiles.Load()) Profiles.Add(p);
        Tree.Reload();
        foreach (var id in _catalogs.Keys.Where(id => Profiles.All(p => p.Id != id)).ToList()) _catalogs.Remove(id);
        foreach (var id in _graphs.Keys.Where(id => Profiles.All(p => p.Id != id)).ToList()) _graphs.Remove(id);
        foreach (var tab in Tabs)
        {
            if (tab.Profile is not { } current) continue;
            var updated = Profiles.FirstOrDefault(p => p.Id == current.Id);
            if (updated != current) InvalidateCatalog(current.Id); // cambió el registro (servidor, usuario…)
            if (updated != current && tab.CanChangeProfile) tab.Profile = updated;
        }
        // Con una sola base registrada, la pestaña sin base la usa (no se conecta hasta ejecutar).
        if (SelectedTab is { Profile: null, Kind: EditorTabKind.Sql } t && Profiles.Count == 1) t.Profile = Profiles[0];
        Tree.MarkActive(SelectedProfile?.Id);
        NotifyProfileDependents();
    }

    /// <summary>
    /// El árbol pide trabajar con esta base: la pestaña de consulta elegida la toma si está libre; si tiene una
    /// transacción abierta, está ejecutando o es de un objeto, la base se abre en una pestaña nueva.
    /// </summary>
    public void Activate(ConnectionProfile profile)
    {
        profile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        _ = CatalogFor(profile); // el usuario eligió la base: el autocompletado empieza a cargarse ya
        var tab = SelectedTab;
        if (tab?.Profile?.Id == profile.Id) return;
        if (tab is { Kind: EditorTabKind.Sql or EditorTabKind.Search, CanChangeProfile: true })
        {
            tab.Profile = profile;
            tab.Status = string.Format(Strings.TabDatabaseIs, profile.Name);
        }
        else
        {
            NewQueryTab(profile).Status = string.Format(Strings.TabNewOnDatabase, profile.Name);
        }
    }

    /// <summary>Se eligió algo en el árbol después de pasar por la pestaña: la consulta nueva va a esa base (D-88).</summary>
    private bool _treeIsNewer;

    /// <summary>Se volvió a la pestaña (clic o foco en su editor): la consulta nueva va a la base de la pestaña.</summary>
    public void TabTouched() => _treeIsNewer = false;

    /// <summary>Clic en el árbol, aunque sea en la fila que ya estaba elegida: la consulta nueva va a la base de esa fila.</summary>
    public void TreeTouched() => _treeIsNewer = true;

    /// <summary>
    /// Ctrl+T o «+»: consulta nueva en la base de lo último que se tocó (D-88): la elegida en el árbol si se la eligió
    /// después de la pestaña; si no, la de la pestaña elegida y, sin pestañas, la del árbol. Sin ninguna no abre nada y lo dice.
    /// </summary>
    public EditorTabViewModel? NewQueryTabFor(ConnectionProfile? treeSelection)
    {
        if (PickProfile(treeSelection) is not { } profile) return null;
        return NewQueryTab(profile);
    }

    /// <summary>Ctrl+Shift+F: búsqueda en la base de lo último que se tocó, igual que la consulta nueva.</summary>
    public SearchTabViewModel? NewSearchTabFor(ConnectionProfile? treeSelection)
    {
        if (PickProfile(treeSelection) is not { } profile) return null;
        return NewSearchTab(profile);
    }

    private readonly SessionStore? _session;

    /// <summary>
    /// Al cerrar (si está la opción, de fábrica sí): las pestañas de consulta con su base y su texto, o el archivo .sql de
    /// donde vuelven a leerse (lo no guardado de un archivo se descartó al salir). Las vacías no se guardan.
    /// </summary>
    public void SaveSession()
    {
        if (_session is null) return;
        if (!Options.RestoreTabs)
        {
            _session.Clear();
            return;
        }
        var tabs = Tabs.Where(t => t.Kind == EditorTabKind.Sql && t is not SearchTabViewModel && (t.FilePath is not null || t.Document.TextLength > 0)).ToList();
        _session.Save(new SessionFile
        {
            Tabs = tabs.Select(t => new SessionTab(t.Profile?.Id, t.BaseTitle, t.FilePath, t.FilePath is null ? t.Document.Text : null)).ToList(),
            Selected = SelectedTab is { } selected ? Math.Max(0, tabs.IndexOf(selected)) : 0,
        });
    }

    /// <summary>Al abrir: las consultas de la última vez, en sus bases (si siguen registradas) y la que estaba elegida.</summary>
    public void RestoreSession()
    {
        if (_session is null || !Options.RestoreTabs || _session.Load() is not { Tabs.Count: > 0 } session) return;
        var restored = new List<EditorTabViewModel>();
        foreach (var t in session.Tabs)
        {
            var profile = t.ProfileId is { } id ? Profiles.FirstOrDefault(p => p.Id == id) : null;
            if (t.FilePath is { } path)
            {
                if (!File.Exists(path)) continue;
                var tab = NewQueryTab(profile);
                try { tab.LoadFile(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tab.Status = string.Format(Strings.FileOpenFailed, Path.GetFileName(path), ex.Message); }
                restored.Add(tab);
            }
            else
            {
                restored.Add(NewQueryTab(profile, t.Text ?? "", t.Title));
                // «Consulta 7»: las nuevas siguen numerando desde la mayor.
                if (System.Text.RegularExpressions.Regex.Match(t.Title, @"(\d+)$") is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n))
                    _queryNumber = Math.Max(_queryNumber, n);
            }
        }
        if (restored.Count == 0) return;
        SelectedTab = restored[Math.Clamp(session.Selected, 0, restored.Count - 1)];
        Notify(string.Format(Strings.SessionRestored, restored.Count));
    }

    /// <summary>Los últimos archivos .sql abiertos o guardados, el más reciente primero.</summary>
    public IReadOnlyList<string> RecentFiles => Options.RecentFiles ?? [];

    /// <summary>
    /// Consulta → «Abrir archivo .sql…» (o uno de los recientes): si ya está abierto, va a su pestaña; si no, lo lee en la
    /// consulta elegida si está vacía o en una nueva, en la base de lo último que se tocó (puede quedar sin base).
    /// </summary>
    public EditorTabViewModel? OpenSqlFile(string path, ConnectionProfile? treeSelection = null)
    {
        if (Tabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            SelectedTab = open;
            return open;
        }
        if (!File.Exists(path))
        {
            Notify(string.Format(Strings.FileNotFound, path));
            SaveSettings(Options with { RecentFiles = RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList() });
            OnPropertyChanged(nameof(RecentFiles));
            return null;
        }
        var empty = SelectedTab is { Kind: EditorTabKind.Sql, FilePath: null, IsModified: false, IsRunning: false } current && current.Document.TextLength == 0
            ? current : null;
        var profile = _treeIsNewer ? treeSelection ?? SelectedProfile : SelectedProfile ?? treeSelection;
        var tab = empty ?? NewQueryTab(profile is null ? null : Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile);
        try
        {
            tab.LoadFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tab.Status = string.Format(Strings.FileOpenFailed, Path.GetFileName(path), ex.Message);
            return tab;
        }
        RememberFile(path);
        return tab;
    }

    /// <summary>Suma el archivo a los recientes (al abrirlo o guardarlo).</summary>
    public void RememberFile(string path)
    {
        SaveSettings(Options with { RecentFiles = [path, .. RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))] });
        OnPropertyChanged(nameof(RecentFiles));
    }

    private ConnectionProfile? PickProfile(ConnectionProfile? treeSelection)
    {
        var picked = _treeIsNewer ? treeSelection ?? SelectedProfile : SelectedProfile ?? treeSelection;
        if (picked is { } profile) return Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        Notify(Tree.IsEmpty ? Strings.RegisterFirst : Strings.PickDatabaseInTree);
        return null;
    }

    /// <summary>
    /// Desconectar una base: se cierran todas sus pestañas (consultas, objetos, búsqueda y monitoreo)
    /// y la conexión del árbol. Si alguna tiene una transacción abierta o cambios sin guardar, pregunta una sola vez
    /// nombrándolas; si se cancela, o si alguna está ejecutando, no se desconecta nada.
    /// </summary>
    public async Task<bool> DisconnectAsync(ConnectionProfile profile)
    {
        var tabs = Tabs.Where(t => t.Profile?.Id == profile.Id).ToList();
        if (tabs.FirstOrDefault(t => t.IsRunning) is { } running)
        {
            Notify(string.Format(Strings.DisconnectWhileRunning, profile.Name, running.BaseTitle));
            return false;
        }
        if (!await CloseTabsAsync(tabs)) return false;
        await Tree.DisconnectAsync(profile.Id);
        InvalidateCatalog(profile.Id);
        Tree.Status = tabs.Count == 0
            ? string.Format(Strings.DisconnectedFrom, profile.Name)
            : string.Format(Strings.DisconnectedClosingTabs, profile.Name, tabs.Count);
        return true;
    }

    /// <summary>La base tiene algo abierto (conexión del árbol o pestañas): «Desconectar» tiene sentido.</summary>
    public bool CanDisconnect(Guid profileId) => Tree.IsConnected(profileId) || Tabs.Any(t => t.Profile?.Id == profileId);

    /// <summary>Pestaña de consulta nueva; sin base explícita usa la de la pestaña elegida.</summary>
    public EditorTabViewModel NewQueryTab(ConnectionProfile? profile = null, string text = "", string? title = null)
    {
        var tab = new EditorTabViewModel(this, profile ?? SelectedProfile, title ?? string.Format(Strings.QueryTabTitle, ++_queryNumber), text);
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Pestaña de búsqueda en metadatos (reutiliza la de la misma base si ya hay una).</summary>
    public SearchTabViewModel NewSearchTab(ConnectionProfile? profile = null)
    {
        profile ??= SelectedProfile;
        var tab = Tabs.OfType<SearchTabViewModel>().FirstOrDefault(t => t.Profile?.Id == profile?.Id && !t.IsRunning);
        if (tab is null)
        {
            tab = new SearchTabViewModel(this, profile) { Kind = EditorTabKind.Search };
            Tabs.Add(tab);
        }
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Abre una consulta con nombre (p. ej. "SELECT FIRST 100 *" de una tabla) en su propia pestaña: reutiliza la que
    /// tenga el mismo título y base si está libre.
    /// </summary>
    public EditorTabViewModel OpenQueryTab(ConnectionProfile profile, string title, string sql)
    {
        var tab = Tabs.FirstOrDefault(t => t.Kind == EditorTabKind.Sql && t.BaseTitle == title && t.Profile?.Id == profile.Id && t.CanChangeProfile)
                  ?? NewQueryTab(profile, title: title);
        tab.Document.Text = sql;
        SelectedTab = tab;
        return tab;
    }

    /// <summary>
    /// Abre el DDL de un objeto en su pestaña (si ya está abierta, la elige). <paramref name="line"/>: lleva el cursor
    /// a esa línea (resultados de la búsqueda en metadatos).
    /// </summary>
    public async Task<EditorTabViewModel?> OpenObjectAsync(ConnectionProfile profile, DbObject obj, int? line = null)
    {
        var existing = Tabs.FirstOrDefault(t => t.Kind == EditorTabKind.Object && t.Profile?.Id == profile.Id
                                               && t.Object is { } o && o.Kind == obj.Kind && o.Name == obj.Name);
        if (existing is not null)
        {
            SelectedTab = existing;
            if (line is { } l) existing.GoTo(l, 1);
            return existing;
        }

        // El aviso va en la pestaña de origen y se le devuelve su estado cuando la pestaña nueva queda abierta.
        var origin = SelectedTab;
        var previousStatus = origin?.Status;
        var reading = string.Format(Strings.ReadingDdl, obj.Name);
        if (origin is not null) origin.Status = reading;
        try
        {
            var script = await ScriptAsync(profile, obj);
            if (origin is not null && origin.Status == reading) origin.Status = previousStatus ?? "";
            var tab = new EditorTabViewModel(this, Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile, obj.Name, script.Ddl)
            {
                Kind = EditorTabKind.Object,
                Object = obj,
                Description = script.Description,
                SelectedResultTab = EditorTabViewModel.DependenciesTab,
            };
            Tabs.Add(tab);
            SelectedTab = tab;
            _ = tab.LoadDependenciesAsync();
            tab.Status = tab.IsCompilable
                ? string.Format(Strings.ObjectTabCompilable, DbObjectKinds.Singular(obj.Kind), obj.Name)
                : string.Format(Strings.ObjectTabDdl, obj.Name);
            if (line is { } l) tab.GoTo(l, 1);
            return tab;
        }
        catch (Exception ex)
        {
            if (SelectedTab is { } t) t.Status = string.Format(Strings.DdlReadFailed, obj.Name, ex.Message);
            return null;
        }
    }

    /// <summary>«Generar SQL» de una tabla o vista del árbol: la sentencia en una consulta nueva de esa base, sin ejecutar.</summary>
    public async Task<EditorTabViewModel?> GenerateTableSqlAsync(ConnectionProfile profile, DbObject obj, TableSqlKind kind)
    {
        try
        {
            var columns = await WithExplorerAsync(profile, explorer => explorer.LoadColumnsAsync(obj.Name));
            if (columns.Count == 0)
            {
                Notify(string.Format(Strings.TableSqlNoColumns, obj.Name)); // se borró después de cargar el árbol
                return null;
            }
            return NewQueryTab(Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile, TableSql.Build(kind, obj.Name, columns));
        }
        catch (Exception ex)
        {
            Notify(string.Format(Strings.DdlReadFailed, obj.Name, ex.Message));
            return null;
        }
    }

    /// <summary>
    /// «Generar SQL» de un procedimiento o una función del árbol: la llamada con sus parámetros, en una consulta nueva de
    /// esa base, sin ejecutar. «SELECT * FROM» solo si el procedimiento devuelve algo; si no, EXECUTE PROCEDURE.
    /// </summary>
    public async Task<EditorTabViewModel?> GenerateRoutineSqlAsync(ConnectionProfile profile, DbObject obj, bool select)
    {
        try
        {
            var catalog = await CatalogForAsync(profile);
            if (!catalog.Signatures.TryGetValue(obj.Name, out var signature))
            {
                Notify(string.Format(Strings.RoutineSqlNotFound, obj.Name));
                return null;
            }
            var sql = obj.Kind == DbObjectKind.Function
                ? RoutineCallSql.Function(signature)
                : RoutineCallSql.Procedure(signature, select && catalog.Columns.ContainsKey(obj.Name));
            return NewQueryTab(Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile, sql);
        }
        catch (Exception ex)
        {
            Notify(string.Format(Strings.DdlReadFailed, obj.Name, ex.Message));
            return null;
        }
    }

    /// <summary>DDL con el explorador del árbol (si la base está conectada) o con uno temporal.</summary>
    internal Task<ObjectScript> ScriptAsync(ConnectionProfile profile, DbObject obj) =>
        WithExplorerAsync(profile, explorer => explorer.ScriptAsync(obj));

    internal Task<IReadOnlyList<DbDependency>> DependenciesAsync(ConnectionProfile profile, DbObject obj) =>
        WithExplorerAsync(profile, explorer => explorer.DependenciesAsync(obj));

    /// <summary>«¿Dónde se usa?» de una columna (lo que hay que mirar antes de cambiarla o borrarla).</summary>
    public Task<IReadOnlyList<DbDependency>> ColumnUsageAsync(ConnectionProfile profile, string table, string column) =>
        WithExplorerAsync(profile, explorer => explorer.ColumnUsageAsync(table, column));

    /// <summary>Lecturas de metadatos con el explorador del árbol (si la base está conectada) o con uno temporal.</summary>
    internal async Task<T> WithExplorerAsync<T>(ConnectionProfile profile, Func<MetadataExplorer, Task<T>> read)
    {
        if (Tree.ExplorerFor(profile.Id) is { } explorer) return await read(explorer);
        var password = _profiles.GetPassword(profile.Id)
                       ?? throw new InvalidOperationException(string.Format(Strings.NoSavedPassword, profile.Name));
        await using var temporary = new MetadataExplorer(profile, password);
        return await read(temporary);
    }

    /// <summary>Cierra una pestaña; con una transacción abierta pregunta (al cerrar se deshace). Puede no quedar ninguna.</summary>
    public async Task CloseTabAsync(EditorTabViewModel tab) => await CloseTabAsync(tab, ask: true);

    private async Task CloseTabAsync(EditorTabViewModel tab, bool ask)
    {
        if (tab.IsRunning)
        {
            tab.Status = Strings.CloseWhileRunning;
            return;
        }
        if (ask && tab.IsModified && !await ConfirmDiscardAsync(new DiscardPrompt(
                string.Format(tab.HasUnsavedFile || tab is AreasTabViewModel ? Strings.CloseDiscardUnsaved : Strings.CloseDiscardChanges, tab.BaseTitle), Strings.DiscardAndClose)))
            return;
        if (ask && tab.Data is { HasPending: true } data
            && !await ConfirmDiscardAsync(new DiscardPrompt(string.Format(Strings.DataCloseDiscard, tab.BaseTitle, data.PendingCount), Strings.DiscardAndClose)))
            return;
        if (ask && tab.InTransaction && !await ConfirmCloseAsync(
                string.Format(Strings.CloseOpenTransaction, tab.BaseTitle)))
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        RememberClosed(tab);
        if (SelectedTab == tab) SelectedTab = Tabs.Count == 1 ? null : Tabs[index == Tabs.Count - 1 ? index - 1 : index + 1];
        Tabs.Remove(tab);
        await tab.DisposeAsync();
    }

    /// <summary>Las consultas cerradas, la última arriba (Ctrl+Shift+T las vuelve a abrir). Solo en memoria, hasta 20.</summary>
    private readonly List<SessionTab> _closed = [];

    public bool CanReopenClosedTab => _closed.Count > 0;

    private void RememberClosed(EditorTabViewModel tab)
    {
        if (tab.Kind != EditorTabKind.Sql || tab is SearchTabViewModel || (tab.FilePath is null && tab.Document.TextLength == 0)) return;
        _closed.Add(new SessionTab(tab.Profile?.Id, tab.BaseTitle, tab.FilePath, tab.FilePath is null ? tab.Document.Text : null));
        if (_closed.Count > 20) _closed.RemoveAt(0);
        OnPropertyChanged(nameof(CanReopenClosedTab));
    }

    /// <summary>Ctrl+Shift+T: vuelve a abrir la última consulta cerrada, con su base y su texto (o su archivo).</summary>
    public EditorTabViewModel? ReopenClosedTab()
    {
        if (_closed.Count == 0)
        {
            Notify(Strings.NothingToReopen);
            return null;
        }
        var closed = _closed[^1];
        _closed.RemoveAt(_closed.Count - 1);
        OnPropertyChanged(nameof(CanReopenClosedTab));
        var profile = closed.ProfileId is { } id ? Profiles.FirstOrDefault(p => p.Id == id) : null;
        if (closed.FilePath is { } path) return OpenSqlFile(path, profile);
        return NewQueryTab(profile, closed.Text ?? "", closed.Title);
    }

    /// <summary>«Duplicar»: una consulta nueva de la misma base con el mismo texto (sin el archivo).</summary>
    public EditorTabViewModel DuplicateTab(EditorTabViewModel tab) => NewQueryTab(tab.Profile, tab.Document.Text);

    /// <summary>
    /// Cierra varias pestañas («cerrar todas», «todas menos esta», «las de la derecha»). Si alguna tiene una
    /// transacción abierta o cambios sin compilar, pregunta una sola vez y las nombra. Las que están ejecutando no se tocan.
    /// Devuelve false si el usuario eligió no cerrarlas.
    /// </summary>
    public async Task<bool> CloseTabsAsync(IEnumerable<EditorTabViewModel> tabs)
    {
        var closing = tabs.Where(Tabs.Contains).ToList();
        var running = closing.Where(t => t.IsRunning).ToList();
        closing = closing.Except(running).ToList();
        if (closing.Count == 0) return true;
        var pending = closing.Where(t => t.InTransaction || t.IsModified || t.Data?.HasPending == true).ToList();
        if (pending.Count > 0)
        {
            var list = string.Join(", ", pending.Select(t => $"«{t.BaseTitle}» ({(t.InTransaction ? Strings.PendingTransaction : t.HasUnsavedFile || t is AreasTabViewModel ? Strings.PendingUnsaved : t.IsModified ? Strings.PendingChanges : Strings.PendingData)})"));
            if (!await ConfirmCloseAsync(string.Format(Strings.CloseManyPending, closing.Count, list)))
                return false;
        }
        foreach (var tab in closing) await CloseTabAsync(tab, ask: false);
        if (running.Count > 0) Notify(string.Format(Strings.CloseManyRunning, running.Count));
        return true;
    }

    /// <summary>
    /// Lo que se pierde al salir, un renglón por tipo: transacciones abiertas o datos sin guardar (rollback), cambios
    /// sin compilar o sin guardar, y consultas que están corriendo. Vacío si se puede salir sin preguntar.
    /// </summary>
    public IReadOnlyList<string> ExitWarnings()
    {
        static string Names(IEnumerable<EditorTabViewModel> tabs) => string.Join(", ", tabs.Select(t => $"«{t.BaseTitle}»"));
        var warnings = new List<string>();
        var open = Tabs.Where(t => t.InTransaction || t.Data?.HasPending == true).ToList();
        if (open.Count > 0) warnings.Add(string.Format(Strings.ExitOpenTransactions, Names(open)));
        var modified = Tabs.Where(t => t.IsModified).ToList();
        if (modified.Count > 0) warnings.Add(string.Format(Strings.ExitUnsavedChanges, Names(modified)));
        var running = Tabs.Where(t => t.IsRunning).ToList();
        if (running.Count > 0) warnings.Add(string.Format(Strings.ExitRunning, Names(running)));
        return warnings;
    }

    public void SelectNextTab(int delta)
    {
        if (Tabs.Count < 2 || SelectedTab is null) return;
        var i = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[((i + delta) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    /// <summary>
    /// Catálogo de autocompletado de una base, compartido por todas sus pestañas. La primera vez lo carga en segundo
    /// plano (con su propia conexión) y mientras tanto devuelve uno vacío; si falla, reintenta a los 30 segundos.
    /// </summary>
    internal MetadataCatalog CatalogFor(ConnectionProfile profile)
    {
        if (!_catalogs.TryGetValue(profile.Id, out var entry) || (entry.Load.IsFaulted && DateTime.UtcNow - entry.Started > TimeSpan.FromSeconds(30)))
        {
            var password = _profiles.GetPassword(profile.Id);
            if (password is null) return MetadataCatalog.Empty;
            entry = (Task.Run(() => MetadataReader.LoadCatalogAsync(profile, password)), DateTime.UtcNow);
            _catalogs[profile.Id] = entry;
        }
        return entry.Load.IsCompletedSuccessfully ? entry.Load.Result : MetadataCatalog.Empty;
    }

    /// <summary>El catálogo de una base, esperando a que termine de cargarse si todavía no está.</summary>
    internal async Task<MetadataCatalog> CatalogForAsync(ConnectionProfile profile)
    {
        CatalogFor(profile);
        return _catalogs.TryGetValue(profile.Id, out var entry) ? await entry.Load : MetadataCatalog.Empty;
    }

    /// <summary>
    /// Descarta el catálogo y las relaciones de una base (después de un DDL o de «Recargar metadatos»): se vuelven a
    /// leer al pedirlos.
    /// </summary>
    public void InvalidateCatalog(Guid profileId)
    {
        _catalogs.Remove(profileId);
        _graphs.Remove(profileId);
    }

    /// <summary>
    /// Las tablas y relaciones de una base para los diagramas (F-8): se leen una vez (en una base grande, cerca de 1 s) y las comparten
    /// todos sus diagramas. Si la lectura falla, la próxima vez se reintenta.
    /// </summary>
    internal Task<Core.Diagrams.SchemaGraph> SchemaGraphAsync(ConnectionProfile profile)
    {
        if (_graphs.TryGetValue(profile.Id, out var task) && !task.IsFaulted && !task.IsCanceled) return task;
        task = WithExplorerAsync(profile, explorer => explorer.LoadSchemaGraphAsync());
        _graphs[profile.Id] = task;
        return task;
    }

    internal void Record(ConnectionProfile profile, string sql, StatementInfo statement, QueryOutcome o) =>
        Record(profile, sql, statement.Keyword, o);

    /// <summary>Un script corrido sin grilla: una sola entrada con el texto entero.</summary>
    internal void RecordScript(ConnectionProfile profile, string script, QueryOutcome o) => Record(profile, script, "SCRIPT", o);

    private void Record(ConnectionProfile profile, string sql, string keyword, QueryOutcome o)
    {
        if (_history is null || !profile.SaveHistory) return;
        // Una contraseña escrita en el editor (CREATE USER … PASSWORD '…') no queda guardada en el historial.
        var entry = new HistoryEntry(0, profile.Id, profile.Name, DateTimeOffset.Now, keyword, Core.Security.SqlSecrets.Mask(sql),
            (long)o.TotalTime.TotalMilliseconds, o.RowsFetched, o.RecordsAffected, o.Cancelled,
            o.Error is null ? null : Core.Security.SqlSecrets.Mask(o.Error), o.Plan);
        try { _history.Add(entry); }
        catch (Exception ex) { if (SelectedTab is { } t) t.Messages = string.Format(Strings.HistorySaveFailed, ex.Message); }
        RefreshHistory();
    }

    public void RefreshHistory()
    {
        if (_history is null) return;
        try
        {
            HistoryItems = _history.Recent(HistoryOnlyThisProfile ? SelectedProfile?.Id : null, HistoryFilter);
        }
        catch (Exception ex)
        {
            if (SelectedTab is { } t) t.Messages = string.Format(Strings.HistoryReadFailed, ex.Message);
        }
    }

    private Task? _disposing;

    /// <summary>
    /// Cierra las sesiones de todas las pestañas (con transacción abierta: rollback) y las del árbol. Una sola vez:
    /// si se pide de nuevo mientras cierra (la ventana al cerrarse y quien la cerró), se espera la misma limpieza.
    /// </summary>
    public ValueTask DisposeAsync() => new(_disposing ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        foreach (var tab in Tabs.ToList()) await tab.DisposeAsync();
        await Tree.DisposeAsync();
    }
}

/// <summary>Pregunta antes de descartar cambios sin compilar; <paramref name="DiscardLabel"/> es el texto del botón.</summary>
public sealed record DiscardPrompt(string Message, string DiscardLabel);

public enum ScriptPreviewChoice
{
    Cancel,
    Run,
    OpenInTab,
}

/// <summary>
/// Comandos para ver antes de ejecutar, con los avisos (producción, borrados). <paramref name="WithCommit"/>: cómo arranca
/// la casilla «Terminar con COMMIT» (vale para copiarlo o llevarlo a una consulta; «Ejecutar» siempre confirma).
/// </summary>
public sealed record ScriptPreview(string Title, string Script, IReadOnlyList<string> Warnings, bool WithCommit = false);

/// <summary>Qué se eligió en la ventana de los comandos y si el script va con COMMIT al final.</summary>
public sealed record ScriptPreviewResult(ScriptPreviewChoice Choice, bool WithCommit);

/// <summary>Un parámetro de una consulta: su nombre («desde», o «#1» si es un «?» sin nombre), lo escrito y si va NULL.</summary>
public sealed record ParameterEntry(string Name, string Text, bool IsNull);

/// <summary>Un texto que se pide (p. ej. el nombre de un rol nuevo).</summary>
public sealed record TextPrompt(string Title, string Message, string Initial = "");

/// <summary>Confirmación de una acción: los motivos, el texto del botón que la hace, el de no hacerla y el título.</summary>
public sealed record ConfirmPrompt(IReadOnlyList<string> Reasons, string RunText, string CancelText, string Title)
{
    /// <summary>
    /// «¿Reiniciar ahora?» después de cambiar el idioma, escrito en el idioma ELEGIDO (quien lo cambia puede no leer
    /// el actual). <paramref name="languageCode"/>: "" (el de Windows), "es" o "en".
    /// </summary>
    public static ConfirmPrompt Restart(string languageCode, int openTabs)
    {
        var culture = UiLanguage.CultureFor(languageCode);
        string T(string key) => UiLanguage.Text(key, culture);
        List<string> reasons = [T(nameof(Strings.LanguageRestartQuestion))];
        if (openTabs > 0) reasons.Add(string.Format(culture, T(nameof(Strings.LanguageRestartTabs)), openTabs));
        return new ConfirmPrompt(reasons, T(nameof(Strings.RestartNow)), T(nameof(Strings.RestartLater)), T(nameof(Strings.LanguageRestartTitle)));
    }
}
