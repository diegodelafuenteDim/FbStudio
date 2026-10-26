using FbStudio.Core.Metadata;

namespace FbStudio.Firebird.Tests;

[Collection(nameof(FirebirdCollection))]
public class DependenciesTests(FirebirdFixture fb)
{
    private async Task<IReadOnlyList<DbDependency>> Dependencies(DbObjectKind kind, string name)
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        return await ex.DependenciesAsync(new DbObject(kind, name));
    }

    private static DbDependency Find(IReadOnlyList<DbDependency> all, DependencyDirection direction, DbObjectKind kind, string name) =>
        Assert.Single(all, d => d.Direction == direction && d.Kind == kind && d.Name == name);

    [Fact]
    public async Task Una_tabla_es_usada_por_vistas_procedimientos_paquetes_y_FK()
    {
        var deps = await Dependencies(DbObjectKind.Table, "TURNOS");

        Find(deps, DependencyDirection.UsedBy, DbObjectKind.View, "V_TURNOS");
        Find(deps, DependencyDirection.UsedBy, DbObjectKind.Procedure, "P_CONTAR");
        Assert.Contains("FECHA", Find(deps, DependencyDirection.UsedBy, DbObjectKind.Procedure, "P_TURNOS_DESDE").Detail);
        Find(deps, DependencyDirection.UsedBy, DbObjectKind.Package, "PKG_TURNOS");
        Assert.Equal("FK_DET_TURNO", Find(deps, DependencyDirection.UsedBy, DbObjectKind.Table, "TURNOS_DET").Detail);
        Assert.DoesNotContain(deps, d => d.Name == "TURNOS"); // sin referencias a sí misma
    }

    [Fact]
    public async Task Donde_se_usa_una_columna()
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var id = await ex.ColumnUsageAsync("TURNOS", "ID");
        Find(id, DependencyDirection.UsedBy, DbObjectKind.View, "V_TURNOS");
        Find(id, DependencyDirection.UsedBy, DbObjectKind.Procedure, "P_TURNOS_DESDE");
        Assert.Equal("FK_DET_TURNO (TURNO)", Find(id, DependencyDirection.UsedBy, DbObjectKind.Table, "TURNOS_DET").Detail);
        Assert.StartsWith("PRIMARY KEY", Assert.Single(id, d => d.Kind == DbObjectKind.Index).Detail);
        Assert.DoesNotContain(id, d => d.Name == "P_CONTAR");                         // usa la tabla, no esta columna

        var fecha = await ex.ColumnUsageAsync("TURNOS", "FECHA");
        Assert.Null(Find(fecha, DependencyDirection.UsedBy, DbObjectKind.Index, "TURNOS_FECHA").Detail);
        Assert.DoesNotContain(fecha, d => d.Name == "TURNOS_DET");

        // La FK de la columna misma: TURNOS_DET.TURNO apunta a TURNOS.
        var turno = await ex.ColumnUsageAsync("TURNOS_DET", "TURNO");
        Assert.Equal("FK_DET_TURNO", Find(turno, DependencyDirection.Uses, DbObjectKind.Table, "TURNOS").Detail);
    }

    [Fact]
    public async Task Un_procedimiento_usa_tablas_con_sus_columnas()
    {
        var deps = await Dependencies(DbObjectKind.Procedure, "P_TURNOS_DESDE");
        var turnos = Find(deps, DependencyDirection.Uses, DbObjectKind.Table, "TURNOS");
        Assert.Equal(["FECHA", "ID"], turnos.Detail!.Split(", ").Order());
        Assert.DoesNotContain(deps, d => d.Direction == DependencyDirection.UsedBy);
    }

    [Fact]
    public async Task Tabla_con_FK_trigger_dominio_y_columna_calculada()
    {
        var deps = await Dependencies(DbObjectKind.Table, "TURNOS_DET");
        Assert.Equal("FK_DET_TURNO", Find(deps, DependencyDirection.Uses, DbObjectKind.Table, "TURNOS").Detail);
        Find(deps, DependencyDirection.UsedBy, DbObjectKind.Trigger, "TURNOS_DET_BIU");

        var domain = await Dependencies(DbObjectKind.Domain, "D_CANTIDAD");
        Assert.Equal("CANTIDAD", Find(domain, DependencyDirection.UsedBy, DbObjectKind.Table, "TURNOS_DET").Detail);

        var trigger = await Dependencies(DbObjectKind.Trigger, "TURNOS_DET_BIU");
        Assert.Contains("NOTA", Find(trigger, DependencyDirection.Uses, DbObjectKind.Table, "TURNOS_DET").Detail);
    }
}
