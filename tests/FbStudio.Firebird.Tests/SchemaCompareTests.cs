using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Comparar bases contra Firebird 3 en Docker: una copia cambiada a propósito vuelve a quedar igual con el script.</summary>
[Collection(nameof(FirebirdCollection))]
public class SchemaCompareTests(FirebirdFixture fb)
{
    [Fact]
    public async Task El_script_para_igualar_deja_la_copia_igual_al_origen()
    {
        var copy = await CopySchemaAsync("/firebird/data/copia_comparar.fdb");
        await RunScriptAsync(copy, """
            ALTER TABLE TURNOS DROP OBS;
            ALTER TABLE TURNOS ALTER FECHA SET NOT NULL;
            ALTER TABLE TURNOS_DET ALTER NOTA SET DEFAULT 'x';
            ALTER TABLE TURNOS_DET DROP CONSTRAINT FK_DET_TURNO;
            DROP INDEX TURNOS_FECHA;
            ALTER INDEX TURNOS_PACIENTE ACTIVE;
            DROP VIEW V_TURNOS;
            DROP TRIGGER TURNOS_BI;
            COMMENT ON TABLE TURNOS IS 'otro comentario';
            ALTER SEQUENCE G_TURNOS INCREMENT BY 5;
            CREATE OR ALTER EXCEPTION E_TURNO 'otro mensaje';
            CREATE TABLE SOBRA (ID INTEGER);
            COMMIT;
            SET TERM ^ ;
            CREATE OR ALTER PROCEDURE P_CONTAR RETURNS (N INTEGER) AS BEGIN N = 0; SUSPEND; END^
            SET TERM ; ^
            COMMIT;
            """);

        await using var source = new MetadataExplorer(fb.Profile, fb.Password);
        await using var target = new MetadataExplorer(copy, fb.Password);
        var everything = new SchemaScriptOptions { Privileges = false };
        var differences = SchemaComparer.Compare(await source.ReadSchemaAsync(everything), await target.ReadSchemaAsync(everything),
            new SchemaCompareOptions { DropExtra = true, Privileges = false });

        SchemaDifference Find(SchemaItemKind kind, string name) => Assert.Single(differences, d => d.Kind == kind && d.Name == name);
        var turnos = Find(SchemaItemKind.Table, "TURNOS");
        Assert.Equal(SchemaChange.Changed, turnos.Change);
        Assert.Contains("columna nueva OBS: VARCHAR(2000)", turnos.Details);
        Assert.Contains("columna FECHA: pasa a admitir NULL", turnos.Details);
        Assert.Contains("comentario distinto", turnos.Details);
        Assert.Contains("orden de las columnas", turnos.Details);              // OBS vuelve a su lugar
        var detalle = Find(SchemaItemKind.Table, "TURNOS_DET");
        Assert.Contains("columna NOTA: valor por defecto DEFAULT 'x' → DEFAULT '-'", detalle.Details);
        Assert.Contains("restricción nueva FK_DET_TURNO", detalle.Details);
        Assert.Equal(SchemaChange.Added, Find(SchemaItemKind.Index, "TURNOS_FECHA").Change);
        Assert.Equal(["pasa a inactivo"], Find(SchemaItemKind.Index, "TURNOS_PACIENTE").Details);
        Assert.Equal(["definición distinta"], Find(SchemaItemKind.Procedure, "P_CONTAR").Details);
        Assert.Equal(SchemaChange.Added, Find(SchemaItemKind.View, "V_TURNOS").Change);
        Assert.Equal(SchemaChange.Added, Find(SchemaItemKind.Trigger, "TURNOS_BI").Change);
        Assert.Equal(SchemaChange.Removed, Find(SchemaItemKind.Table, "SOBRA").Change);
        Assert.Equal(SchemaChange.Changed, Find(SchemaItemKind.Generator, "G_TURNOS").Change);
        Assert.Equal(SchemaChange.Changed, Find(SchemaItemKind.Exception, "E_TURNO").Change);
        Assert.DoesNotContain(differences, d => d.Name == "F_DOBLE" || d.Name == "PKG_TURNOS");   // lo que no cambió no aparece

        // El script para igualar, corrido en la copia como lo corre FbStudio: después no queda ninguna diferencia.
        var script = SchemaComparer.Script(differences, SchemaComparer.Header("docker-fb3", "copia", DateTime.Now));
        Assert.Contains("ALTER TABLE TURNOS ADD OBS VARCHAR(2000);", script);
        Assert.Contains("ALTER TABLE TURNOS ALTER OBS POSITION 4;", script);
        Assert.Contains("DROP TABLE SOBRA;", script);
        Assert.True(script.IndexOf("DROP INDEX", StringComparison.Ordinal) < 0 || script.IndexOf("DROP TABLE SOBRA", StringComparison.Ordinal) > 0);
        await RunScriptAsync(copy, script);

        var again = SchemaComparer.Compare(await source.ReadSchemaAsync(everything), await target.ReadSchemaAsync(everything),
            new SchemaCompareOptions { DropExtra = true, Privileges = false });
        Assert.True(again.Count == 0, string.Join("\n", again.Select(d => $"{d.Kind} {d.Name}: {string.Join("; ", d.Details)}")));
    }

