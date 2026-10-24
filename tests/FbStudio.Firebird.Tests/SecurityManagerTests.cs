using FbStudio.Core.Profiles;
using FbStudio.Core.Security;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Usuarios, roles y permisos (F-28) contra Firebird 3 en Docker.</summary>
[Collection(nameof(FirebirdCollection))]
public class SecurityManagerTests(FirebirdFixture fb)
{
    private ConnectionProfile Sysdba => fb.Profile with { Name = "docker-sysdba", User = "SYSDBA" };

    private static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static IReadOnlyCollection<DbGrant> On(SecuritySnapshot s, string grantee, string obj) =>
        s.Grants.Where(g => g.Grantee == grantee && g.Object == obj).ToList();

    private async Task<string> CurrentUserAsync(string user, string password)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile with { User = user }, password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand("SELECT TRIM(CURRENT_USER) FROM RDB$DATABASE", c, tx);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Lee_usuarios_roles_y_permisos_del_dueño()
    {
        var s = await SecurityManager.LoadAsync(fb.Profile, fb.Password);
        Assert.Equal(fb.Profile.User.ToUpperInvariant(), s.CurrentUser);
        Assert.False(s.CanManageUsers);
        Assert.Contains(s.Users, u => u.Name == s.CurrentUser);           // sin ser administrador, se ve a sí mismo
        Assert.Contains(s.Roles, r => r.Name == "R_LECTURA" && !r.IsSystem);
        // Quien crea una tabla tiene todos los permisos sobre ella, con GRANT OPTION.
        var own = On(s, s.CurrentUser, "TURNOS");
        Assert.Equal("DIRSU", string.Concat(own.Select(g => g.Privilege).Order()));
        Assert.All(own, g => Assert.True(g.GrantOption));
        Assert.All(own, g => Assert.Equal(GrantObjectKind.Table, g.ObjectKind));
        Assert.Contains(s.Grants, g => g.Object == "V_TURNOS" && g.ObjectKind == GrantObjectKind.View);
        Assert.DoesNotContain(s.Grants, g => g.Object.StartsWith("RDB$", StringComparison.Ordinal));

        var admin = await SecurityManager.LoadAsync(Sysdba, fb.Password);
        Assert.True(admin.CanManageUsers);
        Assert.Contains(admin.Users, u => u.Name == "SYSDBA" && u.Plugin == "Srp");
    }

    [Fact]
    public async Task Crea_cambia_y_borra_un_usuario_sin_mostrar_la_contraseña()
    {
        var name = NewName("FBS_");
        var create = SecurityScript.CreateUser(new UserEdit(name, "Clave.1", "María", null, "Pérez", true, false, "Usuario de prueba", null));
        Assert.All(create, st => Assert.DoesNotContain("Clave.1", st.Shown));
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, create);
        var user = (await SecurityManager.LoadAsync(Sysdba, fb.Password)).Users.Single(u => u.Name == name);
        Assert.Equal(("María Pérez", true, false, "Usuario de prueba"), (user.FullName, user.Active, user.Admin, user.Description));
        Assert.Equal(name, await CurrentUserAsync(name, "Clave.1"));

