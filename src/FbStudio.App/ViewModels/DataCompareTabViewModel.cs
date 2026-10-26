using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Una fila distinta en la lista: su clave, qué le pasa y el detalle de las columnas que cambian.</summary>
public sealed class DataCompareItem(TableDataComparison comparison, RowDifference difference)
{
    public RowDifference Difference { get; } = difference;
    public string Key { get; } = TableDataComparer.KeyText(difference);

    public string ChangeLabel => Difference.Change switch
    {
        RowChange.OnlyInSource => Strings.DataCompareMissing,
        RowChange.Different => Strings.DataCompareDifferent,
        _ => Strings.DataCompareExtra,
    };

    public bool IsMissing => Difference.Change == RowChange.OnlyInSource;
    public bool IsDifferent => Difference.Change == RowChange.Different;
    public bool IsExtra => Difference.Change == RowChange.OnlyInTarget;

    /// <summary>«NOMBRE: 'a' → 'b'» de cada columna distinta, en un renglón.</summary>
    public string Details { get; } = string.Join(" · ", TableDataComparer.ChangeDetails(comparison, difference));
}

/// <summary>
/// Pestaña «Comparar datos»: los datos de una tabla en esta base (el origen) contra los de otra (el
/// destino), por la clave primaria. Lee las dos a la vez, cada una con su conexión de solo lectura; lista las filas que
/// faltan en el destino, las distintas y las que sobran, y arma el script para igualar el destino, que se abre en una
/// consulta del destino para revisarlo y ejecutarlo a mano. No escribe nada en ninguna de las dos.
/// </summary>
public sealed partial class DataCompareTabViewModel : EditorTabViewModel
{
    /// <summary>Más filas que esto en alguna de las dos: no se compara (es para tablas chicas).</summary>
    public const int RowLimit = 100_000;

    private CancellationTokenSource? _cts;
    private TableDataComparison? _comparison;
    private bool _disposed;

    public DataCompareTabViewModel(MainWindowViewModel owner, ConnectionProfile source, string table)
        : base(owner, source, string.Format(Strings.DataCompareTabTitle, table))
    {
        Source = source;
        Table = table;
        Targets = owner.Profiles.Where(p => p.Id != source.Id).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        // Destino propuesto: otra base, preferentemente de producción si el origen no lo es.
        _target = Targets.FirstOrDefault(p => p.IsProduction && !source.IsProduction) ?? Targets.FirstOrDefault();
        Status = string.Format(Strings.DataCompareIntro, table, source.Name);
    }

    public ConnectionProfile Source { get; }
    public string Table { get; }
    public IReadOnlyList<ConnectionProfile> Targets { get; }