    [Fact]
    public async Task Sin_borrar_lo_que_sobra_queda_en_la_lista_pero_no_en_el_script()
    {
        var copy = await CopySchemaAsync("/firebird/data/copia_sobra.fdb");
        await RunScriptAsync(copy, "CREATE TABLE SOBRA (ID INTEGER); ALTER TABLE TURNOS ADD EXTRA INTEGER; COMMIT;");
        await using var source = new MetadataExplorer(fb.Profile, fb.Password);
        await using var target = new MetadataExplorer(copy, fb.Password);
        var options = new SchemaScriptOptions { Privileges = false };
        var differences = SchemaComparer.Compare(await source.ReadSchemaAsync(options), await target.ReadSchemaAsync(options), new SchemaCompareOptions());
        var sobra = Assert.Single(differences, d => d.Name == "SOBRA");
        Assert.Equal(SchemaChange.Removed, sobra.Change);
        Assert.Empty(sobra.Statements);
        var turnos = Assert.Single(differences, d => d.Name == "TURNOS");
        Assert.Contains("columna EXTRA: sobra en el destino", turnos.Details);
        Assert.DoesNotContain("DROP", SchemaComparer.Script(differences));
    }

    /// <summary>Una base nueva (como SYSDBA) con el esquema de la de prueba, armada con el script de la base entera.</summary>
    private async Task<FbStudio.Core.Profiles.ConnectionProfile> CopySchemaAsync(string file)
    {
        await using var explorer = new MetadataExplorer(fb.Profile, fb.Password);
        var schema = await explorer.ReadSchemaAsync(new SchemaScriptOptions());
        var copy = fb.Profile with { Database = file, User = "SYSDBA" };
        var create = new FbConnectionStringBuilder(FbConnectionFactory.ConnectionString(copy, fb.Password)) { Charset = schema.Charset };
        await FbConnection.CreateDatabaseAsync(create.ConnectionString, 8192, true, true);
        await RunScriptAsync(copy, SchemaScriptWriter.Write(schema, new SchemaScriptOptions()));
        return copy;
    }

    private async Task RunScriptAsync(FbStudio.Core.Profiles.ConnectionProfile profile, string script)
    {
        await using var session = new QuerySession(profile, fb.Password);
        foreach (var statement in SqlScriptSplitter.Split(script))
        {
            if (StatementClassifier.Classify(statement.Text).Kind == StatementKind.Empty) continue;
            var (outcome, _) = await FirebirdFixture.RunAsync(session, statement.Text);
            Assert.True(outcome.Error is null, $"{outcome.Error}\n--- en ---\n{statement.Text}");
        }
        if (session.InTransaction) await session.CommitAsync();
    }
}
