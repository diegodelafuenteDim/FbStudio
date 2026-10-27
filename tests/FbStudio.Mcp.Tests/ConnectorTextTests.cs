using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Mcp;

namespace FbStudio.Mcp.Tests;

public sealed class ConnectorTextTests
{
    private static readonly ConnectionProfile Profile = new()
    {
        Name = "Pruebas", Host = "srv-secreto.empresa.local", Database = "/datos/secreta.fdb", User = "OPERADOR",
    };

    [Fact]
    public void Los_errores_no_muestran_servidor_ruta_usuario_ni_valores()
    {
        var text = ConnectorText.Error(new Exception(
            "I/O error during \"open\" operation for file \"/datos/secreta.fdb\" on srv-secreto.empresa.local for OPERADOR. " +
            "violation of PRIMARY or UNIQUE KEY constraint \"PK_PACIENTES\" on table \"PACIENTES\" Problematic key value is (\"DNI\" = '20123456'). " +
            "conversion error from string \"Pérez, Juana\". Token unknown 'secreto'"), Profile);

        foreach (var hidden in new[] { "srv-secreto", "/datos", "secreta.fdb", "OPERADOR", "20123456", "Pérez", "secreto'" })
            Assert.DoesNotContain(hidden, text);
        Assert.Contains("Pruebas", text);                                       // la base, por el nombre del registro
        Assert.Contains("PK_PACIENTES", text);                                   // los nombres de objetos sí quedan
        Assert.Contains("Problematic key value is (?)", text);
        Assert.Contains("conversion error from string \"?\"", text);
    }

    [Fact]
    public void Una_respuesta_larga_se_recorta_en_un_renglon_y_dice_como_pedir_menos()
    {
        var text = string.Join('\n', Enumerable.Range(0, 5000).Select(i => $"TABLE TABLA_{i:0000}"));
        var cut = ConnectorText.Truncate(text, 1000);
        Assert.True(cut.Length < 1200);
        Assert.Contains("[Truncated:", cut);
        Assert.EndsWith("the next offset.]", cut);
        Assert.Equal("corto", ConnectorText.Truncate("corto", 1000));
    }

    [Theory]
    [InlineData("SELECT * FROM TURNOS")]
    [InlineData("with x as (select 1 a from rdb$database) select * from x")]
    [InlineData("UPDATE TURNOS SET OBS = 'x' WHERE ID = 1")]
    [InlineData("DELETE FROM TURNOS")]
    [InlineData("INSERT INTO TURNOS (ID) SELECT ID + 1 FROM TURNOS")]
    [InlineData("MERGE INTO A USING B ON A.ID = B.ID WHEN MATCHED THEN DELETE")]
    [InlineData("EXECUTE BLOCK AS BEGIN DELETE FROM TURNOS; END")]
    public void Explain_acepta_lo_que_tiene_plan(string sql) => StructureTools.CheckExplainable(sql);

    [Theory]
    [InlineData("DROP TABLE TURNOS")]
    [InlineData("ALTER TABLE TURNOS ADD X INTEGER")]
    [InlineData("GRANT SELECT ON TURNOS TO PUBLIC")]
    [InlineData("COMMIT")]
    [InlineData("SET GENERATOR G_TURNOS TO 0")]
    [InlineData("EXECUTE PROCEDURE P_TURNOS_DESDE 1")]
    [InlineData("SET TERM ^ ;")]
    [InlineData("")]
    public void Explain_rechaza_lo_demas(string sql) =>
        Assert.Throws<ConnectorException>(() => StructureTools.CheckExplainable(sql));

    [Fact]
    public void Entiende_los_tipos_de_objeto_en_singular_o_plural()
    {
        Assert.Equal(new HashSet<DbObjectKind> { DbObjectKind.Table, DbObjectKind.Procedure, DbObjectKind.Index, DbObjectKind.Generator },
            StructureTools.ParseKinds("tables, PROCEDURE indexes sequence"));
        Assert.Null(StructureTools.ParseKinds(" "));
        Assert.Throws<ConnectorException>(() => StructureTools.ParseKinds("rows"));
    }
}
