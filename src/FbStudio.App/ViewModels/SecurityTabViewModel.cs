using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Security;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>Lo que necesita la ventana de usuario: el usuario a cambiar (null si es nuevo) y los administradores de usuarios conocidos.</summary>
public sealed record UserDialogRequest(DbUser? User, IReadOnlyList<string> Plugins);

/// <summary>Un renglón de la lista de la izquierda: un usuario, un rol o un objeto PSQL que recibe permisos.</summary>
public sealed record GranteeItem(GranteeKind Kind, string Name, string Detail, DbUser? User = null)
{
    public string KindLabel => SecurityTabViewModel.Label(Kind);

    /// <summary>«Usuario · María Pérez · inactivo».</summary>
    public string Subtitle => KindLabel + (Detail.Length > 0 ? " · " + Detail : "");
}

/// <summary>Los permisos de alguien sobre un objeto, con casillas; recuerda cómo estaban para saber qué cambió.</summary>
public sealed partial class PermissionRow : ObservableObject
{
    private readonly string _originalWanted;
    private readonly bool _originalGrantOption;

    public PermissionRow(GrantObjectKind kind, string name, IReadOnlyList<DbGrant> grants, bool editable, GranteeItem? grantee = null)
    {
        Kind = kind;
        Name = name;
        Grantee = grantee;
        Grants = grants;
        IsEditable = editable;
        var whole = grants.Where(g => g.Field is null).ToList();
        _originalWanted = string.Concat(SecurityScript.Privileges.Where(p => whole.Any(g => g.Privilege == p)));
        _originalGrantOption = whole.Any(g => g.GrantOption);
        var columns = grants.Where(g => g.Field is not null).GroupBy(g => g.Privilege)
            .Select(g => $"{SecurityScript.PrivilegeName(g.Key)} ({string.Join(", ", g.Select(x => x.Field).Distinct())})")
            .ToList();
        ColumnsNote = columns.Count == 0 ? "" : string.Format(Strings.SecColumnsNote, string.Join("; ", columns));
        Reset();
    }

    public GrantObjectKind Kind { get; }
    public string Name { get; }
    public string KindLabel => SecurityTabViewModel.Label(Kind);

    /// <summary>De quién es el permiso (en la vista por objeto, el renglón muestra esto en vez del objeto).</summary>
    public GranteeItem? Grantee { get; }
    public string GranteeName => Grantee?.Name ?? "";
    public string GranteeKindLabel => Grantee?.KindLabel ?? "";
    public string GranteeDetail => Grantee?.Detail ?? "";
    public IReadOnlyList<DbGrant> Grants { get; }
    public bool IsEditable { get; }

    /// <summary>«Solo algunas columnas: UPDATE (OBS)»: esos permisos no se cambian desde acá.</summary>
    public string ColumnsNote { get; }
    public bool HasColumns => ColumnsNote.Length > 0;

    private string Applicable => SecurityScript.Applicable(Kind);
    public bool CanSelect => Applicable.Contains('S');
    public bool CanInsert => Applicable.Contains('I');
    public bool CanUpdate => Applicable.Contains('U');
    public bool CanDelete => Applicable.Contains('D');
    public bool CanReferences => Applicable.Contains('R');
    public bool CanExecute => Applicable.Contains('X');
    public bool CanUsage => Applicable.Contains('G');

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _select;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _insert;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _update;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _delete;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _references;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _execute;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged), nameof(Wanted))] private bool _usage;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _grantOption;

    /// <summary>Las letras de lo marcado (S, I, U, D, R, X, G).</summary>
    public string Wanted => string.Concat(new[] { (Select, 'S'), (Insert, 'I'), (Update, 'U'), (Delete, 'D'), (References, 'R'), (Execute, 'X'), (Usage, 'G') }
        .Where(t => t.Item1).Select(t => t.Item2));

    public bool IsChanged => Wanted != _originalWanted || (Wanted.Length > 0 && GrantOption != _originalGrantOption);

    /// <summary>Tiene (o tenía) algún permiso: para «Solo los que tiene».</summary>
    public bool HasAny => _originalWanted.Length > 0 || HasColumns || Wanted.Length > 0;

    public void Reset()
    {
        Select = _originalWanted.Contains('S');
        Insert = _originalWanted.Contains('I');
        Update = _originalWanted.Contains('U');
        Delete = _originalWanted.Contains('D');
        References = _originalWanted.Contains('R');
        Execute = _originalWanted.Contains('X');
        Usage = _originalWanted.Contains('G');
        GrantOption = _originalGrantOption;
    }

    public IReadOnlyList<SecurityStatement> Statements(GranteeKind granteeKind, string grantee, string currentUser) =>
        SecurityScript.Changes(granteeKind, grantee, Kind, Name, Grants, Wanted, GrantOption, currentUser);
}

/// <summary>Los permisos de estructura (DDL) de alguien sobre una clase de objetos: crear, modificar y borrar cualquiera.</summary>
public sealed partial class DdlPermissionRow : ObservableObject
{
    private readonly string _original;
    private readonly bool _originalGrantOption;

