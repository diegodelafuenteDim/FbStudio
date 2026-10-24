using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Pestaña «Áreas» de una base (F-9): grupos de objetos con nombre, armados con reglas (nombres o patrones) y
/// excepciones, con la vista previa al escribir. Se guardan en un archivo por base que se puede compartir: el propio de
/// esta PC o uno elegido (p. ej. en una carpeta de red). Nada se guarda hasta «Guardar».
/// </summary>
public sealed partial class AreasTabViewModel : EditorTabViewModel
{
    public const int SuggestMinTables = 3;

    private IReadOnlyList<DbObject> _objects = [];
    private TableUses _uses = TableUses.None;
    private AreaMembership? _membership;
    private string? _path;
    private DateTime? _stamp;
    private bool _filling;

    public AreasTabViewModel(MainWindowViewModel owner, ConnectionProfile profile) : base(owner, profile, Strings.AreasTitle)
    {
        KindChoices = [Strings.AreasAllKinds, .. DbObjectKinds.All.Select(DbObjectKinds.Label)];
    }

    public override MaterialIconKind Icon => MaterialIconKind.Group;

    /// <summary>Las áreas, y al final «Sin área» (lo que no está en ninguna).</summary>
    public ObservableCollection<AreaItem> Areas { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditableArea), nameof(HasSelection), nameof(RemoveFromCaption))]
    [NotifyCanExecuteChangedFor(nameof(DeleteAreaCommand), nameof(OpenDiagramCommand))]
    private AreaItem? _selectedArea;

    public bool HasSelection => SelectedArea is not null;
    public bool HasEditableArea => SelectedArea is { IsUnassigned: false };

    [ObservableProperty] private string _areaName = "";
    [ObservableProperty] private string _areaDescription = "";
    [ObservableProperty] private string _rulesText = "";
    [ObservableProperty] private string _excludeText = "";
    [ObservableProperty] private bool _includeDependents;

    public IReadOnlyList<string> KindChoices { get; }
    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private string _previewFilter = "";
    [ObservableProperty] private IReadOnlyList<AreaObjectRow> _preview = [];
    [ObservableProperty] private string _previewSummary = "";

    /// <summary>«Archivo de esta PC:» o «Archivo compartido:», y dónde está (la vista recorta la ruta por delante).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileText))]
    private string _fileLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileText))]
    private string _fileLocation = "";

    public string FileText => (FileLabel + " " + FileLocation).Trim();

    /// <summary>La base usa un archivo elegido (no el propio de esta PC).</summary>
    public bool IsSharedFile => !string.IsNullOrWhiteSpace(Profile?.AreasFile);

    /// <summary>Las áreas que se pueden elegir para «Agregar a» (todas menos la que se está viendo).</summary>
    public IReadOnlyList<AreaItem> AddTargets => Areas.Where(a => !a.IsUnassigned && a != SelectedArea).ToList();

    public string RemoveFromCaption => SelectedArea is { IsUnassigned: false } a ? string.Format(Strings.AreasRemoveFrom, a.Name) : "";

    /// <summary>La carga que empezó al abrir la pestaña.</summary>
    public Task Loading { get; set; } = Task.CompletedTask;

    /// <summary>Lee el archivo de áreas y los objetos de la base.</summary>
    public async Task LoadAsync()
    {
        LoadFile();
        if (Profile is not { } profile) return;
        Status = Strings.Loading;
        try
        {
            _objects = await Owner.WithExplorerAsync(profile, explorer => explorer.LoadAllObjectsAsync());
            _uses = await Owner.WithExplorerAsync(profile, explorer => explorer.LoadTableUsesAsync());
            Recount();
            Status = string.Format(Strings.AreasLoaded, Areas.Count(a => !a.IsUnassigned), _objects.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.AreasObjectsFailed, ex.Message.Split('\n')[0]);
        }
    }

    private void LoadFile()
    {
        if (Profile is not { } profile) return;
        var load = Owner.AreaStore.Load(profile);
        _path = load.Path;
        _stamp = load.Stamp;
        var previous = SelectedArea?.Name;
        Areas.Clear();
        foreach (var area in load.File.Areas) Areas.Add(new AreaItem(area));
        Areas.Add(AreaItem.Unassigned());
        IsModified = false;
        ShowFile();
        Recount();
        SelectedArea = Areas.FirstOrDefault(a => a.Name == previous && !a.IsUnassigned) ?? Areas.FirstOrDefault();
        if (load.Error is { } error) Status = string.Format(Strings.AreasLoadFailed, error);
    }

    private void ShowFile()
    {
        FileLabel = _path is null ? Strings.AreasFileMemory : IsSharedFile ? Strings.AreasFileShared : Strings.AreasFileOwn;
        FileLocation = _path ?? "";
    }

    partial void OnSelectedAreaChanged(AreaItem? value)
    {
        _filling = true;
        AreaName = value is { IsUnassigned: false } ? value.Area.Name : "";
        AreaDescription = value?.Area.Description ?? "";
        RulesText = value is { IsUnassigned: false } ? SchemaAreas.FormatRules(value.Area.Rules) : "";
        ExcludeText = value is { IsUnassigned: false } ? SchemaAreas.FormatRules(value.Area.Exclude) : "";
        IncludeDependents = value is { IsUnassigned: false } && value.Area.IncludeDependents;
        _filling = false;
        OnPropertyChanged(nameof(AddTargets));
        RebuildPreview();
    }

    partial void OnAreaNameChanged(string value) => Edit(a => a with { Name = value.Trim() });
    partial void OnAreaDescriptionChanged(string value) => Edit(a => a with { Description = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    partial void OnRulesTextChanged(string value) => Edit(a => a with { Rules = SchemaAreas.ParseRules(value) });
    partial void OnExcludeTextChanged(string value) => Edit(a => a with { Exclude = SchemaAreas.ParseRules(value) });
    partial void OnIncludeDependentsChanged(bool value) => Edit(a => a with { IncludeDependents = value });
    partial void OnKindIndexChanged(int value) => RebuildPreview();
    partial void OnPreviewFilterChanged(string value) => RebuildPreview();

    /// <summary>Un cambio en el área elegida: se recuentan todas (cambia también «Sin área») y se rearma la vista previa.</summary>
    private void Edit(Func<SchemaArea, SchemaArea> change)
    {
        if (_filling || SelectedArea is not { IsUnassigned: false } item) return;
        item.Area = change(item.Area);
        IsModified = true;
        Recount();
    }

    /// <summary>Vuelve a calcular quién está en cada área (las reglas de todas y lo que usa solo sus tablas).</summary>
    private void Recount()
    {
        _membership = _objects.Count == 0 ? null : new AreaMembership(CurrentAreas(), _objects, _uses, _membership);
        var index = 0;
        foreach (var item in Areas)
            item.Count = _membership is null ? null : item.IsUnassigned ? _membership.Unassigned.Count : _membership.Count(index++);
        RebuildPreview();
    }

    private IReadOnlyList<SchemaArea> CurrentAreas() => Areas.Where(a => !a.IsUnassigned).Select(a => a.Area).ToList();

    /// <summary>La posición del área entre las áreas (sin «Sin área»), como las cuenta <see cref="AreaMembership"/>.</summary>
    private int IndexOf(AreaItem item) => Areas.Where(a => !a.IsUnassigned).ToList().IndexOf(item);

    private void RebuildPreview()
    {
        if (SelectedArea is not { } item || _membership is not { } membership)
        {
            Preview = [];
            PreviewSummary = "";
            return;
        }
        var index = IndexOf(item);
        var members = item.IsUnassigned ? membership.Unassigned : membership.Members(index);
        var byUse = item.IsUnassigned ? 0 : members.Count(o => membership.IsByUse(index, o));
        PreviewSummary = Summary(members) + (byUse > 0 ? " (" + string.Format(Strings.AreasByUseCount, byUse) + ")" : "");
        var filter = PreviewFilter.Trim();
        Preview = members
            .Where(o => KindIndex <= 0 || o.Kind == DbObjectKinds.All[KindIndex - 1])
            .Where(o => filter.Length == 0 || (filter.Contains('*') || filter.Contains('?') || filter.Contains('%')
                ? SchemaAreas.Matches(filter, o.Name)
                : o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => new AreaObjectRow(o, item.IsUnassigned ? [] : membership.AreasOf(o).Where(n => n != item.Area.Name).ToList(),
                !item.IsUnassigned && membership.IsByUse(index, o)))
            .ToList();
    }

    /// <summary>«35 objetos: 12 tablas · 3 vistas · 20 procedimientos».</summary>
    private static string Summary(IReadOnlyList<DbObject> members)
    {
        var kinds = members.GroupBy(o => o.Kind).OrderBy(g => g.Key)
            .Select(g => $"{g.Count():N0} {(g.Count() == 1 ? DbObjectKinds.Singular(g.Key) : DbObjectKinds.Label(g.Key)).ToLowerInvariant()}");
        return string.Format(Strings.AreasObjectCount, members.Count) + (members.Count > 0 ? ": " + string.Join(" · ", kinds) : "");
    }

    [RelayCommand]
    private void NewArea()
    {
        var name = Strings.AreasNewName;
        for (var i = 2; Areas.Any(a => !a.IsUnassigned && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
            name = string.Format(Strings.AreasNewNameN, i);
        var item = new AreaItem(new SchemaArea { Name = name, IncludeDependents = true });
        Areas.Insert(Areas.Count - 1, item);
        IsModified = true;
        Recount();
        SelectedArea = item;
        NameRequested?.Invoke();
    }

    /// <summary>Se creó un área: la vista pone el cursor en el nombre.</summary>
    public event Action? NameRequested;

    private bool CanDeleteArea() => SelectedArea is { IsUnassigned: false };

    [RelayCommand(CanExecute = nameof(CanDeleteArea))]
    private void DeleteArea()
    {
        if (SelectedArea is not { IsUnassigned: false } item) return;
        var index = Areas.IndexOf(item);
        Areas.Remove(item);
        IsModified = true;
        Recount();
        SelectedArea = Areas[Math.Min(index, Areas.Count - 1)];
    }

    /// <summary>
    /// Un área por cada familia de prefijos que junta varias tablas sin área («CLI»: CLI_* y CLIENTES_*), sumando lo que
    /// usa solo sus tablas: quedan para revisar antes de guardar.
    /// </summary>
    [RelayCommand]
    private void Suggest()
    {
        var tables = _objects.Where(o => o.Kind == DbObjectKind.Table).Select(o => o.Name);
        var suggestions = SchemaAreas.SuggestByPrefix(tables, CurrentAreas(), SuggestMinTables);
        if (suggestions.Count == 0)
        {
            Status = string.Format(Strings.AreasNoSuggestions, SuggestMinTables);
            return;
        }
        AreaItem? first = null;
        foreach (var s in suggestions)
        {
            var item = new AreaItem(new SchemaArea
            {
                Name = s.Name, Rules = s.Rules, IncludeDependents = true,
                Description = string.Format(Strings.AreasSuggestedDescription, s.Count, s.Prefixes),
            });
            Areas.Insert(Areas.Count - 1, item);
            first ??= item;
        }
        IsModified = true;
        Recount();
        SelectedArea = first;
        Status = string.Format(Strings.AreasSuggested, suggestions.Count);
    }

    /// <summary>Agrega los objetos a un área (por su nombre; si una excepción los dejaba afuera, se saca).</summary>
    public void AddTo(AreaItem target, IEnumerable<AreaObjectRow> rows)
    {
        if (target.IsUnassigned) return;
        var added = rows.ToList();
        foreach (var row in added) target.Area = SchemaAreas.Add(target.Area, row.Object.Name);
        AfterManualChange();
        var outside = _membership is { } m ? added.Where(r => !m.Contains(IndexOf(target), r.Object)).Select(r => r.Object.Name).ToList() : [];
        if (outside.Count > 0) Status = string.Format(Strings.AreasStillExcluded, string.Join(", ", outside), target.Name);
    }

    /// <summary>Saca los objetos del área elegida (quita su regla o agrega una excepción, también a lo que entró por lo que usa).</summary>
    public void RemoveFromSelected(IEnumerable<AreaObjectRow> rows)
    {
        if (SelectedArea is not { IsUnassigned: false } item) return;
        var removed = rows.ToList();
        foreach (var row in removed) item.Area = SchemaAreas.Remove(item.Area, row.Object.Name, row.ByUse);
        // Lo que estaba por su nombre pero también usa solo tablas del área volvería a entrar: se excluye.
        var after = new AreaMembership(CurrentAreas(), _objects, _uses, _membership);
        var back = removed.Where(r => after.Contains(IndexOf(item), r.Object)).Select(r => r.Object.Name).ToList();
        if (back.Count > 0) item.Area = item.Area with { Exclude = [.. item.Area.Exclude, .. back] };
        AfterManualChange();
    }

    private void AfterManualChange()
    {
        IsModified = true;
        if (SelectedArea is { IsUnassigned: false } item)
        {
            _filling = true;
            RulesText = SchemaAreas.FormatRules(item.Area.Rules);
            ExcludeText = SchemaAreas.FormatRules(item.Area.Exclude);
            _filling = false;
        }
        Recount();
    }

    private bool CanOpenDiagram() => SelectedArea is { IsUnassigned: false };

    /// <summary>El diagrama de las tablas del área tal como está escrita ahora (aunque no se haya guardado).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDiagram))]
    private void OpenDiagram()
    {
        if (SelectedArea is { IsUnassigned: false } item && Profile is { } profile) Owner.OpenDiagram(profile, new DiagramSource(item.Area.Name, item.Area));
    }

    public Task OpenObjectAsync(AreaObjectRow row) =>
        Profile is { } profile ? Owner.OpenObjectAsync(profile, row.Object) : Task.CompletedTask;

    [RelayCommand]
    public async Task<bool> SaveAsync() => await SaveToAsync(_path, force: false);

    private async Task<bool> SaveToAsync(string? path, bool force)
    {
        if (Profile is not { } profile) return false;
        var areas = CurrentAreas();
        if (areas.FirstOrDefault(a => a.Name.Length == 0) is not null)
        {
            Status = Strings.AreasEmptyName;
            return false;
        }
        if (areas.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            Status = string.Format(Strings.AreasDuplicateName, repeated.Key);
            return false;
        }
        try
        {
            var file = new SchemaAreaFile { Areas = areas };
            var result = Owner.AreaStore.Save(path, profile.Id, file, _stamp, force);
            if (result.Conflict)
            {
                if (!await Owner.ConfirmActionAsync(new ConfirmPrompt([Strings.AreasConflict], Strings.AreasConflictReplace, Strings.Cancel, Strings.AreasConflictTitle)))
                    return false;
                result = Owner.AreaStore.Save(path, profile.Id, file, _stamp, force: true);
            }
            _path = path;
            _stamp = result.Stamp;
            IsModified = false;
            Status = string.Format(Strings.AreasSaved, areas.Count,
                path is null ? Strings.AreasFileMemory : path == Owner.AreaStore.DefaultPath(profile.Id) ? Strings.AreasFileOwnShort : Path.GetFileName(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Status = string.Format(Strings.AreasSaveFailed, ex.Message);
            return false;
        }
    }

    /// <summary>Vuelve a leer el archivo (lo que guardó otro) y los objetos de la base.</summary>
    [RelayCommand]
    private async Task ReloadAreasAsync()
    {
        if (IsModified && !await Owner.ConfirmDiscardAsync(new DiscardPrompt(Strings.AreasDiscardChanges, Strings.DiscardAndReload))) return;
        await LoadAsync();
    }

    /// <summary>La base pasa a usar otro archivo de áreas (p. ej. el que armó un compañero en una carpeta compartida).</summary>
    public async Task UseFileAsync(string? path)
    {
        if (IsModified && !await Owner.ConfirmDiscardAsync(new DiscardPrompt(Strings.AreasDiscardChanges, Strings.AreasDiscardAndSwitch))) return;
        SetAreasFile(path);
        LoadFile();
        Status = string.Format(Strings.AreasLoaded, Areas.Count(a => !a.IsUnassigned), _objects.Count);
    }

    /// <summary>Guarda las áreas en otro archivo y la base pasa a usarlo (para compartirlo).</summary>
    public async Task SaveAsAsync(string path)
    {
        // El selector de archivos ya preguntó si se reemplaza uno que existe.
        if (await SaveToAsync(path, force: true)) SetAreasFile(path);
    }

    private void SetAreasFile(string? path)
    {
        if (Profile is not { } profile) return;
        var own = Owner.AreaStore.DefaultPath(profile.Id);
        var areasFile = path is null || (own is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(own), StringComparison.OrdinalIgnoreCase)) ? null : path;
        if (areasFile == profile.AreasFile) return;
        Owner.UpdateProfile(profile with { AreasFile = areasFile });
        OnPropertyChanged(nameof(IsSharedFile));
        _path = Owner.AreaStore.PathFor(Profile!);
        ShowFile();
    }

    [RelayCommand]
    private Task UseOwnFileAsync() => UseFileAsync(null);
}

/// <summary>Un área en la lista de la pestaña (o «Sin área»).</summary>
public sealed partial class AreaItem : ObservableObject
{
    public AreaItem(SchemaArea area) => _area = area;

    public static AreaItem Unassigned() => new(new SchemaArea { Name = Strings.AreasUnassigned }) { IsUnassigned = true };

    public bool IsUnassigned { get; private init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Subtitle))]
    private SchemaArea _area;

    /// <summary>Cuántos objetos tiene (null mientras no se leyeron los de la base).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int? _count;

    public string Name => Area.Name.Length > 0 ? Area.Name : "…";
    public string CountText => Count?.ToString("N0") ?? "";
    public string Subtitle => IsUnassigned ? Strings.AreasUnassignedNote
        : string.Join(", ", Area.Rules) + (Area.IncludeDependents ? " " + Strings.AreasSubtitleByUse : "");
    public MaterialIconKind Icon => IsUnassigned ? MaterialIconKind.HelpCircleOutline : MaterialIconKind.Group;
}

/// <summary>Un objeto en la vista previa: tipo, nombre, si entró por lo que usa y en qué otras áreas está.</summary>
public sealed record AreaObjectRow(DbObject Object, IReadOnlyList<string> OtherAreas, bool ByUse = false)
{
    public string UseText => ByUse ? Strings.AreasByUse : "";
    public MaterialIconKind Icon => DatabaseTreeViewModel.IconFor(Object.Kind);
    public string Name => Object.Name;
    public string KindText => DbObjectKinds.Singular(Object.Kind);
    public string? Detail => Object.Kind is DbObjectKind.Trigger or DbObjectKind.Index ? Object.Detail : null;
    public string AlsoIn => OtherAreas.Count == 0 ? "" : string.Format(Strings.AreasAlsoIn, string.Join(", ", OtherAreas));
}
