using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird;

/// <summary>Las propiedades de la base que se pueden cambiar (F-34).</summary>
/// <param name="SweepInterval">Sweep automático cuando OST − OIT pasa este número; 0 = apagado.</param>
/// <param name="ForcedWrites">Escrituras forzadas (cada página al disco al confirmar): sin ellas, un corte de luz puede dañar la base.</param>
/// <param name="ReadOnly">La base no acepta escrituras.</param>
/// <param name="PageBuffers">Páginas de caché de la base; 0 = las que diga la configuración del servidor.</param>
public sealed record DatabaseSettings(int SweepInterval, bool ForcedWrites, bool ReadOnly, int PageBuffers);

/// <summary>
/// Lee las propiedades de MON$DATABASE (cualquier usuario) y las cambia por la API de servicios, como gfix (hace falta
/// SYSDBA, el dueño de la base o el rol RDB$ADMIN). Pasar a solo lectura necesita que no haya nadie más conectado.
/// </summary>
public static class DatabaseProperties
{
    private const string ReadSql = "SELECT MON$SWEEP_INTERVAL, MON$FORCED_WRITES, MON$READ_ONLY FROM MON$DATABASE";

    /// <summary>
    /// Sweep, escrituras forzadas y solo lectura de MON$DATABASE; la caché, del encabezado (MON$PAGE_BUFFERS dice la que
    /// está en uso, que con 0 en la base es la de la configuración del servidor).
    /// </summary>
    public static async Task<DatabaseSettings> ReadAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        int sweep;
        bool forcedWrites, readOnly;
        await using (var connection = FbConnectionFactory.Create(profile, password, "propiedades"))
        {
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = new FbCommand(ReadSql, connection, tx);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            (sweep, forcedWrites, readOnly) = (Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)) == 1, Convert.ToInt32(r.GetValue(2)) == 1);
            await r.CloseAsync();
            await tx.CommitAsync(ct);
        }
        var header = (await DatabaseStatistics.ReadAsync(profile, password, StatisticsScope.Header, ct: ct)).Header;
        return new DatabaseSettings(sweep, forcedWrites, readOnly, (int)(header.PageBuffers ?? 0));
    }

    /// <summary>
    /// Aplica solo lo que cambió. Salir de solo lectura va primero y entrar va al final: con la base en solo lectura
    /// el resto no se puede cambiar.
    /// </summary>
    public static async Task ApplyAsync(ConnectionProfile profile, string password, DatabaseSettings before, DatabaseSettings after,
        CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        var service = new FbConfiguration(FbConnectionFactory.ConnectionString(profile, password, "propiedades"));
        if (before.ReadOnly && !after.ReadOnly) await service.SetAccessModeAsync(false, ct);
        if (after.SweepInterval != before.SweepInterval) await service.SetSweepIntervalAsync(after.SweepInterval, ct);
        if (after.ForcedWrites != before.ForcedWrites) await service.SetForcedWritesAsync(after.ForcedWrites, ct);
        if (after.PageBuffers != before.PageBuffers) await service.SetPageBuffersAsync(after.PageBuffers, ct);
        if (!before.ReadOnly && after.ReadOnly) await service.SetAccessModeAsync(true, ct);
    }
}
