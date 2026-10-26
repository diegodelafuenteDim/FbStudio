using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Core.Security;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>La pestaña «Usuarios y permisos» (F-28), contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class SecurityTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-security-" + Guid.NewGuid().ToString("N"));

    private ConnectionProfile Sysdba => fb.Profile with { Id = Guid.NewGuid(), Name = "docker-sysdba", User = "SYSDBA" };

    private (MainWindow Window, MainWindowViewModel Vm) Open(params ConnectionProfile[] profiles)
    {
        var store = AppServices.CreateProfileStore(_folder);
        foreach (var p in profiles) store.Upsert(p, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1500, Height = 900 };
        window.Show();
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private static PermissionRow Row(SecurityTabViewModel tab, string name) => tab.Rows.Single(r => r.Name == name);

    [Fact]
    public Task Crea_un_usuario_le_da_permisos_y_roles_y_lo_borra() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(Sysdba);
        var prompts = new List<ConfirmPrompt>();
        var typed = new List<TypedConfirm>();
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(true); };
        vm.ConfirmTypedAsync = p => { typed.Add(p); return Task.FromResult(true); };
        var tab = vm.OpenSecurity(vm.Profiles.Single())!;
        await tab.Working;
        Pump();
        Assert.Equal((EditorTabKind.Security, "Usuarios y permisos"), (tab.Kind, tab.BaseTitle));
        Assert.True(tab.CanManageUsers);
        Assert.False(tab.ShowAdminNote);
        Assert.Contains(tab.Grantees, g => g is { Kind: GranteeKind.User, Name: "SYSDBA" });
        Assert.Contains(tab.Grantees, g => g is { Kind: GranteeKind.User, Name: "PUBLIC" });
        Assert.Contains(tab.Grantees, g => g is { Kind: GranteeKind.Role, Name: "R_LECTURA" });
        Assert.False(tab.HasGrantee);
        tab.SelectedGrantee = tab.Grantees.Single(g => g.Name == "SYSDBA");
        Assert.True(tab.EditUserCommand.CanExecute(null));
        Assert.False(tab.DeleteUserCommand.CanExecute(null));             // SYSDBA (y uno mismo) no se borra desde acá

        // Un usuario nuevo: escrito en minúsculas, queda en mayúsculas (como sin comillas) y elegido.
        var name = NewName("fbs_ui_");
        UserDialogRequest? asked = null;
        vm.EditUserAsync = r => { asked = r; return Task.FromResult<UserEdit?>(new UserEdit(name, "Clave.1", "Ana", null, "Ruiz", true, false, null, null)); };
        await tab.NewUserAsync();
        var user = name.ToUpperInvariant();
        Assert.Null(asked!.User);
        Assert.Equal(["Srp"], asked.Plugins);
        Assert.Equal($"Usuario {user} creado.", tab.Status);
        Assert.Equal((user, "Usuario · Ana Ruiz"), (tab.GranteeTitle, tab.GranteeDetail));
        Assert.DoesNotContain("Clave.1", tab.Document.Text);
        Assert.Contains("PASSWORD '********'", tab.Document.Text);

        // Permisos y roles: se juntan aunque se pase a otro, y «Aplicar» los ejecuta todos.
        Row(tab, "TURNOS").Select = true;
        Row(tab, "TURNOS").Insert = true;
        Row(tab, "P_CONTAR").Execute = true;
        Assert.False(Row(tab, "P_CONTAR").CanSelect);
        tab.Roles.Single(r => r.Role == "R_LECTURA").IsMember = true;
        Assert.Equal((3, "3 cambios sin aplicar"), (tab.PendingCount, tab.PendingText));
        tab.SelectedGrantee = tab.Grantees.Single(g => g.Name == "R_LECTURA");
        Assert.False(tab.HasRoles);                                      // un rol no tiene roles
        Row(tab, "V_TURNOS").Select = true;
        Assert.Equal(4, tab.PendingCount);
        tab.ShowSql();
        Assert.Contains($"GRANT SELECT, INSERT ON TURNOS TO {user};", tab.Document.Text);
        Assert.Contains($"GRANT R_LECTURA TO {user};", tab.Document.Text);
        Assert.Contains("GRANT SELECT ON V_TURNOS TO ROLE R_LECTURA;", tab.Document.Text);

        await tab.ApplyAsync();
        Assert.StartsWith("Se van a ejecutar 4 sentencias GRANT/REVOKE en docker-sysdba", prompts[^1].Reasons[0]);
        Assert.Equal("Aplicado: 4 sentencias.", tab.Status);
        Assert.Equal(0, tab.PendingCount);
        Assert.True(Row(tab, "V_TURNOS").Select);                         // sigue elegido el rol, ya leído de nuevo
        Assert.False(Row(tab, "V_TURNOS").IsChanged);

        // «Solo los que tiene».
        tab.SelectedGrantee = tab.Grantees.Single(g => g.Name == user);
        tab.OnlyGranted = true;
        Assert.Equal(["TURNOS", "P_CONTAR"], tab.Rows.Select(r => r.Name));
        Assert.True(tab.Roles.Single(r => r.Role == "R_LECTURA").IsMember);

        // Descartar vuelve todo como estaba.
        Row(tab, "TURNOS").Insert = false;
        Row(tab, "TURNOS").GrantOption = true;
        Assert.Equal(1, tab.PendingCount);
        tab.DiscardCommand.Execute(null);
        Assert.Equal(0, tab.PendingCount);
        Assert.Equal((true, false), (Row(tab, "TURNOS").Insert, Row(tab, "TURNOS").GrantOption));

        // Cambiar el usuario: solo lo que cambió, sin mostrar la contraseña.
        vm.EditUserAsync = r => Task.FromResult<UserEdit?>(new UserEdit(r.User!.Name, "Clave.2", "Ana", "María", "Ruiz", true, false, null, null));
        await tab.EditUserAsync();
        Assert.Equal($"Usuario {user} cambiado.", tab.Status);
        Assert.Equal("Usuario · Ana María Ruiz", tab.GranteeDetail);
        Assert.DoesNotContain("Clave.2", tab.Document.Text);

        // Borrar pide escribir el nombre.
        await tab.DeleteUserAsync();
        Assert.Equal(user, typed.Single().Expected);
        Assert.Equal($"Usuario {user} borrado.", tab.Status);
        Assert.DoesNotContain(tab.Snapshot!.Users, u => u.Name == user);
        // Sus permisos en la base quedan: figura como «no está en el servidor».
        Assert.Contains(tab.Grantees, g => g.Name == user && g.Detail == "no está en el servidor");

        // Se deja la base como estaba: otras pruebas cuentan sus objetos y sus permisos.
        await FbStudio.Firebird.SecurityManager.ExecuteAsync(vm.Profiles.Single(), fb.Password,
            [new SecurityStatement($"REVOKE ALL ON ALL FROM {user}"), new SecurityStatement("REVOKE SELECT ON V_TURNOS FROM ROLE R_LECTURA")]);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Roles_nuevos_y_borrados_y_en_produccion_pregunta() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(Sysdba with { Environment = ProfileEnvironment.Produccion });
        var prompts = new List<ConfirmPrompt>();
        var answer = true;
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(answer); };
        vm.ConfirmTypedAsync = _ => Task.FromResult(true);
        var tab = vm.OpenSecurity(vm.Profiles.Single())!;
        await tab.Working;

        var role = NewName("r_ui_");
        vm.AskTextAsync = _ => Task.FromResult<string?>(role);
        await tab.NewRoleAsync();
        Assert.Contains("PRODUCCIÓN", prompts[^1].Reasons[0]);
        Assert.Equal($"Rol {role.ToUpperInvariant()} creado.", tab.Status);
        Assert.Equal(role.ToUpperInvariant(), tab.SelectedGrantee!.Name);

        Row(tab, "TURNOS").Select = true;
        answer = false;                                                    // en producción dice que no: no se aplica
        await tab.ApplyAsync();
        Assert.Contains("Es una base de PRODUCCIÓN.", prompts[^1].Reasons);
        Assert.Equal(1, tab.PendingCount);
        Assert.DoesNotContain(tab.Snapshot!.Grants, g => g.Grantee == role.ToUpperInvariant());

        // Borrar el rol con cambios sin aplicar: primero pregunta si se descartan.
        answer = true;
        await tab.DeleteRoleAsync();
        Assert.Equal("Cambios sin aplicar", prompts[^1].Title);
        Assert.Equal($"Rol {role.ToUpperInvariant()} borrado.", tab.Status);
        Assert.DoesNotContain(tab.Grantees, g => g.Name == role.ToUpperInvariant());

        // Los objetos PSQL también reciben permisos.
        tab.GranteeScope = 1;
        Assert.Contains(tab.Grantees, g => g is { Kind: GranteeKind.Procedure, Name: "P_CONTAR" });
        Assert.Contains(tab.Grantees, g => g is { Kind: GranteeKind.Trigger, Name: "TURNOS_BI" });
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Permisos_de_estructura_crear_modificar_y_borrar() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(Sysdba);
        vm.ConfirmActionAsync = _ => Task.FromResult(true);
        vm.ConfirmTypedAsync = _ => Task.FromResult(true);
        var tab = vm.OpenSecurity(vm.Profiles.Single())!;
        await tab.Working;
        var role = NewName("r_ddl_").ToUpperInvariant();
        vm.AskTextAsync = _ => Task.FromResult<string?>(role);
        await tab.NewRoleAsync();
        Assert.True(tab.HasDdl);
        Assert.Equal(13, tab.DdlRows.Count);
        DdlPermissionRow Ddl(string label) => tab.DdlRows.Single(r => r.Label == label);
        Assert.False(Ddl("La base (ALTER DATABASE)").CanCreateOrDrop);           // la base: solo modificar

        Ddl("Tablas").Create = true;
        Ddl("Procedimientos").Alter = true;
        Ddl("Procedimientos").Drop = true;
        Ddl("La base (ALTER DATABASE)").Alter = true;
        Assert.Equal(3, tab.PendingCount);
        await tab.ApplyAsync();
        Assert.Contains($"GRANT CREATE TABLE TO ROLE {role};", tab.Document.Text);
        Assert.Contains($"GRANT ALTER ANY PROCEDURE TO ROLE {role};", tab.Document.Text);
        Assert.Contains($"GRANT DROP ANY PROCEDURE TO ROLE {role};", tab.Document.Text);
        Assert.Contains($"GRANT ALTER DATABASE TO ROLE {role};", tab.Document.Text);

        // Al volver a leer, quedan tildados; destildar los quita.
        Assert.True(Ddl("Tablas").Create);
        Assert.True(Ddl("Procedimientos").Drop);
        Assert.Contains(tab.Snapshot!.Grants, g => g is { GranteeKind: GranteeKind.Role, ObjectKind: GrantObjectKind.Metadata, Object: "SQL$TABLES", Privilege: 'C' } && g.Grantee == role);
        Ddl("Tablas").Create = false;
        await tab.ApplyAsync();
        Assert.Contains($"REVOKE CREATE TABLE FROM ROLE {role};", tab.Document.Text);
        Assert.False(Ddl("Tablas").Create);

        await tab.DeleteRoleAsync();
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Por_objeto_muestra_quien_tiene_permisos_y_los_cambia_igual() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(Sysdba);
        vm.ConfirmActionAsync = _ => Task.FromResult(true);
        vm.ConfirmTypedAsync = _ => Task.FromResult(true);
        var tab = vm.OpenSecurity(vm.Profiles.Single())!;
        await tab.Working;
        var role = NewName("r_obj_").ToUpperInvariant();
        vm.AskTextAsync = _ => Task.FromResult<string?>(role);
        await tab.NewRoleAsync();

        // Desde el árbol («Permisos…» de una tabla): la vista por objeto, con quién tiene permisos sobre ella.
        tab.ShowObject(GrantObjectKind.Table, "TURNOS");
        Pump();
        Assert.True(tab.IsObjectScope);
        Assert.Equal("TURNOS", tab.ObjectTitle);
        Assert.Contains(tab.ObjectItems, o => o is { Kind: GrantObjectKind.Procedure, Name: "P_CONTAR" });
        var row = tab.ObjectRows.Single(r => r.GranteeName == role);
        Assert.Equal("Rol", row.GranteeKindLabel);
        Assert.False(row.HasAny);
        Assert.Contains(tab.ObjectRows, r => r is { GranteeName: "PUBLIC" });
        row.Select = true;
        Assert.Equal(1, tab.PendingCount);

        // Es el mismo renglón que en la vista del rol: lo marcado se ve allá y se aplica una sola vez.
        tab.GranteeScope = 0;
        tab.SelectedGrantee = tab.Grantees.Single(g => g.Name == role);
        Assert.Same(row, Row(tab, "TURNOS"));
        Row(tab, "V_TURNOS").Select = true;
        Assert.Equal(2, tab.PendingCount);
        tab.GranteeScope = 2;
        await tab.ApplyAsync();
        Assert.Contains($"GRANT SELECT ON TURNOS TO ROLE {role};", tab.Document.Text);
        Assert.Contains($"GRANT SELECT ON V_TURNOS TO ROLE {role};", tab.Document.Text);

        // Al volver a leer sigue en el mismo objeto; «Solo los que tienen» deja a los que tienen algo.
        Assert.Equal("TURNOS", tab.ObjectTitle);
        Assert.Contains("con permiso", tab.ObjectDetail);
        Assert.True(tab.ObjectRows.Single(r => r.GranteeName == role).Select);
        tab.OnlyGranted = true;
        Assert.Contains(tab.ObjectRows, r => r.GranteeName == role);
        Assert.All(tab.ObjectRows, r => Assert.True(r.HasAny));
        tab.ObjectFilter = "r_obj";
        Assert.Equal([role], tab.ObjectRows.Select(r => r.GranteeName));

        tab.GranteeScope = 0;
        tab.SelectedGrantee = tab.Grantees.Single(g => g.Name == role);
        await tab.DeleteRoleAsync();
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Sin_ser_administrador_o_de_solo_lectura_no_se_cambia_lo_que_no_se_puede() => ui.Session.DispatchAsync(async () =>
    {
        var readOnly = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-ro", ReadOnly = true };
        var (w, vm) = Open(fb.Profile, readOnly);
        var tab = vm.OpenSecurity(vm.Profiles.Single(p => p.Id == fb.Profile.Id))!;
        await tab.Working;
        Assert.True(tab.ShowAdminNote);                                   // el usuario de prueba no administra usuarios
        Assert.False(tab.NewUserCommand.CanExecute(null));
        Assert.True(tab.NewRoleCommand.CanExecute(null));

        var ro = vm.OpenSecurity(vm.Profiles.Single(p => p.Id == readOnly.Id))!;
        await ro.Working;
        Assert.True(ro.IsReadOnlyProfile);
        Assert.False(ro.NewRoleCommand.CanExecute(null));
        ro.SelectedGrantee = ro.Grantees.First(g => g.Kind == GranteeKind.User);
        Assert.All(ro.Rows, r => Assert.False(r.IsEditable));
        Row(ro, "TURNOS").Select = !Row(ro, "TURNOS").Select;              // aunque se marque algo, no se aplica
        Assert.False(ro.ApplyCommand.CanExecute(null));
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task La_ventana_de_usuario_valida_y_devuelve_lo_escrito() => ui.Session.DispatchAsync(async () =>
    {
        var owner = new Window();
        owner.Show();
        var dialog = new UserDialog(new UserDialogRequest(null, ["Legacy_UserManager", "Srp"]));
        var result = dialog.ShowDialog<UserEdit?>(owner);
        Pump();
        Assert.Equal("Nuevo usuario", dialog.Title);
        Assert.True(dialog.Plugin.IsVisible);
        Assert.Equal("Falta el nombre del usuario.", dialog.Validate(isNew: true));
        dialog.UserName.Text = "pepe";
        Assert.Equal("Falta la contraseña.", dialog.Validate(isNew: true));
        dialog.Password.Text = "a";
        dialog.Repeat.Text = "b";
        Assert.Equal("Las dos contraseñas no son iguales.", dialog.Validate(isNew: true));
        dialog.Repeat.Text = "a";
        dialog.LastName.Text = "Pérez";
        dialog.Active.IsChecked = false;
        dialog.Plugin.SelectedIndex = 1;
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Button>().First(b => b.Content as string == "Guardar")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new UserEdit("pepe", "a", "", "", "Pérez", false, false, "", "Legacy_UserManager"), await result);

        // Al cambiar: el nombre no se toca y la contraseña vacía no se cambia.
        var edit = new UserDialog(new UserDialogRequest(new DbUser("PEPE", "José", Plugin: "Srp"), ["Srp"]));
        Assert.False(edit.UserName.IsEnabled);
        Assert.False(edit.Plugin.IsVisible);
        Assert.Null(edit.Validate(isNew: false));
        owner.Close();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
