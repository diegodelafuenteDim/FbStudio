using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>Cómo leer el plan de una sentencia ya preparada (lo usan la consulta y el conector MCP).</summary>
internal static class QueryPlans
{
    /// <summary>El plan detallado (Firebird 3+) o, si no hay, el clásico; null si el servidor no da ninguno.</summary>
    public static async Task<(string? Plan, bool Explained)> ReadAsync(FbCommand cmd, CancellationToken ct)
    {
        try
        {
            var explained = await cmd.GetCommandExplainedPlanAsync(ct);
            if (!string.IsNullOrWhiteSpace(explained)) return (explained.Trim(), true);
        }
        catch (FbException) { /* servidor anterior a 3.0 */ }

        try
        {
            var classic = await cmd.GetCommandPlanAsync(ct);
            return (string.IsNullOrWhiteSpace(classic) ? null : classic.Trim(), false);
        }
        catch (FbException) { return (null, false); }
    }
}

public sealed partial class MetadataExplorer
{
    /// <summary>
    /// El plan de una sentencia sin ejecutarla (conector MCP, D-114): se prepara en una transacción de solo lectura, se lee
    /// el plan y se descarta. Preparar no corre nada: ni la sentencia, ni triggers, ni mueve generadores.
    /// </summary>
    public Task<(string? Plan, bool Explained)> PlanAsync(string sql, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            await using var cmd = new FbCommand(sql, connection, tx);
            await cmd.PrepareAsync(ct);
            return await QueryPlans.ReadAsync(cmd, ct);
        }, ct);
}
