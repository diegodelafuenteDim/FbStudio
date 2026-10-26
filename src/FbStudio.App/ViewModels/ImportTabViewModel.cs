using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Collections;
using Avalonia.Threading;
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

/// <summary>Una columna de la tabla y la del archivo que va en ella (0 = no se importa; n = la columna n − 1 del archivo).</summary>
public sealed partial class ImportColumnChoice(DbColumn column) : ObservableObject
{
    public DbColumn Column { get; } = column;
    public string Name => Column.Name;
    public string Detail => (Column.Type + (Column.NotNull ? " NOT NULL" : "") + (Column.IsPrimaryKey ? " · PK" : "")).Trim();

    [ObservableProperty] private int _fileIndex;

    /// <summary>«Actualizar o agregar»: la columna es parte de la clave con la que se busca la fila (de entrada, la PK).</summary>
    [ObservableProperty] private bool _isKey = column.IsPrimaryKey;
}

/// <summary>Una columna de la tabla nueva (importar a una tabla nueva): de qué columna del archivo sale, su nombre y su tipo.</summary>
public sealed partial class ImportNewColumn(string header, int fileIndex, string name, string type) : ObservableObject
{
    public string Header { get; } = header;
    public int FileIndex { get; } = fileIndex;

    [ObservableProperty] private bool _include = true;
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _type = type;
}

/// <summary>
/// Pestaña «Importar · TABLA» (F-26): un CSV o un Excel a una tabla. Las columnas que se llaman igual se relacionan
/// solas y se pueden cambiar; se ve una vista previa del archivo y, al importar, todo va en una transacción (si una fila
/// falla se deshace todo, o se saltea, según se elija). Agrega las filas o, si se elige, actualiza las que ya están con
/// la misma clave y agrega las demás. Sin tabla, arma una nueva desde el archivo (nombres y tipos que se pueden cambiar)
/// y después importa. En producción pregunta; vaciar la tabla antes pide escribir su nombre; con un registro de solo
/// lectura no se ofrece.
/// </summary>
public sealed partial class ImportTabViewModel : EditorTabViewModel
{
    public const int PreviewRows = 100;

