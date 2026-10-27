using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Mcp;

namespace FbStudio.Firebird.Tests;

/// <summary>
/// Las herramientas del conector MCP (F-37, D-114) contra el Firebird de Docker: lo que devuelven, que nunca traen filas
/// y que pedir el plan de un DELETE no borra nada.
/// </summary>
[Collection(nameof(FirebirdCollection))]
public sealed class McpToolsTests(FirebirdFixture fx) : IAsyncLifetime
{
    private ConnectionProfile Enabled => fx.Profile with { Name = "Docker", AssistantAccess = true };
    private DatabaseCatalog _catalog = null!;
    private StructureTools Tools => new(_catalog);

    public Task InitializeAsync()
    {
        var other = new ConnectionProfile { Name = "Otra", Host = "srv", Database = "/otra.fdb", User = "U" };
        _catalog = new DatabaseCatalog(new ConnectorContext(
            () => [Enabled, other],
            p => p.Name == "Docker" ? [new SchemaArea { Name = "DETALLE", Rules = ["TURNOS_DET*"] }] : [],
            id => id == Enabled.Id ? fx.Password : null));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _catalog.DisposeAsync();

    [Fact]
    public async Task El_esquema_lista_tablas_y_filtra_por_area_tipo_y_nombre()
    {
        var tables = await Tools.GetSchemaAsync("docker", null, null, null, 0, default);   // sin distinguir mayúsculas
        Assert.StartsWith("Docker: ", tables);
        Assert.Contains("Areas: DETALLE", tables);
        Assert.Contains("TABLE TURNOS\n", tables);
        Assert.DoesNotContain("\nPROCEDURE ", tables);                                   // sin filtro, solo tablas

        var area = await Tools.GetSchemaAsync("Docker", "detalle", null, null, 0, default);
        Assert.Contains("Filter: area DETALLE", area);
        Assert.Contains("TABLE TURNOS_DET\n", area);
        Assert.Contains("TRIGGER TURNOS_DET_BIU on TURNOS_DET", area);                     // los triggers van con su tabla
        Assert.DoesNotContain("TABLE TURNOS\n", area);

        var procs = await Tools.GetSchemaAsync("Docker", null, "procedures, functions", null, 0, default);
        Assert.Contains("PROCEDURE P_TURNOS_DESDE", procs);
        Assert.Contains("FUNCTION F_DOBLE (PSQL)", procs);

        var indexes = await Tools.GetSchemaAsync("Docker", null, "index", "TURNOS_*", 0, default);
        Assert.Contains("INDEX TURNOS_PACIENTE on TURNOS (inactive)", indexes);
    }

    [Fact]
    public async Task Describe_devuelve_el_DDL_y_el_comentario()
    {
        var ddl = await Tools.DescribeObjectAsync("Docker", "turnos", default);
        Assert.StartsWith("-- TABLE TURNOS — Turnos de prueba\n", ddl);
        Assert.Contains("CREATE TABLE TURNOS (", ddl);
        Assert.Contains("#FK_%PACIENTES,%CODIGO,%NOMBRE#", ddl);                            // la FK lógica del comentario

        var proc = await Tools.DescribeObjectAsync("Docker", "P_TURNOS_DESDE", default);
        Assert.Contains("FOR SELECT ID, FECHA FROM TURNOS", proc);
        await Assert.ThrowsAsync<ConnectorException>(() => Tools.DescribeObjectAsync("Docker", "NO_EXISTE", default));
    }

    [Fact]
    public async Task Busca_en_los_metadatos_y_nunca_en_los_datos()
    {
        var hits = await Tools.SearchMetadataAsync("Docker", "Devuelve los turnos", default);
        Assert.Contains("PROCEDURE P_TURNOS_DESDE · source line", hits);
        var data = await Tools.SearchMetadataAsync("Docker", "PACIENTE 4242", default);  // un valor de la tabla
        Assert.Contains("does not appear", data);
    }

    [Fact]
    public async Task Dependencias_uso_de_columnas_y_camino_entre_tablas()
    {
        var deps = await Tools.GetDependenciesAsync("Docker", "TURNOS", default);
        Assert.Contains("Used by:", deps);
        Assert.Contains("VIEW V_TURNOS", deps);
        Assert.Contains("PROCEDURE P_TURNOS_DESDE", deps);

        var usage = await Tools.FindColumnUsageAsync("Docker", "turnos", "fecha", default);
        Assert.StartsWith("TURNOS.FECHA\nUsed by:", usage);
        Assert.Contains("VIEW V_TURNOS", usage);
        await Assert.ThrowsAsync<ConnectorException>(() => Tools.FindColumnUsageAsync("Docker", "TURNOS", "NO_EXISTE", default));

        var path = await Tools.FindJoinPathAsync("Docker", "TURNOS_DET", "turnos", default);
        Assert.Contains("Path: TURNOS_DET -> TURNOS (1 relationship)", path);
        Assert.Contains("JOIN TURNOS ON TURNOS.ID = TURNOS_DET.TURNO", path);
    }

    [Fact]
    public async Task El_plan_se_prepara_y_nunca_se_ejecuta()
    {
        var select = await Tools.ExplainQueryAsync("Docker", "SELECT * FROM TURNOS WHERE FECHA > CURRENT_DATE;", default);
        Assert.Contains("only prepared, never executed", select);
        Assert.Contains("TURNOS", select);
        Assert.DoesNotContain("PACIENTE 1", select);

        // Preparar un DELETE, un UPDATE o un bloque que borra no cambia nada.
        await Tools.ExplainQueryAsync("Docker", "DELETE FROM TURNOS", default);
        await Tools.ExplainQueryAsync("Docker", "UPDATE TURNOS SET OBS = 'borrado'", default);
        await Tools.ExplainQueryAsync("Docker", "EXECUTE BLOCK AS BEGIN DELETE FROM TURNOS; END", default);
        await using var session = fx.NewSession(readOnly: true);
        var (outcome, rows) = await FirebirdFixture.RunAsync(session, "SELECT COUNT(*), SUM(IIF(OBS = 'borrado', 1, 0)) FROM TURNOS");
        Assert.Null(outcome.Error);
        Assert.Equal(FirebirdFixture.Rows, Convert.ToInt32(rows[0][0]));
        Assert.Equal(0, Convert.ToInt32(rows[0][1]));

        await Assert.ThrowsAsync<ConnectorException>(() => Tools.ExplainQueryAsync("Docker", "DROP TABLE TURNOS", default));
    }

    [Fact]
    public async Task Datos_de_la_base_sin_rutas()
    {
        var info = await Tools.GetDatabaseInfoAsync("Docker", default);
        Assert.Contains("Server: Firebird 3.", info);
        Assert.Contains("ODS 12.", info);
        Assert.Contains("nbackup: normal", info);
        Assert.DoesNotContain(fx.Profile.Database, info);
        Assert.DoesNotContain(fx.Profile.Host, info);
    }

    [Fact]
    public async Task La_foto_del_monitoreo_muestra_transacciones_y_sentencias_sin_valores()
    {
        // Otra conexión con una transacción abierta y una sentencia a medio leer, con un valor de un paciente.
        await using var c = FbConnectionFactory.Create(fx.Profile, fx.Password, "prueba-monitoreo");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FirebirdSql.Data.FirebirdClient.FbCommand("SELECT ID, OBS FROM TURNOS WHERE PACIENTE <> 'PACIENTE 4242' ORDER BY ID", c, tx);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var snapshot = await new MonitorTools(_catalog, showClients: true).GetMonitorSnapshotAsync("Docker", default);
        Assert.Contains("Transactions: next ", snapshot);
        Assert.Contains("Oldest open transactions", snapshot);
        Assert.Contains("OAT held by: user", snapshot);                                      // la de la otra conexión, no la de la foto
        Assert.Contains("THE OAT", snapshot);
        Assert.Contains("SELECT ID, OBS FROM TURNOS WHERE PACIENTE <> ? ORDER BY ID", snapshot);   // el SQL, sin el valor
        Assert.DoesNotContain("4242", snapshot);
        Assert.Contains("user " + fx.Profile.User.ToUpperInvariant(), snapshot, StringComparison.OrdinalIgnoreCase);

        var hidden = await new MonitorTools(_catalog, showClients: false).GetMonitorSnapshotAsync("Docker", default);
        Assert.DoesNotContain("user " + fx.Profile.User.ToUpperInvariant(), hidden, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connections by program", hidden);
        Assert.Contains("attachment ", hidden);
    }

    [Fact]
    public async Task Las_estadisticas_dan_el_encabezado_y_las_tablas_pedidas()
    {
        var tools = new MonitorTools(_catalog, showClients: true);
        var header = await tools.GetStatisticsAsync("Docker", null, default);
        Assert.Contains("Docker · statistics (header", header);
        Assert.Contains("Transactions: OIT ", header);
        Assert.DoesNotContain(fx.Profile.Database, header);
        Assert.Contains("pass up to 20 table names", header);

        var turnos = await tools.GetStatisticsAsync("Docker", "turnos", default);
        Assert.Contains("TABLE TURNOS: 50,000 records", turnos);
        Assert.Contains("INDEX TURNOS_FECHA: depth ", turnos);
        await Assert.ThrowsAsync<ConnectorException>(() => tools.GetStatisticsAsync("Docker", "NO_EXISTE", default));
    }

    [Fact]
    public async Task Solo_las_bases_habilitadas_y_con_contrasena()
    {
        var ex = await Assert.ThrowsAsync<ConnectorException>(() => Tools.GetSchemaAsync("Otra", null, null, null, 0, default));
        Assert.Contains("Enabled: Docker", ex.Message);                                     // la otra no está habilitada

        await using var sinClave = new DatabaseCatalog(new ConnectorContext(() => [Enabled], _ => [], _ => null));
        var noPassword = await Assert.ThrowsAsync<ConnectorException>(() => new StructureTools(sinClave).GetSchemaAsync("Docker", null, null, null, 0, default));
        Assert.Contains("no saved password", noPassword.Message);
    }
}
