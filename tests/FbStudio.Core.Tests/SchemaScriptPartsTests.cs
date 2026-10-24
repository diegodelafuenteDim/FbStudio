using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class SchemaScriptPartsTests
{
    private static readonly DatabaseSchema Schema = new()
    {
        Generators = [new GeneratorDefinition("G_PEDIDOS", 40)],
        Tables =
        [
            new TableDefinition("CLIENTES", [new FieldDefinition("ID", "INTEGER", NotNull: true)],
                [new ConstraintDefinition("PK_CLIENTES", ConstraintKind.PrimaryKey, ["ID"])], [], []),
            new TableDefinition("PEDIDOS",
                [new FieldDefinition("ID", "INTEGER", NotNull: true, Identity: true), new FieldDefinition("CLIENTE", "INTEGER"), new FieldDefinition("NOTA", "VARCHAR(20)")],
                [
                    new ConstraintDefinition("PK_PEDIDOS", ConstraintKind.PrimaryKey, ["ID"]),
                    new ConstraintDefinition("FK_PEDIDOS_CLIENTE", ConstraintKind.ForeignKey, ["CLIENTE"], ReferencedTable: "CLIENTES", ReferencedColumns: ["ID"]),
                    new ConstraintDefinition("CHK_NOTA", ConstraintKind.Check, [], CheckSource: "CHECK (NOTA <> '')"),
                ],
                [new IndexDefinition("PEDIDOS_NOTA", "PEDIDOS", ["NOTA"])],
                [new TriggerDefinition("PEDIDOS_BI", "PEDIDOS", 1, 0, false, "AS BEGIN END", "Arma la nota")]),
            new TableDefinition("RENGLONES", [new FieldDefinition("PEDIDO", "INTEGER")],
                [new ConstraintDefinition("FK_RENGLONES_PEDIDO", ConstraintKind.ForeignKey, ["PEDIDO"], ReferencedTable: "PEDIDOS", ReferencedColumns: ["ID"])], [], []),
            new TableDefinition("SUELTA", [new FieldDefinition("X", "INTEGER")], [], [], []),
        ],
        Privileges = [new PrivilegeDefinition("PUBLIC", 8, "S", 0, "PEDIDOS", 0)],
        IdentityValues = [new IdentityValue("PEDIDOS", "ID", 123), new IdentityValue("CLIENTES", "ID", 0)],
    };

    [Fact]
    public void La_estructura_deja_para_despues_las_FK_los_CHECK_los_triggers_y_los_permisos()
    {
        var structure = SchemaScriptWriter.Write(Schema, new SchemaScriptOptions(), parts: SchemaScriptParts.Structure);
        Assert.Contains("CREATE TABLE PEDIDOS", structure);
        Assert.Contains("CREATE INDEX PEDIDOS_NOTA", structure);
        Assert.Contains("SET GENERATOR G_PEDIDOS TO 40;", structure);
        Assert.DoesNotContain("FOREIGN KEY", structure);
        Assert.DoesNotContain("CHECK (NOTA", structure);
        Assert.DoesNotContain("PEDIDOS_BI", structure);
        Assert.DoesNotContain("GRANT", structure);

        var deferred = SchemaScriptWriter.Write(Schema, new SchemaScriptOptions(), parts: SchemaScriptParts.Deferred);
        Assert.DoesNotContain("CREATE TABLE", deferred);
        Assert.DoesNotContain("CREATE INDEX", deferred);
        Assert.Contains("FOREIGN KEY (CLIENTE) REFERENCES CLIENTES (ID)", deferred);
        Assert.Contains("CHECK (NOTA <> '')", deferred);
        Assert.Contains("CREATE OR ALTER TRIGGER PEDIDOS_BI", deferred);
        Assert.Contains("COMMENT ON TRIGGER PEDIDOS_BI IS 'Arma la nota';", deferred);
        Assert.Contains("GRANT SELECT ON PEDIDOS TO PUBLIC;", deferred);

        // Entero, con todo y en el orden de siempre: las FK antes que los índices, los triggers antes que los permisos.
        var all = SchemaScriptWriter.Write(Schema, new SchemaScriptOptions());
        Assert.True(all.IndexOf("FOREIGN KEY", StringComparison.Ordinal) < all.IndexOf("CREATE INDEX", StringComparison.Ordinal));
        Assert.True(all.IndexOf("CREATE OR ALTER TRIGGER", StringComparison.Ordinal) < all.IndexOf("GRANT", StringComparison.Ordinal));
    }

    [Fact]
    public void La_identity_queda_con_su_valor_actual_por_GEN_ID_y_sin_valores_no_se_toca()
    {
        var script = SchemaScriptWriter.Write(Schema, new SchemaScriptOptions(), parts: SchemaScriptParts.Structure);
        Assert.Contains("WHERE RDB$RELATION_NAME = 'PEDIDOS' AND RDB$FIELD_NAME = 'ID' INTO :G;", script);
        Assert.Contains("' || (123 - V) || ') FROM RDB$DATABASE' INTO :V;", script);
        Assert.DoesNotContain("'CLIENTES' AND RDB$FIELD_NAME", script);                  // en 0 queda como en una base nueva
        Assert.True(script.IndexOf("EXECUTE BLOCK", StringComparison.Ordinal) > script.IndexOf("CREATE TABLE PEDIDOS", StringComparison.Ordinal));

        var withoutValues = SchemaScriptWriter.Write(Schema, new SchemaScriptOptions { GeneratorValues = false });
        Assert.DoesNotContain("EXECUTE BLOCK", withoutValues);
        Assert.DoesNotContain("SET GENERATOR", withoutValues);
    }

    [Fact]
    public void Se_copian_tambien_las_tablas_que_referencian_las_FK_de_las_elegidas()
    {
        Assert.Equal(["CLIENTES", "PEDIDOS", "RENGLONES"], TableSelection.WithReferenced(Schema, ["RENGLONES"]));
        Assert.Equal(["CLIENTES", "SUELTA"], TableSelection.WithReferenced(Schema, ["SUELTA", "CLIENTES", "NO_EXISTE"]));
        Assert.Empty(TableSelection.WithReferenced(Schema, []));
    }
}
