using FbStudio.Core.Security;

namespace FbStudio.Core.Tests;

public class SecurityScriptTests
{
    [Theory]
    [InlineData("CREATE USER JUAN PASSWORD 'secreto'", "CREATE USER JUAN PASSWORD '********'")]
    [InlineData("alter user juan set password 'it''s' firstname 'Juan'", "alter user juan set password '********' firstname 'Juan'")]
    [InlineData("SELECT 'PASSWORD' FROM RDB$DATABASE", "SELECT 'PASSWORD' FROM RDB$DATABASE")]
    public void Las_contrasenas_escritas_en_SQL_no_quedan_guardadas(string sql, string kept) =>
        Assert.Equal(kept, SqlSecrets.Mask(sql));

    [Fact]
    public void Esconde_la_contrasena_tambien_con_las_comillas_dobladas()
    {
        var statement = new SecurityStatement("ALTER USER JUAN PASSWORD 'it''s'", "ALTER USER JUAN", "it's");
        Assert.Equal("ALTER USER JUAN PASSWORD '********'", statement.Hide(statement.Sql));
        Assert.Equal("No se pudo cambiar a ********", statement.Hide("No se pudo cambiar a it's"));
    }

    private static DbGrant G(char p, bool go = false, string grantor = "SYSDBA", string? field = null) =>
        new("PEPE", GranteeKind.User, "TURNOS", GrantObjectKind.Table, p, field, go, grantor);

    [Fact]
    public void Sin_cambios_no_hay_sentencias_y_los_permisos_que_no_aplican_se_ignoran()
    {
        Assert.Empty(SecurityScript.Changes(GranteeKind.User, "PEPE", GrantObjectKind.Table, "TURNOS", [G('S'), G('U')], "US", false, "SYSDBA"));
        // EXECUTE no existe sobre una tabla; USAGE sí sobre un generador.
        Assert.Empty(SecurityScript.Changes(GranteeKind.User, "PEPE", GrantObjectKind.Table, "TURNOS", [], "X", false, "SYSDBA"));
        Assert.Equal("GRANT USAGE ON GENERATOR G_TURNOS TO PEPE",
            SecurityScript.Changes(GranteeKind.User, "PEPE", GrantObjectKind.Generator, "G_TURNOS", [], "GS", false, "SYSDBA").Single().Sql);
    }

    [Fact]
    public void Los_permisos_de_algunas_columnas_no_se_tocan()
    {
        var changes = SecurityScript.Changes(GranteeKind.User, "PEPE", GrantObjectKind.Table, "TURNOS", [G('U', field: "OBS")], "", false, "SYSDBA");
        Assert.Empty(changes);
    }

    [Fact]
    public void Comillas_cuando_hacen_falta_y_GRANTED_BY_para_lo_que_dio_otro()
    {
        var changes = SecurityScript.Changes(GranteeKind.Role, "Lectura", GrantObjectKind.Procedure, "mi proc",
            [new DbGrant("Lectura", GranteeKind.Role, "mi proc", GrantObjectKind.Procedure, 'X', null, false, "DUEÑO")], "", false, "SYSDBA");
        Assert.Equal("REVOKE EXECUTE ON PROCEDURE \"mi proc\" FROM ROLE \"Lectura\" GRANTED BY \"DUEÑO\"", changes.Single().Sql);
        Assert.Equal("GRANT SELECT ON T TO PUBLIC", SecurityScript.Changes(GranteeKind.User, "PUBLIC", GrantObjectKind.Table, "T", [], "S", false, "X").Single().Sql);
        Assert.Equal("REVOKE R_LECTURA FROM PEPE", SecurityScript.RevokeRole("R_LECTURA", "PEPE", "sysdba", "SYSDBA").Sql);
    }

    [Fact]
    public void Revoca_agrupando_por_quien_lo_dio()
    {
        var changes = SecurityScript.Changes(GranteeKind.User, "PEPE", GrantObjectKind.Table, "TURNOS",
            [G('S', grantor: "ANA"), G('I', grantor: "ANA"), G('D')], "", false, "SYSDBA");
        Assert.Equal(["REVOKE SELECT, INSERT ON TURNOS FROM PEPE GRANTED BY ANA", "REVOKE DELETE ON TURNOS FROM PEPE"], changes.Select(c => c.Sql));
    }

    [Fact]
    public void La_contraseña_no_se_muestra()
    {
        var create = SecurityScript.CreateUser(new UserEdit("pepe", "Mi'Clave", "José", null, null, false, true, "Turnos", "Legacy_UserManager"));
        Assert.Equal("CREATE USER \"pepe\" PASSWORD 'Mi''Clave' FIRSTNAME 'José' GRANT ADMIN ROLE INACTIVE USING PLUGIN Legacy_UserManager", create[0].Sql);
        Assert.Equal("CREATE USER \"pepe\" PASSWORD '********' FIRSTNAME 'José' GRANT ADMIN ROLE INACTIVE USING PLUGIN Legacy_UserManager", create[0].Shown);
        Assert.Equal("COMMENT ON USER \"pepe\" IS 'Turnos'", create[1].Sql);
        Assert.Equal("error con ******** adentro", create[0].Hide("error con Mi'Clave adentro"));

        // Solo el comentario cambió: solo COMMENT.
        var before = new DbUser("PEPE", "José", Description: "Turnos", Plugin: "Srp");
        Assert.Equal(["COMMENT ON USER PEPE IS NULL"],
            SecurityScript.AlterUser(before, new UserEdit("PEPE", "", "José", null, null, true, false, " ", null)).Select(s => s.Sql));
        Assert.Throws<ArgumentException>(() => SecurityScript.CreateUser(new UserEdit("X", "", null, null, null, true, false, null, null)));
    }
}