    public DdlPermissionRow(SecurityScript.DdlClass cls, IReadOnlyList<DbGrant> grants, bool editable)
    {
        Class = cls;
        Grants = grants;
        IsEditable = editable;
        _original = string.Concat(SecurityScript.DdlPrivileges.Where(p => grants.Any(g => g.Privilege == p)));
        _originalGrantOption = grants.Any(g => g.GrantOption);
        Reset();
    }

    public SecurityScript.DdlClass Class { get; }
    public IReadOnlyList<DbGrant> Grants { get; }
    public bool IsEditable { get; }
    public string Label => SecurityTabViewModel.DdlLabel(Class);
    public bool CanCreateOrDrop => !Class.OnlyAlter;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _create;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _alter;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _drop;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _grantOption;

    public string Wanted => string.Concat(new[] { (Create, 'C'), (Alter, 'L'), (Drop, 'O') }.Where(t => t.Item1).Select(t => t.Item2));

    public bool IsChanged => Wanted != _original || (Wanted.Length > 0 && GrantOption != _originalGrantOption);

    public void Reset()
    {
        Create = _original.Contains('C');
        Alter = _original.Contains('L');
        Drop = _original.Contains('O');
        GrantOption = _originalGrantOption;
    }

    public IReadOnlyList<SecurityStatement> Statements(GranteeKind kind, string grantee, string currentUser) =>
        SecurityScript.DdlChanges(kind, grantee, Class, Grants, Wanted, GrantOption, currentUser);
}

/// <summary>Si un usuario tiene un rol, con casilla.</summary>
public sealed partial class RoleMembership : ObservableObject
{
    public RoleMembership(string role, DbGrant? grant, bool editable)
    {
        Role = role;
        Grant = grant;
        IsEditable = editable;
        _isMember = grant is not null;
        var label = role == "RDB$ADMIN" ? Strings.SecRoleDbAdmin : role;
        Label = grant is { GrantOption: true } ? string.Format(Strings.SecRoleAdminOption, label) : label;
    }

    public string Role { get; }
    public DbGrant? Grant { get; }
    public bool IsEditable { get; }
    public string Label { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsChanged))] private bool _isMember;

    public bool IsChanged => IsMember != (Grant is not null);

    public void Reset() => IsMember = Grant is not null;

    public SecurityStatement? Statement(string user, string currentUser) =>
        !IsChanged ? null : IsMember ? SecurityScript.GrantRole(Role, user) : SecurityScript.RevokeRole(Role, user, Grant!.Grantor, currentUser);
}

/// <summary>Un objeto de la lista «Por objeto»: tabla, vista, procedimiento… y cuántos tienen algún permiso sobre él.</summary>
public sealed record SecurityObjectItem(GrantObjectKind Kind, string Name, int Grantees)
{
    public string KindLabel => SecurityTabViewModel.Label(Kind);

    /// <summary>«Tabla · 3 con permiso».</summary>
    public string Subtitle => KindLabel + (Grantees > 0 ? " · " + string.Format(Strings.SecObjectGranted, Grantees) : "");
}

/// <summary>
/// Pestaña «Usuarios y permisos» de una base (F-28). A la izquierda los usuarios del servidor y los roles de la base (o
/// los objetos PSQL, que también reciben permisos); a la derecha, los roles y los permisos del elegido, con casillas
/// por objeto. O al revés, «Por objeto»: se elige una tabla (o vista, procedimiento…) y se ve quién tiene permisos
/// sobre ella, con las mismas casillas. Los cambios de permisos se juntan (aunque se pase de un usuario a otro) y «Aplicar» muestra el SQL,
/// pregunta y los ejecuta todos en una transacción. Crear, cambiar o borrar usuarios y roles se hace en el momento;
/// borrar pide escribir el nombre. Con un registro de solo lectura se puede mirar pero no cambiar.
/// </summary>
public sealed partial class SecurityTabViewModel : EditorTabViewModel
{
    private static readonly GrantObjectKind[] ObjectKindsInOrder =
        [GrantObjectKind.Table, GrantObjectKind.View, GrantObjectKind.Procedure, GrantObjectKind.Function, GrantObjectKind.Package,
         GrantObjectKind.Generator, GrantObjectKind.Exception];

    private static readonly (GranteeKind Grantee, DbObjectKind Object)[] PsqlGrantees =
        [(GranteeKind.Procedure, DbObjectKind.Procedure), (GranteeKind.Function, DbObjectKind.Function), (GranteeKind.Package, DbObjectKind.Package),
         (GranteeKind.Trigger, DbObjectKind.Trigger), (GranteeKind.View, DbObjectKind.View)];

