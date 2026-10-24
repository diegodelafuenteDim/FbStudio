using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Script de la base entera contra Firebird 3 en Docker: se recrea en una base vacía y cada objeto tiene que quedar igual.</summary>
[Collection(nameof(FirebirdCollection))]
public class SchemaScriptTests(FirebirdFixture fb)
{
    [Fact]
    public async Task El_script_de_la_base_recrea_cada_objeto_igual_en_una_base_vacia()
    {
        await DdlAsync("GRANT SELECT, UPDATE (OBS) ON TURNOS TO ROLE R_LECTURA");
        // Como en una tabla de una base de prueba: el comentario de línea queda guardado dentro del default.
        await DdlAsync("CREATE TABLE UI_COMENTADA (ID INTEGER DEFAULT 0 -- contador de fallos\n, NOTA VARCHAR(10))");
        try
        {
            await using var source = new MetadataExplorer(fb.Profile, fb.Password);
            var steps = new List<SchemaProgress>();
            var schema = await source.ReadSchemaAsync(new SchemaScriptOptions(), new Progress<SchemaProgress>(steps.Add));
            var script = SchemaScriptWriter.Write(schema, new SchemaScriptOptions(),
                SchemaScriptWriter.Header(fb.Profile.Name, fb.Profile.Database, DateTime.Now, schema.Charset));
            Assert.Contains("GRANT SELECT, UPDATE (OBS) ON TURNOS TO ROLE R_LECTURA;", script);
            Assert.DoesNotContain("no se incluyen", script);                       // los usos implícitos del dueño no cuentan
            Assert.True(script.IndexOf("CREATE OR ALTER VIEW V_TURNOS", StringComparison.Ordinal) > script.IndexOf("CREATE TABLE TURNOS ", StringComparison.Ordinal));
            Assert.Contains("ALTER TABLE TURNOS_DET ADD TOTAL COMPUTED BY (CANTIDAD * 2);", script);
            Assert.Contains("ALTER TABLE TURNOS_DET ALTER TOTAL POSITION 4;", script);   // vuelve a su lugar
            Assert.Contains("ID   INTEGER DEFAULT 0 /* contador de fallos */,", script);

            // Una base vacía con el mismo charset, y el script corrido como lo corre FbStudio (con sus COMMIT).
            var copy = fb.Profile with { Database = "/firebird/data/copia_script.fdb", User = "SYSDBA" };
            var create = new FbConnectionStringBuilder(FbConnectionFactory.ConnectionString(copy, fb.Password)) { Charset = schema.Charset };
            await FbConnection.CreateDatabaseAsync(create.ConnectionString, 8192, true, true);
            await using (var session = new QuerySession(copy, fb.Password))
            {
                foreach (var statement in SqlScriptSplitter.Split(script))
                {
                    var info = StatementClassifier.Classify(statement.Text);
                    if (info.Kind == StatementKind.Empty) continue;
                    var (outcome, _) = await FirebirdFixture.RunAsync(session, statement.Text);
                    Assert.True(outcome.Error is null, $"{outcome.Error}\n--- en ---\n{statement.Text}");
                }
                Assert.False(session.InTransaction);                                 // terminó con COMMIT
            }

            // Cada objeto, con el mismo DDL en las dos (los roles cambian de dueño: SYSDBA creó la copia).
            await using var target = new MetadataExplorer(copy, fb.Password);
            foreach (var kind in DbObjectKinds.All.Where(k => k is not DbObjectKind.Role))
            {
                var names = (await source.LoadObjectsAsync(kind)).Select(o => o.Name).ToList();
                Assert.Equal(names, (await target.LoadObjectsAsync(kind)).Select(o => o.Name).ToList());
                foreach (var name in names)
                {
                    var obj = new DbObject(kind, name);
                    Assert.Equal((await source.ScriptAsync(obj)).Ddl, (await target.ScriptAsync(obj)).Ddl);
                }
            }
            Assert.Equal(["R_LECTURA"], (await target.LoadObjectsAsync(DbObjectKind.Role)).Select(o => o.Name));
            var copied = await target.ReadSchemaAsync(new SchemaScriptOptions { Domains = false, Tables = false, Views = false, Procedures = false, Functions = false, Packages = false, Triggers = false, Generators = false, Exceptions = false });
            Assert.Contains(copied.Privileges, p => p is { Grantee: "R_LECTURA", Object: "TURNOS", Privilege: "U", Field: "OBS" });
            Assert.True(steps.Count > 0 && steps[^1].Done == steps[^1].Total);
        }
        finally
        {
            await DdlAsync("REVOKE SELECT, UPDATE (OBS) ON TURNOS FROM ROLE R_LECTURA");
            await DdlAsync("DROP TABLE UI_COMENTADA");
        }
    }

    /// <summary>Una sentencia confirmada enseguida (los GRANT de preparación).</summary>
    private async Task DdlAsync(string sql)
    {
        await using var session = fb.NewSession();
        var (outcome, _) = await FirebirdFixture.RunAsync(session, sql);
        Assert.Null(outcome.Error);
        await session.CommitAsync();
    }

    [Fact]
    public void Las_vistas_salen_despues_de_las_que_usan_y_los_permisos_agrupados()
    {
        var schema = new DatabaseSchema
        {
            Views =
            [
                new ViewDefinition("V_A", [], "SELECT * FROM V_B", []),
                new ViewDefinition("V_B", [], "SELECT * FROM V_C", []),
                new ViewDefinition("V_C", [], "SELECT 1 AS X FROM RDB$DATABASE", []),
            ],
            ViewDependencies = new Dictionary<string, IReadOnlyList<string>> { ["V_A"] = ["V_B"], ["V_B"] = ["V_C"] },
        };
        Assert.Equal(["V_C", "V_B", "V_A"], SchemaScriptWriter.OrderViews(schema).Select(v => v.Name));

        var grants = SchemaScriptWriter.Grants(
        [
            new("APP", 8, "S", 0, "TURNOS", 0), new("APP", 8, "I", 0, "TURNOS", 0), new("APP", 8, "U", 0, "TURNOS", 0, "OBS"),
            new("R_LECTURA", 13, "S", 1, "TURNOS", 0),
            new("APP", 8, "X", 0, "P_CONTAR", 5),
            new("DIEGO", 8, "M", 2, "R_LECTURA", 13),
            new("APP", 8, "G", 0, "G_TURNOS", 14),
            new("APP", 8, "C", 0, "SQL$TABLES", 20),                             // permiso de DDL: no se escribe
        ]).ToList();
        Assert.Equal(
        [
            "GRANT USAGE ON SEQUENCE G_TURNOS TO USER APP;",
            "GRANT EXECUTE ON PROCEDURE P_CONTAR TO USER APP;",
            "GRANT R_LECTURA TO USER DIEGO WITH ADMIN OPTION;",
            "GRANT SELECT, INSERT, UPDATE (OBS) ON TURNOS TO USER APP;",
            "GRANT SELECT ON TURNOS TO ROLE R_LECTURA WITH GRANT OPTION;",
            "-- 1 permiso(s) de DDL o de otro tipo no se incluyen: revisarlos a mano.",
        ], grants);
    }
}
