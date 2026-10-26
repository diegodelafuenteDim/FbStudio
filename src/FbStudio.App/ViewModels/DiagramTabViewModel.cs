using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.App.Views;
using FbStudio.Core.Diagrams;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>De dónde sale un diagrama: una tabla (y sus vecinas) o un área (con su definición, guardada o no).</summary>
public sealed record DiagramSource(string Name, SchemaArea? Area = null)
{
    public bool IsArea => Area is not null;
}

/// <summary>Una tabla comodín escondida: «USUARIOS · 12 relaciones con lo dibujado (156 en total)».</summary>
public sealed record HiddenHubRow(HiddenHub Hub)
{
    public string Caption => Hub.Table;
    public string Detail => string.Format(Strings.DiagramHubDetail, Hub.Links, Hub.Neighbors);
}

/// <summary>
/// Pestaña «Diagrama» (F-8): una tabla con sus vecinas por FK, o un área. Las tablas comodín (las que se relacionan con
/// casi todo) se esconden y se listan al costado para mostrarlas si hacen falta; las FK lógicas de los comentarios van
/// punteadas. MSAGL acomoda las cajas; después se mueven a mano. Se exporta a PNG y SVG. Solo lee la base.
/// </summary>
public sealed partial class DiagramTabViewModel : EditorTabViewModel
{
    private SchemaGraph? _graph;
    private IReadOnlyList<string> _areaTables = [];
    private readonly HashSet<string> _shownHubs = new(StringComparer.Ordinal);
    private readonly List<string> _added = [];
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _path = [];
    private bool _loaded;
    private bool _applying;

    // Lo movido a mano (o lo leído de un diagrama guardado) se conserva al sumar o sacar tablas, hasta «Reacomodar».
    private bool _movedByHand;
    private DiagramFile? _file;

    public DiagramTabViewModel(MainWindowViewModel owner, ConnectionProfile profile, DiagramSource source, DiagramFile? file = null)
        : base(owner, profile, string.Format(source.IsArea ? Strings.DiagramTitleArea : Strings.DiagramTitleTable, source.Name))
    {
        Source = source;
        _file = file;
        DiagramScene.MoreText = n => Count(n, Strings.DiagramMoreColumnOne, Strings.DiagramMoreColumns);
    }

    public override MaterialIconKind Icon => MaterialIconKind.Sitemap;

    public DiagramSource Source { get; }
    public bool IsArea => Source.IsArea;
    public bool IsTable => !Source.IsArea;

    public IReadOnlyList<string> HopChoices { get; } = [Strings.DiagramHops1, Strings.DiagramHops2];
    public IReadOnlyList<string> ColumnChoices { get; } = [Strings.DiagramColumnsKeys, Strings.DiagramColumnsAll, Strings.DiagramColumnsNone];
    public IReadOnlyList<string> LayoutChoices { get; } = [Strings.DiagramLayoutAuto, Strings.DiagramLayoutLayered, Strings.DiagramLayoutForces, Strings.DiagramLayoutDistances];

    /// <summary>0 automático, 1 en capas, 2 por fuerzas, 3 por distancias (<see cref="DiagramLayoutKind"/>).</summary>
    [ObservableProperty] private int _layoutIndex;

    /// <summary>0 = un paso (las vecinas directas), 1 = dos pasos.</summary>
    [ObservableProperty] private int _hopIndex;
    [ObservableProperty] private bool _hideHubs = true;
    [ObservableProperty] private bool _showLogical = true;
    [ObservableProperty] private int _columnsIndex;
    [ObservableProperty] private bool _showOutside;

