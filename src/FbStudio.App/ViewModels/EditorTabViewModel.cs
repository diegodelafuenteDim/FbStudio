using System.Diagnostics;
using FbStudio.App.Resources;
using System.Text;
using System.Threading.Channels;
using Avalonia.Collections;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.Core.Export;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Qué parte del editor ejecuta F9 / Ctrl+Enter.</summary>
public enum ExecutionScope
{
    /// <summary>Todo el editor (F9 sin selección).</summary>
    All,
    /// <summary>La selección (F9 con selección).</summary>
    Selection,
    /// <summary>La sentencia donde está el cursor (Ctrl+Enter).</summary>
    Statement,
}

public enum EditorTabKind
{
    Sql,
    Object,
    Search,
    Monitor,
    Statistics,
    Script,
    Compare,
    Maintenance,
    Trace,
    Import,
    Security,
    Areas,
    Diagram,
}

/// <summary>
/// Una pestaña de editor: su base, su sesión (conexión + transacción propias, D-04), su texto y sus resultados.
/// Las pestañas de objeto (procedimiento, vista, tabla…) muestran el DDL y, si es PSQL, permiten compilarlo.
/// </summary>
public partial class EditorTabViewModel : ObservableObject, IAsyncDisposable
{
    public const int MessagesTab = 4; // Resultados, Plan, Estadísticas, Historial, Mensajes, Dependencias
    public const int DependenciesTab = 5;
    public const int DataTab = 6;
    private const int UiChunkRows = 5_000;

    private readonly MainWindowViewModel _owner;
    private QuerySession? _session;
    private CancellationTokenSource? _cts;

    // Mientras se espera la primera fila: cuánto lleva y si el plan avisa una lectura completa.
    private readonly Stopwatch _runClock = new();
    private string? _runKeyword;
    private string? _planWarning;
    private Avalonia.Threading.DispatcherTimer? _waitTimer;

    // Último resultado con filas: para exportarlo (o re-ejecutarlo completo).
    private string? _lastSql;
    private IReadOnlyList<object?>? _lastParameters;
    private StatementInfo? _lastStatement;
    private ColumnInfo[] _lastColumns = [];

    // Texto tal como se leyó (o se compiló) por última vez: para avisar de cambios sin compilar.
    private string _savedText;

    public EditorTabViewModel(MainWindowViewModel owner, ConnectionProfile? profile, string baseTitle, string text = "")
    {
        _owner = owner;
        _profile = profile;
        _baseTitle = baseTitle;
        Document = new TextDocument(text);
        _savedText = text;
        Document.TextChanged += (_, _) => { if (Kind == EditorTabKind.Object) IsModified = Document.Text != _savedText; };
    }

    public EditorTabKind Kind { get; init; } = EditorTabKind.Sql;

    /// <summary>Objeto que muestra una pestaña de objeto (null en pestañas de consulta).</summary>
    public DbObject? Object { get; init; }

    public bool IsObjectTab => Kind == EditorTabKind.Object;

    /// <summary>Tablas y vistas tienen la pestaña «Datos» (F-3).</summary>
    public bool HasDataTab => Kind == EditorTabKind.Object && Object is { } o && DbObjectKinds.IsRelation(o.Kind);

    private TableDataViewModel? _data;

    /// <summary>Datos de la tabla o vista (se leen recién al elegir la pestaña).</summary>
    public TableDataViewModel? Data => HasDataTab ? _data ??= new TableDataViewModel(this) : null;

    /// <summary>Procedimientos, triggers, vistas y funciones se pueden editar y compilar desde su pestaña.</summary>
    public bool IsCompilable => Kind == EditorTabKind.Object && Object is { } o && DbObjectKinds.IsCompilable(o.Kind);

    public string ObjectCaption => Object is { } o ? $"{DbObjectKinds.Singular(o.Kind)} {o.Name}" + (o.Detail is { } d ? $" · {d}" : "") : "";

    private string _baseTitle;

