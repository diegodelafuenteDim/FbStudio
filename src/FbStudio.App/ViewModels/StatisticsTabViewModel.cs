using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Statistics;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Una fila de la tabla del encabezado: el nombre en el idioma de la interfaz, el valor y el nombre que usa gstat.</summary>
public sealed record HeaderRow(string Label, string Value, string Key);

/// <summary>
/// Pestaña «Estadísticas» de una base: el encabezado (gstat -h) al abrirla, que lee una sola página y es instantáneo, y
/// a pedido la completa (páginas de datos e índices de cada tabla), que recorre toda la base, o la de las tablas que se
/// elijan (gstat -t). Se puede comparar con otra base: se lee lo mismo en las dos y se ven lado a lado (F-27). Va por
/// la API de servicios: no abre transacciones ni hace falta conectar la base en el árbol. El texto de gstat queda a la
/// derecha.
/// </summary>
public sealed partial class StatisticsTabViewModel : EditorTabViewModel
{
    public const int CompareTablesDetail = 3;

    private CancellationTokenSource? _cts;
    private List<TableChoice> _allTables = [];
    private bool _disposed;

    public StatisticsTabViewModel(MainWindowViewModel owner, ConnectionProfile profile) : base(owner, profile, Strings.StatsTabTitle)
    {
        Status = Strings.StatsIntro;
        CompareProfiles = owner.Profiles.Where(p => p.Id != profile.Id).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        CompareChoices = [Strings.StatsCompareNone, .. CompareProfiles.Select(p => p.Name)];
    }

