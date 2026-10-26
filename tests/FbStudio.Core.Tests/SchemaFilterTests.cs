using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

/// <summary>Limitar el script y la comparación a un área (F-9).</summary>
public class SchemaFilterTests
{
    private static FieldDefinition F(string name, string type = "INTEGER") => new(name, type);

    [Fact]
    public void Deja_los_objetos_del_area_sus_dominios_y_permisos_y_saca_las_FK_a_tablas_de_afuera()
    {
        var schema = new DatabaseSchema
        {
            Domains = [new(F("D_IMPORTE", "NUMERIC(12,2)"), null), new(F("D_OTRO", "VARCHAR(10)"), null)],
            Roles = [new("R_TURNOS", "SYSDBA"), new("R_OTRO", "SYSDBA")],
            Generators = [new("G_TURNOS", 10), new("G_OTRO", 5)],
            Tables =
            [
                new("TUR_A", [F("ID"), F("IMPORTE", "D_IMPORTE")], [new("PK_A", ConstraintKind.PrimaryKey, ["ID"])], [], []),
                new("TUR_B", [F("ID"), F("ID_A"), F("ID_OTRA")],
                [
                    new("FK_B_A", ConstraintKind.ForeignKey, ["ID_A"], ReferencedTable: "TUR_A", ReferencedColumns: ["ID"]),
                    new("FK_B_OTRA", ConstraintKind.ForeignKey, ["ID_OTRA"], ReferencedTable: "OTRA", ReferencedColumns: ["ID"]),
                ], [], []),
                new("OTRA", [F("ID")], [], [], []),
            ],
            Procedures = [new("TUR_P", [F("DESDE", "D_IMPORTE")], [], "BEGIN END"), new("OTRO_P", [], [], "BEGIN END")],
            Privileges = [new("R_TURNOS", 13, "S", 0, "TUR_A", 0), new("R_OTRO", 13, "S", 0, "OTRA", 0)],
            IdentityValues = [new("TUR_A", "ID", 3), new("OTRA", "ID", 9)],
        };
        var area = SchemaFilter.Only(schema, "Turnos", new HashSet<string> { "TUR_A", "TUR_B", "TUR_P", "G_TURNOS" });

        Assert.Equal("Turnos", area.Area);
        Assert.Equal(["TUR_A", "TUR_B"], area.Tables.Select(t => t.Name));
        Assert.Equal(["FK_B_A"], area.Tables[1].Constraints.Select(c => c.Name));     // la FK a OTRA no va
        Assert.Equal(1, area.ForeignKeysOutside);
        Assert.Equal(["TUR_P"], area.Procedures.Select(p => p.Name));
        Assert.Equal(["D_IMPORTE"], area.Domains.Select(d => d.Field.Name));          // el que usan; el otro no
        Assert.Equal(["G_TURNOS"], area.Generators.Select(g => g.Name));
        Assert.Equal(["TUR_A"], area.Privileges.Select(p => p.Object));
        Assert.Equal(["R_TURNOS"], area.Roles.Select(r => r.Name));
        Assert.Equal(["TUR_A"], area.IdentityValues.Select(i => i.Table));
    }
}
