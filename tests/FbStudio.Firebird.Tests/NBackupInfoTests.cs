using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird.Tests;

/// <summary>
/// Lo de nbackup que FbStudio mira (F-29, paso 1), contra Firebird 3 en Docker. Cada prueba usa su propia base: poner
/// la compartida en modo backup cambiaría lo que ven las demás.
/// </summary>
[Collection(nameof(FirebirdCollection))]
public class NBackupInfoTests(FirebirdFixture fb)
{
    /// <summary>Una base nueva, vacía, de SYSDBA.</summary>
    private async Task<ConnectionProfile> NewDatabaseAsync()
    {
        var profile = fb.Profile with { Name = "docker-nbackup", Database = $"/firebird/data/nbk_{Guid.NewGuid():N}.fdb", User = "SYSDBA" };
        await FbConnection.CreateDatabaseAsync(FbConnectionFactory.ConnectionString(profile, fb.Password), 8192, true, true);
        return profile;
    }

    private async Task ExecAsync(ConnectionProfile profile, string sql)
    {
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand(sql, c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private Task NBackupAsync(ConnectionProfile profile, int level, string file) =>
        new FbNBackup(FbConnectionFactory.ConnectionString(profile, fb.Password, "test")) { Level = level, BackupFile = file }.ExecuteAsync();

    [Fact]
    public async Task Muestra_el_historial_sin_la_carpeta_del_servidor()
    {
        var db = await NewDatabaseAsync();
        var empty = await NBackupInfo.ReadAsync(db, fb.Password);
        Assert.Equal((BackupState.Normal, false), (empty.State, empty.CustomDeltaFile));
        Assert.Empty(empty.History);

        var name = Path.GetFileNameWithoutExtension(db.Database);
        await NBackupAsync(db, 0, $"/firebird/data/{name}.nbk0");
        await ExecAsync(db, "CREATE TABLE T (ID INTEGER)");
        await NBackupAsync(db, 1, $"/firebird/data/{name}.nbk1");

        var status = await NBackupInfo.ReadAsync(db, fb.Password);
        Assert.Equal(BackupState.Normal, status.State);                    // el servicio termina el modo backup solo
        Assert.Equal([1, 0], status.History.Select(h => h.Level));          // lo último primero
        Assert.Equal([$"{name}.nbk1", $"{name}.nbk0"], status.History.Select(h => h.FileName));
        Assert.All(status.History, h => Assert.DoesNotContain("/", h.FileName));
        Assert.Equal(("Nivel 0 (completo)", "Nivel 1 (incremental)"), (status.History[1].LevelText, status.History[0].LevelText));
        Assert.True(status.History[0].Scn > status.History[1].Scn);
        Assert.NotNull(status.History[0].Guid);

        // El delta en otro archivo (ADD DIFFERENCE FILE) se nota, sin decir dónde.
        await ExecAsync(db, $"ALTER DATABASE ADD DIFFERENCE FILE '/firebird/data/{name}.delta'");
        Assert.True((await NBackupInfo.ReadAsync(db, fb.Password)).CustomDeltaFile);
        await ExecAsync(db, "ALTER DATABASE DROP DIFFERENCE FILE");
        Assert.False((await NBackupInfo.ReadAsync(db, fb.Password)).CustomDeltaFile);
    }

    [Fact]
    public async Task Avisa_si_la_base_quedo_en_modo_backup()
    {
        var db = await NewDatabaseAsync();
        await ExecAsync(db, "ALTER DATABASE BEGIN BACKUP");                // como un nbackup -L que nadie terminó
        try
        {
            var stalled = await NBackupInfo.ReadAsync(db, fb.Password);
            Assert.Equal((BackupState.Stalled, false), (stalled.State, stalled.CustomDeltaFile)); // el delta de fábrica no cuenta como propio

            // Al conectar (árbol y barra de estado).
            await using (var c = FbConnectionFactory.Create(db, fb.Password, "test"))
            {
                await c.OpenAsync();
                var info = await ServerProbe.InfoAsync(c, db, fb.Password);
                Assert.Equal(BackupState.Stalled, info.BackupState);
                Assert.StartsWith("La base está en MODO BACKUP (nbackup)", info.Warning);
            }

            // En el monitoreo.
            await using (var monitor = new MonitorSession(db, fb.Password))
                Assert.Equal(BackupState.Stalled, (await monitor.SummaryAsync()).BackupState);

            // En las estadísticas: gstat -h lo dice en los atributos.
            var header = (await DatabaseStatistics.ReadAsync(db, fb.Password, StatisticsScope.Header)).Header;
            Assert.Equal(BackupState.Stalled, header.BackupState);
        }
        finally
        {
            await ExecAsync(db, "ALTER DATABASE END BACKUP");
        }
        Assert.Equal(BackupState.Normal, (await NBackupInfo.ReadAsync(db, fb.Password)).State);
        Assert.Equal(BackupState.Normal, (await DatabaseStatistics.ReadAsync(db, fb.Password, StatisticsScope.Header)).Header.BackupState);
    }
}
