using FbStudio.Core.Metadata;

namespace FbStudio.Firebird.Tests;

[Collection(nameof(FirebirdCollection))]
public class MetadataExplorerTests(FirebirdFixture fb)
{
    [Fact]
    public async Task Encuentra_un_objeto_por_su_nombre()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        Assert.Equal(new DbObject(DbObjectKind.Table, "TURNOS"), await ex.FindObjectAsync("TURNOS"));
        Assert.Equal(DbObjectKind.View, (await ex.FindObjectAsync("V_TURNOS"))!.Kind);
        Assert.Equal(DbObjectKind.Procedure, (await ex.FindObjectAsync("P_CONTAR"))!.Kind);
        Assert.Equal(DbObjectKind.Function, (await ex.FindObjectAsync("F_DOBLE"))!.Kind);
        Assert.Equal(DbObjectKind.Package, (await ex.FindObjectAsync("PKG_TURNOS"))!.Kind);
        Assert.Equal(DbObjectKind.Generator, (await ex.FindObjectAsync("G_TURNOS"))!.Kind);
        Assert.Equal(DbObjectKind.Exception, (await ex.FindObjectAsync("E_TURNO"))!.Kind);
        Assert.Equal(new DbObject(DbObjectKind.Trigger, "TURNOS_BI", "TURNOS"), await ex.FindObjectAsync("TURNOS_BI"));
        Assert.Equal(new DbObject(DbObjectKind.Index, "TURNOS_FECHA", "TURNOS"), await ex.FindObjectAsync("TURNOS_FECHA"));
        Assert.Equal(DbObjectKind.Domain, (await ex.FindObjectAsync("D_IMPORTE"))!.Kind);
        Assert.Equal(DbObjectKind.Role, (await ex.FindObjectAsync("R_LECTURA"))!.Kind);
        Assert.Null(await ex.FindObjectAsync("NO_EXISTE"));
        Assert.Null(await ex.FindObjectAsync("RDB$RELATIONS"));                            // las del sistema no
    }

    [Fact]
    public async Task Cuenta_los_objetos_de_cada_categoria()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var counts = await ex.CountAsync();

        Assert.Equal(2, counts[DbObjectKind.Table]);
        Assert.Equal(1, counts[DbObjectKind.View]);
        Assert.Equal(2, counts[DbObjectKind.Procedure]);
        Assert.Equal(1, counts[DbObjectKind.Function]);
        Assert.Equal(2, counts[DbObjectKind.Trigger]);
        Assert.Equal(1, counts[DbObjectKind.Generator]);
        Assert.Equal(1, counts[DbObjectKind.Exception]);
        Assert.Equal(2, counts[DbObjectKind.Domain]);
        Assert.Equal(1, counts[DbObjectKind.Role]);
        Assert.Equal(1, counts[DbObjectKind.Package]);
        Assert.True(counts[DbObjectKind.Index] >= 2);
    }

    [Fact]
    public async Task Lee_la_version_del_motor_y_los_datos_de_la_base()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var info = await ex.ServerInfoAsync();

        Assert.Equal(3, info.Major);
        Assert.True(info.IsTested);
        Assert.Null(info.Warning);
        Assert.StartsWith("Firebird 3.0.", info.Display);
        Assert.Equal((12, 3), (info.OdsMajor, info.Dialect));
        Assert.True(info.PageSize >= 4096);
        Assert.Same(info, ex.Server);
    }

    [Fact]
    public async Task Lee_tablas_columnas_y_FK_para_los_diagramas()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var graph = await ex.LoadSchemaGraphAsync();
        var fk = Assert.Single(graph.Links, l => l.Name == "FK_DET_TURNO");
        Assert.Equal(("TURNOS_DET", "TURNOS", false), (fk.Table, fk.Referenced, fk.IsLogical));
        Assert.Equal(["TURNO"], fk.Columns);
        Assert.Equal(["ID"], fk.ReferencedColumns);
        var turnos = graph.Table("TURNOS")!;
        Assert.True(turnos.Columns[0] is { Name: "ID", IsPrimaryKey: true, NotNull: true });
        Assert.Equal("INTEGER", turnos.Columns[0].Type);
        Assert.True(graph.Table("V_TURNOS")!.IsView);
        Assert.Contains("TURNOS_DET", graph.Neighbors("TURNOS"));
        // TURNOS.PACIENTE dice #FK_%PACIENTES… pero esa tabla no existe en la base de prueba: no se dibuja.
        Assert.DoesNotContain(graph.Links, l => l.IsLogical && l.Table == "TURNOS");
    }

    [Fact]
    public async Task Lee_que_tablas_usa_cada_procedimiento_vista_y_trigger_y_la_tabla_de_cada_trigger()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var uses = await ex.LoadTableUsesAsync();
        Assert.Contains("TURNOS", uses.Of(new DbObject(DbObjectKind.Procedure, "P_CONTAR")));
        Assert.Contains("TURNOS", uses.Of(new DbObject(DbObjectKind.Procedure, "P_TURNOS_DESDE")));
        Assert.Equal(["TURNOS"], uses.Of(new DbObject(DbObjectKind.View, "V_TURNOS")));
        Assert.DoesNotContain(uses.Of(new DbObject(DbObjectKind.View, "V_TURNOS")), t => t.StartsWith("RDB$"));

        var all = await ex.LoadAllObjectsAsync();
        Assert.Equal("TURNOS_DET", all.Single(o => o.Name == "TURNOS_DET_BIU").Owner);
        Assert.Equal("TURNOS", all.Single(o => o.Name == "TURNOS_FECHA").Owner);
        Assert.Null(all.Single(o => o.Name == "P_CONTAR").Owner);
    }

    [Fact]
    public async Task Lista_objetos_con_su_detalle()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);

        var trigger = Assert.Single(await ex.LoadObjectsAsync(DbObjectKind.Trigger), t => t.Name == "TURNOS_BI");
        Assert.Equal(("TURNOS_BI", "TURNOS", false), (trigger.Name, trigger.Detail, trigger.Inactive));

        var domain = Assert.Single(await ex.LoadObjectsAsync(DbObjectKind.Domain), d => d.Name == "D_IMPORTE");
        Assert.Equal(("D_IMPORTE", "NUMERIC(12,2)"), (domain.Name, domain.Detail));

        var indices = await ex.LoadObjectsAsync(DbObjectKind.Index);
        Assert.True(Assert.Single(indices, i => i.Name == "TURNOS_PACIENTE").Inactive);
        Assert.False(Assert.Single(indices, i => i.Name == "TURNOS_FECHA").Inactive);

        Assert.Equal("V_TURNOS", Assert.Single(await ex.LoadObjectsAsync(DbObjectKind.View)).Name);
        Assert.Equal(["P_CONTAR", "P_TURNOS_DESDE"], (await ex.LoadObjectsAsync(DbObjectKind.Procedure)).Select(p => p.Name));
        Assert.Equal(("F_DOBLE", "PSQL"), Assert.Single(await ex.LoadObjectsAsync(DbObjectKind.Function)) is var f ? (f.Name, f.Detail) : default);
    }

    [Fact]
    public async Task Columnas_con_tipo_PK_dominio_y_descripcion()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var cols = await ex.LoadColumnsAsync("TURNOS");

        Assert.Equal(["ID", "FECHA", "PACIENTE", "OBS", "IMPORTE"], cols.Select(c => c.Name));
        var id = cols[0];
        Assert.Equal(("INTEGER", true, true), (id.Type, id.NotNull, id.IsPrimaryKey));
        Assert.Equal("VARCHAR(60)", cols[2].Type);
        Assert.Equal("#FK_%PACIENTES,%CODIGO,%NOMBRE#", cols[2].Description);
        Assert.Equal(("NUMERIC(12,2)", "D_IMPORTE"), (cols[4].Type, cols[4].Domain));
        Assert.Null(cols[1].Domain); // dominio implícito RDB$… no se muestra
    }
}
