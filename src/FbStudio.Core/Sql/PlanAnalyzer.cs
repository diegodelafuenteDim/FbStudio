using System.Text.RegularExpressions;

namespace FbStudio.Core.Sql;

/// <summary>Lecturas útiles del plan y de los errores de Firebird para avisar a tiempo.</summary>
public static partial class PlanAnalyzer
{
    // Plan detallado (Firebird 3): -> Table "CLINICA_TURNOS" as "T" Full Scan
    [GeneratedRegex("""Table "([^"]+)"(?: as "[^"]+")? Full Scan""", RegexOptions.CultureInvariant)]
    private static partial Regex ExplainedFullScan();

    // Plan clásico: PLAN (T NATURAL) / PLAN JOIN (A NATURAL, B INDEX (X)) — muestra alias, no siempre la tabla.
    [GeneratedRegex(@"([A-Za-z0-9_$""]+) NATURAL", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyNatural();

    [GeneratedRegex(@"concurrent transaction number is (\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ConcurrentTransaction();

    /// <summary>
    /// Tablas que el plan recorre enteras (sin índice). Se ignoran las tablas de sistema (RDB$, MON$), que son chicas
    /// o virtuales.
    /// </summary>
    public static IReadOnlyList<string> FullScans(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan)) return [];
        var matches = ExplainedFullScan().Matches(plan);
        var names = matches.Count > 0
            ? matches.Select(m => m.Groups[1].Value)
            : LegacyNatural().Matches(plan).Select(m => m.Groups[1].Value.Trim('"'));
        return names
            .Where(n => !n.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("MON$", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Transacción que tiene tomado el registro, en un error de bloqueo ("concurrent transaction number is N").</summary>
    public static long? LockingTransaction(string? error) =>
        error is not null && ConcurrentTransaction().Match(error) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var id) ? id : null;
}