    public string BaseTitle
    {
        get => _baseTitle;
        protected set
        {
            _baseTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TitleText));
        }
    }

    public TextDocument Document { get; }
    public MainWindowViewModel Owner => _owner;
    public virtual MaterialIconKind Icon => Object is { } o ? DatabaseTreeViewModel.IconFor(o.Kind) : MaterialIconKind.FileDocumentEditOutline;
    public AvaloniaList<object[]> Rows { get; } = [];
    /// <summary>
    /// Catálogo de la base para autocompletar: compartido entre pestañas y cargado en segundo plano la primera vez
    /// que se pide (mientras tanto, vacío).
    /// </summary>
    public MetadataCatalog Catalog => Profile is { } p ? _owner.CatalogFor(p) : MetadataCatalog.Empty;

    /// <summary>
    /// Ctrl+clic o F12 (F-32): abre el objeto cuyo nombre está en <paramref name="offset"/>. Un alias («t» o «t.ID»)
    /// lleva a su tabla; una columna sin alias, a la tabla del FROM que la tiene; lo demás se busca por nombre en la base
    /// (tabla, vista, procedimiento, función, paquete, generador, excepción, trigger, índice, dominio o rol).
    /// </summary>
    public async Task<bool> GoToDefinitionAsync(string text, int offset)
    {
        if (Profile is not { } profile)
        {
            Status = Strings.NoDatabase;
            return false;
        }
        if (SqlNavigation.NameAt(text, offset) is not { } name)
        {
            Status = Strings.GoToNoName;
            return false;
        }
        var target = name.Qualifier is { } q
            ? SqlNavigation.TableOfAlias(text, offset, q) ?? q
            : SqlNavigation.TableOfAlias(text, offset, name.Name) ?? name.Name;
        try
        {
            Status = string.Format(Strings.GoToOpening, target);
            var obj = await _owner.WithExplorerAsync(profile, e => e.FindObjectAsync(target));
            if (obj is null && name.Qualifier is null)
            {
                var statement = SqlScriptSplitter.AtCaret(text, offset)?.Text ?? text;
                var catalog = Catalog;
                if (SqlCompletion.TablesInText(statement).FirstOrDefault(t => catalog.Columns.TryGetValue(t, out var columns)
                        && columns.Contains(name.Name, StringComparer.OrdinalIgnoreCase)) is { } table)
                    obj = await _owner.WithExplorerAsync(profile, e => e.FindObjectAsync(table.ToUpperInvariant()));
            }
            if (obj is null)
            {
                Status = string.Format(Strings.GoToNotFound, target, profile.Name);
                return false;
            }
            Status = Strings.Ready;
            await _owner.OpenObjectAsync(profile, obj);
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.ErrorStatus, ex.Message.Split('\n', 2)[0].Trim());
            return false;
        }
    }

    /// <summary>La vista reconstruye las columnas de la grilla cuando llega un resultado con filas.</summary>
    public event Action<ColumnInfo[]>? ColumnsChanged;

    /// <summary>La vista mueve el cursor a una línea (errores de compilación con "line N, column M").</summary>
    public event Action<int, int>? GoToRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(TitleText), nameof(EnvironmentTone), nameof(IsSuccessTone), nameof(IsWarningTone), nameof(IsDangerTone),
        nameof(IsDevTone), nameof(IsTestTone), nameof(IsProdTone), nameof(CompileHint), nameof(CanChooseTransactionMode), nameof(TransactionModeTip))]
    [NotifyCanExecuteChangedFor(nameof(CompileCommand))]
    private ConnectionProfile? _profile;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Comentario del objeto (COMMENT ON).</summary>
    [ObservableProperty] private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(TitleText))]
    private bool _isModified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesHeader), nameof(UsesItems))]
    private IReadOnlyList<DependencyRow> _uses = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedByHeader), nameof(UsedByItems))]
    private IReadOnlyList<DependencyRow> _usedBy = [];

    /// <summary>Filtra las dos listas de dependencias por nombre o detalle (columnas, FK).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesItems), nameof(UsedByItems), nameof(UsesHeader), nameof(UsedByHeader))]
    private string _dependencyFilter = "";

    /// <summary>Lo que usa el objeto, agrupado por tipo («Tablas (11)») y filtrado.</summary>
    public IReadOnlyList<DependencyListItem> UsesItems => DependencyListItem.Build(Uses, DependencyFilter);

    /// <summary>Quién usa al objeto, agrupado por tipo («Procedimientos (167)») y filtrado.</summary>
    public IReadOnlyList<DependencyListItem> UsedByItems => DependencyListItem.Build(UsedBy, DependencyFilter);

    [ObservableProperty] private string _dependenciesStatus = "";

    public string UsesHeader => Header(Strings.DependenciesUses, Uses);
    public string UsedByHeader => Header(Strings.DependenciesUsedBy, UsedBy);

    private string Header(string label, IReadOnlyList<DependencyRow> rows) =>
        DependencyFilter.Trim().Length == 0
            ? $"{label} ({rows.Count})"
            : $"{label} ({string.Format(Strings.TreeFilterCount, rows.Count(r => DependencyListItem.Matches(r, DependencyFilter)), rows.Count)})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(CommitCommand), nameof(RollbackCommand), nameof(CompileCommand))]
    [NotifyPropertyChangedFor(nameof(CanChangeProfile), nameof(CanChooseTransactionMode))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(RollbackCommand))]
    [NotifyPropertyChangedFor(nameof(CanChangeProfile), nameof(TransactionText), nameof(Title), nameof(IsTransactionOld), nameof(IsTransactionVeryOld),
        nameof(CanChooseTransactionMode), nameof(TransactionModeTip))]
    private bool _inTransaction;

    /// <summary>
    /// Con una transacción de escritura abierta, cada 30 s se vuelve a mirar hace cuánto: pasa a ámbar y a rojo según
    /// Opciones, y al pasar a rojo se avisa una vez (una olvidada traba la OAT del servidor para todos).
    /// </summary>
    partial void OnInTransactionChanged(bool value)
    {
        _warnedOldTransaction = false;
        if (value)
        {
            _transactionTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => RefreshTransactionAge());
            _transactionTimer.Start();
        }
        else _transactionTimer?.Stop();
    }

    private DispatcherTimer? _transactionTimer;
    private bool _warnedOldTransaction;

    /// <summary>La hora (UTC) para medir la transacción; las pruebas la adelantan.</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Hace cuánto está abierta la transacción de escritura (null si no hay).</summary>
    public TimeSpan? TransactionAge => InTransaction && _session?.TransactionStartedAt is { } started ? Clock() - started : null;

    public bool IsTransactionOld => TransactionAge is { } age && age.TotalMinutes >= _owner.Options.TransactionWarnMinutes && !IsTransactionVeryOld;
    public bool IsTransactionVeryOld => TransactionAge is { } age && age.TotalMinutes >= _owner.Options.TransactionDangerMinutes;

    /// <summary>Vuelve a calcular hace cuánto está abierta (lo llama el temporizador) y avisa una vez al pasar a rojo.</summary>
    public void RefreshTransactionAge()
    {
        OnPropertyChanged(nameof(TransactionText));
        OnPropertyChanged(nameof(IsTransactionOld));
        OnPropertyChanged(nameof(IsTransactionVeryOld));
        if (IsTransactionVeryOld && !_warnedOldTransaction && TransactionAge is { } age)
        {
            _warnedOldTransaction = true;
            _owner.Notify(string.Format(Strings.TransactionVeryOldNotice, BaseTitle, AgeText(age)));
        }
    }

    /// <summary>«menos de 1 min», «12 min», «1 h 05 min».</summary>
    internal static string AgeText(TimeSpan age) =>
        age.TotalMinutes < 1 ? Strings.AgeUnderAMinute
        : age.TotalHours < 1 ? string.Format(Strings.AgeMinutes, (int)age.TotalMinutes)
        : string.Format(Strings.AgeHoursMinutes, (int)age.TotalHours, age.Minutes);

    [ObservableProperty] private string _status = Strings.Ready;

    /// <summary>La última ejecución no llegó a dar un resultado (error de conexión, por ejemplo).</summary>
    private bool _lastRunThrew;
    [ObservableProperty] private string _plan = "";
    [ObservableProperty] private string _stats = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages))]
    private string _messages = "";

    /// <summary>Hay mensajes: la pestaña Mensajes ofrece guardarlos o copiarlos.</summary>
    public bool HasMessages => Messages.Length > 0;

    /// <summary>
    /// Lo que se guarda con «Guardar registro…»: cuándo, en qué base (sin la contraseña) y los mensajes, con saltos de
    /// línea de Windows.
    /// </summary>
    public string LogForFile(DateTime now)
    {
        var header = new List<string> { string.Format(Strings.LogFileHeader, now) };
        if (Profile is { } p) header.Add(string.Format(Strings.LogFileDatabase, p.Name, p.User, p.Host, p.Port, p.Database));
        var text = string.Join("\n", header) + "\n\n" + Messages;
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
    [ObservableProperty] private int _selectedResultTab;

    partial void OnSelectedResultTabChanged(int value)
    {
        if (value == DataTab && Data is { } data) _ = data.EnsureLoadedAsync();
    }
    [ObservableProperty] private bool _canExportFull;

    public string Title => (InTransaction ? "● " : "") + TitleText;

    /// <summary>El título sin el ● (en la pestaña, el ● va aparte y en rojo).</summary>
    public string TitleText => BaseTitle + (IsModified ? " *" : "") + (Profile is { } p ? $" · {p.Name}" : "");

    /// <summary>Color del entorno para el borde de la pestaña (verde, naranja, rojo; gris sin base).</summary>
    public Tone EnvironmentTone => Tones.ForEnvironment(Profile?.Environment);
    public bool IsSuccessTone => EnvironmentTone == Tone.Success;
    public bool IsWarningTone => EnvironmentTone == Tone.Warning;
    public bool IsDangerTone => EnvironmentTone == Tone.Danger;
    public bool IsDevTone => EnvironmentTone == Tone.Development;
    public bool IsTestTone => EnvironmentTone == Tone.Test;
    public bool IsProdTone => EnvironmentTone == Tone.Production;

    public bool CanChangeProfile => !IsRunning && !InTransaction;
    public string TransactionText => !InTransaction ? ""
        : TransactionAge is { } age ? string.Format(Strings.TransactionOpenBannerAge, AgeText(age))
        : Strings.TransactionOpenBanner;

    /// <summary>Último texto de la consulta (para exportar todo o un script INSERT).</summary>
    public string? LastSql => _lastSql;

    /// <summary>
    /// Filas elegidas en la grilla como INSERT o UPDATE, para el portapapeles. En la pestaña Datos la tabla, su clave y
    /// sus columnas calculadas son las de la pestaña. En Resultados se toma la tabla del FROM de la última consulta y se
    /// leen de los metadatos su clave y sus columnas: las del resultado que no son de la tabla (expresiones, alias) o que
    /// son calculadas no se escriben. Devuelve el texto (null si no se pudo) y el mensaje para la barra de estado.
    /// </summary>
    public async Task<(string? Text, string Message)> RowScriptAsync(IReadOnlyList<ColumnInfo> columns, IReadOnlyList<object?[]> rows, bool update, bool fromData)
    {
        if (rows.Count == 0) return (null, Strings.RowScriptNoRows);
        var (target, problem) = fromData ? DataScriptTarget() : await QueryScriptTargetAsync(columns);
        if (target is null) return (null, problem ?? Strings.RowScriptNoTable);
        if (update)
        {
            if (target.KeyColumns is not { Count: > 0 } keys) return (null, problem ?? string.Format(Strings.RowScriptNoKey, RowScript.QuoteTable(target.Table)));
            var missing = keys.Where(k => columns.All(c => c.Name != k)).ToList();
            if (missing.Count > 0) return (null, string.Format(Strings.RowScriptKeyMissing, RowScript.QuoteTable(target.Table), string.Join(", ", missing)));
            if (columns.All(c => keys.Contains(c.Name) || target.SkipColumns.Contains(c.Name))) return (null, Strings.RowScriptNothingToSet);
        }
        if (columns.All(c => target.SkipColumns.Contains(c.Name))) return (null, string.Format(Strings.RowScriptNoColumns, RowScript.QuoteTable(target.Table)));
        var text = update ? RowScript.Update(target, columns, rows) : RowScript.Insert(target, columns, rows);
        var skipped = columns.Select(c => c.Name).Where(target.SkipColumns.Contains).Distinct().ToList();
        var message = string.Format(Strings.RowScriptCopied, rows.Count, update ? "UPDATE" : "INSERT", RowScript.QuoteTable(target.Table))
                      + (skipped.Count > 0 ? " " + string.Format(Strings.RowScriptSkipped, string.Join(", ", skipped)) : "")
                      + (target.TableGuessed ? " " + string.Format(Strings.RowScriptGuessedTable, target.Table) : "");
        return (text, message);
    }

    private (RowScriptTarget? Target, string? Problem) DataScriptTarget()
    {
        if (Data is not { IsLoaded: true } data) return (null, Strings.RowScriptNoTable);
        var keys = data.KeyColumnNames;
        var target = new RowScriptTarget(SqlIdentifier.Quote(data.Table), keys.Count > 0 ? keys : null, data.ComputedColumnNames);
        return (target, keys.Count == 0 ? string.Format(Strings.RowScriptNoKey, SqlIdentifier.Quote(data.Table)) : null);
    }

    private async Task<(RowScriptTarget? Target, string? Problem)> QueryScriptTargetAsync(IReadOnlyList<ColumnInfo> columns)
    {
        var written = StatementClassifier.MainTable(_lastSql);
        if (written is null || Profile is not { } profile)
            return (new RowScriptTarget(Strings.ExportInsertDefaultTable, null, new HashSet<string>(), TableGuessed: true), Strings.RowScriptNoTable);
        var name = written.StartsWith('"') ? written.Trim('"').Replace("\"\"", "\"") : written.ToUpperInvariant();
        IReadOnlyList<DbColumn> tableColumns;
        try
        {
            tableColumns = await _owner.WithExplorerAsync(profile, e => e.LoadColumnsAsync(name));
        }
        catch (Exception)
        {
            tableColumns = []; // sin metadatos: el INSERT va con todas las columnas
        }
        if (tableColumns.Count == 0) return (new RowScriptTarget(written, null, new HashSet<string>()), Strings.RowScriptNoTable);
        var writable = tableColumns.Where(c => !c.IsComputed).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var skip = columns.Select(c => c.Name).Where(n => !writable.Contains(n)).ToHashSet(StringComparer.Ordinal);
        var keys = tableColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
        var quoted = SqlIdentifier.Quote(name);
        return (new RowScriptTarget(quoted, keys.Count > 0 ? keys : null, skip), keys.Count == 0 ? string.Format(Strings.RowScriptNoKey, quoted) : null);
    }
    public ColumnInfo[] LastColumns => _lastColumns;

    async partial void OnProfileChanged(ConnectionProfile? value)
    {
        if (value is { IsProduction: true } or { ReadOnly: true })
        {
            AutoCommit = false;
            IsolationIndex = 0;
        }
        CanExportFull = false;
        if (_session is not null)
        {
            var old = _session;
            _session = null;
            await old.DisposeAsync();
        }
    }

    /// <summary>
    /// Lo que la red de seguridad necesita además de la sentencia: en producción, un SELECT que va a correr dentro de una
    /// transacción de escritura abierta, con los procedimientos y funciones que usa (D-73). En los demás casos no se lee el SQL.
    /// </summary>
    private SafetyContext Context(ConnectionProfile profile, StatementInfo statement, string sql, bool writeTransactionOpen) =>
        profile.IsProduction && writeTransactionOpen && statement.Kind == StatementKind.Select
            ? new SafetyContext(true, SqlRoutines.Used(sql, Catalog))
            : SafetyContext.None;

    /// <summary>Ejecuta una sentencia. Devuelve null si no llegó al servidor (bloqueada, sin confirmar) o falló la conexión.</summary>
    public async Task<QueryOutcome?> RunAsync(string sql)
    {
        if (IsRunning) return null;
        if (Profile is not { } profile)
        {
            Status = _owner.Tree.IsEmpty ? Strings.RegisterFirst : Strings.SearchPickDatabase;
            return null;
        }

        var statement = StatementClassifier.Classify(sql);
        var decision = SafetyPolicy.Evaluate(profile, statement, Context(profile, statement, sql, InTransaction));
        if (decision.Verdict == SafetyVerdict.Block)
        {
            Messages = string.Join(Environment.NewLine, decision.Reasons);
            Status = string.Format(Strings.Blocked, decision.Reasons[0]);
            SelectedResultTab = MessagesTab;
            return null;
        }
        if (decision.Verdict == SafetyVerdict.Confirm && !await _owner.ConfirmAsync(decision))
        {
            Status = Strings.NotRunCancelledInConfirmation;
            return null;
        }

        // Parámetros (F-25): «?» y «:nombre» se piden antes de ejecutar y van como valores, no pegados al SQL.
        var toRun = sql;
        IReadOnlyList<object?>? values = null;
        if (SqlParameters.Applies(statement) && SqlParameters.Find(sql) is { IsEmpty: false } parameters)
        {
            if (await _owner.RequestParametersAsync(parameters.Names) is not { } byName)
            {
                Status = Strings.ParamsCancelled;
                return null;
            }
            toRun = parameters.Sql;
            values = parameters.Values(byName);
        }

        IsRunning = true;
        _lastRunThrew = false;
        _cts = new CancellationTokenSource();
        Status = Strings.Connecting;
        try
        {
            var outcome = await ExecuteOneAsync(profile, toRun, statement, _cts.Token, values, historySql: sql);
            ShowOutcome(statement, outcome);
            if (await DescribeLockAsync(outcome.Error) is { } who)
            {
                Status = string.Format(Strings.Blocked, who) + ".";
                Messages = Status + Environment.NewLine + Environment.NewLine + Messages;
            }
            return outcome;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.ErrorStatus, ex.Message);
            Messages = ex.ToString();
            _lastRunThrew = true;
            SelectedResultTab = MessagesTab;
            return null;
        }
        finally
        {
            InTransaction = _session?.InTransaction == true;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Manda una sentencia (ya aprobada por la política de seguridad) y la registra en el historial.</summary>
    /// <param name="parameters">Los valores de los «?» (F-25).</param>
    /// <param name="historySql">Lo que se guarda en el historial: el SQL como se escribió, con sus «:nombre».</param>
    private async Task<QueryOutcome> ExecuteOneAsync(ConnectionProfile profile, string sql, StatementInfo statement, CancellationToken ct,
        IReadOnlyList<object?>? parameters = null, string? historySql = null)
    {
        var clock = Stopwatch.StartNew();
        var session = await EnsureSessionAsync(profile, ct);
        Status = string.Format(Strings.Running, statement.Keyword);
        _runKeyword = statement.Keyword;
        _planWarning = null;
        _runClock.Restart();
        _waitTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateWaitingStatus());
        _waitTimer.Start();

        var channel = Channel.CreateBounded<object[][]>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });
        var pump = Task.Run(() => PumpToUiAsync(channel.Reader, clock));
        var options = new ExecuteOptions(MaxRows: _owner.RowLimit, Parameters: parameters);
        QueryOutcome outcome;
        try
        {
            outcome = await Task.Run(() => session.ExecuteAsync(sql, statement, options,
                cols => Dispatcher.UIThread.Post(() =>
                {
                    Rows.Clear();
                    ColumnsChanged?.Invoke(cols);
                }),
                channel.Writer, ct,
                onPlan: (plan, explained) => Dispatcher.UIThread.Post(() => OnPlanKnown(plan, explained))));
            await pump;
        }
        finally
        {
            _waitTimer.Stop();
            _runKeyword = null;
        }
        // Un CREATE/ALTER/DROP puede cambiar tablas y procedimientos: el autocompletado se vuelve a leer.
        if (statement.Kind == StatementKind.Ddl && outcome.Error is null) _owner.InvalidateCatalog(profile.Id);
        Remember(sql, statement, outcome, parameters);
        _owner.Record(profile, historySql ?? sql, statement, outcome);
        return outcome;
    }

    /// <summary>El plan llega antes que las filas: se muestra y, si recorre una tabla entera, se avisa ya.</summary>
    private void OnPlanKnown(string? plan, bool explained)
    {
        Plan = FormatPlan(plan, explained);
        var scans = PlanAnalyzer.FullScans(plan);
        _planWarning = scans.Count == 0 ? null : string.Format(Strings.PlanFullScanWarning, string.Join(", ", scans));
        UpdateWaitingStatus();
    }

    /// <summary>Mientras no llega ninguna fila: "Ejecutando SELECT… 12 s · Esc cancela" (y el aviso del plan, si hay).</summary>
    private void UpdateWaitingStatus()
    {
        // Solo mientras el estado sigue siendo «Ejecutando X…» (el de espera empieza igual).
        if (!IsRunning || _runKeyword is null || Rows.Count > 0
            || !Status.StartsWith(string.Format(Strings.Running, _runKeyword).TrimEnd('…'), StringComparison.Ordinal)) return;
        Status = string.Format(Strings.RunningWaiting, _runKeyword, _runClock.Elapsed.TotalSeconds) + (_planWarning is { } w ? " · " + w : "");
    }

    /// <summary>«Plan detallado» o «Plan» y el texto del plan; «(sin plan)» si el servidor no lo dio.</summary>
    private static string FormatPlan(string? plan, bool explained) =>
        plan is null ? Strings.NoPlan : $"{(explained ? Strings.PlanDetailed : Strings.PlanTab)}{Environment.NewLine}{Environment.NewLine}{plan}";

    /// <summary>Aviso del plan de la última ejecución ("⚠ lectura completa de …"), o null.</summary>
    public string? PlanWarning => _planWarning;

    /// <summary>Número de la transacción de escritura de la pestaña (si tiene una abierta).</summary>
    internal long? TransactionId => _session?.TransactionId;

    /// <summary>Dice quién tiene tomado el registro en un error de bloqueo: otra pestaña de FbStudio u otra conexión (MON$).</summary>
    internal async Task<string?> DescribeLockAsync(string? error)
    {
        if (PlanAnalyzer.LockingTransaction(error) is not { } id || Profile is not { } profile) return null;
        if (_owner.Tabs.FirstOrDefault(t => t != this && t.TransactionId == id) is { } holder)
            return string.Format(Strings.LockHeldByTab, holder.BaseTitle, id);
        TransactionOwner? owner = null;
        try { owner = await _owner.WithExplorerAsync(profile, e => e.TransactionOwnerAsync(id)); }
        catch { /* sin MON$ igual se informa el número */ }
        return owner is null
            ? string.Format(Strings.LockHeldByOther, id)
            : string.Format(Strings.LockHeldBy, id, owner.User, owner.Address, owner.Process, owner.Since);
    }

    /// <summary>Con más sentencias que esto, F9 corre el script sin grilla (Ctrl+F9 lo pide siempre).</summary>
    public const int LongScript = 200;

    /// <summary>Sin grilla y con más sentencias que esto, el registro lista solo los errores y un resumen por tipo.</summary>
    public const int LogEachStatementUpTo = 500;

    /// <summary>
    /// F9: ejecuta un texto (todo el editor, la selección o la sentencia del cursor). Con una sola
    /// sentencia es una ejecución normal; con varias, un script en orden, en la misma transacción, que se detiene en el
    /// primer error o sigue (Opciones → Consultas). Un COMMIT o ROLLBACK escrito en el script se ejecuta como cualquier
    /// sentencia. Ctrl+F9, o un script de más de <see cref="LongScript"/> sentencias, corre sin grilla.
    /// </summary>
    /// <param name="text">Texto a ejecutar.</param>
    /// <param name="offset">Dónde empieza <paramref name="text"/> dentro del editor (para las líneas y los errores).</param>
    /// <param name="scope">Qué parte del editor es: se avisa en la barra de estado si no es todo.</param>
    /// <param name="asScript">Ctrl+F9: como script sin grilla aunque sea una sola sentencia.</param>
    public async Task RunTextAsync(string text, int offset, ExecutionScope scope, bool asScript = false)
    {
        if (IsRunning) return;
        var statements = SqlScriptSplitter.Split(text)
            .Select(s => (Span: s with { Start = s.Start + offset }, Info: StatementClassifier.Classify(s.Text)))
            .Where(s => s.Info.Kind != StatementKind.Empty)
            .ToList();
        if (statements.Count == 0)
        {
            Status = Strings.NothingToRun;
            return;
        }
        var notice = ScopeNotice(scope, statements[0].Span.Start, statements[^1].Span.Start + statements[^1].Span.Length);

        if (statements.Count == 1 && !asScript)
        {
            var outcome = await RunAsync(statements[0].Span.Text);
            if (outcome?.Error is { } error && FirebirdErrorPosition.Parse(error) is { } position)
            {
                var at = position.InDocument(Document.Text, statements[0].Span.Start);
                GoTo(at.Line, at.Column);
            }
            if (notice is not null && outcome is not null) Status += " · " + notice;
            return;
        }
        var gridless = asScript || statements.Count > LongScript;
        await RunScriptAsync(text, statements, notice, gridless, automatic: gridless && !asScript);
    }

    /// <param name="gridless">
    /// Sin grilla (para scripts largos): sin plan ni lecturas por tabla, la grilla queda como estaba, una sola entrada
    /// en el historial y el avance cada 200 ms.
    /// </param>
    /// <param name="automatic">Se eligió sin grilla por el largo del script (se avisa).</param>
    private async Task RunScriptAsync(string text, List<(StatementSpan Span, StatementInfo Info)> statements, string? notice,
        bool gridless, bool automatic)
    {
        if (Profile is not { } profile)
        {
            Status = _owner.Tree.IsEmpty ? Strings.RegisterFirst : Strings.SearchPickDatabase;
            return;
        }

        // Los parámetros se piden solo con una sentencia sola (en un script no se sabría a cuál va cada «?»).
        if (statements.Any(s => SqlParameters.Applies(s.Info) && !SqlParameters.Find(s.Span.Text).IsEmpty))
        {
            Status = Strings.ParamsOnlySingle;
            Messages = Status;
            SelectedResultTab = MessagesTab;
            return;
        }

        // Seguridad: se revisa todo el script antes de mandar la primera sentencia. La transacción de escritura queda
        // abierta desde lo primero que modifica hasta un COMMIT o ROLLBACK del script.
        var open = InTransaction;
        var decisions = new List<(StatementSpan Span, StatementInfo Info, SafetyContext Context, SafetyDecision Decision)>();
        foreach (var (span, info) in statements)
        {
            var context = Context(profile, info, span.Text, open);
            decisions.Add((span, info, context, SafetyPolicy.Evaluate(profile, info, context)));
            if (info.Kind == StatementKind.Transaction && info.Keyword is "COMMIT" or "ROLLBACK") open = false;
            else if (info.Modifies) open = true;
        }
        var blocked = decisions.FirstOrDefault(d => d.Decision.Verdict == SafetyVerdict.Block);
        if (blocked.Decision is not null)
        {
            Messages = string.Format(Strings.ScriptLineReason, LineOf(blocked.Span.Start), string.Join(Environment.NewLine, blocked.Decision.Reasons));
            Status = string.Format(Strings.ScriptBlocked, blocked.Decision.Reasons[0]);
            SelectedResultTab = MessagesTab;
            return;
        }
        var risky = decisions.Where(d => d.Decision.Verdict == SafetyVerdict.Confirm).ToList();
        if (risky.Count > 0)
        {
            var reasons = new List<string>();
            var production = risky.Count(d => profile.IsProduction && d.Info.Modifies);
            if (production > 0) reasons.Add(string.Format(Strings.ScriptModifiesProduction, production, profile.Name));
            reasons.AddRange(risky.Where(d => d.Info.AffectsAllRows).Select(d => string.Format(Strings.ScriptNoWhere, LineOf(d.Span.Start), d.Info.Keyword)));
            reasons.AddRange(risky.Where(d => d.Context.Routines.Count > 0)
                .Select(d => string.Format(Strings.ScriptRoutineInTransaction, LineOf(d.Span.Start), string.Join(", ", d.Context.Routines))));
            if (!await _owner.ConfirmAsync(new SafetyDecision(SafetyVerdict.Confirm, reasons)))
            {
                Status = Strings.ScriptCancelledInConfirmation;
                return;
            }
        }

        var stopOnError = _owner.Options.ScriptStopOnError;
        var detailed = !gridless || statements.Count <= LogEachStatementUpTo;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var progress = Stopwatch.StartNew();
        var log = new StringBuilder();
        var done = 0;
        var errorLines = new List<int>();
        string? firstError = null;
        var stopped = false;   // por un error (si se para en el primero) o porque se cortó la conexión
        var cancelled = false;
        string? lockNote = null;
        long affected = 0;
        var byKeyword = new Dictionary<string, (int Count, long Rows)>(StringComparer.Ordinal);
        (StatementInfo Info, QueryOutcome Outcome)? lastWithRows = null;
        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                var (span, info) = statements[i];
                if (_cts.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                var line = LineOf(span.Start);
                // Sin grilla puede haber miles de sentencias: el avance se muestra cada 200 ms.
                if (!gridless || i == 0 || progress.ElapsedMilliseconds >= 200)
                {
                    Status = gridless
                        ? string.Format(Strings.ScriptProgressFast, i + 1, statements.Count, (double)i / statements.Count, errorLines.Count)
                        : string.Format(Strings.ScriptProgress, i + 1, statements.Count, info.Keyword, line);
                    progress.Restart();
                }
                var outcome = gridless
                    ? await ExecuteInScriptAsync(profile, span.Text, info, _cts.Token)
                    : await ExecuteOneAsync(profile, span.Text, info, _cts.Token);
                if (outcome.Error is not null)
                {
                    errorLines.Add(line);
                    log.AppendLine(string.Format(Strings.ScriptLogError, line, info.Keyword, ShortError(outcome.Error)));
                    if (firstError is null)
                    {
                        // El cursor va al primer error; si es un bloqueo, se dice quién tiene el registro.
                        firstError = outcome.Error;
                        lockNote = await DescribeLockAsync(outcome.Error);
                        if (FirebirdErrorPosition.Parse(outcome.Error) is { } position)
                        {
                            var at = position.InDocument(Document.Text, span.Start);
                            GoTo(at.Line, at.Column);
                        }
                        else
                        {
                            GoTo(line, 1);
                        }
                    }
                    // Una sentencia que falla se deshace sola: la transacción y lo anterior siguen en pie.
                    if (stopOnError)
                    {
                        stopped = true;
                        break;
                    }
                    continue;
                }
                if (outcome.Cancelled)
                {
                    log.AppendLine(string.Format(Strings.ScriptLogCancelled, line, info.Keyword));
                    cancelled = true;
                    break;
                }
                done++;
                if (outcome.RecordsAffected > 0) affected += outcome.RecordsAffected;
                var k = byKeyword.GetValueOrDefault(info.Keyword);
                byKeyword[info.Keyword] = (k.Count + 1, k.Rows + (outcome.Columns.Length > 0 ? outcome.RowsFetched : Math.Max(0, outcome.RecordsAffected)));
                if (detailed) log.AppendLine(string.Format(Strings.ScriptLogOk, line, Describe(info, outcome, gridless)));
                if (!gridless && outcome.Columns.Length > 0) lastWithRows = (info, outcome);
            }
        }
        catch (Exception ex)
        {
            log.AppendLine($"✗ {ex.Message}");
            firstError ??= ex.Message;
            stopped = true;
        }
        finally
        {
            InTransaction = _session?.InTransaction == true;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }

        if (lastWithRows is { } last)
        {
            Plan = FormatPlan(last.Outcome.Plan, last.Outcome.PlanIsExplained);
            Stats = FormatStats(last.Outcome);
        }
        var summary = string.Format(Strings.ScriptSummary, done, statements.Count, clock.Elapsed.TotalSeconds);
        if (errorLines.Count == 1 && !stopped)
            summary += " · " + string.Format(Strings.ScriptErrorAt, errorLines[0]);
        else if (errorLines.Count > 0 && !stopped)
            summary += " · " + string.Format(Strings.ScriptErrorsAt, errorLines.Count, ListLines(errorLines));
        else if (stopped)
            summary += " · " + (errorLines.Count > 0 ? string.Format(Strings.ScriptStoppedAtLine, errorLines[0]) : Strings.ScriptStopped);
        if (lockNote is not null) summary += " · " + lockNote;
        else if (cancelled) summary += " · " + Strings.ScriptCancelled;
        if (gridless) summary += " · " + (automatic ? string.Format(Strings.ScriptGridlessAuto, LongScript) : Strings.ScriptGridless);
        if (InTransaction) summary += Strings.TransactionOpenSuffix;
        if (notice is not null) summary += " · " + notice;

        var head = new StringBuilder(summary).AppendLine().AppendLine();
        if (!detailed)
        {
            // Script largo: un renglón por tipo de sentencia; abajo, solo los errores.
            foreach (var (keyword, (count, rows)) in byKeyword.OrderByDescending(e => e.Value.Count))
                head.AppendLine(string.Format(Strings.ScriptByKeyword, keyword, count, rows));
            head.AppendLine();
        }
        if (firstError is not null) log.AppendLine().AppendLine(firstError);
        Messages = head.ToString() + log;
        Status = summary;
        SelectedResultTab = !gridless && errorLines.Count == 0 && !stopped && lastWithRows is not null ? 0 : MessagesTab;

        // Sin grilla, el historial guarda el script entero una vez (no miles de entradas).
        if (gridless)
            _owner.RecordScript(profile, text, new QueryOutcome
            {
                TotalTime = clock.Elapsed,
                RecordsAffected = (int)Math.Min(affected, int.MaxValue),
                Cancelled = cancelled,
                Error = firstError,
            });
    }

    /// <summary>
    /// El error en un renglón, con lo que dice algo: «Column unknown · NO_EXISTE» en vez de «Dynamic SQL Error» (las
    /// líneas genéricas y la posición se saltean; el texto completo del primer error va al final del registro).
    /// </summary>
    internal static string ShortError(string error)
    {
        var useful = error.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0
                        && !l.StartsWith("Dynamic SQL Error", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("SQL error code", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("At line", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("At block", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("At procedure", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return useful.Count > 0 ? string.Join(" · ", useful) : error.Split('\n', 2)[0].Trim();
    }

    /// <summary>«3, 7, 12» (y «…» pasadas las diez).</summary>
    private static string ListLines(List<int> lines) =>
        string.Join(", ", lines.Take(10)) + (lines.Count > 10 ? ", …" : "");

    /// <summary>
    /// Una sentencia del modo sin grilla: sin plan ni lecturas por tabla y sin tocar la grilla (las filas de un SELECT
    /// solo se cuentan). No va al historial de a una: el script entero se guarda al final.
    /// </summary>
    private async Task<QueryOutcome> ExecuteInScriptAsync(ConnectionProfile profile, string sql, StatementInfo statement, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(profile, ct);
        var discard = Channel.CreateUnbounded<object[][]>();
        var options = new ExecuteOptions(MaxRows: _owner.RowLimit, Script: true);
        var outcome = await Task.Run(() => session.ExecuteAsync(sql, statement, options, _ => { }, discard.Writer, ct));
        if (statement.Kind == StatementKind.Ddl && outcome.Error is null) _owner.InvalidateCatalog(profile.Id);
        return outcome;
    }

    private static string Describe(StatementInfo info, QueryOutcome o, bool gridless = false) =>
        o.Columns.Length > 0
            ? string.Format(gridless ? Strings.ScriptRowsCounted : Strings.ScriptRows, info.Keyword, o.RowsFetched) + (o.LimitReached ? Strings.ScriptLimitReached : "")
            : info.Kind == StatementKind.Transaction
                ? string.Format(Strings.StatementDone, info.Keyword)
                : $"{info.Keyword}: {(o.RecordsAffected >= 0 ? string.Format(Strings.RowsAffected, o.RecordsAffected) : Strings.Executed)}";

    private int LineOf(int offset) => Document.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength)).LineNumber;

    private string? ScopeNotice(ExecutionScope scope, int start, int end) => scope switch
    {
        ExecutionScope.Selection when LineOf(start) == LineOf(end) => string.Format(Strings.ScopeSelectionLine, LineOf(start)),
        ExecutionScope.Selection => string.Format(Strings.ScopeSelectionLines, LineOf(start), LineOf(end)),
        ExecutionScope.Statement => string.Format(Strings.ScopeStatement, LineOf(start)),
        _ => null,
    };

    [RelayCommand]
    private Task CloseAsync() => _owner.CloseTabAsync(this);

    [RelayCommand(CanExecute = nameof(CanCompile))]
    private Task Compile() => CompileAsync();

    /// <summary>Con un perfil de solo lectura el botón queda deshabilitado (y CompileAsync igual lo bloquea).</summary>
    private bool CanCompile() => IsCompilable && !IsRunning && Profile?.ReadOnly != true;

    public string CompileHint => Profile?.ReadOnly == true
        ? Strings.CompileReadOnly
        : Strings.CompileHint;

    /// <summary>
    /// Compila el objeto de la pestaña: manda su CREATE OR ALTER (pasando por la política de seguridad) y, si sale bien,
    /// confirma enseguida: el DDL no queda en una transacción abierta. Si falla, la sentencia ya se deshizo y el cursor
    /// va a la línea del error.
    /// </summary>
    public async Task<bool> CompileAsync()
    {
        if (!IsCompilable || Object is not { } obj || IsRunning) return false;
        if (InTransaction)
        {
            Status = Strings.CompileOpenTransaction;
            return false;
        }
        var text = Document.Text;
        var statements = SqlScriptSplitter.Split(text);
        if (statements.Count != 1)
        {
            Status = string.Format(Strings.CompileOneStatement, statements.Count);
            return false;
        }
        var span = statements[0];
        if (StatementClassifier.DdlTarget(span.Text) is not { } target)
        {
            Status = Strings.CompileNotCreateOrAlter;
            return false;
        }
        if (target.Name != obj.Name)
        {
            Status = string.Format(Strings.CompileOtherObject, target.Name, obj.Name);
            return false;
        }

        var clock = Stopwatch.StartNew();
        var outcome = await RunAsync(span.Text);
        var error = outcome?.Error ?? (outcome is null && _lastRunThrew ? Messages : null);
        if (outcome is null || error is not null)
        {
            if (error is not null) ReportCompileError(error, text, span.Start);
            return false;
        }
        if (_session?.InTransaction == true)
        {
            try
            {
                await _session.CommitAsync();
            }
            catch (Exception ex)
            {
                // Algunos errores aparecen recién al confirmar (p. ej. objeto en uso): se deshace y se informa.
                try { await _session.RollbackAsync(); } catch { /* la sesión se cierra igual al cerrar la pestaña */ }
                InTransaction = _session.InTransaction;
                ReportCompileError(ex.Message, text, span.Start);
                return false;
            }
            InTransaction = false;
        }
        MarkSaved();
        Status = string.Format(Strings.Compiled, DbObjectKinds.Singular(obj.Kind), obj.Name, clock.ElapsedMilliseconds);
        Messages = Status;
        _ = LoadDependenciesAsync(); // al compilar pueden cambiar
        return true;
    }

    private void ReportCompileError(string error, string document, int statementStart)
    {
        Messages = error;
        SelectedResultTab = MessagesTab;
        var summary = CompileErrorSummary(error);
        if (FirebirdErrorPosition.Parse(error) is { } position)
        {
            var at = position.InDocument(document, statementStart);
            GoTo(at.Line, at.Column);
            Status = string.Format(Strings.CompileFailedAt, at.Line, at.Column, summary);
        }
        else
        {
            Status = string.Format(Strings.CompileFailed, summary);
        }
    }

    /// <summary>Las líneas útiles del error de Firebird, sin las genéricas ("unsuccessful metadata update", "SQL error code").</summary>
    private static string CompileErrorSummary(string error)
    {
        var useful = error.Split('\n')
            .Select(l => l.Trim().TrimStart('-').Trim())
            .Where(l => l.Length > 0
                        && !l.StartsWith("unsuccessful metadata update", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("Dynamic SQL Error", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("SQL error code", StringComparison.OrdinalIgnoreCase)
                        && !l.EndsWith(" failed", StringComparison.OrdinalIgnoreCase))
            .Take(3);
        var summary = string.Join(" · ", useful);
        return summary.Length > 0 ? summary : error.Split('\n', 2)[0].Trim();
    }

    /// <summary>Vuelve a leer el DDL del objeto desde la base (descarta los cambios sin compilar, preguntando).</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (Kind != EditorTabKind.Object || Object is not { } obj || Profile is not { } profile || IsRunning) return;
        if (IsModified && !await _owner.ConfirmDiscardAsync(new DiscardPrompt(string.Format(Strings.ReloadDiscardChanges, obj.Name), Strings.DiscardAndReload)))
            return;
        try
        {
            var script = await _owner.ScriptAsync(profile, obj);
            Document.Text = script.Ddl;
            MarkSaved();
            Description = script.Description;
            Status = string.Format(Strings.DdlReloaded, obj.Name);
            _ = LoadDependenciesAsync();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.DdlReloadFailed, ex.Message);
        }
    }

    /// <summary>Lee qué usa el objeto y quién lo usa (pestaña Dependencias).</summary>
    public async Task LoadDependenciesAsync()
    {
        if (Object is not { } obj || Profile is not { } profile) return;
        DependenciesStatus = Strings.DependenciesReading;
        try
        {
            var all = await _owner.DependenciesAsync(profile, obj);
            Uses = all.Where(d => d.Direction == DependencyDirection.Uses).Select(d => new DependencyRow(d)).ToList();
            UsedBy = all.Where(d => d.Direction == DependencyDirection.UsedBy).Select(d => new DependencyRow(d)).ToList();
            DependenciesStatus = UsedBy.Count == 0
                ? Strings.DependenciesNone
                : Strings.DependenciesHint;
        }
        catch (Exception ex)
        {
            DependenciesStatus = string.Format(Strings.DependenciesFailed, ex.Message);
        }
    }

    public Task OpenDependencyAsync(DependencyRow row) =>
        Profile is { } profile ? _owner.OpenObjectAsync(profile, new DbObject(row.Dependency.Kind, row.Dependency.Name)) : Task.CompletedTask;

    /// <summary>El texto actual pasa a ser "lo que está en la base" (después de leer o compilar).</summary>
    public void MarkSaved()
    {
        _savedText = Document.Text;
        IsModified = false;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cts?.Cancel();
        OnCancelRequested();
        Status = Strings.Cancelling;
    }

    [RelayCommand(CanExecute = nameof(CanEndTransaction))]
    private async Task CommitAsync()
    {
        if (_session is null) return;
        await _session.CommitAsync();
        InTransaction = _session.InTransaction;
        Status = Strings.CommitDone;
    }

    [RelayCommand(CanExecute = nameof(CanEndTransaction))]
    private async Task RollbackAsync()
    {
        if (_session is null) return;
        await _session.RollbackAsync();
        InTransaction = _session.InTransaction;
        Status = Strings.RollbackDone;
    }

    private bool CanEndTransaction() => InTransaction && !IsRunning;

    public void GoTo(int line, int column) => GoToRequested?.Invoke(line, column);

    /// <summary>Las pestañas especiales (búsqueda) cancelan su propio trabajo con Esc.</summary>
    protected virtual void OnCancelRequested()
    {
    }

    /// <summary>
    /// Ubica un resultado de la búsqueda en el DDL de la pestaña: la fuente va al final del DDL, así que la línea se
    /// traslada exacta; si no coincide (paquetes, texto editado), se busca el texto.
    /// </summary>
    public void GoToSearchHit(MetadataSearchHit hit, string term)
    {
        var lines = Document.Text.ReplaceLineEndings("\n").Split('\n');
        var needle = hit.Area == SearchArea.Column && hit.Detail is { } column ? column : term;
        var target = 0;
        if (hit.Line is { } line && line >= 1 && line <= lines.Length && lines[line - 1].Contains(needle, StringComparison.OrdinalIgnoreCase))
            target = line;
        if (target == 0) target = Array.FindIndex(lines, l => l.Contains(needle, StringComparison.OrdinalIgnoreCase)) + 1;
        if (target == 0) return;
        GoTo(target, lines[target - 1].IndexOf(needle, StringComparison.OrdinalIgnoreCase) + 1);
    }

    private async Task<QuerySession> EnsureSessionAsync(ConnectionProfile profile, CancellationToken ct)
    {
        if (_session is not null && _session.Profile.Id == profile.Id)
        {
            _session.LockTimeout = _owner.LockTimeout; // la opción pudo cambiar desde la última ejecución
            ApplyTransactionMode(_session);
            return _session;
        }
        var password = _owner.ProfileStore.GetPassword(profile.Id)
                       ?? throw new InvalidOperationException(string.Format(Strings.NoSavedPassword, profile.Name));
        var session = new QuerySession(profile, password) { LockTimeout = _owner.LockTimeout };
        ApplyTransactionMode(session);
        await session.OpenAsync(ct);
        _session = session;
        _ = _owner.CatalogFor(profile); // si todavía no está, empieza a cargarse para autocompletar
        return session;
    }

    // ---------- autocommit y aislamiento (D-84), solo en Desarrollo y Prueba ----------

    /// <summary>Confirmar cada sentencia que escribe al terminar bien. En producción o solo lectura no se puede.</summary>
    [ObservableProperty] private bool _autoCommit;

    /// <summary>Aislamiento de la próxima transacción de escritura (índice de <see cref="Isolations"/>).</summary>
    [ObservableProperty] private int _isolationIndex;

    public IReadOnlyList<string> Isolations { get; } = [Strings.IsolationReadCommitted, Strings.IsolationSnapshot, Strings.IsolationTableStability];

    /// <summary>Se elige en Desarrollo y Prueba, sin transacción abierta ni algo ejecutándose.</summary>
    public bool CanChooseTransactionMode => Profile is { IsProduction: false, ReadOnly: false } && !InTransaction && !IsRunning;

    public string TransactionModeTip => Profile is { IsProduction: true } or { ReadOnly: true } ? Strings.TransactionModeProductionTip
        : InTransaction ? Strings.TransactionModeOpenTip
        : Strings.TransactionModeTip;

    private TransactionIsolation Isolation => (TransactionIsolation)Math.Clamp(IsolationIndex, 0, Isolations.Count - 1);

    private void ApplyTransactionMode(QuerySession session)
    {
        var allowed = Profile is { IsProduction: false, ReadOnly: false };
        session.AutoCommit = allowed && AutoCommit;
        if (!session.InTransaction) session.Isolation = allowed ? Isolation : TransactionIsolation.ReadCommitted;
    }

    partial void OnAutoCommitChanged(bool value)
    {
        if (_session is not null) ApplyTransactionMode(_session);
    }

    partial void OnIsolationIndexChanged(int value)
    {
        if (_session is not null) ApplyTransactionMode(_session);
    }

    private void ShowOutcome(StatementInfo statement, QueryOutcome o)
    {
        Plan = FormatPlan(o.Plan, o.PlanIsExplained);
        Stats = FormatStats(o);
        var tx = o.TransactionOpen ? Strings.TransactionOpenSuffix : o.AutoCommitted ? Strings.AutoCommittedSuffix : "";
        if (o.Error is not null)
        {
            Status = string.Format(Strings.ErrorStatus, o.Error.Split('\n', 2)[0].Trim());
            Messages = o.Error;
            SelectedResultTab = MessagesTab;
        }
        else if (o.Cancelled)
        {
            Status = string.Format(Strings.CancelledAfter, Rows.Count, o.TotalTime.TotalSeconds, o.CancelLatency?.TotalMilliseconds) + tx;
        }
        else if (o.Columns.Length == 0)
        {
            Status = statement.Kind == StatementKind.Transaction
                ? string.Format(Strings.StatementDone, statement.Keyword) + "."
                : string.Format(Strings.StatementTook, statement.Keyword, o.RecordsAffected >= 0 ? string.Format(Strings.RowsAffected, o.RecordsAffected) : Strings.Executed, o.TotalTime.TotalMilliseconds) + tx;
            Messages = Status;
            SelectedResultTab = MessagesTab;
        }
        else
        {
            Status = string.Format(Strings.RowsIn, Rows.Count, o.TotalTime.TotalSeconds)
                     + (o.LimitReached ? string.Format(Strings.LimitReached, _owner.RowLimit) : "") + tx
                     + (_planWarning is { } w && o.TotalTime > TimeSpan.FromSeconds(1) ? " · " + w : "");
            SelectedResultTab = 0;
        }
    }

    private void Remember(string sql, StatementInfo statement, QueryOutcome o, IReadOnlyList<object?>? parameters = null)
    {
        if (o.Columns.Length == 0 || o.Error is not null) return;
        _lastSql = sql;
        _lastParameters = parameters;
        _lastStatement = statement;
        _lastColumns = o.Columns;
        CanExportFull = statement.Kind == StatementKind.Select && (o.LimitReached || o.Cancelled);
    }

    /// <summary>
    /// Exporta el resultado: lo cargado en la grilla o, con <paramref name="full"/>, re-ejecutando el último SELECT sin
    /// límite y escribiendo en streaming (sin cargar las filas en memoria).
    /// </summary>
    public async Task ExportAsync(string path, bool full, IResultExporter exporter)
    {
        if (IsRunning) return;
        var clock = Stopwatch.StartNew();
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            long written;
            if (!full || _lastSql is null || Profile is null)
            {
                var columns = _lastColumns;
                var rows = Rows.ToArray();
                written = await Task.Run(() => exporter.Write(path, columns, rows));
            }
            else
            {
                written = await ExportFullAsync(path, exporter, clock, _cts.Token);
            }
            Status = string.Format(Strings.Exported, exporter.Name, written, clock.Elapsed.TotalSeconds, path);
        }
        catch (OperationCanceledException)
        {
            Status = Strings.ExportCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.ExportFailed, ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    public Task ExportCsvAsync(string path, bool full, CsvOptions? options = null) =>
        ExportAsync(path, full, new CsvExporter(options ?? CsvOptions.ExcelAr));

    private async Task<long> ExportFullAsync(string path, IResultExporter exporter, Stopwatch clock, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(Profile!, ct);
        var channel = Channel.CreateBounded<object[][]>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });
        var columnsReady = new TaskCompletionSource<ColumnInfo[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        // En paralelo y con otra conexión se cuentan las filas, para mostrar "N de M (x %)" y el tiempo que falta.
        long total = -1;
        if (_lastStatement?.Kind == StatementKind.Select && _owner.ProfileStore.GetPassword(Profile!.Id) is { } password)
        {
            var profile = Profile!;
            var sql = _lastSql!;
            var parameters = _lastParameters;
            _ = Task.Run(async () =>
            {
                if (await QuerySession.CountRowsAsync(profile, password, sql, ct, parameters) is { } count) Interlocked.Exchange(ref total, count);
            }, CancellationToken.None);
        }

        IEnumerable<object[]> Stream()
        {
            long read = 0;
            foreach (var batch in channel.Reader.ReadAllAsync(CancellationToken.None).ToBlockingEnumerable())
            {
                foreach (var row in batch) yield return row;
                read += batch.Length;
                if (read % 50_000 < batch.Length)
                {
                    var n = read;
                    var of = Interlocked.Read(ref total);
                    var elapsed = clock.Elapsed.TotalSeconds;
                    var text = of > 0
                        ? string.Format(Strings.ExportingOf, n, of, Math.Min(1.0, (double)n / of), elapsed, Math.Max(0, elapsed * (of - n) / n))
                        : string.Format(Strings.Exporting, n, elapsed);
                    Dispatcher.UIThread.Post(() => Status = text);
                }
            }
        }

        var writer = Task.Run(async () =>
        {
            var columns = await columnsReady.Task;
            return exporter.Write(path, columns, Stream());
        });
        var outcome = await Task.Run(() => session.ExecuteAsync(_lastSql!, _lastStatement!, new ExecuteOptions(MaxRows: int.MaxValue, Parameters: _lastParameters),
            cols => columnsReady.TrySetResult(cols), channel.Writer, ct));
        columnsReady.TrySetResult(_lastColumns); // por si la consulta falló antes de devolver columnas
        var written = await writer;
        if (outcome.Cancelled) throw new OperationCanceledException();
        if (outcome.Error is not null) throw new InvalidOperationException(outcome.Error);
        return written;
    }

    /// <summary>Junta lotes y los aplica en el hilo de UI de a hasta 5.000 filas, esperando a la UI (contrapresión).</summary>
    private async Task PumpToUiAsync(ChannelReader<object[][]> reader, Stopwatch clock)
    {
        var chunk = new List<object[]>(UiChunkRows);
        while (await reader.WaitToReadAsync())
        {
            while (chunk.Count < UiChunkRows && reader.TryRead(out var batch)) chunk.AddRange(batch);
            var toAdd = chunk.ToArray();
            chunk.Clear();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Rows.AddRange(toAdd);
                Status = string.Format(Strings.Receiving, Rows.Count, clock.Elapsed.TotalSeconds);
            }, DispatcherPriority.Background);
        }
    }

    private static string FormatStats(QueryOutcome o)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Strings.StatsTimes, o.PrepareTime.TotalMilliseconds, o.FirstRowTime?.TotalMilliseconds, o.TotalTime.TotalSeconds));
        sb.AppendLine(string.Format(Strings.StatsCounts, o.RowsFetched, o.RecordsAffected >= 0 ? o.RecordsAffected.ToString("N0") : "-", o.Fetches, o.PageReads, o.PageWrites));
        if (o.CancelLatency is TimeSpan lat) sb.AppendLine(string.Format(Strings.StatsCancelLatency, lat.TotalMilliseconds));
        sb.AppendLine();
        sb.AppendLine(Strings.StatsReadsByTable);
        foreach (var r in o.Reads)
            sb.AppendLine(string.Format(Strings.StatsTableLine, r.Table, r.Indexed, r.NonIndexed) + (r.NonIndexed > 0 ? Strings.StatsSequentialRead : ""));
        if (o.Reads.Count == 0) sb.AppendLine(Strings.StatsNoData);
        return sb.ToString();
    }

    public virtual async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_session is not null) await _session.DisposeAsync(); // con transacción abierta: rollback
        _session = null;
        if (_data is not null) await _data.DisposeAsync();
    }
}

