using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Workspace;
using Material.Icons;

namespace FbStudio.App.ViewModels;

public enum TreeNodeKind
{
    Folder,
    Database,
    Category,
    Object,
    Column,
    Message,
    /// <summary>«Áreas» de una base (F-9): adentro, cada área con sus objetos por tipo y «Sin área».</summary>
    Areas,
    Area,
}

/// <summary>Un nodo del árbol de bases (carpeta → base → categoría → objeto → columna).</summary>
public sealed partial class TreeNode : ObservableObject
{
    public required TreeNodeKind Kind { get; init; }
    public required int Level { get; init; }
    public TreeNode? Parent { get; init; }
    public ConnectionProfile? Profile { get; init; }
    public DbObjectKind? Category { get; init; }
    public DbObject? Object { get; init; }
    public DbColumn? Column { get; init; }

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private MaterialIconKind _icon;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccessTone), nameof(IsWarningTone), nameof(IsDangerTone))]
    private Tone _tone;

    public bool IsSuccessTone => Tone == Tone.Success;
    public bool IsWarningTone => Tone == Tone.Warning;
    public bool IsDangerTone => Tone == Tone.Danger;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isDimmed;

    /// <summary>Con un filtro, la categoría muestra sus coincidencias aunque esté cerrada.</summary>
    [ObservableProperty] private bool _filterOpen;

    /// <summary>Carpeta armada por el usuario (no la de un ambiente): se puede renombrar y arrastrar.</summary>
    public bool IsCustomFolder { get; init; }

    /// <summary>Ruta completa de una carpeta propia («Clinica/Producción»); null en los grupos de ambiente.</summary>
    public string? FolderPath { get; init; }

    /// <summary>Ambiente de un grupo (las bases sin carpeta propia); null en las carpetas propias.</summary>
    public ProfileEnvironment? GroupEnvironment { get; init; }

    /// <summary>Cantidad de objetos de una categoría (para "1 de 479" cuando hay filtro).</summary>
    public int Total { get; init; }

    public List<TreeNode>? Children { get; set; }

    public bool IsExpandable => Kind is TreeNodeKind.Folder or TreeNodeKind.Database or TreeNodeKind.Category or TreeNodeKind.Areas or TreeNodeKind.Area
        || (Kind == TreeNodeKind.Object && Object is { } o && DbObjectKinds.IsRelation(o.Kind));

    public string Glyph => !IsExpandable ? "" : IsExpanded || FilterOpen ? "▾" : "▸";

    public double Indent => Level * 16;

    /// <summary>Versión del motor y datos de la base, una vez conectada (se agrega al tooltip).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _serverSummary;

    /// <summary>Un aviso corto al lado de la base («⚠ modo backup»); el largo va en el tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlert), nameof(Tooltip))]
    private string? _alert;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    private string? _alertDetail;

    public bool HasAlert => Alert is not null;

    public string? Tooltip => Column?.Description
        ?? (Kind == TreeNodeKind.Database ? string.Join(Environment.NewLine, new[] { Profile?.Display, ServerSummary, AlertDetail }.Where(s => s is not null)) : null);

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    partial void OnFilterOpenChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    /// <summary>Base a la que pertenece el nodo (para saber dónde ejecutar o qué conexión usar).</summary>
    public TreeNode? DatabaseNode
    {
        get
        {
            for (var n = this; n is not null; n = n.Parent) if (n.Kind == TreeNodeKind.Database) return n;
            return null;
        }
    }
}