    public override MaterialIconKind Icon => MaterialIconKind.TableSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Direction))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private ConnectionProfile? _target;

    public string Direction => string.Format(Strings.DataCompareDirection, Table, Source.Name, Target?.Name ?? "…");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDifferences))]
    private IReadOnlyList<DataCompareItem> _items = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand), nameof(CancelCompareCommand))]
    private bool _isComparing;

    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInTargetCommand))]
    private string _script = "";

    /// <summary>Incluir en el script el DELETE de las filas que están solo en el destino.</summary>
    [ObservableProperty] private bool _deleteExtra;

    /// <summary>A qué base se compara la que está en el script (cambiar el destino no cambia un script ya armado).</summary>
    private ConnectionProfile? _scriptTarget;

    public bool HasDifferences => Items.Count > 0;

    /// <summary>La última comparación (para las pruebas).</summary>
    public Task Comparing { get; private set; } = Task.CompletedTask;

    private bool CanCompare() => !IsComparing && Target is not null;

    [RelayCommand(CanExecute = nameof(CanCompare))]
    public Task Compare()
    {
        if (IsComparing) return Comparing;
        return Comparing = CompareCoreAsync();
    }

    private async Task CompareCoreAsync()
    {
        if (_disposed || Target is not { } target) return;
        var sourcePassword = Owner.ProfileStore.GetPassword(Source.Id);
        var targetPassword = Owner.ProfileStore.GetPassword(target.Id);
        if (sourcePassword is null || targetPassword is null)
        {
            Status = string.Format(Strings.NoSavedPassword, sourcePassword is null ? Source.Name : target.Name);
            return;
        }
        IsComparing = true;
        _cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        Status = string.Format(Strings.DataCompareReading, Table, Source.Name, target.Name);
        try
        {
            // Las columnas de las dos (las calculadas no se comparan) y la clave del origen.
            await using var sourceExplorer = new MetadataExplorer(Source, sourcePassword);
            await using var targetExplorer = new MetadataExplorer(target, targetPassword);
            var described = await Task.WhenAll(sourceExplorer.LoadColumnsAsync(Table, _cts.Token), targetExplorer.LoadColumnsAsync(Table, _cts.Token));
            var (sourceColumns, targetColumns) = (described[0], described[1]);
            if (targetColumns.Count == 0)
            {
                Fail(string.Format(Strings.DataCompareNoTable, Table, target.Name));
                return;
            }
            var key = sourceColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
            var inTarget = targetColumns.Where(c => !c.IsComputed).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            if (key.Count == 0 || !key.All(inTarget.Contains))
            {
                Fail(string.Format(key.Count == 0 ? Strings.DataCompareNoKey : Strings.DataCompareKeyMissing, Table, target.Name));
                return;
            }
            var common = sourceColumns.Where(c => !c.IsComputed && inTarget.Contains(c.Name)).Select(c => c.Name).ToList();
            var onlySource = sourceColumns.Where(c => !c.IsComputed && !inTarget.Contains(c.Name)).Select(c => c.Name).ToList();
            var inSource = sourceColumns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var onlyTarget = targetColumns.Where(c => !c.IsComputed && !inSource.Contains(c.Name)).Select(c => c.Name).ToList();

            var read = await Task.WhenAll(
                TableDataReader.ReadAsync(Source, sourcePassword, Table, common, key, RowLimit, _cts.Token),
                TableDataReader.ReadAsync(target, targetPassword, Table, common, key, RowLimit, _cts.Token));
            if (read.Any(r => r.TooMany))
            {
                Fail(string.Format(Strings.DataCompareTooMany, Table, RowLimit));
                return;
            }
            var keyPositions = key.Select(k => common.IndexOf(k)).ToList();
            var comparison = TableDataComparer.Compare(Table, read[0].Columns, keyPositions, read[0].Rows, read[1].Rows);
            if (_disposed) return;
            _comparison = comparison;
            _scriptTarget = target;
            Items = comparison.Differences.Select(d => new DataCompareItem(comparison, d)).ToList();
            var summary = comparison.Differences.Count == 0
                ? string.Format(Strings.DataCompareEqual, comparison.SourceRows)
                : string.Format(Strings.DataCompareSummary, comparison.OnlyInSource, comparison.Different, comparison.OnlyInTarget, comparison.Equal);
            if (onlySource.Count > 0) summary += " · " + string.Format(Strings.DataCompareOnlySourceColumns, string.Join(", ", onlySource));
            if (onlyTarget.Count > 0) summary += " · " + string.Format(Strings.DataCompareOnlyTargetColumns, string.Join(", ", onlyTarget));
            Summary = summary;
            BuildScript();
            Status = string.Format(Strings.DataCompareDone, Table, clock.Elapsed.TotalSeconds) + " · " + Summary;
        }
        catch (OperationCanceledException)
        {
            Status = Strings.CompareCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.CompareFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            IsComparing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Fail(string message)
    {
        _comparison = null;
        Items = [];
        Script = "";
        Summary = message;
        Status = message;
    }

    partial void OnDeleteExtraChanged(bool value) => BuildScript();

    private void BuildScript()
    {
        if (_comparison is not { } c || _scriptTarget is not { } target) return;
        var header = string.Format(Strings.DataCompareScriptHeader, c.Table, Source.Name, target.Name, DateTime.Now) + "\n";
        Script = c.Differences.Count == 0 ? "" : TableDataComparer.Script(c, DeleteExtra, header);
    }

    [RelayCommand(CanExecute = nameof(IsComparing))]
    private void CancelCompare() => _cts?.Cancel();

    private bool HasScript() => Script.Length > 0;

    /// <summary>El script a una pestaña de consulta del DESTINO, para revisarlo y ejecutarlo a mano (F9), sin COMMIT.</summary>
    [RelayCommand(CanExecute = nameof(HasScript))]
    private void OpenInTarget()
    {
        if (_scriptTarget is not { } target) return;
        var tab = Owner.OpenQueryTab(target, string.Format(Strings.DataCompareScriptTabTitle, Table, target.Name), Script);
        tab.Status = string.Format(Strings.DataCompareScriptOpened, target.Name);
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
