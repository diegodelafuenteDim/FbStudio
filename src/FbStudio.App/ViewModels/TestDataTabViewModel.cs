using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Import;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Una columna de la tabla y qué se pone en ella al generar datos de prueba (se puede cambiar).</summary>
public sealed partial class TestColumnChoice(DbColumn column, string? referenced, string? referencedColumn) : ObservableObject
{
    public DbColumn Column { get; } = column;
    public string Name => Column.Name;

    /// <summary>La tabla y columna de la FK (los valores salen de ahí), o null.</summary>
    public string? Referenced { get; } = referenced;
    public string? ReferencedColumn { get; } = referencedColumn;

    public string Detail => (Column.Type + (Column.NotNull ? " NOT NULL" : "") + (Column.IsPrimaryKey ? " · PK" : "")
                             + (Referenced is { } r ? $" · FK → {r}" : "")).Trim();

    [ObservableProperty] private int _kindIndex;

    public TestValueKind Kind => TestDataTabViewModel.Kinds[KindIndex];
}

/// <summary>
/// Pestaña «Datos de prueba · TABLA»: llena una tabla con filas inventadas para probar (pantallas, consultas, índices).
/// Cada columna va con lo que conviene por su tipo y su nombre (se puede cambiar), las FK con valores que ya existen y la
/// clave numerada desde la mayor. Se ve una muestra antes y todo va en una transacción; una fila que falla (p. ej. por un
/// índice único) se saltea. No se ofrece en producción ni con un registro de solo lectura.
/// </summary>
public sealed partial class TestDataTabViewModel : EditorTabViewModel
{
    public const int PreviewRows = 20;
    public const int MaxRows = 100_000;

