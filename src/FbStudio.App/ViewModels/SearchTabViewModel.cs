using System.Diagnostics;
using FbStudio.App.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Pestaña "Buscar en metadatos": texto en fuentes PSQL, nombres, columnas y descripciones de la base de la pestaña.
/// Doble clic en un resultado abre el objeto en la línea encontrada.
/// </summary>
public sealed partial class SearchTabViewModel : EditorTabViewModel
{
    private CancellationTokenSource? _searchCts;
    private IReadOnlyList<MetadataSearchHit> _allHits = [];
    private IReadOnlyList<SchemaArea> _areas = [];
    private IReadOnlyList<DbObject>? _objects;
    private TableUses? _uses;
    private TimeSpan _elapsed;

    public SearchTabViewModel(MainWindowViewModel owner, ConnectionProfile? profile) : base(owner, profile, Strings.ToolSearch)
    {
        Status = Strings.SearchIntro;
        LoadAreaChoices();
        Owner.AreaStore.Changed += LoadAreaChoices;
    }

    /// <summary>«Todas las áreas», las de la base y «Sin área»; vacía si la base no tiene áreas (F-9).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAreas))]
    private IReadOnlyList<string> _areaChoices = [];

    /// <summary>0 = todas; después cada área; la última, «Sin área».</summary>
    [ObservableProperty] private int _areaIndex;

    public bool HasAreas => AreaChoices.Count > 0;

    /// <summary>El último filtrado por área (lee los objetos de la base la primera vez).</summary>
    public Task AreaFiltering { get; private set; } = Task.CompletedTask;

    partial void OnAreaIndexChanged(int value)
    {
        if (_allHits.Count > 0) AreaFiltering = ShowHitsAsync();
    }

    private void LoadAreaChoices()
    {
        var current = AreaIndex > 0 && AreaIndex < AreaChoices.Count ? AreaChoices[AreaIndex] : null;
        _areas = Profile is { } p ? Owner.AreaStore.Areas(p) : [];
        AreaChoices = _areas.Count == 0 ? [] : [Strings.SearchAreaAll, .. _areas.Select(a => a.Name), Strings.AreasUnassigned];
        AreaIndex = current is null ? 0 : Math.Max(0, AreaChoices.ToList().IndexOf(current));
    }

    public override MaterialIconKind Icon => MaterialIconKind.Magnify;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _inSources = true;
    [ObservableProperty] private bool _inNames = true;
    [ObservableProperty] private bool _inColumns = true;
    [ObservableProperty] private bool _inDescriptions = true;
    [ObservableProperty] private IReadOnlyList<SearchHitRow> _hits = [];

    /// <summary>Lo que muestra la lista: grupos por tipo → objetos → coincidencias (según lo que esté desplegado).</summary>
    [ObservableProperty] private IReadOnlyList<SearchListItem> _items = [];

    private IReadOnlyList<SearchListItem> _groups = [];
    [ObservableProperty] private string _summary = "";

    /// <summary>Texto de la última búsqueda (para ubicar la línea al abrir un resultado).</summary>
    public string LastTerm { get; private set; } = "";

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (IsRunning) return;
        var term = SearchText.Trim();
        if (term.Length < 2)
        {
            Status = Strings.SearchTooShort;
            return;
        }
        if (Profile is not { } profile)
        {
            Status = Strings.SearchPickDatabase;
            return;
        }