    [ObservableProperty] private DiagramScene? _scene;
    [ObservableProperty] private IReadOnlyList<HiddenHubRow> _hiddenHubs = [];
    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedTitle), nameof(SelectedInfo))]
    [NotifyCanExecuteChangedFor(nameof(ExpandSelectedCommand), nameof(RemoveSelectedCommand), nameof(OpenSelectedCommand))]
    private DiagramNode? _selectedNode;

    public bool HasSelection => SelectedNode is not null;
    public bool HasHiddenHubs => HiddenHubs.Count > 0;

    public string SelectedTitle => SelectedNode?.Name ?? "";

    /// <summary>«12 columnas · usa: TURNOS, MEDICOS · la usan: TURNOS_DET».</summary>
    public string SelectedInfo
    {
        get
        {
            if (SelectedNode is not { } n || _graph?.Table(n.Name) is not { } table) return "";
            var uses = _graph.Links.Where(l => l.Table == n.Name && (ShowLogical || !l.IsLogical) && l.Referenced != n.Name).Select(l => l.Referenced).Distinct().Order(StringComparer.Ordinal).ToList();
            var usedBy = _graph.Links.Where(l => l.Referenced == n.Name && (ShowLogical || !l.IsLogical) && l.Table != n.Name).Select(l => l.Table).Distinct().Order(StringComparer.Ordinal).ToList();
            return string.Join(Environment.NewLine, new[]
            {
                Count(table.Columns.Count, Strings.DiagramColumnOne, Strings.DiagramColumnCount) + (_graph.IsHub(n.Name, ShowLogical) ? " · " + Strings.DiagramIsHub : ""),
                uses.Count > 0 ? string.Format(Strings.DiagramUses, Names(uses)) : null,
                usedBy.Count > 0 ? string.Format(Strings.DiagramUsedBy, Names(usedBy)) : null,
            }.Where(s => s is not null));
        }
    }

    /// <summary>«1 tabla» o «12 tablas».</summary>
    private static string Count(int n, string one, string many) => n == 1 ? one : string.Format(many, n);

    private static string Names(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(8)) + (names.Count > 8 ? string.Format(Strings.DiagramAndMore, names.Count - 8) : "");

    /// <summary>La carga que empezó al abrir la pestaña.</summary>
    public Task Loading { get; set; } = Task.CompletedTask;

    /// <summary>Se armó o reacomodó el diagrama: la vista lo encuadra.</summary>
    public event Action? Rearranged;

    /// <summary>Lee las tablas y relaciones (y, para un área, quiénes están en ella) y arma el diagrama.</summary>
    public async Task LoadAsync()
    {
        if (Profile is not { } profile) return;
        Status = Strings.Loading;
        try
        {
            _graph = await Owner.SchemaGraphAsync(profile);
            if (Source.Area is { } area)
            {
                // Al diagrama solo le importan las tablas y vistas: alcanzan las del grafo (no hace falta leer todo lo demás).
                var objects = _graph.Tables.Select(t => new DbObject(t.IsView ? DbObjectKind.View : DbObjectKind.Table, t.Name)).ToList();
                var uses = area.IncludeDependents ? await Owner.WithExplorerAsync(profile, explorer => explorer.LoadTableUsesAsync()) : null;
                _areaTables = new AreaMembership([area], objects, uses).Members(0)
                    .Where(o => DbObjectKinds.IsRelation(o.Kind)).Select(o => o.Name).ToList();
            }
            _loaded = true;
            if (_file is { } file)
            {
                _file = null;
                Apply(file);
            }
            else Rebuild();
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.DiagramLoadFailed, ex.Message.Split('\n')[0]);
        }
    }

    /// <summary>Lo de un diagrama guardado: las opciones, las tablas sumadas y sacadas, el camino y dónde estaba cada una.</summary>
    private void Apply(DiagramFile file)
    {
        _applying = true;
        try
        {
            HopIndex = Math.Clamp(file.Hops - 1, 0, HopChoices.Count - 1);
            HideHubs = file.HideHubs;
            ShowLogical = file.ShowLogical;
            ColumnsIndex = Math.Clamp(file.Columns, 0, ColumnChoices.Count - 1);
            ShowOutside = file.ShowOutside;
            LayoutIndex = Math.Clamp(file.Layout, 0, LayoutChoices.Count - 1);
            _added.Clear();
            _added.AddRange(file.Added);
            _removed.Clear();
            _removed.UnionWith(file.Removed);
            _shownHubs.Clear();
            _shownHubs.UnionWith(file.ShownHubs);
            _path = file.Path;
        }
        finally
        {
            _applying = false;
        }
        Rebuild(file.Positions);
        _movedByHand = file.Positions.Count > 0;
        Status = file.Database is { } db && db != Profile?.Name
            ? string.Format(Strings.DiagramOpenedFromOther, db, Summary)
            : Summary;
    }

    /// <summary>Se movió una tabla a mano: desde acá, sumar o sacar tablas no desacomoda las que ya están.</summary>
    public void NodeMovedByHand() => _movedByHand = true;

    /// <summary>Guarda el diagrama (.fbsdiagram) con sus opciones y dónde quedó cada tabla.</summary>
    public void Save(string path)
    {
        try
        {
            Workspace.DiagramFiles.Write(path, new DiagramFile
            {
                Database = Profile?.Name,
                Source = Source.Name,
                Area = Source.Area,
                Hops = HopIndex + 1,
                HideHubs = HideHubs,
                ShowLogical = ShowLogical,
                Columns = ColumnsIndex,
                ShowOutside = ShowOutside,
                Layout = LayoutIndex,
                Added = [.. _added],
                Removed = [.. _removed.Order(StringComparer.Ordinal)],
                ShownHubs = [.. _shownHubs.Order(StringComparer.Ordinal)],
                Path = _path,
                Positions = Scene?.Positions() ?? [],
            });
            Status = string.Format(Strings.DiagramSaved, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = string.Format(Strings.DiagramExportFailed, ex.Message);
        }
    }

    partial void OnHopIndexChanged(int value) => Rebuild();
    partial void OnHideHubsChanged(bool value) => Rebuild();
    partial void OnShowLogicalChanged(bool value) => Rebuild();
    partial void OnColumnsIndexChanged(int value) => Rebuild();
    partial void OnShowOutsideChanged(bool value) => Rebuild();
    partial void OnLayoutIndexChanged(int value) => Relayout();
    partial void OnHiddenHubsChanged(IReadOnlyList<HiddenHubRow> value) => OnPropertyChanged(nameof(HasHiddenHubs));

    /// <summary>Elige qué tablas van, arma las cajas y las acomoda.</summary>
    /// <param name="positions">Dónde poner las tablas (de un diagrama guardado); si no, lo movido a mano se conserva.</param>
    private void Rebuild(IReadOnlyDictionary<string, DiagramPosition>? positions = null)
    {
        if (!_loaded || _applying || _graph is not { } g) return;
        positions ??= _movedByHand ? Scene?.Positions() : null;
        if (!IsArea && !g.Contains(Source.Name))
        {
            Status = string.Format(Strings.DiagramTableMissing, Source.Name);
            return;
        }
        var baseSelection = IsArea
            ? g.ForArea(_areaTables, ShowOutside, HideHubs, _shownHubs, ShowLogical)
            : g.Around([Source.Name], HopIndex + 1, HideHubs, _shownHubs, ShowLogical);
        var tables = baseSelection.Tables.Concat(IsArea ? [] : _added).Where(t => !_removed.Contains(t)).Distinct().ToList();
        var outside = baseSelection.Outside.Concat(IsArea ? _added : []).Where(t => !_removed.Contains(t) && !tables.Contains(t)).Distinct().ToList();
        // Las comodines pedidas que no están cerca igual se dibujan (de afuera, sin columnas).
        outside.AddRange(_shownHubs.Where(h => !tables.Contains(h) && !outside.Contains(h) && !_removed.Contains(h) && g.Contains(h)));
        var selection = g.Compose(tables, outside, HideHubs, _shownHubs, ShowLogical);
        var mode = ColumnsIndex switch { 1 => DiagramColumnsMode.All, 2 => DiagramColumnsMode.None, _ => DiagramColumnsMode.Keys };
        var scene = DiagramScene.Build(g, selection, mode, DiagramPainter.Measure, ShowLogical);
        scene.Path = _path;
        DiagramLayout.Arrange(scene, (DiagramLayoutKind)LayoutIndex);
        if (positions is not null) scene.Place(positions);
        SelectedNode = null;
        Scene = scene;
        HiddenHubs = selection.HiddenHubs.Select(h => new HiddenHubRow(h)).ToList();
        var logical = selection.Links.Count(l => l.IsLogical);
        Summary = string.Join(" · ", new[]
        {
            Count(scene.Nodes.Count, Strings.DiagramTableOne, Strings.DiagramTables),
            Count(selection.Links.Count, Strings.DiagramLinkOne, Strings.DiagramLinks),
            logical > 0 ? Count(logical, Strings.DiagramLogicalOne, Strings.DiagramSummaryLogical) : null,
            selection.HiddenHubs.Count > 0 ? Count(selection.HiddenHubs.Count, Strings.DiagramHiddenOne, Strings.DiagramSummaryHidden) : null,
        }.Where(s => s is not null));
        Status = Summary;
        Rearranged?.Invoke();
    }

    /// <summary>Vuelve a leer las tablas y relaciones de la base (después de cambiar su estructura).</summary>
    [RelayCommand]
    private async Task RereadAsync()
    {
        if (Profile is not { } profile) return;
        Owner.InvalidateCatalog(profile.Id);
        await LoadAsync();
    }

    /// <summary>Vuelve a acomodar todo con MSAGL (lo movido a mano se pierde).</summary>
    [RelayCommand]
    private void Relayout()
    {
        _movedByHand = false;
        if (Scene is null) return;
        DiagramLayout.Arrange(Scene, (DiagramLayoutKind)LayoutIndex);
        Rearranged?.Invoke();
    }

    /// <summary>Dibuja una tabla comodín que estaba escondida.</summary>
    public void ShowHub(HiddenHubRow row)
    {
        _shownHubs.Add(row.Hub.Table);
        _removed.Remove(row.Hub.Table);
        Rebuild();
    }

    private bool CanUseSelected() => SelectedNode is not null;

    /// <summary>Suma las vecinas de la tabla elegida (sin las comodines escondidas).</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private void ExpandSelected()
    {
        if (SelectedNode is not { } n || _graph is not { } g) return;
        var drawn = Scene?.Nodes.Select(x => x.Name).ToHashSet() ?? [];
        var neighbors = g.Neighbors(n.Name, ShowLogical)
            .Where(x => !drawn.Contains(x) && (!HideHubs || !g.IsHub(x, ShowLogical) || _shownHubs.Contains(x)))
            .Order(StringComparer.Ordinal).ToList();
        if (neighbors.Count == 0)
        {
            Status = string.Format(Strings.DiagramNoMoreNeighbors, n.Name);
            return;
        }
        foreach (var x in neighbors)
        {
            _removed.Remove(x);
            if (!_added.Contains(x)) _added.Add(x);
        }
        Rebuild();
    }

    /// <summary>Saca la tabla elegida del diagrama.</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private void RemoveSelected()
    {
        if (SelectedNode is not { } n) return;
        _removed.Add(n.Name);
        _added.Remove(n.Name);
        _shownHubs.Remove(n.Name);
        Rebuild();
    }

    /// <summary>Agrega una tabla por su nombre (con sus relaciones con lo que ya está).</summary>
    [RelayCommand]
    private async Task AddTableAsync()
    {
        if (_graph is not { } g) return;
        if (await Owner.AskTextAsync(new TextPrompt(Strings.DiagramAddTableTitle, Strings.DiagramAddTablePrompt)) is not { } text) return;
        var name = text.Trim().Trim('"');
        var found = g.Table(name) ?? g.Table(name.ToUpperInvariant());
        if (found is null)
        {
            Status = string.Format(Strings.DiagramTableMissing, name);
            return;
        }
        _removed.Remove(found.Name);
        if (!_added.Contains(found.Name)) _added.Add(found.Name);
        if (g.IsHub(found.Name, ShowLogical)) _shownHubs.Add(found.Name);
        Rebuild();
    }

    /// <summary>
    /// «Camino hasta…»: la cadena de FK más corta desde la tabla elegida (o la del diagrama) hasta otra. Se suman las
    /// tablas del medio (también una comodín, si el camino pasa por ella) y el camino queda resaltado.
    /// </summary>
    [RelayCommand]
    private async Task PathToAsync()
    {
        if (_graph is not { } g) return;
        if ((SelectedNode?.Name ?? (IsArea ? null : Source.Name)) is not { } from)
        {
            Status = Strings.DiagramPathPickFirst;
            return;
        }
        if (await Owner.AskTextAsync(new TextPrompt(Strings.DiagramPathTitle, string.Format(Strings.DiagramPathPrompt, from))) is not { } text) return;
        var name = text.Trim().Trim('"');
        if ((g.Table(name) ?? g.Table(name.ToUpperInvariant())) is not { } target)
        {
            Status = string.Format(Strings.DiagramTableMissing, name);
            return;
        }
        if (g.Path(from, target.Name, ShowLogical) is not { } path)
        {
            Status = string.Format(ShowLogical ? Strings.DiagramNoPath : Strings.DiagramNoPathPhysical, from, target.Name);
            return;
        }
        foreach (var t in path)
        {
            _removed.Remove(t);
            if (t != Source.Name && !_added.Contains(t)) _added.Add(t);
            if (g.IsHub(t, ShowLogical)) _shownHubs.Add(t);
        }
        _path = path;
        Rebuild();
        SelectedNode = Scene?.Node(target.Name);
        Status = string.Format(path.Count == 2 ? Strings.DiagramPathDirect : Strings.DiagramPathFound, string.Join(" → ", path), path.Count - 1);
    }

    /// <summary>El diagrama como texto de Mermaid, para «Copiar como Mermaid».</summary>
    public string? MermaidText => Scene is { } scene ? DiagramMermaid.Write(scene, BaseTitle) : null;

    /// <summary>El camino resaltado (para las pruebas).</summary>
    public IReadOnlyList<string> PathShown => Scene?.Path ?? [];

    [RelayCommand(CanExecute = nameof(CanUseSelected))]
    private Task OpenSelectedAsync() => SelectedNode is { } n ? OpenAsync(n) : Task.CompletedTask;

    /// <summary>Abre la tabla o vista en su pestaña (DDL, datos, dependencias).</summary>
    public Task OpenAsync(DiagramNode node) =>
        Profile is { } profile
            ? Owner.OpenObjectAsync(profile, new DbObject(node.IsView ? DbObjectKind.View : DbObjectKind.Table, node.Name))
            : Task.CompletedTask;

    /// <summary>Guarda el diagrama como PNG, SVG, draw.io o .fbsdiagram (para abrirlo después), según la extensión.</summary>
    public void Export(string path)
    {
        if (Scene is not { } scene) return;
        if (path.EndsWith(DiagramFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            Save(path);
            return;
        }
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, DiagramSvg.Write(scene, BaseTitle));
            else if (path.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, DiagramMermaid.Write(scene, BaseTitle));
            else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, "```mermaid\n" + DiagramMermaid.Write(scene, BaseTitle) + "```\n");
            else if (path.EndsWith(".drawio", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, DiagramDrawio.Write(scene, BaseTitle));
            else
                DiagramPainter.SavePng(scene, path);
            Status = string.Format(Strings.DiagramExported, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Status = string.Format(Strings.DiagramExportFailed, ex.Message);
        }
    }
}