    /// <summary>Lo que se puede elegir para cada columna, en el orden de la lista.</summary>
    public static IReadOnlyList<TestValueKind> Kinds { get; } = Enum.GetValues<TestValueKind>();

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public TestDataTabViewModel(MainWindowViewModel owner, ConnectionProfile profile, string table)
        : base(owner, profile, string.Format(Strings.TestDataTabTitle, table))
    {
        Table = table;
        Status = profile.IsProduction ? Strings.TestDataProduction : profile.ReadOnly ? Strings.TestDataReadOnly : Strings.TestDataIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.DatabasePlusOutline;

    public string Table { get; }

    /// <summary>En producción o con un registro de solo lectura no se genera nada.</summary>
    public bool IsBlocked => Profile is { } p && (p.IsProduction || p.ReadOnly);

    public string BlockedText => Profile?.IsProduction == true ? Strings.TestDataProduction : Strings.TestDataReadOnly;

    public ObservableCollection<TestColumnChoice> Columns { get; } = [];

    /// <summary>Los nombres de lo que se puede elegir para una columna (en el idioma de la interfaz).</summary>
    public IReadOnlyList<string> KindLabels { get; } = Kinds.Select(Label).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunText))]
    private int _rowCount = 100;

    [ObservableProperty] private int _nullPercent = 10;
    [ObservableProperty] private int _seed = 1;

    public IReadOnlyList<int> NullPercents { get; } = [0, 10, 25, 50];

    public string RunText => string.Format(Strings.TestDataRun, Math.Clamp(RowCount, 1, MaxRows));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(PreviewCommand), nameof(StopCommand))]
    private bool _isWorking;

    /// <summary>Llegó la muestra: la vista la muestra en su grilla.</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>>? PreviewChanged;

    /// <summary>La última operación (para las pruebas).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    public static string Label(TestValueKind kind) => kind switch
    {
        TestValueKind.Skip => Strings.TestKindSkip,
        TestValueKind.Sequence => Strings.TestKindSequence,
        TestValueKind.Reference => Strings.TestKindReference,
        TestValueKind.FirstName => Strings.TestKindFirstName,
        TestValueKind.LastName => Strings.TestKindLastName,
        TestValueKind.FullName => Strings.TestKindFullName,
        TestValueKind.Email => Strings.TestKindEmail,
        TestValueKind.Phone => Strings.TestKindPhone,
        TestValueKind.Document => Strings.TestKindDocument,
        TestValueKind.Address => Strings.TestKindAddress,
        TestValueKind.City => Strings.TestKindCity,
        TestValueKind.Text => Strings.TestKindText,
        TestValueKind.Code => Strings.TestKindCode,
        TestValueKind.Integer => Strings.TestKindInteger,
        TestValueKind.Decimal => Strings.TestKindDecimal,
        TestValueKind.Date => Strings.TestKindDate,
        TestValueKind.Timestamp => Strings.TestKindTimestamp,
        TestValueKind.Time => Strings.TestKindTime,
        _ => Strings.TestKindBoolean,
    };

    /// <summary>Las columnas de la tabla, sus FK (de un solo campo) y lo que conviene para cada una.</summary>
    public Task LoadAsync() => Working = LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        if (Profile is not { } profile) return;
        try
        {
            var columns = await Owner.WithExplorerAsync(profile, e => e.LoadColumnsAsync(Table));
            var graph = await Owner.SchemaGraphAsync(profile);
            var fks = graph.Links.Where(l => !l.IsLogical && l.Table == Table && l.Columns.Count == 1)
                .GroupBy(l => l.Columns[0]).ToDictionary(g => g.Key, g => g.First());
            Columns.Clear();
            foreach (var c in columns)
            {
                var fk = fks.GetValueOrDefault(c.Name);
                var choice = new TestColumnChoice(c, fk?.Referenced, fk?.ReferencedColumns.FirstOrDefault());
                choice.KindIndex = Kinds.ToList().IndexOf(TestDataGenerator.Suggest(c, fk is not null));
                Columns.Add(choice);
            }
            PreviewCommand.NotifyCanExecuteChanged();
            RunCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.TestDataFailed, First(ex));
        }
    }

    private bool CanWork() => !IsWorking && !IsBlocked && Profile is not null && Columns.Count > 0;

    /// <summary>Una muestra de lo que se va a agregar (no escribe nada).</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    public Task PreviewAsync() => Working = PreviewCoreAsync();

    private async Task PreviewCoreAsync()
    {
        if (await OptionsAsync() is not { } options) return;
        var chosen = Chosen();
        var columns = chosen.Select((c, i) => new ColumnInfo(i, c.Name, c.Type)).ToArray();
        var rows = new AvaloniaList<object[]>(TestDataGenerator.Rows(chosen, PreviewRows, options)
            .Select(r => r.Select(v => v ?? DBNull.Value).ToArray()));
        PreviewChanged?.Invoke(columns, rows);
        Status = string.Format(Strings.TestDataPreviewed, PreviewRows);
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    public Task RunAsync() => Working = RunCoreAsync();

    private async Task RunCoreAsync()
    {
        if (Profile is not { } profile || IsBlocked) return;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        var count = Math.Clamp(RowCount, 1, MaxRows);
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.TestDataConfirm, count, Table, profile.Name)],
                RunText, Strings.Cancel, Strings.TestDataConfirmTitle)))
            return;
        if (await OptionsAsync() is not { } options) return;
        var chosen = Chosen();
        IsWorking = true;
        _cts = new CancellationTokenSource();
        Status = string.Format(Strings.ImportRunning, 0);
        var progress = new Progress<long>(n => Status = string.Format(Strings.ImportRunning, n));
        try
        {
            var rows = TestDataGenerator.Rows(chosen, count, options).Select((r, i) => ((long)(i + 1), r));
            var result = await Task.Run(() => DataImporter.ImportAsync(profile, password, Table, chosen.Select(c => c.Name).ToList(), rows,
                new ImportOptions(StopOnError: false), progress, _cts.Token));
            foreach (var e in result.Errors.Take(20)) Log(string.Format(Strings.TestDataErrorLine, e.Row, e.Message));
            Status = result.FailedRows > 0
                ? string.Format(Strings.TestDataDoneWithErrors, result.Inserted, result.Elapsed.TotalSeconds, result.FailedRows)
                : string.Format(Strings.TestDataDone, result.Inserted, result.Elapsed.TotalSeconds);
            if (result.Inserted > 0) Owner.InvalidateCatalog(profile.Id);
            // Para seguir sumando sin repetir la clave ni los mismos valores.
            Seed++;
        }
        catch (OperationCanceledException)
        {
            Status = Strings.ImportCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.TestDataFailed, First(ex));
        }
        finally
        {
            Log(Status);
            IsWorking = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanStop() => IsWorking;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    private List<TestColumn> Chosen() =>
        Columns.Where(c => c.Kind != TestValueKind.Skip).Select(c => new TestColumn(c.Name, c.Column.Type, c.Column.NotNull, c.Kind)).ToList();

    /// <summary>Lo que hace falta leer de la base: dónde sigue la clave y los valores de las tablas referenciadas.</summary>
    private async Task<TestDataOptions?> OptionsAsync()
    {
        if (Profile is not { } profile || Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, Profile?.Name);
            return null;
        }
        try
        {
            long first = 1;
            if (Columns.FirstOrDefault(c => c.Kind == TestValueKind.Sequence) is { } key)
                first = await TestDataQueries.MaxAsync(profile, password, Table, key.Name) + 1;
            var references = new Dictionary<string, IReadOnlyList<object>>();
            foreach (var c in Columns.Where(c => c.Kind == TestValueKind.Reference && c.Referenced is not null && c.ReferencedColumn is not null))
            {
                var values = await TestDataQueries.ValuesAsync(profile, password, c.Referenced!, c.ReferencedColumn!);
                if (values.Count == 0 && c.Column.NotNull)
                {
                    Status = string.Format(Strings.TestDataNoReference, c.Name, c.Referenced);
                    return null;
                }
                references[c.Name] = values;
            }
            return new TestDataOptions(Seed, Math.Clamp(NullPercent, 0, 100), first, references,
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es");
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.TestDataFailed, First(ex));
            return null;
        }
    }

    private void Log(string line)
    {
        if (_disposed) return;
        Document.Insert(Document.TextLength, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
    }

    private static string First(Exception ex) => ex.Message.Split('\n', 2)[0].Trim();

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
