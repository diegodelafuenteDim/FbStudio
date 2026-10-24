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