/// <summary>
/// Árbol de bases registradas, mostrado como lista plana virtualizada (cada fila con su sangría):
/// así abrir "Procedimientos (3.149)" no crea miles de controles de árbol.
/// </summary>
public sealed partial class DatabaseTreeViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _profiles;
    private readonly Dictionary<Guid, MetadataExplorer> _explorers = [];
    private List<TreeNode> _roots = [];
    private readonly HashSet<TreeNode> _filterTried = [];
    private bool _loadingForFilter;

    public DatabaseTreeViewModel(ProfileStore profiles)
    {
        _profiles = profiles;
        Reload();
    }

    /// <summary>Filas visibles (árbol aplanado).</summary>
    [ObservableProperty] private IReadOnlyList<TreeNode> _rows = [];
    [ObservableProperty] private TreeNode? _selectedNode;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _status = "";

    public bool IsEmpty => _roots.Count == 0;

    /// <summary>El usuario pidió trabajar con esta base (doble clic, Conectar, Nueva consulta).</summary>
    public event Action<ConnectionProfile>? DatabaseActivated;

    /// <summary>Insertar un nombre en el editor (doble clic en objeto o columna).</summary>
    public event Action<string>? InsertRequested;

    /// <summary>Poner esta consulta en el editor y ejecutarla.</summary>
    public event Action<ConnectionProfile, string, string>? QueryRequested;

    /// <summary>Abrir un objeto (su DDL) en una pestaña.</summary>
    public event Action<ConnectionProfile, DbObject>? ObjectRequested;

    public bool IsConnected(Guid profileId) => _explorers.ContainsKey(profileId);

    /// <summary>Las áreas de una base (F-9); sin esto, el árbol no las muestra.</summary>
    public Func<ConnectionProfile, IReadOnlyList<SchemaArea>>? AreasFor { get; set; }

    /// <summary>Se guardaron áreas: se rearma su nodo en las bases conectadas (y se vuelve a abrir si estaba abierto).</summary>
    public void RefreshAreas()
    {
        foreach (var db in Flatten(_roots, all: true).Where(n => n.Kind == TreeNodeKind.Database && n.Children is not null).ToList())
        {
            var index = db.Children!.FindIndex(n => n.Kind == TreeNodeKind.Areas);
            var wasOpen = index >= 0 && db.Children[index].IsExpanded;
            if (index >= 0) db.Children.RemoveAt(index);
            if (AreasFor?.Invoke(db.Profile!) is not { Count: > 0 } areas) continue;
            var node = AreasNode(db, areas.Count);
            db.Children.Add(node);
            if (wasOpen) _ = ToggleAsync(node);
        }
        Refresh();
    }

    /// <summary>Rearma las carpetas y bases (después de registrar, editar o quitar). Conserva las conexiones abiertas.</summary>
    public void Reload()
    {
        var profiles = _profiles.Load();
        var expanded = Flatten(_roots, all: true).Where(n => n.IsExpanded).Select(Key).ToHashSet();
        // Sin carpeta propia, cada base va al grupo de su ambiente (Producción arriba); las carpetas propias van
        // después, por nombre, y pueden tener otras adentro («Clinica/Producción»).
        var groups = profiles
            .Where(p => ProfileFolders.Clean(p.Folder) is null)
            .GroupBy(p => p.Environment)
            .OrderBy(g => FolderOrder(EnvironmentLabel(g.Key)))
            .Select(g =>
            {
                var folder = new TreeNode
                {
                    Kind = TreeNodeKind.Folder, Level = 0, Text = EnvironmentLabel(g.Key), GroupEnvironment = g.Key, IsExpanded = true,
                    Icon = MaterialIconKind.FolderOutline,
                };
                folder.Children = g.OrderBy(p => p.Name).Select(p => DatabaseNode(p, folder)).ToList();
                return folder;
            });
        _roots = [.. groups, .. CustomFolders(profiles, null, 0)];
        foreach (var n in Flatten(_roots, all: true)) if (expanded.Contains(Key(n)) && n.Children is not null) n.IsExpanded = true;
        foreach (var id in _explorers.Keys.Where(id => profiles.All(p => p.Id != id)).ToList())
        {
            _ = _explorers[id].DisposeAsync();
            _explorers.Remove(id);
        }
        OnPropertyChanged(nameof(IsEmpty));
        Refresh();
    }

    public void MarkActive(Guid? profileId)
    {
        foreach (var db in Flatten(_roots, all: true).Where(n => n.Kind == TreeNodeKind.Database))
            db.IsActive = db.Profile!.Id == profileId;
    }

    /// <summary>Abre o cierra un nodo; al abrir una base, se conecta; al abrir una categoría u objeto, carga sus hijos.</summary>
    public async Task ToggleAsync(TreeNode node)
    {
        if (!node.IsExpandable) return;
        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            Refresh();
            return;
        }
        if (node.Children is null && !await LoadChildrenAsync(node)) return;
        node.IsExpanded = true;
        Refresh();
    }

    /// <summary>Doble clic: en una base conecta, la abre y la activa; en un objeto abre su DDL; en una columna inserta el nombre.</summary>
    public async Task ActivateAsync(TreeNode node)
    {
        switch (node.Kind)
        {
            case TreeNodeKind.Database:
                if (!node.IsExpanded) await ToggleAsync(node);
                if (IsConnected(node.Profile!.Id)) DatabaseActivated?.Invoke(node.Profile);
                break;
            case TreeNodeKind.Object when node.Object is { } o && node.DatabaseNode?.Profile is { } p:
                ObjectRequested?.Invoke(p, o);
                break;
            case TreeNodeKind.Column when node.Column is { } c:
                InsertRequested?.Invoke(QuoteIdentifier(c.Name));
                break;
            default:
                await ToggleAsync(node);
                break;
        }
    }

    public async Task ConnectAsync(TreeNode db)
    {
        if (db.Kind != TreeNodeKind.Database) return;
        if (!db.IsExpanded) await ToggleAsync(db);
        if (IsConnected(db.Profile!.Id)) DatabaseActivated?.Invoke(db.Profile);
    }

    /// <summary>Lo que se leyó del servidor al conectar la base (null si no está conectada o no se pudo leer).</summary>
    public ServerInfo? ServerFor(Guid profileId) => _explorers.TryGetValue(profileId, out var explorer) ? explorer.Server : null;

    /// <summary>Se leyeron los datos del servidor de una base (versión, tipo, cifrado de la conexión).</summary>
    public event Action<Guid>? ServerInfoLoaded;

    public Task DisconnectAsync(Guid profileId) =>
        Flatten(_roots, all: true).FirstOrDefault(n => n.Kind == TreeNodeKind.Database && n.Profile!.Id == profileId) is { } db
            ? DisconnectAsync(db)
            : Task.CompletedTask;

    public async Task DisconnectAsync(TreeNode db)
    {
        if (db.Kind != TreeNodeKind.Database || !_explorers.Remove(db.Profile!.Id, out var explorer)) return;
        await explorer.DisposeAsync();
        db.Children = null;
        db.IsExpanded = false;
        db.Detail = null;
        db.ServerSummary = null;
        db.Alert = null;
        db.AlertDetail = null;
        SetDatabaseIcon(db);
        Refresh();
    }

    public async Task RefreshDatabaseAsync(TreeNode db)
    {
        if (db.Kind != TreeNodeKind.Database || !IsConnected(db.Profile!.Id)) return;
        db.Children = null;
        _explorers[db.Profile.Id].ForgetServerInfo();
        await LoadChildrenAsync(db);
        db.IsExpanded = true;
        Refresh();
    }

    public void RequestSelectFirst100(TreeNode node)
    {
        if (node is { Kind: TreeNodeKind.Object, Object: { } o } && DbObjectKinds.IsRelation(o.Kind) && node.DatabaseNode?.Profile is { } p)
            QueryRequested?.Invoke(p, o.Name, $"SELECT FIRST 100 *\nFROM {QuoteIdentifier(o.Name)}");
    }

    public void RequestNewQuery(TreeNode node)
    {
        if (node.DatabaseNode?.Profile is { } p) DatabaseActivated?.Invoke(p);
    }

    /// <summary>Catálogo de columnas ya cargadas (para el autocompletado no hace falta esperar).</summary>
    public MetadataExplorer? ExplorerFor(Guid profileId) => _explorers.GetValueOrDefault(profileId);

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasFilter));
        Refresh();
    }

    public bool HasFilter => Filter.Trim().Length > 0;

    [RelayCommand]
    private void ClearFilter() => Filter = "";

    /// <summary>Las carpetas propias de un nivel (las de la raíz con <paramref name="parent"/> null): primero las carpetas y después las bases.</summary>
    private List<TreeNode> CustomFolders(IReadOnlyList<ConnectionProfile> profiles, TreeNode? parent, int level)
    {
        var names = ProfileFolders.All(profiles)
            .Where(path => string.Equals(ProfileFolders.Parent(path), parent?.FolderPath, StringComparison.CurrentCultureIgnoreCase)
                           || (parent is null && ProfileFolders.Parent(path) is null))
            .ToList();
        return names.Select(path =>
        {
            var folder = new TreeNode
            {
                Kind = TreeNodeKind.Folder, Level = level, Parent = parent, Text = ProfileFolders.Name(path), FolderPath = path,
                IsCustomFolder = true, IsExpanded = true, Icon = MaterialIconKind.Folder,
            };
            folder.Children =
            [
                .. CustomFolders(profiles, folder, level + 1),
                .. profiles.Where(p => ProfileFolders.Same(p.Folder, path)).OrderBy(p => p.Name).Select(p => DatabaseNode(p, folder)),
            ];
            return folder;
        }).ToList();
    }

    private TreeNode DatabaseNode(ConnectionProfile p, TreeNode folder)
    {
        var node = new TreeNode { Kind = TreeNodeKind.Database, Level = folder.Level + 1, Parent = folder, Profile = p, Text = p.Name };
        SetDatabaseIcon(node);
        if (_explorers.TryGetValue(p.Id, out var explorer)) ShowServer(node, explorer.Server);
        return node;
    }

    /// <summary>«conectada · Firebird 3.0.11 · Classic» en el árbol; el sistema, ODS, dialecto, página y charset en el tooltip.</summary>
    private static void ShowServer(TreeNode db, ServerInfo? server)
    {
        db.Detail = server is null ? Strings.TreeConnected : $"{Strings.TreeConnected} · {server.ShortDisplay}";
        db.ServerSummary = server is null ? null : string.Join(Environment.NewLine, new[] { server.Summary, server.WireText }.Where(s => s is not null));
        // La base que quedó en modo backup de nbackup se marca (F-29): el delta crece mientras dure.
        db.Alert = server is null ? null : BackupStates.Short(server.BackupState);
        db.AlertDetail = server is null ? null : BackupStates.Warning(server.BackupState);
    }

    private void SetDatabaseIcon(TreeNode db)
    {
        var connected = IsConnected(db.Profile!.Id);
        db.Icon = connected ? MaterialIconKind.DatabaseCheck : MaterialIconKind.DatabaseOutline;
        db.Tone = Tones.ForEnvironment(db.Profile.Environment);
        db.IsDimmed = !connected;
    }

    private async Task<bool> LoadChildrenAsync(TreeNode node)
    {
        node.IsBusy = true;
        Status = Strings.Loading;
        try
        {
            switch (node.Kind)
            {
                case TreeNodeKind.Folder:
                    return true;
                case TreeNodeKind.Database:
                {
                    var profile = node.Profile!;
                    if (!_explorers.TryGetValue(profile.Id, out var explorer))
                    {
                        var password = _profiles.GetPassword(profile.Id);
                        if (password is null)
                        {
                            Status = string.Format(Strings.TreeNoSavedPassword, profile.Name);
                            return false;
                        }
                        explorer = new MetadataExplorer(profile, password);
                        try
                        {
                            var counts = await explorer.CountAsync();
                            _explorers[profile.Id] = explorer;
                            node.Children = BuildCategories(node, counts);
                        }
                        catch
                        {
                            await explorer.DisposeAsync();
                            throw;
                        }
                    }
                    else
                    {
                        node.Children = BuildCategories(node, await explorer.CountAsync());
                    }
                    // Las áreas van al final, si la base tiene; el archivo puede estar en una carpeta de red.
                    if (AreasFor is { } areasFor && await Task.Run(() => areasFor(profile)) is { Count: > 0 } areas)
                        node.Children.Add(AreasNode(node, areas.Count));
                    ServerInfo? server = null;
                    try { server = await explorer.ServerInfoAsync(); }
                    catch (Exception) { /* la versión es informativa: si no se puede leer, la base igual queda conectada */ }
                    ShowServer(node, server);
                    SetDatabaseIcon(node);
                    Status = string.Format(Strings.TreeConnectedTo, profile.Name) + (server?.Warning is { } warning ? " " + warning : "");
                    ServerInfoLoaded?.Invoke(profile.Id);
                    return true;
                }
                case TreeNodeKind.Category:
                {
                    var explorer = _explorers[node.DatabaseNode!.Profile!.Id];
                    var objects = await explorer.LoadObjectsAsync(node.Category!.Value);
                    node.Children = objects.Select(o => ObjectNode(node, o)).ToList();
                    Status = $"{objects.Count:N0} {DbObjectKinds.Label(node.Category.Value).ToLowerInvariant()}";
                    return true;
                }
                case TreeNodeKind.Areas:
                {
                    var profile = node.DatabaseNode!.Profile!;
                    var areas = AreasFor?.Invoke(profile) ?? [];
                    var explorer = _explorers[profile.Id];
                    var objects = await explorer.LoadAllObjectsAsync();
                    var uses = areas.Any(a => a.IncludeDependents) ? await explorer.LoadTableUsesAsync() : null;
                    var membership = new AreaMembership(areas, objects, uses);
                    node.Children =
                    [
                        .. areas.Select((a, i) => AreaNode(node, a.Name, membership.Members(i), MaterialIconKind.Group)),
                        AreaNode(node, Strings.AreasUnassigned, membership.Unassigned, MaterialIconKind.HelpCircleOutline),
                    ];
                    Status = string.Format(Strings.AreasLoaded, areas.Count, objects.Count);
                    return true;
                }
                case TreeNodeKind.Object when node.Object is { } o:
                {
                    var explorer = _explorers[node.DatabaseNode!.Profile!.Id];
                    var columns = await explorer.LoadColumnsAsync(o.Name);
                    node.Children = columns.Select(c => new TreeNode
                    {
                        Kind = TreeNodeKind.Column, Level = node.Level + 1, Parent = node, Column = c,
                        Text = c.Name, Detail = (c.Type + " " + c.Flags).Trim(),
                        Icon = c.IsPrimaryKey ? MaterialIconKind.KeyVariant : MaterialIconKind.TableColumn,
                        Tone = c.IsPrimaryKey ? Tone.Warning : Tone.Muted,
                    }).ToList();
                    Status = string.Format(Strings.TreeColumnCount, o.Name, columns.Count);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.TreeConnectFailed, ex.Message.Split('\n')[0]);
            return false;
        }
        finally
        {
            node.IsBusy = false;
        }
    }

    private static List<TreeNode> BuildCategories(TreeNode db, IReadOnlyDictionary<DbObjectKind, int> counts) =>
        DbObjectKinds.All
            .Where(k => counts.GetValueOrDefault(k) > 0 || k is DbObjectKind.Table or DbObjectKind.View or DbObjectKind.Procedure)
            .Select(k => new TreeNode
            {
                Kind = TreeNodeKind.Category, Level = db.Level + 1, Parent = db, Category = k,
                Text = DbObjectKinds.Label(k), Detail = counts.GetValueOrDefault(k).ToString("N0"), Icon = IconFor(k),
                Total = counts.GetValueOrDefault(k),
            }).ToList();

    private static TreeNode ObjectNode(TreeNode parent, DbObject o) => new()
    {
        Kind = TreeNodeKind.Object, Level = parent.Level + 1, Parent = parent, Object = o,
        Text = o.Name, Detail = o.Detail, Icon = IconFor(o.Kind), IsDimmed = o.Inactive,
    };

    private static TreeNode AreasNode(TreeNode db, int count) => new()
    {
        Kind = TreeNodeKind.Areas, Level = db.Level + 1, Parent = db, Text = Strings.AreasTitle, Detail = count.ToString("N0"),
        Icon = MaterialIconKind.Group,
    };

    /// <summary>Un área con sus objetos por tipo, ya cargados (un tipo sin objetos en el área no aparece).</summary>
    private static TreeNode AreaNode(TreeNode parent, string name, IReadOnlyList<DbObject> members, MaterialIconKind icon)
    {
        var area = new TreeNode
        {
            Kind = TreeNodeKind.Area, Level = parent.Level + 1, Parent = parent, Text = name, Detail = members.Count.ToString("N0"),
            Icon = icon, Total = members.Count,
        };
        area.Children = members.GroupBy(o => o.Kind).OrderBy(g => g.Key).Select(g =>
        {
            var category = new TreeNode
            {
                Kind = TreeNodeKind.Category, Level = area.Level + 1, Parent = area, Category = g.Key,
                Text = DbObjectKinds.Label(g.Key), Detail = g.Count().ToString("N0"), Icon = IconFor(g.Key), Total = g.Count(),
            };
            category.Children = g.Select(o => ObjectNode(category, o)).ToList();
            return category;
        }).ToList();
        return area;
    }

    public static MaterialIconKind IconFor(DbObjectKind kind) => kind switch
    {
        DbObjectKind.Table => MaterialIconKind.TableLarge,
        DbObjectKind.View => MaterialIconKind.TableEye,
        DbObjectKind.Procedure => MaterialIconKind.CodeBraces,
        DbObjectKind.Function => MaterialIconKind.FunctionVariant,
        DbObjectKind.Package => MaterialIconKind.PackageVariant,
        DbObjectKind.Trigger => MaterialIconKind.LightningBoltOutline,
        DbObjectKind.Generator => MaterialIconKind.Numeric,
        DbObjectKind.Exception => MaterialIconKind.AlertCircleOutline,
        DbObjectKind.Domain => MaterialIconKind.ShapeOutline,
        DbObjectKind.Index => MaterialIconKind.SortAscending,
        DbObjectKind.Role => MaterialIconKind.ShieldAccountOutline,
        _ => MaterialIconKind.CircleSmall,
    };

    /// <summary>Recalcula las filas visibles aplicando el filtro a los objetos de las categorías abiertas.</summary>
    private void Refresh()
    {
        var f = Filter.Trim();
        Func<TreeNode, bool> match = _ => true;
        if (f.Length > 0)
        {
            if (f.Contains('*') || f.Contains('%'))
            {
                var regex = new Regex("^" + Regex.Escape(f).Replace(@"\*", ".*").Replace("%", ".*") + "$", RegexOptions.IgnoreCase);
                match = n => regex.IsMatch(n.Text);
            }
            else
            {
                match = n => n.Text.Contains(f, StringComparison.OrdinalIgnoreCase);
            }
        }
        var rows = new List<TreeNode>();
        var matches = 0;
        var pending = false;
        void Walk(IEnumerable<TreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Kind == TreeNodeKind.Object && f.Length > 0 && !match(n)) continue;
                if (n.Kind == TreeNodeKind.Object && f.Length > 0) matches++;
                rows.Add(n);
                if (n.Kind == TreeNodeKind.Category)
                {
                    // Con filtro, cada categoría dice cuántos coinciden y se abre sola si hay alguno.
                    if (f.Length == 0)
                    {
                        n.Detail = n.Total.ToString("N0");
                        n.IsDimmed = false;
                        n.FilterOpen = false;
                    }
                    else if (n.Children is null)
                    {
                        pending |= n.Total > 0 && !_filterTried.Contains(n);
                        n.Detail = n.Total > 0 && !_filterTried.Contains(n) ? string.Format(Strings.TreeFilterPending, n.Total) : string.Format(Strings.TreeFilterCount, 0, n.Total);
                        n.IsDimmed = true;
                        n.FilterOpen = false;
                        continue;
                    }
                    else
                    {
                        var found = n.Children.Count(match);
                        n.Detail = string.Format(Strings.TreeFilterCount, found, n.Total);
                        n.IsDimmed = found == 0;
                        n.FilterOpen = found > 0 && !n.IsExpanded;
                        if (found > 0) Walk(n.Children);
                        continue;
                    }
                }
                if (n.IsExpanded && n.Children is not null) Walk(n.Children);
            }
        }
        Walk(_roots);
        Rows = rows;
        if (f.Length > 0)
        {
            Status = pending ? string.Format(Strings.TreeFilterSearching, f) : string.Format(Strings.TreeFilterMatches, matches, f);
            if (pending) _ = LoadCategoriesForFilterAsync();
        }
    }

    /// <summary>Con un filtro, carga las categorías que todavía no se abrieron en las bases conectadas (solo los nombres).</summary>
    private async Task LoadCategoriesForFilterAsync()
    {
        if (_loadingForFilter) return;
        _loadingForFilter = true;
        try
        {
            var categories = Flatten(_roots, all: true)
                .Where(n => n.Kind == TreeNodeKind.Category && n.Children is null && n.Total > 0 && !_filterTried.Contains(n))
                .ToList();
            foreach (var category in categories)
            {
                if (!HasFilter) break;
                _filterTried.Add(category);
                await LoadChildrenAsync(category);
            }
        }
        finally
        {
            _loadingForFilter = false;
        }
        Refresh();
    }

    private static IEnumerable<TreeNode> Flatten(IEnumerable<TreeNode> nodes, bool all)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if ((all || n.IsExpanded) && n.Children is not null)
                foreach (var c in Flatten(n.Children, all)) yield return c;
        }
    }

    private static string Key(TreeNode n) => n.Kind switch
    {
        TreeNodeKind.Folder => "f:" + (n.FolderPath ?? n.Text),
        TreeNodeKind.Database => "d:" + n.Profile!.Id,
        _ => n.Kind + ":" + n.DatabaseNode?.Profile?.Id + ":" + n.Text,
    };

    private static string EnvironmentLabel(ProfileEnvironment e) => ProfileEnvironments.Label(e);

    /// <summary>Producción arriba, después Prueba y Desarrollo; las carpetas propias al final.</summary>
    private static int FolderOrder(string label) =>
        label == EnvironmentLabel(ProfileEnvironment.Produccion) ? 0
        : label == EnvironmentLabel(ProfileEnvironment.Prueba) ? 1
        : label == EnvironmentLabel(ProfileEnvironment.Desarrollo) ? 2
        : 3;

    public static string QuoteIdentifier(string name) => FbStudio.Core.Sql.SqlIdentifier.Quote(name);

    public async ValueTask DisposeAsync()
    {
        foreach (var e in _explorers.Values) await e.DisposeAsync();
        _explorers.Clear();
    }
}