    private ImportFile? _file;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <param name="table">null: importar a una tabla nueva, que se arma desde el archivo.</param>
    public ImportTabViewModel(MainWindowViewModel owner, ConnectionProfile profile, string? table)
        : base(owner, profile, table is null ? Strings.ImportNewTableTabTitle : string.Format(Strings.ImportTabTitle, table))
    {
        Table = table ?? "";
        _isNewTable = table is null;
        Status = profile.ReadOnly ? Strings.ImportReadOnly : table is null ? Strings.ImportNewTableIntro : Strings.ImportIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.DatabaseImportOutline;

    public string Table { get; private set; }

    /// <summary>Importar a una tabla nueva (todavía no creada). Al crearla, la pestaña sigue como la de esa tabla.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunText))]
    private bool _isNewTable;

    /// <summary>El nombre de la tabla nueva (de entrada, el del archivo).</summary>
    [ObservableProperty] private string _newTableName = "";

    /// <summary>Las columnas de la tabla nueva, una por columna del archivo, con el tipo según lo que trae.</summary>
    public ObservableCollection<ImportNewColumn> NewColumns { get; } = [];

    /// <summary>Actualizar las filas que ya están (por la clave) y agregar las demás, en vez de agregarlas todas.</summary>
    [ObservableProperty] private bool _upsert;

    public bool IsReadOnlyProfile => Profile?.ReadOnly == true;

    public ObservableCollection<ImportColumnChoice> Columns { get; } = [];

    /// <summary>Lo que se elige para cada columna de la tabla: «(no importar)» y las del archivo.</summary>
    [ObservableProperty] private IReadOnlyList<string> _fileColumns = [Strings.ImportSkipColumn];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExcel), nameof(IsCsv))]
    private string _filePath = "";

    public bool IsExcel => FilePath.Length > 0 && ImportFile.IsExcel(FilePath);
    public bool IsCsv => FilePath.Length > 0 && !IsExcel;

    [ObservableProperty] private bool _hasHeader = true;
    public IReadOnlyList<string> Delimiters { get; } = [Strings.ImportAuto, ";", ",", Strings.ImportTabChar, "|"];
    [ObservableProperty] private int _delimiterIndex;
    public IReadOnlyList<string> Encodings { get; } = [Strings.ImportAuto, "UTF-8", Strings.ImportEncoding1252];
    [ObservableProperty] private int _encodingIndex;
    [ObservableProperty] private IReadOnlyList<string> _sheets = [];
    [ObservableProperty] private int _sheetIndex;

    [ObservableProperty] private bool _emptyIsNull = true;
    [ObservableProperty] private bool _stopOnError = true;
    [ObservableProperty] private bool _emptyTableFirst;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunText))]
    private long? _rowCount;

    public string RunText => IsNewTable
        ? RowCount is { } rows ? string.Format(Strings.ImportRunNewTable, rows) : Strings.ImportRunNewTableNoCount
        : RowCount is { } n ? string.Format(Strings.ImportRun, n) : Strings.ImportRunNoCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand))]
    private bool _isImporting;

    /// <summary>Llegó la vista previa: la vista la muestra en su grilla.</summary>
    public event Action<ColumnInfo[], AvaloniaList<object[]>>? PreviewChanged;

    /// <summary>La última operación (para las pruebas).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    partial void OnUpsertChanged(bool value)
    {
        if (value) EmptyTableFirst = false; // vaciar y actualizar no van juntos
    }

    partial void OnHasHeaderChanged(bool value) => Reload();
    partial void OnDelimiterIndexChanged(int value) => Reload();
    partial void OnEncodingIndexChanged(int value) => Reload();
    partial void OnSheetIndexChanged(int value) => Reload();

    /// <summary>Las columnas de la tabla (sin las calculadas, que no se escriben).</summary>
    public Task LoadTableAsync() => Working = LoadTableCoreAsync();

    private async Task LoadTableCoreAsync()
    {
        if (Profile is not { } profile) return;
        try
        {
            var columns = await Owner.WithExplorerAsync(profile, e => e.LoadColumnsAsync(Table));
            Columns.Clear();
            foreach (var c in columns.Where(c => !c.IsComputed)) Columns.Add(new ImportColumnChoice(c));
            AutoMap();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.ImportFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
    }

    /// <summary>Elige el archivo: lee las hojas (Excel), los nombres de columnas, relaciona y arma la vista previa.</summary>
    public void OpenFile(string path)
    {
        FilePath = path;
        if (IsExcel)
        {
            try { Sheets = ImportFile.Sheets(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException) { Sheets = []; }
            if (SheetIndex != 0) SheetIndex = 0; // dispara Reload
        }
        Reload();
    }

    private void Reload()
    {
        if (FilePath.Length == 0 || !File.Exists(FilePath)) return;
        try
        {
            _file = IsExcel
                ? ImportFile.Excel(FilePath, SheetIndex, HasHeader)
                : ImportFile.Csv(FilePath, HasHeader, Delimiter(), Encoding());
            FileColumns = [Strings.ImportSkipColumn, .. _file.Headers];
            AutoMap();
            ShowPreview();
            RowCount = null;
            var file = _file;
            var culture = CultureInfo.CurrentCulture;
            var newTable = IsNewTable;
            if (newTable)
            {
                NewColumns.Clear();
                if (NewTableName.Length == 0 || NewTableName == _suggestedName) NewTableName = _suggestedName = ImportSchema.TableName(FilePath);
            }
            Status = string.Format(newTable ? Strings.ImportAnalyzing : Strings.ImportReading, Path.GetFileName(FilePath));
            var reading = Status;
            // Contar las filas (y, para una tabla nueva, ver qué tipo va en cada columna) recorre el archivo entero: aparte,
            // para no frenar la pantalla.
            _ = Task.Run(() =>
            {
                long count = 0;
                var counted = file.Rows().Select(r => { count++; return r; });
                var columns = newTable ? ImportSchema.Infer(file.Headers, counted, culture) : null;
                if (!newTable) count = counted.LongCount();
                return (count, columns);
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && ReferenceEquals(file, _file)) Dispatcher.UIThread.Post(() =>
                {
                    RowCount = t.Result.count;
                    // Si mientras tanto se importó (o pasó otra cosa), su resultado no se tapa con «leído».
                    var untouched = Status == reading;
                    if (t.Result.columns is { } inferred)
                    {
                        NewColumns.Clear();
                        foreach (var c in inferred) NewColumns.Add(new ImportNewColumn(file.Headers[c.FileIndex], c.FileIndex, c.Name, c.Type));
                        if (untouched) Status = string.Format(Strings.ImportNewTableRead, Path.GetFileName(FilePath), t.Result.count, file.Headers.Count);
                    }
                    else if (untouched)
                    {
                        Status = string.Format(Strings.ImportRead, Path.GetFileName(FilePath), t.Result.count, file.Headers.Count,
                            Columns.Count(c => c.FileIndex > 0), Columns.Count);
                    }
                });
            }, TaskScheduler.Default);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _file = null;
            Status = string.Format(Strings.ImportReadFailed, ex.Message);
        }
    }

    private string _suggestedName = "";

    private char? Delimiter() => DelimiterIndex switch { 1 => ';', 2 => ',', 3 => '\t', 4 => '|', _ => null };

    private Encoding? Encoding() => EncodingIndex switch
    {
        1 => new UTF8Encoding(false),
        2 => CodePages1252(),
        _ => null,
    };

    private static Encoding CodePages1252()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    private void AutoMap()
    {
        if (_file is null) return;
        var map = ImportMapping.Auto(_file.Headers, Columns.Select(c => c.Name).ToList());
        for (var i = 0; i < Columns.Count; i++) Columns[i].FileIndex = map[i] is { } f ? f + 1 : 0;
    }

    private void ShowPreview()
    {
        if (_file is null) return;
        var columns = _file.Headers.Select((h, i) => new ColumnInfo(i, h, "VARCHAR")).ToArray();
        var rows = new AvaloniaList<object[]>(_file.Rows().Take(PreviewRows)
            .Select(r => Enumerable.Range(0, columns.Length).Select(i => i < r.Length && r[i] is { } v ? v : DBNull.Value).ToArray()));
        PreviewChanged?.Invoke(columns, rows);
    }

    private bool CanRun() => !IsImporting && !IsReadOnlyProfile && Profile is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public Task RunAsync() => Working = RunCoreAsync();

    private async Task RunCoreAsync()
    {
        if (Profile is not { } profile || _file is not { } file) return;
        // Lo que se importa: (columna de la tabla, columna del archivo, tipo).
        List<(string Name, int FileIndex, string Type)> chosen;
        string table, ddl = "";
        IReadOnlyList<string>? keys = null;
        if (IsNewTable)
        {
            if (RowCount is null)
            {
                Status = string.Format(Strings.ImportAnalyzing, Path.GetFileName(FilePath));
                return;
            }
            table = NewTableName.Trim();
            chosen = NewColumns.Where(c => c.Include).Select(c => (c.Name.Trim(), c.FileIndex, c.Type.Trim())).ToList();
            if (NewTableProblem(table, chosen) is { } problem)
            {
                Status = problem;
                return;
            }
            if (await Owner.WithExplorerAsync(profile, e => e.FindObjectAsync(table)) is { } existing)
            {
                Status = string.Format(Strings.ImportNewTableExists, table, DbObjectKinds.Singular(existing.Kind).ToLowerInvariant());
                return;
            }
            ddl = ImportSchema.CreateTableSql(table, chosen.Select(c => new NewTableColumn(c.Name, c.Type, c.FileIndex)).ToList());
        }
        else
        {
            table = Table;
            chosen = Columns.Where(c => c.FileIndex > 0).Select(c => (c.Name, c.FileIndex - 1, c.Column.Type)).ToList();
            if (Upsert)
            {
                keys = Columns.Where(c => c.FileIndex > 0 && c.IsKey).Select(c => c.Name).ToList();
                if (keys.Count == 0)
                {
                    Status = Strings.ImportNoKey;
                    return;
                }
            }
        }
        if (chosen.Count == 0)
        {
            Status = Core.Resources.CoreStrings.ImportNoColumns;
            return;
        }
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        var count = RowCount ?? await Task.Run(() => file.Rows().LongCount());
        var emptyFirst = EmptyTableFirst && !Upsert && !IsNewTable;
        if (profile.IsProduction && !await Owner.ConfirmActionAsync(new ConfirmPrompt(
                [string.Format(IsNewTable ? Strings.ImportNewTableProduction : Strings.ImportProduction, count, table, profile.Name)],
                Strings.ImportRunNoCount, Strings.Cancel, Strings.ImportProductionTitle)))
            return;
        if (emptyFirst && !await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.ImportEmptyTitle,
                [string.Format(Strings.ImportEmptyConfirm, table, profile.Name)], table, Strings.ImportRunNoCount)))
            return;

        IsImporting = true;
        _cts = new CancellationTokenSource();
        var culture = CultureInfo.CurrentCulture;
        var emptyIsNull = EmptyIsNull;
        var indexes = chosen.Select(c => c.FileIndex).ToArray();
        var types = chosen.Select(c => c.Type).ToArray();
        var creating = IsNewTable;
        IEnumerable<(long, object?[])> Rows() => file.Rows().Select((r, i) => ((long)(file.FirstDataRow + i),
            indexes.Select((f, k) => ImportMapping.Convert(f < r.Length ? r[f] : null, types[k], culture, emptyIsNull)).ToArray()));
        Log(string.Format(Strings.ImportStart, table, Path.GetFileName(FilePath)));
        if (creating) Log(ddl);
        Status = string.Format(Strings.ImportRunning, 0);
        var progress = new Progress<long>(n => Status = string.Format(Strings.ImportRunning, n));
        try
        {
            var result = await Task.Run(() => DataImporter.ImportAsync(profile, password, table, chosen.Select(c => c.Name).ToList(), Rows(),
                new ImportOptions(StopOnError, emptyFirst, keys, creating ? ddl : null), progress, _cts.Token));
            foreach (var e in result.Errors) Log(string.Format(Strings.ImportErrorLine, e.Row, e.Message));
            Status = !result.Committed && result.Errors.Count > 0
                ? string.Format(creating ? Strings.ImportNewTableRolledBack : Strings.ImportRolledBack, result.Errors[0].Row, result.Errors[0].Message)
                : result.FailedRows > 0
                    ? string.Format(keys is null ? Strings.ImportDoneWithErrors : Strings.ImportUpsertDoneWithErrors, result.Inserted, result.Elapsed.TotalSeconds, result.FailedRows)
                    : string.Format(keys is null ? Strings.ImportDone : Strings.ImportUpsertDone, result.Inserted, result.Elapsed.TotalSeconds);
            if (result.Committed && result.Deleted > 0) Status += " " + string.Format(Strings.ImportDoneDeleted, result.Deleted);
            if (result.TableCreated) await BecomeTableAsync(profile, table);
            else if (result.Committed && result.Inserted > 0) Owner.InvalidateCatalog(profile.Id);
        }
        catch (OperationCanceledException)
        {
            Status = Strings.ImportCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.ImportFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            Log(Status);
            IsImporting = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Lo que impide crear la tabla nueva (sin nombre, columnas sin nombre o repetidas, tipo vacío), o null.</summary>
    private static string? NewTableProblem(string table, IReadOnlyList<(string Name, int FileIndex, string Type)> columns)
    {
        if (table.Length == 0) return Strings.ImportNewTableNoName;
        if (columns.Any(c => c.Name.Length == 0 || c.Type.Length == 0)) return Strings.ImportNewTableEmptyColumn;
        if (columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } repeated)
            return string.Format(Strings.ImportNewTableRepeated, repeated.Key);
        return null;
    }

    /// <summary>La tabla nueva quedó creada con lo importado: la pestaña pasa a ser la de esa tabla y el árbol la muestra.</summary>
    private async Task BecomeTableAsync(ConnectionProfile profile, string table)
    {
        Status += " " + string.Format(Strings.ImportNewTableCreated, table);
        Table = table;
        OnPropertyChanged(nameof(Table));
        BaseTitle = string.Format(Strings.ImportTabTitle, table);
        IsNewTable = false;
        NewColumns.Clear();
        Owner.InvalidateCatalog(profile.Id);
        await LoadTableCoreAsync();
        await Owner.Tree.RefreshDatabaseAsync(profile.Id);
    }

    private bool CanStop() => IsImporting;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    private void Log(string line)
    {
        if (_disposed) return;
        Document.Insert(Document.TextLength, $"[{DateTime.Now:HH:mm:ss}] {line}\n");
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