        LoadAreaChoices();
        IsRunning = true;
        _searchCts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        Status = string.Format(Strings.SearchRunning, term, profile.Name);
        try
        {
            var options = new MetadataSearchOptions(InSources, InNames, InColumns, InDescriptions);
            var token = _searchCts.Token;
            _allHits = await Owner.WithExplorerAsync(profile, explorer => explorer.SearchAsync(term, options, token));
            _objects = null; // los objetos de la base pueden haber cambiado: el filtro por área los vuelve a leer
            _uses = null;
            _elapsed = clock.Elapsed;
            LastTerm = term;
            BaseTitle = string.Format(Strings.SearchTabTitle, term);
            await ShowHitsAsync();
        }
        catch (OperationCanceledException)
        {
            Status = Strings.SearchCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.SearchFailed, ex.Message);
        }
        finally
        {
            IsRunning = false;
            _searchCts.Dispose();
            _searchCts = null;
        }
    }

    protected override void OnCancelRequested() => _searchCts?.Cancel();

    /// <summary>Muestra los resultados; con un área elegida, solo los de sus objetos (los triggers e índices van con su tabla).</summary>
    private async Task ShowHitsAsync()
    {
        var hits = _allHits;
        var areaName = AreaIndex > 0 && AreaIndex < AreaChoices.Count ? AreaChoices[AreaIndex] : null;
        if (areaName is not null && Profile is { } profile && hits.Count > 0)
        {
            try
            {
                _objects ??= await Owner.WithExplorerAsync(profile, explorer => explorer.LoadAllObjectsAsync());
                if (_areas.Any(a => a.IncludeDependents))
                    _uses ??= await Owner.WithExplorerAsync(profile, explorer => explorer.LoadTableUsesAsync());
                var membership = new AreaMembership(_areas, _objects, _uses);
                var members = (AreaIndex <= _areas.Count ? membership.Members(AreaIndex - 1) : membership.Unassigned)
                    .Select(o => (o.Kind, o.Name)).ToHashSet();
                hits = hits.Where(h => members.Contains((h.Kind, h.Name))).ToList();
            }
            catch (Exception ex)
            {
                Status = string.Format(Strings.SearchFailed, ex.Message);
                return;
            }
        }
        Hits = hits.Select(h => new SearchHitRow(h)).ToList();
        _groups = SearchListItem.Build(hits, LastTerm);
        RebuildItems();
        var objects = hits.Select(h => (h.Kind, h.Name)).Distinct().Count();
        Summary = hits.Count == 0
            ? string.Format(Strings.SearchNoResults, LastTerm)
            : string.Format(Strings.SearchSummary, hits.Count, objects, _elapsed.TotalSeconds);
        if (areaName is not null) Summary = string.Format(Strings.SearchAreaFiltered, Summary, areaName);
        Status = Summary;
    }

    public override async ValueTask DisposeAsync()
    {
        Owner.AreaStore.Changed -= LoadAreaChoices;
        await base.DisposeAsync();
    }

    /// <summary>Despliega o pliega un grupo o un objeto con varias coincidencias.</summary>
    public void Toggle(SearchListItem item)
    {
        if (item.Children.Count == 0) return;
        item.IsExpanded = !item.IsExpanded;
        RebuildItems();
    }

    /// <summary>Doble clic / Enter: un grupo se pliega o despliega; un objeto o una línea se abren en su lugar.</summary>
    public Task ActivateAsync(SearchListItem item)
    {
        if (item.Kind == SearchItemKind.Group)
        {
            Toggle(item);
            return Task.CompletedTask;
        }
        return item.Hit is { } hit ? OpenHitAsync(new SearchHitRow(hit)) : Task.CompletedTask;
    }

    private void RebuildItems()
    {
        var rows = new List<SearchListItem>();
        foreach (var group in _groups)
        {
            rows.Add(group);
            if (!group.IsExpanded) continue;
            foreach (var obj in group.Children)
            {
                rows.Add(obj);
                if (obj.IsExpanded) rows.AddRange(obj.Children);
            }
        }
        Items = rows;
    }

    public async Task OpenHitAsync(SearchHitRow row)
    {
        if (Profile is not { } profile) return;
        var tab = await Owner.OpenObjectAsync(profile, new DbObject(row.Hit.Kind, row.Hit.Name));
        tab?.GoToSearchHit(row.Hit, LastTerm);
    }
}

/// <summary>Una fila de resultados de la búsqueda.</summary>
public sealed record SearchHitRow(MetadataSearchHit Hit)
{
    public MaterialIconKind Icon => DatabaseTreeViewModel.IconFor(Hit.Kind);
    public string Caption => $"{DbObjectKinds.Singular(Hit.Kind)} {Hit.Name}";
    public string Where => SearchListItem.WhereOf(Hit);
    public string Snippet => Hit.Snippet;
}