/// <summary>Una fila de la pestaña Dependencias.</summary>
public sealed record DependencyRow(DbDependency Dependency)
{
    public MaterialIconKind Icon => DatabaseTreeViewModel.IconFor(Dependency.Kind);
    public string Caption => $"{DbObjectKinds.Singular(Dependency.Kind)} {Dependency.Name}";
    public string? Detail => Dependency.Detail;
}

/// <summary>Fila de la lista de dependencias: un encabezado de tipo («Procedimientos (167)») o un objeto.</summary>
public sealed record DependencyListItem(bool IsHeader, MaterialIconKind Icon, string Caption, string? Detail, DependencyRow? Row)
{
    public Avalonia.Media.FontWeight Weight => IsHeader ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
    public double Indent => IsHeader ? 0 : 20;

    public static bool Matches(DependencyRow row, string filter)
    {
        var f = filter.Trim();
        return f.Length == 0
               || row.Dependency.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
               || (row.Dependency.Detail?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public static IReadOnlyList<DependencyListItem> Build(IReadOnlyList<DependencyRow> rows, string filter)
    {
        var items = new List<DependencyListItem>();
        foreach (var group in rows.Where(r => Matches(r, filter)).GroupBy(r => r.Dependency.Kind).OrderBy(g => g.Key))
        {
            var count = group.Count();
            items.Add(new DependencyListItem(true, DatabaseTreeViewModel.IconFor(group.Key), $"{DbObjectKinds.Label(group.Key)} ({count:N0})", null, null));
            items.AddRange(group.Select(r => new DependencyListItem(false, r.Icon, r.Dependency.Name, r.Detail, r)));
        }
        return items;
    }
}
