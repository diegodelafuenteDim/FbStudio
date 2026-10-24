namespace FbStudio.Firebird.Tests;

/// <summary>Estadísticas de la base (gstat por la API de servicios) contra Firebird 3 en Docker, con un usuario que no es SYSDBA.</summary>
[Collection(nameof(FirebirdCollection))]
public class DatabaseStatisticsTests(FirebirdFixture fb)
{
    [Fact]
    public async Task El_encabezado_trae_las_transacciones_y_el_tamano_de_pagina()
    {
        var result = await DatabaseStatistics.ReadAsync(fb.Profile, fb.Password, StatisticsScope.Header);
        var h = result.Header;
        Assert.Equal("12.0", h.OdsVersion);                                  // Firebird 3
        Assert.True(h.PageSize is 4096 or 8192 or 16384 or 32768, $"Página: {h.PageSize}");
        Assert.Equal(3, h.Dialect);
        // La ruta no se muestra (D-54): en su lugar va la base como la nombra el registro (el alias «test»).
        Assert.Equal(fb.Profile.Database, h.Database);
        Assert.Contains(result.Lines, l => l.Contains($"Database \"{fb.Profile.Database}\""));
        Assert.DoesNotContain(result.Lines, l => l.Contains("/firebird/data/test"));
        Assert.True(h.OldestTransaction <= h.OldestActive && h.OldestActive <= h.NextTransaction,
            $"OIT {h.OldestTransaction}, OAT {h.OldestActive}, próxima {h.NextTransaction}");
        // gstat no escribe el intervalo si nunca se cambió: es el de fábrica, el mismo que informa la base.
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        Assert.Equal(await new FirebirdSql.Data.FirebirdClient.FbDatabaseInfo(c).GetSweepIntervalAsync(), h.SweepInterval);
        Assert.True(h.SweepIntervalIsDefault);
        Assert.DoesNotContain(result.Lines, l => l.Contains("Data pages", StringComparison.OrdinalIgnoreCase)); // solo el encabezado
    }

    [Fact]
    public async Task La_completa_agrega_paginas_de_datos_e_indices_por_tabla()
    {
        var seen = new List<string>();
        var result = await DatabaseStatistics.ReadAsync(fb.Profile, fb.Password, StatisticsScope.Full, seen.Add);
        Assert.Contains(result.Lines, l => l.StartsWith("TURNOS (", StringComparison.Ordinal));
        Assert.Contains(result.Lines, l => l.Contains("Data pages", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Lines, l => l.TrimStart().StartsWith("Index ", StringComparison.Ordinal));
        Assert.Equal(result.Lines, seen);                                    // las líneas llegan a medida que el servidor las manda
        Assert.Equal("12.0", result.Header.OdsVersion);
    }

    [Fact]
    public async Task Con_tablas_elegidas_lee_solo_esas_y_sus_indices()
    {
        var result = await DatabaseStatistics.ReadAsync(fb.Profile, fb.Password, StatisticsScope.Full, tables: ["TURNOS_DET", "TURNOS"]);
        var analysis = FbStudio.Core.Statistics.DatabaseAnalysis.Parse(result.Lines);
        Assert.Equal(["TURNOS", "TURNOS_DET"], analysis.Tables.Select(t => t.Name).Order());
        Assert.Contains(analysis.Indexes, i => i.Table == "TURNOS" && i.Name.StartsWith("RDB$PRIMARY", StringComparison.Ordinal));
        Assert.True(analysis.Tables.Single(t => t.Name == "TURNOS").Records > 0);
        Assert.Equal("12.0", result.Header.OdsVersion);
        Assert.DoesNotContain(result.Lines, l => l.Contains("/firebird/data/test"));

        // Una tabla que no existe: lo dice Firebird.
        var error = await Assert.ThrowsAnyAsync<Exception>(() => DatabaseStatistics.ReadAsync(fb.Profile, fb.Password, StatisticsScope.Full, tables: ["NO_EXISTE"]));
        Assert.Contains("NO_EXISTE", error.Message);
        Assert.IsNotType<NotSupportedException>(error);
    }
}