    public override MaterialIconKind Icon => MaterialIconKind.ChartBoxOutline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeader), nameof(OitText), nameof(OatText), nameof(OstText), nameof(NextText), nameof(ActiveGapText),
        nameof(SweepText), nameof(SweepGapText), nameof(IsSweepDue), nameof(SweepDueText), nameof(PageText), nameof(ForcedWritesText),
        nameof(IsForcedWritesOff), nameof(BackupWarning), nameof(HasBackupWarning))]
    private DatabaseHeader? _header;

    /// <summary>Todos los datos del encabezado, con el nombre traducido.</summary>
    [ObservableProperty] private IReadOnlyList<HeaderRow> _headerRows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadHeaderCommand), nameof(ReadFullCommand), nameof(CancelReadCommand))]
    private bool _isReading;

    /// <summary>«Leído a las 16:50:01 (0,1 s)».</summary>
    [ObservableProperty] private string _readAt = "";

    /// <summary>Tablas e índices de la última estadística completa (null hasta leerla).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysis), nameof(TablesHeader), nameof(IndexesHeader))]
    private DatabaseAnalysis? _analysis;

    public bool HasAnalysis => Analysis is not null;

    public string TablesHeader => Analysis is { } a ? string.Format(Strings.StatsTablesTab, a.Tables.Count) : Strings.StatsTablesTabEmpty;
    public string IndexesHeader => Analysis is { } a ? string.Format(Strings.StatsIndexesTab, a.Indexes.Count()) : Strings.StatsIndexesTabEmpty;

    /// <summary>Pestaña de la derecha: 0 tablas, 1 índices, 2 texto de gstat.</summary>
    [ObservableProperty] private int _selectedDetail = 2;

    /// <summary>La vista carga las grillas de tablas e índices.</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>, ColumnInfo[], AvaloniaList<object[]>>? AnalysisChanged;

    /// <summary>La vista carga las grillas de la comparación (tablas e índices).</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>, ColumnInfo[], AvaloniaList<object[]>>? ComparisonChanged;

    // ── Tablas elegidas (gstat -t) ──

    /// <summary>Las tablas de la base, para elegir (se leen la primera vez que se abre la lista).</summary>
    public IReadOnlyList<TableChoice> AllTables => _allTables;

    [ObservableProperty] private IReadOnlyList<TableChoice> _visibleTables = [];
    [ObservableProperty] private string _tableFilter = "";
    [ObservableProperty] private bool _tablesLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TablesText), nameof(FullText))]
    private int _chosenCount;

    public string TablesText => ChosenCount == 0 ? Strings.StatsTablesAll : string.Format(Strings.StatsTablesChosen, ChosenCount);
    public string FullText => ChosenCount == 0 ? Strings.StatsReadFull : string.Format(Strings.StatsReadTables, ChosenCount);
    public IReadOnlyList<string> ChosenTables => _allTables.Where(t => t.IsChecked).Select(t => t.Name).ToList();

    partial void OnTableFilterChanged(string value) => ApplyFilter();

    // ── Comparar con otra base ──

    public IReadOnlyList<ConnectionProfile> CompareProfiles { get; }

    /// <summary>«(no comparar)» y las otras bases registradas.</summary>
    public IReadOnlyList<string> CompareChoices { get; }

    [ObservableProperty] private int _compareIndex;

    public ConnectionProfile? CompareProfile => CompareIndex > 0 && CompareIndex <= CompareProfiles.Count ? CompareProfiles[CompareIndex - 1] : null;

    /// <summary>La última comparación (null hasta hacer una).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComparison), nameof(CompareTablesHeader), nameof(CompareIndexesHeader))]
    private StatisticsComparison? _comparison;

    public bool HasComparison => Comparison is not null;
    public string CompareTablesHeader => string.Format(Strings.StatsCompareTablesTab, Comparison?.Tables.Count ?? 0);
    public string CompareIndexesHeader => string.Format(Strings.StatsCompareIndexesTab, Comparison?.Indexes.Count ?? 0);

    /// <summary>«A = Prueba · B = Producción».</summary>
    [ObservableProperty] private string _compareLegend = "";

    /// <summary>Lo último que se leyó (para las pruebas: esperar a que termine).</summary>
    public Task Reading { get; private set; } = Task.CompletedTask;

    public bool HasHeader => Header is not null;
    public string OitText => N(Header?.OldestTransaction);
    public string OatText => N(Header?.OldestActive);
    public string OstText => N(Header?.OldestSnapshot);
    public string NextText => N(Header?.NextTransaction);
    public string ActiveGapText => N(Header?.ActiveGap);
    public string SweepGapText => string.Format(Strings.StatsSweepGap, N(Header?.SweepGap));

    public string SweepText => Header switch
    {
        null => "—",
        { SweepInterval: 0 } => Strings.StatsSweepOff,
        { SweepIntervalIsDefault: true } h => string.Format(Strings.StatsSweepDefault, N(h.SweepInterval)),
        var h => N(h.SweepInterval),
    };

    /// <summary>OST − OIT pasó el intervalo: el sweep automático ya debería haber corrido (o lo frena una transacción vieja).</summary>
    public bool IsSweepDue => Header?.SweepDue == true;

    public string SweepDueText => Header is { } h ? string.Format(Strings.StatsSweepDue, N(h.SweepGap), N(h.SweepInterval)) : "";

    public string PageText => Header is { } h ? string.Format(Strings.StatsPage, N(h.PageSize), h.OdsVersion ?? "?") : "—";

    public string ForcedWritesText => Header is null ? "—" : Header.ForcedWrites ? Strings.StatsForcedWritesOn : Strings.StatsForcedWritesOff;

    /// <summary>La base quedó en modo backup de nbackup (gstat dice «backup lock», F-29).</summary>
    public string? BackupWarning => Header is { } h ? BackupStates.Warning(h.BackupState) : null;
    public bool HasBackupWarning => BackupWarning is not null;

    /// <summary>Sin escrituras forzadas, un corte de luz puede dañar la base: se marca.</summary>
    public bool IsForcedWritesOff => Header is { ForcedWrites: false };

    private static string N(long? n) => n is { } v ? v.ToString("N0") : "—";

    private bool CanRead() => !IsReading;

    /// <summary>F9 o «Leer encabezado»: una sola página, instantáneo aunque la base sea enorme.</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    public Task ReadHeaderAsync() => StartReading(StatisticsScope.Header);

    /// <summary>
    /// La completa recorre toda la base (o las tablas elegidas): pregunta antes, avisa si es producción y, si se compara,
    /// dice que también se lee la otra base.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    public async Task ReadFullAsync()
    {
        if (Profile is not { } profile) return;
        var tables = ChosenTables;
        var other = CompareProfile;
        List<string> reasons =
        [
            tables.Count == 0
                ? string.Format(Strings.StatsFullConfirm, profile.Name)
                : string.Format(Strings.StatsTablesConfirm, tables.Count, profile.Name, string.Join(", ", tables.Take(10)) + (tables.Count > 10 ? "…" : "")),
        ];
        if (profile.IsProduction) reasons.Add(Strings.StatsFullProduction);
        if (other is not null)
        {
            reasons.Add(string.Format(Strings.StatsCompareConfirm, other.Name));
            if (other.IsProduction) reasons.Add(string.Format(Strings.StatsCompareProduction, other.Name));
        }
        var (title, run) = tables.Count == 0 ? (Strings.StatsFullTitle, Strings.StatsFullRun) : (Strings.StatsTablesTitle, Strings.StatsTablesRun);
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, run, Strings.Cancel, title))) return;
        if (IsReading) return;
        await (Reading = ReadAsync(StatisticsScope.Full, tables, other));
    }

    /// <summary>Lee las tablas de la base para la lista (una vez).</summary>
    public async Task LoadTablesAsync()
    {
        if (_allTables.Count > 0 || TablesLoading || Profile is not { } profile) return;
        TablesLoading = true;
        try
        {
            var tables = await Owner.WithExplorerAsync(profile, e => e.LoadObjectsAsync(DbObjectKind.Table));
            if (_disposed) return;
            _allTables = tables.Select(t => new TableChoice(t.Name)).ToList();
            foreach (var t in _allTables) t.PropertyChanged += (_, _) => ChosenCount = _allTables.Count(c => c.IsChecked);
            OnPropertyChanged(nameof(AllTables));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.StatsFailed, ex.Message.Split('\n', 2)[0].Trim());
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

    /// <summary>«Todas»: quita la elección.</summary>
    /// <summary>Las áreas de la base, para tildar de una vez sus tablas.</summary>
    public IReadOnlyList<string> AreaNames => Profile is { } p ? Owner.AreaStore.Areas(p).Select(a => a.Name).ToList() : [];

    public bool HasAreas => AreaNames.Count > 0;

    /// <summary>Tilda las tablas de un área (además de las que ya estaban tildadas).</summary>
    public async Task ChooseAreaAsync(string areaName)
    {
        if (Profile is not { } profile || Owner.AreaStore.Areas(profile).FirstOrDefault(a => a.Name == areaName) is not { } area) return;
        await LoadTablesAsync();
        var members = await Owner.AreaMembersAsync(profile, area);
        foreach (var t in _allTables.Where(t => members.Contains(t.Name))) t.IsChecked = true;
        Status = string.Format(Strings.StatsAreaChosen, _allTables.Count(t => members.Contains(t.Name)), area.Name);
    }

    [RelayCommand]
    private void ClearTables()
    {
        foreach (var t in _allTables) t.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(IsReading))]
    private void CancelRead() => _cts?.Cancel();

    private Task StartReading(StatisticsScope scope)
    {
        if (IsReading) return Reading;
        return Reading = ReadAsync(scope, [], null);
    }

    private async Task ReadAsync(StatisticsScope scope, IReadOnlyList<string> tables, ConnectionProfile? other)
    {
        if (_disposed || Profile is not { } profile) return;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        IsReading = true;
        _cts = new CancellationTokenSource();
        Status = scope == StatisticsScope.Header ? Strings.StatsReadingHeader
            : tables.Count == 0 ? Strings.StatsReadingFull : string.Format(Strings.StatsReadingTables, tables.Count);
        var count = 0;
        // La completa puede tardar minutos: se va contando lo que llega (el texto se muestra al final).
        void Counted(string _)
        {
            if (Interlocked.Increment(ref count) % 200 == 0)
                Dispatcher.UIThread.Post(() => { if (IsReading) Status = string.Format(Strings.StatsReadingLines, count); });
        }
        try
        {
            var result = await DatabaseStatistics.ReadAsync(profile, password, scope, Counted, _cts.Token, tables);
            if (_disposed) return;
            Header = result.Header;
            HeaderRows = result.Header.Entries.Select(e => new HeaderRow(Label(e.Key), e.Value, e.Key)).ToList();
            Document.Text = string.Join("\n", result.Lines).Trim();
            ReadAt = string.Format(Strings.StatsReadAt, DateTime.Now, result.Elapsed.TotalSeconds);
            Status = scope == StatisticsScope.Header ? string.Format(Strings.StatsHeaderDone, profile.Name)
                : tables.Count == 0 ? string.Format(Strings.StatsFullDone, result.Lines.Count, result.Elapsed.TotalSeconds)
                : string.Format(Strings.StatsTablesDone, tables.Count, result.Lines.Count, result.Elapsed.TotalSeconds);
            if (scope == StatisticsScope.Full)
            {
                // Las tablas y los índices, en grillas que se ordenan y filtran; lo que conviene mirar va en «Aviso».
                ShowAnalysis(DatabaseAnalysis.Parse(result.Lines));
                var warnings = Analysis!.Tables.Count(t => t.ManyVersions) + Analysis.Indexes.Count(i => i.TooDeep || i.PoorSelectivity);
                Status += " · " + string.Format(Strings.StatsAnalysisSummary, Analysis.Tables.Count, Analysis.Indexes.Count())
                          + (warnings > 0 ? " · " + string.Format(Strings.StatsAnalysisWarnings, warnings) : "");
                SelectedDetail = 0;
                if (other is not null) await CompareAsync(profile, other, tables, Counted);
            }
        }
        catch (OperationCanceledException)
        {
            Status = Strings.StatsCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.StatsFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            IsReading = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Lee lo mismo en la otra base y arma la comparación. Si falla (sin contraseña, una tabla que allá no existe…), queda
    /// lo de esta base y se dice por qué no se comparó.
    /// </summary>
    private async Task CompareAsync(ConnectionProfile profile, ConnectionProfile other, IReadOnlyList<string> tables, Action<string> counted)
    {
        var done = Status;
        if (Owner.ProfileStore.GetPassword(other.Id) is not { } password)
        {
            Status = done + " · " + string.Format(Strings.NoSavedPassword, other.Name);
            return;
        }
        Status = string.Format(Strings.StatsCompareReading, other.Name);
        try
        {
            var result = await DatabaseStatistics.ReadAsync(other, password, StatisticsScope.Full, counted, _cts!.Token, tables);
            if (_disposed) return;
            Document.Insert(Document.TextLength, "\n\n" + string.Format(Strings.StatsCompareTextHeader, other.Name) + "\n" + string.Join("\n", result.Lines).Trim());
            ShowComparison(profile.Name, other.Name, StatisticsComparison.Compare(Analysis!, DatabaseAnalysis.Parse(result.Lines)));
            Status = done + " · " + string.Format(Strings.StatsCompareDone, other.Name, result.Elapsed.TotalSeconds, Comparison!.Tables.Count, Comparison.Indexes.Count);
            SelectedDetail = CompareTablesDetail;
        }
        catch (OperationCanceledException)
        {
            Status = done + " · " + Strings.StatsCancelled;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            Status = done + " · " + string.Format(Strings.StatsCompareFailed, other.Name, ex.Message.Split('\n', 2)[0].Trim());
        }
    }

    /// <summary>Arma las filas de las grillas de la comparación.</summary>
    internal void ShowComparison(string nameA, string nameB, StatisticsComparison comparison)
    {
        Comparison = comparison;
        CompareLegend = string.Format(Strings.StatsCompareLegend, nameA, nameB);
        static object V(long? n) => n is { } v ? v : DBNull.Value;
        static object D(double? d) => d is { } v ? Math.Round((decimal)v, 2) : DBNull.Value;
        string Only(object? a, object? b) => a is null ? string.Format(Strings.StatsOnlyIn, nameB) : b is null ? string.Format(Strings.StatsOnlyIn, nameA) : "";
        ColumnInfo[] tableColumns =
        [
            new(0, Strings.StatsColTable, "VARCHAR"), new(1, Strings.StatsColRecordsA, "BIGINT"), new(2, Strings.StatsColRecordsB, "BIGINT"),
            new(3, Strings.StatsColRecordsDiff, "BIGINT"), new(4, Strings.StatsColPagesA, "BIGINT"), new(5, Strings.StatsColPagesB, "BIGINT"),
            new(6, Strings.StatsColPagesDiff, "BIGINT"), new(7, Strings.StatsColVersionsA, "BIGINT"), new(8, Strings.StatsColVersionsB, "BIGINT"),
            new(9, Strings.StatsColFillA, "BIGINT"), new(10, Strings.StatsColFillB, "BIGINT"), new(11, Strings.StatsColNote, "VARCHAR"),
        ];
        var tableRows = new AvaloniaList<object[]>(comparison.Tables.Select(t => new object[]
        {
            t.Name, V(t.A?.Records), V(t.B?.Records), V(t.RecordsDifference), V(t.A?.DataPages), V(t.B?.DataPages), V(t.DataPagesDifference),
            V(t.A?.Versions), V(t.B?.Versions), V(t.A?.AverageFill), V(t.B?.AverageFill), Only(t.A, t.B),
        }));
        ColumnInfo[] indexColumns =
        [
            new(0, Strings.StatsColTable, "VARCHAR"), new(1, Strings.StatsColIndex, "VARCHAR"), new(2, Strings.StatsColDepthA, "BIGINT"),
            new(3, Strings.StatsColDepthB, "BIGINT"), new(4, Strings.StatsColNodesA, "BIGINT"), new(5, Strings.StatsColNodesB, "BIGINT"),
            new(6, Strings.StatsColDupA, "NUMERIC"), new(7, Strings.StatsColDupB, "NUMERIC"), new(8, Strings.StatsColNote, "VARCHAR"),
        ];
        var indexRows = new AvaloniaList<object[]>(comparison.Indexes.Select(i => new object[]
        {
            i.Table, i.Name, V(i.A?.Depth), V(i.B?.Depth), V(i.A?.Nodes), V(i.B?.Nodes), D(i.A?.DuplicateRatio * 100), D(i.B?.DuplicateRatio * 100),
            i.DeeperInB ? string.Format(Strings.StatsDeeperIn, nameB) : Only(i.A, i.B),
        }));
        ComparisonChanged?.Invoke(tableColumns, tableRows, indexColumns, indexRows);
    }

    /// <summary>Arma las filas de las grillas de tablas e índices.</summary>
    internal void ShowAnalysis(DatabaseAnalysis analysis)
    {
        Analysis = analysis;
        Comparison = null; // la de antes era de otra lectura
        static object V(long? n) => n is { } v ? v : DBNull.Value;
        static object D(double? d) => d is { } v ? Math.Round((decimal)v, 2) : DBNull.Value;
        ColumnInfo[] tableColumns =
        [
            new(0, Strings.StatsColTable, "VARCHAR"), new(1, Strings.StatsColRecords, "BIGINT"), new(2, Strings.StatsColRecordLength, "NUMERIC"),
            new(3, Strings.StatsColVersions, "BIGINT"), new(4, Strings.StatsColMaxVersions, "BIGINT"), new(5, Strings.StatsColVersionLength, "NUMERIC"),
            new(6, Strings.StatsColFragments, "BIGINT"), new(7, Strings.StatsColDataPages, "BIGINT"), new(8, Strings.StatsColFill, "BIGINT"),
            new(9, Strings.StatsColEmptyPages, "BIGINT"), new(10, Strings.StatsColFullPages, "BIGINT"), new(11, Strings.StatsColCompression, "NUMERIC"),
            new(12, Strings.StatsColFormats, "BIGINT"), new(13, Strings.StatsColWarning, "VARCHAR"),
        ];
        var tableRows = new AvaloniaList<object[]>(analysis.Tables.Select(t => new object[]
        {
            t.Name, V(t.Records), D(t.AverageRecordLength), V(t.Versions), V(t.MaxVersions), D(t.AverageVersionLength), V(t.Fragments),
            V(t.DataPages), V(t.AverageFill), V(t.EmptyPages), V(t.FullPages), D(t.CompressionRatio), V(t.Formats),
            t.ManyVersions ? Strings.StatsWarnVersions : "",
        }));
        ColumnInfo[] indexColumns =
        [
            new(0, Strings.StatsColTable, "VARCHAR"), new(1, Strings.StatsColIndex, "VARCHAR"), new(2, Strings.StatsColDepth, "BIGINT"),
            new(3, Strings.StatsColLeafBuckets, "BIGINT"), new(4, Strings.StatsColNodes, "BIGINT"), new(5, Strings.StatsColDuplicates, "BIGINT"),
            new(6, Strings.StatsColDuplicatePct, "NUMERIC"), new(7, Strings.StatsColMaxDuplicates, "BIGINT"), new(8, Strings.StatsColKeyLength, "NUMERIC"),
            new(9, Strings.StatsColClustering, "BIGINT"), new(10, Strings.StatsColWarning, "VARCHAR"),
        ];
        var indexRows = new AvaloniaList<object[]>(analysis.Indexes.Select(i => new object[]
        {
            i.Table, i.Name, V(i.Depth), V(i.LeafBuckets), V(i.Nodes), V(i.TotalDuplicates), D(i.DuplicateRatio * 100), V(i.MaxDuplicates),
            D(i.AverageKeyLength), V(i.ClusteringFactor), IndexWarning(i),
        }));
        AnalysisChanged?.Invoke(tableColumns, tableRows, indexColumns, indexRows);
    }

    private static string IndexWarning(IndexStatistics index) => string.Join(" · ", new[]
    {
        index.TooDeep ? string.Format(Strings.StatsWarnDeep, index.Depth) : null,
        index.PoorSelectivity ? string.Format(Strings.StatsWarnSelectivity, index.DuplicateRatio) : null,
    }.Where(w => w is not null));

    /// <summary>Nombre del dato en el idioma de la interfaz; los que no se conocen quedan como los escribe gstat.</summary>
    private static string Label(string key) => key.ToLowerInvariant() switch
    {
        "flags" => Strings.StatHdrFlags,
        "generation" => Strings.StatHdrGeneration,
        "system change number" => Strings.StatHdrScn,
        "page size" => Strings.StatHdrPageSize,
        "ods version" => Strings.StatHdrOds,
        "oldest transaction" => Strings.StatHdrOit,
        "oldest active" => Strings.StatHdrOat,
        "oldest snapshot" => Strings.StatHdrOst,
        "next transaction" => Strings.StatHdrNext,
        "sequence number" => Strings.StatHdrSequence,
        "next attachment id" => Strings.StatHdrNextAttachment,
        "implementation" => Strings.StatHdrImplementation,
        "shadow count" => Strings.StatHdrShadows,
        "page buffers" => Strings.StatHdrPageBuffers,
        "next header page" => Strings.StatHdrNextHeader,
        "database dialect" => Strings.StatHdrDialect,
        "creation date" => Strings.StatHdrCreated,
        "attributes" => Strings.StatHdrAttributes,
        "sweep interval" => Strings.StatHdrSweep,
        _ => key,
    };

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
