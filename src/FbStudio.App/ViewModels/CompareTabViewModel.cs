using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Una diferencia en la lista de la izquierda, con su tilde para incluirla en el script.</summary>
public sealed partial class CompareItem(SchemaDifference difference) : ObservableObject
{
    public SchemaDifference Difference { get; } = difference;

    /// <summary>Lo que sobra sin la opción de borrar no tiene nada que ejecutar: se ve, pero no se tilda.</summary>
    public bool CanInclude => Difference.Statements.Count > 0;

    [ObservableProperty] private bool _isChecked = difference.Statements.Count > 0;

    public string KindLabel => CompareTabViewModel.KindLabel(Difference.Kind);
    public string Name => Difference.Name;

    public string ChangeLabel => Difference.Change switch
    {
        SchemaChange.Added => Strings.CompareAdded,
        SchemaChange.Changed => Strings.CompareChanged,
        _ => Strings.CompareExtra,
    };

    public bool IsAdded => Difference.Change == SchemaChange.Added;
    public bool IsChanged => Difference.Change == SchemaChange.Changed;
    public bool IsExtra => Difference.Change == SchemaChange.Removed;

    /// <summary>El primer detalle (y cuántos más hay).</summary>
    public string Summary => Difference.Details.Count switch
    {
        0 => "",
        1 => Difference.Details[0],
        var n => $"{Difference.Details[0]} (+{n - 1})",
    };
}