        var alter = SecurityScript.AlterUser(user, new UserEdit(name, "Clave.2", "María", "José", "Gómez", true, false, "", null));
        Assert.Equal($"ALTER USER {name} PASSWORD '********' MIDDLENAME 'José' LASTNAME 'Gómez' USING PLUGIN Srp", alter[0].Shown);
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, alter);
        user = (await SecurityManager.LoadAsync(Sysdba, fb.Password)).Users.Single(u => u.Name == name);
        Assert.Equal(("María José Gómez", (string?)null), (user.FullName, user.Description));
        Assert.Equal(name, await CurrentUserAsync(name, "Clave.2"));

        // Sin cambiar nada no hay sentencias; desactivarlo no deja entrar.
        Assert.Empty(SecurityScript.AlterUser(user, new UserEdit(name, null, "María", "José", "Gómez", true, false, null, null)));
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, SecurityScript.AlterUser(user, new UserEdit(name, null, "María", "José", "Gómez", false, false, null, null)));
        await Assert.ThrowsAnyAsync<FbException>(() => CurrentUserAsync(name, "Clave.2"));

        // Si una sentencia falla no queda nada aplicado, y el error no trae la contraseña.
        var again = SecurityScript.CreateUser(new UserEdit(name, "Otra.3", null, null, null, true, false, null, null));
        var role = NewName("R_");
        var error = await Assert.ThrowsAsync<SecurityStatementException>(() =>
            SecurityManager.ExecuteAsync(Sysdba, fb.Password, [SecurityScript.CreateRole(role), .. again]));
        Assert.Equal(-1, error.Index);                                     // los usuarios se graban al confirmar
        Assert.DoesNotContain("Otra.3", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain((await SecurityManager.LoadAsync(Sysdba, fb.Password)).Roles, r => r.Name == role);

        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, [SecurityScript.DropUser(user)]);
        Assert.DoesNotContain((await SecurityManager.LoadAsync(Sysdba, fb.Password)).Users, u => u.Name == name);
    }

    [Fact]
    public async Task Da_y_quita_permisos_a_usuarios_roles_y_procedimientos()
    {
        var user = NewName("FBS_");
        var role = NewName("R_");
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password,
        [
            .. SecurityScript.CreateUser(new UserEdit(user, "Clave.1", null, null, null, true, false, null, null)),
            SecurityScript.CreateRole(role),
        ]);
        var s = await SecurityManager.LoadAsync(Sysdba, fb.Password);

        // A un usuario: tabla, vista, procedimiento, generador y excepción.
        List<SecurityStatement> grant =
        [
            .. SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Table, "TURNOS", [], "SI", false, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.View, "V_TURNOS", [], "S", false, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Procedure, "P_CONTAR", [], "X", false, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Generator, "G_TURNOS", [], "G", false, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Exception, "E_TURNO", [], "G", false, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.Role, role, GrantObjectKind.Table, "TURNOS_DET", [], "SU", true, s.CurrentUser),
            .. SecurityScript.Changes(GranteeKind.Procedure, "P_TURNOS_DESDE", GrantObjectKind.Table, "TURNOS", [], "S", false, s.CurrentUser),
            SecurityScript.GrantRole(role, user),
        ];
        Assert.Equal($"GRANT SELECT, INSERT ON TURNOS TO {user}", grant[0].Sql);
        Assert.Equal($"GRANT SELECT, UPDATE ON TURNOS_DET TO ROLE {role} WITH GRANT OPTION", grant[5].Sql);
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, grant);
        s = await SecurityManager.LoadAsync(Sysdba, fb.Password);
        Assert.Equal("IS", string.Concat(On(s, user, "TURNOS").Select(g => g.Privilege).Order()));
        Assert.Equal(GrantObjectKind.View, On(s, user, "V_TURNOS").Single().ObjectKind);
        Assert.Equal(('X', GrantObjectKind.Procedure), (On(s, user, "P_CONTAR").Single().Privilege, On(s, user, "P_CONTAR").Single().ObjectKind));
        Assert.Equal(('G', GrantObjectKind.Generator), (On(s, user, "G_TURNOS").Single().Privilege, On(s, user, "G_TURNOS").Single().ObjectKind));
        Assert.Equal(GrantObjectKind.Exception, On(s, user, "E_TURNO").Single().ObjectKind);
        Assert.All(On(s, role, "TURNOS_DET"), g => Assert.Equal((GranteeKind.Role, true), (g.GranteeKind, g.GrantOption)));
        Assert.Equal(GranteeKind.Procedure, On(s, "P_TURNOS_DESDE", "TURNOS").Single().GranteeKind);
        Assert.Equal(('M', GrantObjectKind.Role), (On(s, user, role).Single().Privilege, On(s, user, role).Single().ObjectKind));

        // De SELECT + INSERT a SELECT con GRANT OPTION; al rol se le saca el GRANT OPTION.
        var change = SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Table, "TURNOS", On(s, user, "TURNOS"), "S", true, s.CurrentUser)
            .Concat(SecurityScript.Changes(GranteeKind.Role, role, GrantObjectKind.Table, "TURNOS_DET", On(s, role, "TURNOS_DET"), "SU", false, s.CurrentUser))
            .ToList();
        Assert.Equal([$"REVOKE INSERT ON TURNOS FROM {user}", $"GRANT SELECT ON TURNOS TO {user} WITH GRANT OPTION",
            $"REVOKE GRANT OPTION FOR SELECT, UPDATE ON TURNOS_DET FROM ROLE {role}"], change.Select(c => c.Sql));
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, change);
        s = await SecurityManager.LoadAsync(Sysdba, fb.Password);
        Assert.Equal(('S', true), (On(s, user, "TURNOS").Single().Privilege, On(s, user, "TURNOS").Single().GrantOption));
        Assert.All(On(s, role, "TURNOS_DET"), g => Assert.False(g.GrantOption));

        // Lo que dio otro (el dueño de la tabla) lo quita el administrador con GRANTED BY.
        await SecurityManager.ExecuteAsync(fb.Profile, fb.Password,
            SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Table, "TURNOS_DET", [], "D", false, fb.Profile.User));
        s = await SecurityManager.LoadAsync(Sysdba, fb.Password);
        var byOwner = On(s, user, "TURNOS_DET").Single();
        Assert.Equal(fb.Profile.User.ToUpperInvariant(), byOwner.Grantor);
        var revoke = SecurityScript.Changes(GranteeKind.User, user, GrantObjectKind.Table, "TURNOS_DET", [byOwner], "", false, s.CurrentUser);
        Assert.Equal($"REVOKE DELETE ON TURNOS_DET FROM {user} GRANTED BY {byOwner.Grantor}", revoke.Single().Sql);
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password, [.. revoke, SecurityScript.RevokeRole(role, user, On(s, user, role).Single().Grantor, s.CurrentUser)]);
        s = await SecurityManager.LoadAsync(Sysdba, fb.Password);
        Assert.Empty(On(s, user, "TURNOS_DET"));
        Assert.Empty(On(s, user, role));

        // Se deja la base como estaba: otras pruebas cuentan sus objetos y arman el script con sus permisos.
        await SecurityManager.ExecuteAsync(Sysdba, fb.Password,
        [
            new SecurityStatement($"REVOKE ALL ON ALL FROM {user}"),
            new SecurityStatement("REVOKE SELECT ON TURNOS FROM PROCEDURE P_TURNOS_DESDE"),
            SecurityScript.DropRole(role),
            SecurityScript.DropUser(s.Users.Single(u => u.Name == user)),
        ]);
        Assert.DoesNotContain((await SecurityManager.LoadAsync(Sysdba, fb.Password)).Grants, g => g.Grantee == user || g.Grantee == "P_TURNOS_DESDE");
    }

    [Fact]
    public async Task Con_un_registro_de_solo_lectura_no_ejecuta()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SecurityManager.ExecuteAsync(fb.Profile with { ReadOnly = true }, fb.Password, [SecurityScript.CreateRole("R_NUNCA")]));
    }
}