    private readonly Dictionary<GrantObjectKind, IReadOnlyList<string>> _objects = [];
    private readonly Dictionary<GranteeKind, IReadOnlyList<string>> _psql = [];
    private readonly Dictionary<(GranteeKind, string), GranteeState> _states = [];
    private ILookup<(GranteeKind, string, GrantObjectKind, string), DbGrant> _grantsByPair = Array.Empty<DbGrant>().ToLookup(g => (g.GranteeKind, g.Grantee, g.ObjectKind, g.Object));
    private List<PermissionRow> _objectRowsAll = [];
    private SecuritySnapshot? _snapshot;

    /// <summary>
    /// Lo que se va armando de alguien que recibe permisos: un renglón por objeto (se arman a pedido: todos al elegirlo a
    /// la izquierda, o de a uno desde la vista por objeto) y sus roles. Los cambios sin aplicar quedan acá, así valen en
    /// las dos vistas.
    /// </summary>
    private sealed class GranteeState
    {
        public Dictionary<(GrantObjectKind, string), PermissionRow> Rows { get; } = [];
        public List<PermissionRow>? Ordered { get; set; }
        public List<RoleMembership> Roles { get; } = [];

        /// <summary>Los permisos de estructura (solo usuarios y roles).</summary>
        public List<DdlPermissionRow> Ddl { get; } = [];
    }
    private bool _disposed;

    public SecurityTabViewModel(MainWindowViewModel owner, ConnectionProfile profile) : base(owner, profile, Strings.SecTabTitle)
    {
        Status = profile.ReadOnly ? Strings.SecReadOnly : Strings.SecIntro;
    }

    public override MaterialIconKind Icon => MaterialIconKind.AccountKeyOutline;

    public bool IsReadOnlyProfile => Profile?.ReadOnly == true;

    /// <summary>Lo último leído (para las pruebas).</summary>
    public SecuritySnapshot? Snapshot => _snapshot;