/// <summary>
/// Pestaña «Comparar bases»: lee el origen y el destino a la vez (dos conexiones de solo lectura),
/// lista lo nuevo, lo distinto y lo que sobra, y arma con lo tildado el script para que el destino quede igual al
/// origen. El script se abre en una pestaña de consulta del destino para revisarlo y ejecutarlo a mano.
/// </summary>
public sealed partial class CompareTabViewModel : EditorTabViewModel
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public CompareTabViewModel(MainWindowViewModel owner, ConnectionProfile source, ConnectionProfile target, SchemaCompareOptions options)
        : base(owner, target, Strings.CompareTabTitle)
    {
        Source = source;
        Target = target;
        CompareOptions = options;
        Status = string.Format(Strings.CompareIntro, source.Name, target.Name);
    }

    public ConnectionProfile Source { get; }
    public ConnectionProfile Target { get; }
    public SchemaCompareOptions CompareOptions { get; private set; }

    public override MaterialIconKind Icon => MaterialIconKind.CompareHorizontal;

    public string Direction => string.Format(Strings.CompareDirection, Source.Name, Target.Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDifferences))]
    private IReadOnlyList<CompareItem> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDetails), nameof(SelectedSourceDdl), nameof(SelectedTargetDdl), nameof(HasSelection))]
    private CompareItem? _selectedItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCompareCommand), nameof(BuildScriptCommand))]
    private bool _isComparing;

    [ObservableProperty] private string _summary = "";

    /// <summary>El script para igualar (lo de la pestaña «Script»).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInTargetCommand))]
    private string _script = "";

    /// <summary>Pestaña de la derecha: 0 la diferencia elegida, 1 el script.</summary>
    [ObservableProperty] private int _selectedDetail;

    public bool HasDifferences => Items.Count > 0;
    public bool HasSelection => SelectedItem is not null;
    public string SelectedDetails => SelectedItem is { } i ? string.Join("\n", i.Difference.Details.Select(d => "• " + d)) : "";
    public string SelectedSourceDdl => SelectedItem?.Difference.SourceDdl ?? Strings.CompareNotThere;
    public string SelectedTargetDdl => SelectedItem?.Difference.TargetDdl ?? Strings.CompareNotThere;

    /// <summary>La última comparación (para las pruebas).</summary>
    public Task Comparing { get; private set; } = Task.CompletedTask;

    public Task CompareAsync(SchemaCompareOptions? options = null)
    {
        if (IsComparing) return Comparing;
        if (options is not null) CompareOptions = options;
        return Comparing = CompareCoreAsync();
    }

    private async Task CompareCoreAsync()
    {
        if (_disposed) return;
        var sourcePassword = Owner.ProfileStore.GetPassword(Source.Id);
        var targetPassword = Owner.ProfileStore.GetPassword(Target.Id);
        if (sourcePassword is null || targetPassword is null)
        {
            Status = string.Format(Strings.NoSavedPassword, sourcePassword is null ? Source.Name : Target.Name);
            return;
        }
        IsComparing = true;
        _cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var shown = Stopwatch.StartNew();
        (SchemaProgress? Source, SchemaProgress? Target) progress = (null, null);
        void Show()
        {
            if (shown.ElapsedMilliseconds < 150) return;
            shown.Restart();
            static string Pct(SchemaProgress? p) => p is { Total: > 0 } x ? $"{100 * x.Done / x.Total} %" : "…";
            Status = string.Format(Strings.CompareReading, Source.Name, Pct(progress.Source), Target.Name, Pct(progress.Target));
        }
        try
        {
            // Las dos a la vez, cada una con su conexión: tarda lo que la más grande.
            var options = new SchemaScriptOptions { Privileges = CompareOptions.Privileges, GeneratorValues = false };
            await using var sourceExplorer = new MetadataExplorer(Source, sourcePassword);
            await using var targetExplorer = new MetadataExplorer(Target, targetPassword);
            var readSource = sourceExplorer.ReadSchemaAsync(options, new Progress<SchemaProgress>(p => { progress.Source = p; Show(); }), _cts.Token);
            var readTarget = targetExplorer.ReadSchemaAsync(options, new Progress<SchemaProgress>(p => { progress.Target = p; Show(); }), _cts.Token);
            var schemas = await Task.WhenAll(readSource, readTarget);
            if (_disposed) return;
            if (CompareOptions.Area is { } areaName)
            {
                // Las reglas del área del origen, aplicadas a las dos bases.
                if (Owner.AreaStore.Areas(Source).FirstOrDefault(a => a.Name == areaName) is not { } area)
                {
                    Status = string.Format(Strings.SchemaScriptAreaMissing, areaName);
                    return;
                }
                schemas[0] = SchemaFilter.Only(schemas[0], area.Name, await Owner.AreaMembersAsync(Source, area));
                schemas[1] = SchemaFilter.Only(schemas[1], area.Name, await Owner.AreaMembersAsync(Target, area));
            }
            var differences = SchemaComparer.Compare(schemas[0], schemas[1], CompareOptions);
            Items = differences.Select(d => new CompareItem(d)).ToList();
            foreach (var item in Items) item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CompareItem.IsChecked)) Script = ""; };
            SelectedItem = Items.FirstOrDefault();
            Script = "";
            SelectedDetail = 0;
            var (added, changed, extra) = (differences.Count(d => d.Change == SchemaChange.Added), differences.Count(d => d.Change == SchemaChange.Changed),
                differences.Count(d => d.Change == SchemaChange.Removed));
            Summary = differences.Count == 0 ? Strings.CompareNoDifferences : string.Format(Strings.CompareSummary, differences.Count, added, changed, extra);
            Status = string.Format(Strings.CompareDone, Source.Name, Target.Name, clock.Elapsed.TotalSeconds) + " · " + Summary;
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

    [RelayCommand(CanExecute = nameof(IsComparing))]
    private void CancelCompare() => _cts?.Cancel();

    private bool CanBuildScript() => !IsComparing;

    /// <summary>Arma el script con lo tildado y lo muestra en la pestaña «Script».</summary>
    [RelayCommand(CanExecute = nameof(CanBuildScript))]
    public void BuildScript()
    {
        var chosen = Items.Where(i => i.IsChecked && i.CanInclude).Select(i => i.Difference).ToList();
        if (chosen.Count == 0)
        {
            Status = Strings.CompareNothingChecked;
            return;
        }
        Script = SchemaComparer.Script(chosen, SchemaComparer.Header(Source.Name, Target.Name, DateTime.Now));
        SelectedDetail = 1;
        Status = string.Format(Strings.CompareScriptReady, chosen.Count, Target.Name);
    }

    [RelayCommand]
    private void CheckAll(string? value)
    {
        var check = value != "0";
        foreach (var item in Items.Where(i => i.CanInclude)) item.IsChecked = check;
    }

    private bool HasScript() => Script.Length > 0;

    /// <summary>El script a una pestaña de consulta del DESTINO, para revisarlo y ejecutarlo a mano (F9).</summary>
    [RelayCommand(CanExecute = nameof(HasScript))]
    private void OpenInTarget()
    {
        var tab = Owner.OpenQueryTab(Target, string.Format(Strings.CompareScriptTabTitle, Target.Name), Script);
        tab.Status = string.Format(Strings.CompareScriptOpened, Target.Name);
    }

    public static string KindLabel(SchemaItemKind kind) => kind switch
    {
        SchemaItemKind.Role => Strings.CompareKindRole,
        SchemaItemKind.Domain => Strings.CompareKindDomain,
        SchemaItemKind.Generator => Strings.CompareKindGenerator,
        SchemaItemKind.Exception => Strings.CompareKindException,
        SchemaItemKind.Table => Strings.CompareKindTable,
        SchemaItemKind.Index => Strings.CompareKindIndex,
        SchemaItemKind.View => Strings.CompareKindView,
        SchemaItemKind.Procedure => Strings.CompareKindProcedure,
        SchemaItemKind.Function => Strings.CompareKindFunction,
        SchemaItemKind.Package => Strings.CompareKindPackage,
        SchemaItemKind.Trigger => Strings.CompareKindTrigger,
        _ => Strings.CompareKindPrivilege,
    };

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