    /// <summary>La última operación (para las pruebas: esperar a que termine).</summary>
    public Task Working { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(NewUserCommand), nameof(EditUserCommand), nameof(DeleteUserCommand),
        nameof(NewRoleCommand), nameof(DeleteRoleCommand), nameof(ApplyCommand), nameof(DiscardCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(ShowAdminNote))]
    [NotifyCanExecuteChangedFor(nameof(NewUserCommand), nameof(EditUserCommand), nameof(DeleteUserCommand), nameof(NewRoleCommand),
        nameof(DeleteRoleCommand), nameof(ApplyCommand))]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdminNote))]
    [NotifyCanExecuteChangedFor(nameof(NewUserCommand), nameof(EditUserCommand), nameof(DeleteUserCommand))]
    private bool _canManageUsers;

    public bool CanEdit => IsLoaded && !IsBusy && !IsReadOnlyProfile;
    public bool ShowAdminNote => IsLoaded && !CanManageUsers;

    // ── Lista de la izquierda ──

    public IReadOnlyList<string> GranteeScopes { get; } = [Strings.SecGranteeScopeUsers, Strings.SecGranteeScopePsql, Strings.SecGranteeScopeObjects];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsObjectScope), nameof(IsGranteeScope))]
    private int _granteeScope;

    /// <summary>«Por objeto»: a la izquierda los objetos y a la derecha quién tiene permisos sobre el elegido.</summary>
    public bool IsObjectScope => GranteeScope == 2;
    public bool IsGranteeScope => !IsObjectScope;
    [ObservableProperty] private string _granteeFilter = "";
    [ObservableProperty] private IReadOnlyList<GranteeItem> _grantees = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGrantee), nameof(IsUserSelected), nameof(IsRoleSelected), nameof(GranteeTitle), nameof(GranteeDetail))]
    [NotifyCanExecuteChangedFor(nameof(EditUserCommand), nameof(DeleteUserCommand), nameof(DeleteRoleCommand))]
    private GranteeItem? _selectedGrantee;

    public bool HasGrantee => SelectedGrantee is not null;
    public bool IsUserSelected => SelectedGrantee is { Kind: GranteeKind.User, User: not null };
    public bool IsRoleSelected => SelectedGrantee?.Kind == GranteeKind.Role;
    public string GranteeTitle => SelectedGrantee?.Name ?? "";
    public string GranteeDetail => SelectedGrantee?.Subtitle ?? "";

    partial void OnGranteeScopeChanged(int value)
    {
        RebuildGrantees();
        RebuildObjects();
    }

    partial void OnGranteeFilterChanged(string value)
    {
        RebuildGrantees();
        RebuildObjects();
    }

    // ── Por objeto ──

    [ObservableProperty] private IReadOnlyList<SecurityObjectItem> _objectItems = [];

    /// <summary>Qué tipo de objeto lista «Por objeto» (0 = todos, como <see cref="ObjectKinds"/>).</summary>
    [ObservableProperty] private int _objectScopeKindIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasObject), nameof(ObjectTitle), nameof(ObjectDetail))]
    private SecurityObjectItem? _selectedObject;

    public bool HasObject => SelectedObject is not null;
    public string ObjectTitle => SelectedObject?.Name ?? "";
    public string ObjectDetail => SelectedObject?.Subtitle ?? "";

    /// <summary>Un renglón por usuario, rol (y objeto PSQL con permisos) sobre el objeto elegido.</summary>
    [ObservableProperty] private IReadOnlyList<PermissionRow> _objectRows = [];

    partial void OnObjectScopeKindIndexChanged(int value) => RebuildObjects();
    partial void OnSelectedObjectChanged(SecurityObjectItem? value) => ShowObjectRows();

    /// <summary>Lleva a «Por objeto» con ese objeto elegido (clic derecho en el árbol → «Permisos…»).</summary>
    public void ShowObject(GrantObjectKind kind, string name)
    {
        GranteeFilter = "";
        ObjectScopeKindIndex = 0;
        GranteeScope = 2;
        SelectedObject = ObjectItems.FirstOrDefault(o => o.Kind == kind && o.Name == name);
    }
    partial void OnSelectedGranteeChanged(GranteeItem? value) => ShowGrantee();

    // ── Lo de la derecha ──

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasRoles))] private IReadOnlyList<RoleMembership> _roles = [];
    public bool HasRoles => Roles.Count > 0;

    /// <summary>Los permisos de estructura del elegido (crear tablas, modificar cualquier procedimiento…), si es usuario o rol.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDdl))] private IReadOnlyList<DdlPermissionRow> _ddlRows = [];
    public bool HasDdl => DdlRows.Count > 0;

    public static string DdlLabel(SecurityScript.DdlClass cls) => cls.Name switch
    {
        "SQL$TABLES" => Strings.SecDdlTables,
        "SQL$VIEWS" => Strings.SecDdlViews,
        "SQL$PROCEDURES" => Strings.SecDdlProcedures,
        "SQL$FUNCTIONS" => Strings.SecDdlFunctions,
        "SQL$PACKAGES" => Strings.SecDdlPackages,
        "SQL$GENERATORS" => Strings.SecDdlGenerators,
        "SQL$DOMAINS" => Strings.SecDdlDomains,
        "SQL$EXCEPTIONS" => Strings.SecDdlExceptions,
        "SQL$ROLES" => Strings.SecDdlRoles,
        "SQL$CHARSETS" => Strings.SecDdlCharsets,
        "SQL$COLLATIONS" => Strings.SecDdlCollations,
        "SQL$FILTERS" => Strings.SecDdlFilters,
        _ => Strings.SecDdlDatabase,
    };

    public IReadOnlyList<string> ObjectKinds { get; } =
    [
        Strings.SecObjectKindAll, Strings.SecObjectKindTables, Strings.SecObjectKindViews, Strings.SecObjectKindProcedures,
        Strings.SecObjectKindFunctions, Strings.SecObjectKindPackages, Strings.SecObjectKindGenerators, Strings.SecObjectKindExceptions,
    ];

    [ObservableProperty] private int _objectKindIndex;
    [ObservableProperty] private string _objectFilter = "";
    [ObservableProperty] private bool _onlyGranted;
    [ObservableProperty] private IReadOnlyList<PermissionRow> _rows = [];

    partial void OnObjectKindIndexChanged(int value) => ApplyRowFilter();
    // En «Por objeto», el filtro y «Solo los que tienen» de la derecha valen para los usuarios y roles.
    partial void OnObjectFilterChanged(string value) => ApplyRowFilter();
    partial void OnOnlyGrantedChanged(bool value) => ApplyRowFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DiscardCommand), nameof(ShowSqlCommand))]
    private int _pendingCount;

    public string PendingText => PendingCount == 0 ? Strings.SecNoPending : string.Format(Strings.SecPending, PendingCount);

    public static string Label(GranteeKind kind) => kind switch
    {
        GranteeKind.User => Strings.SecKindUser,
        GranteeKind.Role => Strings.SecKindRole,
        GranteeKind.Procedure => Strings.SecKindProcedure,
        GranteeKind.Function => Strings.SecKindFunction,
        GranteeKind.Package => Strings.SecKindPackage,
        GranteeKind.Trigger => Strings.SecKindTrigger,
        _ => Strings.SecKindView,
    };

    public static string Label(GrantObjectKind kind) => kind switch
    {
        GrantObjectKind.Table => Strings.SecKindTable,
        GrantObjectKind.View => Strings.SecKindView,
        GrantObjectKind.Procedure => Strings.SecKindProcedure,
        GrantObjectKind.Function => Strings.SecKindFunction,
        GrantObjectKind.Package => Strings.SecKindPackage,
        GrantObjectKind.Generator => Strings.SecKindGenerator,
        GrantObjectKind.Exception => Strings.SecKindException,
        _ => Strings.SecKindRole,
    };

    // ── Leer ──

    private bool CanRefresh() => !IsBusy;

    /// <summary>F9 o «Actualizar»: vuelve a leer todo (con cambios sin aplicar, pregunta si se descartan).</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (!await DiscardPendingAsync()) return;
        await (Working = LoadAsync());
    }

    public Task LoadAsync(GranteeItem? select = null) => Working = LoadCoreAsync(select);

    private async Task LoadCoreAsync(GranteeItem? select)
    {
        if (_disposed || Profile is not { } profile) return;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        var previous = select ?? SelectedGrantee;
        IsBusy = true;
        Status = Strings.SecLoading;
        try
        {
            var snapshot = await SecurityManager.LoadAsync(profile, password);
            foreach (var kind in ObjectKindsInOrder)
            {
                var objectKind = kind switch
                {
                    GrantObjectKind.Table => DbObjectKind.Table,
                    GrantObjectKind.View => DbObjectKind.View,
                    GrantObjectKind.Procedure => DbObjectKind.Procedure,
                    GrantObjectKind.Function => DbObjectKind.Function,
                    GrantObjectKind.Package => DbObjectKind.Package,
                    GrantObjectKind.Generator => DbObjectKind.Generator,
                    _ => DbObjectKind.Exception,
                };
                _objects[kind] = (await Owner.WithExplorerAsync(profile, e => e.LoadObjectsAsync(objectKind))).Select(o => o.Name).ToList();
            }
            foreach (var (granteeKind, objectKind) in PsqlGrantees)
                _psql[granteeKind] = granteeKind == GranteeKind.View
                    ? _objects[GrantObjectKind.View]
                    : (await Owner.WithExplorerAsync(profile, e => e.LoadObjectsAsync(objectKind))).Select(o => o.Name).ToList();
            if (_disposed) return;
            var previousObject = SelectedObject;
            _snapshot = snapshot;
            _grantsByPair = snapshot.Grants.ToLookup(g => (g.GranteeKind, g.Grantee, g.ObjectKind, g.Object));
            _states.Clear();
            PendingCount = 0;
            CanManageUsers = snapshot.CanManageUsers;
            IsLoaded = true;
            if (previous is { Kind: not (GranteeKind.User or GranteeKind.Role) } && !IsObjectScope) GranteeScope = 1;
            RebuildGrantees();
            SelectedGrantee = previous is null ? null : Grantees.FirstOrDefault(g => g.Kind == previous.Kind && g.Name == previous.Name);
            RebuildObjects();
            SelectedObject = previousObject is null ? null : ObjectItems.FirstOrDefault(o => o.Kind == previousObject.Kind && o.Name == previousObject.Name);
            ShowObjectRows();
            Status = string.Format(Strings.SecLoaded, snapshot.Users.Count, snapshot.Roles.Count(r => !r.IsSystem), snapshot.Grants.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.SecFailed, First(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildGrantees()
    {
        if (_snapshot is not { } s)
        {
            Grantees = [];
            return;
        }
        var list = new List<GranteeItem>();
        if (GranteeScope == 0)
        {
            list.AddRange(UsersAndRoles(s));
        }
        else if (GranteeScope == 1)
        {
            foreach (var (granteeKind, _) in PsqlGrantees)
            foreach (var name in _psql.GetValueOrDefault(granteeKind) ?? [])
                list.Add(new GranteeItem(granteeKind, name, ""));
        }
        var filter = GranteeFilter.Trim();
        Grantees = filter.Length == 0 ? list : list.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Los usuarios del servidor (y los que tienen permisos sin estar en él, PUBLIC incluido) y los roles de la base.</summary>
    private static List<GranteeItem> UsersAndRoles(SecuritySnapshot s)
    {
        var list = new List<GranteeItem>();
        foreach (var u in s.Users)
        {
            var flags = new[] { u.FullName, u.Active ? null : Strings.SecInactive, u.Admin ? Strings.SecAdminFlag : null };
            list.Add(new GranteeItem(GranteeKind.User, u.Name, string.Join(" · ", flags.Where(f => !string.IsNullOrEmpty(f))), u));
        }
        // Los que tienen permisos pero no están en el servidor (PUBLIC, usuarios borrados u otro servidor de usuarios).
        foreach (var name in s.Grants.Where(g => g.GranteeKind == GranteeKind.User).Select(g => g.Grantee).Distinct()
                     .Where(n => !s.Users.Any(u => u.Name == n)).Order(StringComparer.Ordinal))
            list.Add(new GranteeItem(GranteeKind.User, name, name == "PUBLIC" ? Strings.SecPublicNote : Strings.SecNotOnServer));
        if (!list.Any(g => g.Name == "PUBLIC")) list.Add(new GranteeItem(GranteeKind.User, "PUBLIC", Strings.SecPublicNote));
        foreach (var r in s.Roles.Where(r => !r.IsSystem))
            list.Add(new GranteeItem(GranteeKind.Role, r.Name, r.Description ?? ""));
        return list;
    }

    private GranteeState StateFor(GranteeItem g)
    {
        if (_states.TryGetValue((g.Kind, g.Name), out var state)) return state;
        state = new GranteeState();
        if (g.Kind == GranteeKind.User && g.Name != "PUBLIC" && _snapshot is { } s)
        {
            foreach (var role in s.Roles.Where(r => !r.IsSystem || r.Name == "RDB$ADMIN").Select(r => r.Name))
            {
                var membership = new RoleMembership(role, _grantsByPair[(g.Kind, g.Name, GrantObjectKind.Role, role)].FirstOrDefault(), !IsReadOnlyProfile);
                membership.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RoleMembership.IsChanged)) UpdatePending(); };
                state.Roles.Add(membership);
            }
        }
        if (g.Kind is GranteeKind.User or GranteeKind.Role)
        {
            foreach (var cls in SecurityScript.DdlClasses)
            {
                var row = new DdlPermissionRow(cls, _grantsByPair[(g.Kind, g.Name, GrantObjectKind.Metadata, cls.Name)].ToList(), !IsReadOnlyProfile);
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DdlPermissionRow.IsChanged)) UpdatePending(); };
                state.Ddl.Add(row);
            }
        }
        _states[(g.Kind, g.Name)] = state;
        return state;
    }

    /// <summary>El renglón de los permisos de alguien sobre un objeto (el mismo en las dos vistas).</summary>
    private PermissionRow RowFor(GranteeItem g, GranteeState state, GrantObjectKind kind, string name)
    {
        if (state.Rows.TryGetValue((kind, name), out var row)) return row;
        row = new PermissionRow(kind, name, _grantsByPair[(g.Kind, g.Name, kind, name)].ToList(), !IsReadOnlyProfile, g);
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PermissionRow.IsChanged)) UpdatePending(); };
        state.Rows[(kind, name)] = row;
        return row;
    }

    private void ShowGrantee()
    {
        if (SelectedGrantee is not { } g || _snapshot is not { } s)
        {
            Roles = [];
            Rows = [];
            DdlRows = [];
            return;
        }
        var state = StateFor(g);
        DdlRows = state.Ddl;
        if (state.Ordered is null)
        {
            var mine = s.Grants.Where(x => x.GranteeKind == g.Kind && x.Grantee == g.Name).ToList();
            var rows = new List<PermissionRow>();
            foreach (var kind in ObjectKindsInOrder)
            {
                var names = (_objects.GetValueOrDefault(kind) ?? []).Concat(mine.Where(x => x.ObjectKind == kind).Select(x => x.Object)).Distinct();
                foreach (var name in names.Order(StringComparer.Ordinal)) rows.Add(RowFor(g, state, kind, name));
            }
            state.Ordered = rows;
        }
        Roles = state.Roles;
        ApplyRowFilter();
    }

    private void RebuildObjects()
    {
        if (_snapshot is not { } s)
        {
            ObjectItems = [];
            return;
        }
        GrantObjectKind? only = ObjectScopeKindIndex is > 0 and <= 7 ? ObjectKindsInOrder[ObjectScopeKindIndex - 1] : null;
        var filter = GranteeFilter.Trim();
        var granted = s.Grants.Where(g => g.ObjectKind != GrantObjectKind.Role).GroupBy(g => (g.ObjectKind, g.Object))
            .ToDictionary(x => x.Key, x => x.Select(g => (g.GranteeKind, g.Grantee)).Distinct().Count());
        var selected = SelectedObject;
        ObjectItems = ObjectKindsInOrder.Where(k => only is null || k == only)
            .SelectMany(k => (_objects.GetValueOrDefault(k) ?? []).Order(StringComparer.Ordinal).Select(n => (Kind: k, Name: n)))
            .Where(o => filter.Length == 0 || o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(o => new SecurityObjectItem(o.Kind, o.Name, granted.GetValueOrDefault((o.Kind, o.Name))))
            .ToList();
        if (selected is not null && !ObjectItems.Any(o => o.Kind == selected.Kind && o.Name == selected.Name)) SelectedObject = null;
    }

    /// <summary>Quién puede tener permisos sobre el objeto elegido: usuarios, PUBLIC, roles y los objetos PSQL que ya tienen.</summary>
    private void ShowObjectRows()
    {
        if (SelectedObject is not { } o || _snapshot is not { } s)
        {
            _objectRowsAll = [];
            ObjectRows = [];
            return;
        }
        var candidates = UsersAndRoles(s);
        candidates.AddRange(s.Grants.Where(g => g.ObjectKind == o.Kind && g.Object == o.Name && g.GranteeKind is not (GranteeKind.User or GranteeKind.Role))
            .Select(g => (g.GranteeKind, g.Grantee)).Distinct().Order()
            .Select(g => new GranteeItem(g.GranteeKind, g.Grantee, "")));
        _objectRowsAll = candidates.Select(g => RowFor(g, StateFor(g), o.Kind, o.Name)).ToList();
        ApplyRowFilter();
    }

    private void ApplyRowFilter()
    {
        var filter = ObjectFilter.Trim();
        if (IsObjectScope)
        {
            ObjectRows = _objectRowsAll.Where(r => (filter.Length == 0 || r.GranteeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                                   && (!OnlyGranted || r.HasAny || r.IsChanged)).ToList();
            return;
        }
        if (SelectedGrantee is not { } g || !_states.TryGetValue((g.Kind, g.Name), out var state) || state.Ordered is not { } ordered)
        {
            Rows = [];
            return;
        }
        GrantObjectKind? kind = ObjectKindIndex is > 0 and <= 7 ? ObjectKindsInOrder[ObjectKindIndex - 1] : null;
        Rows = ordered.Where(r => (kind is null || r.Kind == kind)
                                  && (filter.Length == 0 || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                  && (!OnlyGranted || r.HasAny || r.IsChanged))
            .ToList();
    }

    private void UpdatePending() =>
        PendingCount = _states.Values.Sum(s => s.Rows.Values.Count(r => r.IsChanged) + s.Roles.Count(r => r.IsChanged) + s.Ddl.Count(r => r.IsChanged));

    /// <summary>Todas las sentencias de los cambios sin aplicar, de todos los usuarios y roles tocados (en las dos vistas).</summary>
    public IReadOnlyList<SecurityStatement> PendingStatements()
    {
        if (_snapshot is not { } s) return [];
        var statements = new List<SecurityStatement>();
        foreach (var ((kind, name), state) in _states.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2, StringComparer.Ordinal))
        {
            foreach (var role in state.Roles) if (role.Statement(name, s.CurrentUser) is { } st) statements.Add(st);
            foreach (var row in state.Rows.Values.Where(r => r.IsChanged).OrderBy(r => Array.IndexOf(ObjectKindsInOrder, r.Kind)).ThenBy(r => r.Name, StringComparer.Ordinal))
                statements.AddRange(row.Statements(kind, name, s.CurrentUser));
            foreach (var ddl in state.Ddl.Where(r => r.IsChanged)) statements.AddRange(ddl.Statements(kind, name, s.CurrentUser));
        }
        return statements;
    }

    // ── Aplicar ──

    private bool HasPending() => PendingCount > 0;
    private bool CanApply() => PendingCount > 0 && CanEdit;

    /// <summary>Muestra abajo el SQL de los cambios sin aplicar, sin ejecutarlo.</summary>
    [RelayCommand(CanExecute = nameof(HasPending))]
    public void ShowSql()
    {
        var statements = PendingStatements();
        Document.Text = string.Format(Strings.SecSqlHeader, statements.Count) + "\n" + string.Concat(statements.Select(st => st.Shown + ";\n"));
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    public Task ApplyAsync() => Working = ApplyCoreAsync();

    private async Task ApplyCoreAsync()
    {
        if (Profile is not { } profile) return;
        var statements = PendingStatements();
        if (statements.Count == 0)
        {
            DiscardChanges();
            return;
        }
        ShowSql();
        List<string> reasons = [string.Format(Strings.SecApplyConfirm, statements.Count, profile.Name)];
        if (profile.IsProduction) reasons.Add(Strings.SecProduction);
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt(reasons, Strings.SecApply, Strings.Cancel, Strings.SecApplyTitle))) return;
        if (!await RunAsync(statements)) return;
        await LoadCoreAsync(SelectedGrantee);
        Status = string.Format(Strings.SecApplied, statements.Count);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void Discard() => DiscardChanges();

    private void DiscardChanges()
    {
        foreach (var state in _states.Values)
        {
            foreach (var row in state.Rows.Values.Where(r => r.IsChanged)) row.Reset();
            foreach (var role in state.Roles.Where(r => r.IsChanged)) role.Reset();
            foreach (var ddl in state.Ddl.Where(r => r.IsChanged)) ddl.Reset();
        }
        UpdatePending();
        ApplyRowFilter();
    }

    /// <summary>Antes de algo que vuelve a leer todo: si hay cambios sin aplicar, pregunta si se descartan.</summary>
    private async Task<bool> DiscardPendingAsync()
    {
        if (PendingCount == 0) return true;
        if (!await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.SecDiscardPending, PendingCount)],
                Strings.SecDiscardRun, Strings.Cancel, Strings.SecDiscardPendingTitle))) return false;
        DiscardChanges();
        return true;
    }

    /// <summary>Ejecuta y anota abajo lo que se ejecutó (sin contraseñas); si falla, dice cuál sentencia y por qué.</summary>
    private async Task<bool> RunAsync(IReadOnlyList<SecurityStatement> statements)
    {
        if (Profile is not { } profile) return false;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return false;
        }
        IsBusy = true;
        try
        {
            await SecurityManager.ExecuteAsync(profile, password, statements);
            Document.Text = string.Concat(statements.Select(st => st.Shown + ";\n"));
            return true;
        }
        catch (SecurityStatementException ex)
        {
            Status = ex.Index < 0 ? string.Format(Strings.SecNotAppliedCommit, ex.Message) : string.Format(Strings.SecNotApplied, ex.Statement.Shown, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.SecFailed, First(ex));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Usuarios y roles ──

    private bool CanManage() => CanEdit && CanManageUsers;
    private bool CanManageSelectedUser() => CanManage() && IsUserSelected;

    // SYSDBA y uno mismo no se borran desde acá: se quedaría sin poder entrar.
    private bool CanDeleteUser() => CanManageSelectedUser() && SelectedGrantee!.Name != "SYSDBA"
                                    && !string.Equals(SelectedGrantee.Name, _snapshot?.CurrentUser, StringComparison.OrdinalIgnoreCase);

    private bool CanDeleteRole() => CanEdit && IsRoleSelected;

    private IReadOnlyList<string> Plugins() =>
        (_snapshot?.Users.Select(u => u.Plugin).Where(p => p is not null).Select(p => p!).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList()) ?? [];

    /// <summary>En producción, crear o cambiar un usuario pregunta (vale para todas las bases del servidor).</summary>
    private async Task<bool> ConfirmServerChangeAsync(ConnectionProfile profile) =>
        !profile.IsProduction || await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.SecUserProduction, profile.Name)],
            Strings.SecRun, Strings.Cancel, Strings.SecUserProductionTitle));

    [RelayCommand(CanExecute = nameof(CanManage))]
    public Task NewUserAsync() => Working = NewUserCoreAsync();

    private async Task NewUserCoreAsync()
    {
        if (Profile is not { } profile || !await DiscardPendingAsync()) return;
        if (await Owner.EditUserAsync(new UserDialogRequest(null, Plugins())) is not { } asked) return;
        var edit = asked with { Name = SecurityScript.StoredName(asked.Name) };
        if (!await ConfirmServerChangeAsync(profile) || !await RunAsync(SecurityScript.CreateUser(edit))) return;
        GranteeScope = 0;
        await LoadCoreAsync(new GranteeItem(GranteeKind.User, edit.Name, ""));
        Status = string.Format(Strings.SecUserCreated, edit.Name);
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedUser))]
    public Task EditUserAsync() => Working = EditUserCoreAsync();

    private async Task EditUserCoreAsync()
    {
        if (Profile is not { } profile || SelectedGrantee?.User is not { } user || !await DiscardPendingAsync()) return;
        if (await Owner.EditUserAsync(new UserDialogRequest(user, Plugins())) is not { } edit) return;
        var statements = SecurityScript.AlterUser(user, edit);
        if (statements.Count == 0)
        {
            Status = Strings.SecUserUnchanged;
            return;
        }
        if (!await ConfirmServerChangeAsync(profile) || !await RunAsync(statements)) return;
        await LoadCoreAsync(SelectedGrantee);
        Status = string.Format(Strings.SecUserChanged, user.Name);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteUser))]
    public Task DeleteUserAsync() => Working = DeleteUserCoreAsync();

    private async Task DeleteUserCoreAsync()
    {
        if (Profile is not { } profile || SelectedGrantee?.User is not { } user || !await DiscardPendingAsync()) return;
        if (!await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.SecDeleteUserTitle, [string.Format(Strings.SecDeleteUserConfirm, user.Name, profile.Name)],
                user.Name, Strings.SecDeleteUser.TrimEnd('…')))) return;
        if (!await RunAsync([SecurityScript.DropUser(user)])) return;
        await LoadCoreAsync(null);
        SelectedGrantee = null;
        Status = string.Format(Strings.SecUserDeleted, user.Name);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public Task NewRoleAsync() => Working = NewRoleCoreAsync();

    private async Task NewRoleCoreAsync()
    {
        if (Profile is not { } profile || !await DiscardPendingAsync()) return;
        if ((await Owner.AskTextAsync(new TextPrompt(Strings.SecRoleTitle, Strings.SecRolePrompt)))?.Trim() is not { Length: > 0 } asked) return;
        var name = SecurityScript.StoredName(asked);
        if (profile.IsProduction && !await Owner.ConfirmActionAsync(new ConfirmPrompt([string.Format(Strings.SecRoleProduction, name, profile.Name)],
                Strings.SecRun, Strings.Cancel, Strings.SecRoleProductionTitle))) return;
        if (!await RunAsync([SecurityScript.CreateRole(name)])) return;
        GranteeScope = 0;
        await LoadCoreAsync(new GranteeItem(GranteeKind.Role, name, ""));
        Status = string.Format(Strings.SecRoleCreated, name);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRole))]
    public Task DeleteRoleAsync() => Working = DeleteRoleCoreAsync();

    private async Task DeleteRoleCoreAsync()
    {
        if (Profile is not { } profile || SelectedGrantee is not { Kind: GranteeKind.Role } role || !await DiscardPendingAsync()) return;
        if (!await Owner.ConfirmTypedAsync(new TypedConfirm(Strings.SecDeleteRoleTitle, [string.Format(Strings.SecDeleteRoleConfirm, role.Name, profile.Name)],
                role.Name, Strings.SecDeleteRole.TrimEnd('…')))) return;
        if (!await RunAsync([SecurityScript.DropRole(role.Name)])) return;
        await LoadCoreAsync(null);
        SelectedGrantee = null;
        Status = string.Format(Strings.SecRoleDeleted, role.Name);
    }

    private static string First(Exception ex) => ex.Message.Split('\n', 2)[0].Trim();

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        await base.DisposeAsync();
    }
}
