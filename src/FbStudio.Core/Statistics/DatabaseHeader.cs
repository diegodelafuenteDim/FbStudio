using System.Globalization;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Statistics;

/// <summary>Un dato del encabezado tal como lo escribe el servidor («Page size», «8192»).</summary>
public sealed record HeaderEntry(string Key, string Value);

/// <summary>
/// Encabezado de la base (lo que muestra <c>gstat -h</c>): solo lee la página de encabezado, así que es instantáneo
/// aunque la base sea enorme. Los nombres de los datos son los del servidor (en inglés); los que se usan para calcular
/// tienen propiedad propia.
/// </summary>
public sealed partial class DatabaseHeader
{
    private readonly Dictionary<string, string> _values;

    private DatabaseHeader(string? database, IReadOnlyList<HeaderEntry> entries)
    {
        Database = database;
        Entries = entries;
        _values = entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Ruta de la base según el servidor.</summary>
    public string? Database { get; }

    public IReadOnlyList<HeaderEntry> Entries { get; }

    public string? Value(string key) => _values.GetValueOrDefault(key);

    public long? Number(string key) =>
        long.TryParse(Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public long? PageSize => Number("Page size");
    public string? OdsVersion => Value("ODS version");
    public long? Generation => Number("Generation");

    /// <summary>OIT: la transacción de interés más vieja (por debajo, todo está confirmado o limpio).</summary>
    public long? OldestTransaction => Number("Oldest transaction");

    /// <summary>OAT: la transacción activa más vieja.</summary>
    public long? OldestActive => Number("Oldest active");

    /// <summary>OST: la foto más vieja que alguien todavía puede ver (hasta ahí puede limpiar la recolección de basura).</summary>
    public long? OldestSnapshot => Number("Oldest snapshot");

    public long? NextTransaction => Number("Next transaction");

    /// <summary>Intervalo del sweep automático si nunca se cambió: gstat no lo escribe.</summary>
    public const long DefaultSweepInterval = 20_000;

    /// <summary>Intervalo del sweep automático (0 = apagado). Sin la línea en el encabezado, es el de fábrica.</summary>
    public long SweepInterval => Number("Sweep interval") ?? DefaultSweepInterval;

    /// <summary>El intervalo es el de fábrica (nadie lo cambió con gfix -h).</summary>
    public bool SweepIntervalIsDefault => Number("Sweep interval") is null;

    public long? Dialect => Number("Database dialect");
    public long? PageBuffers => Number("Page buffers");
    public string? Attributes => Value("Attributes");

    /// <summary>«force write» en los atributos: cada escritura va al disco antes de confirmar.</summary>
    public bool ForcedWrites => Attributes?.Contains("force write", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Modo backup de nbackup: gstat lo escribe en los atributos («backup lock», «backup merge»).</summary>
    public Metadata.BackupState BackupState =>
        Attributes?.Contains("backup lock", StringComparison.OrdinalIgnoreCase) == true ? Metadata.BackupState.Stalled
        : Attributes?.Contains("backup merge", StringComparison.OrdinalIgnoreCase) == true ? Metadata.BackupState.Merge
        : Metadata.BackupState.Normal;

    /// <summary>Próxima − OAT: transacciones que empezaron después de la activa más vieja.</summary>
    public long? ActiveGap => NextTransaction - OldestActive;

    /// <summary>OST − OIT: lo que el sweep automático compara con su intervalo para dispararse.</summary>
    public long? SweepGap => OldestSnapshot - OldestTransaction;

    /// <summary>Con el sweep automático prendido, la brecha OST − OIT ya pasó el intervalo: el sweep debería estar corriendo.</summary>
    public bool SweepDue => SweepInterval > 0 && SweepGap > SweepInterval;

    /// <summary>Lee la salida de gstat (una línea por elemento): «\tPage size\t\t8192», «\tSweep interval:\t\t20000».</summary>
    public static DatabaseHeader Parse(IEnumerable<string> lines)
    {
        string? database = null;
        var entries = new List<HeaderEntry>();
        foreach (var raw in lines.SelectMany(l => l.Split('\n')))
        {
            var line = raw.TrimEnd('\r');
            if (DatabaseLine().Match(line) is { Success: true } db)
            {
                database ??= db.Groups["path"].Value;
                continue;
            }
            if (line.TrimStart().StartsWith("Gstat", StringComparison.OrdinalIgnoreCase)) continue;
            if (EntryLine().Match(line) is not { Success: true } m) continue;
            var key = m.Groups["key"].Value.Trim().TrimEnd(':').Trim();
            if (key.Length == 0 || key.StartsWith('*')) continue;
            entries.Add(new HeaderEntry(key, m.Groups["value"].Value.Trim()));
        }
        return new DatabaseHeader(database, entries);
    }

    [GeneratedRegex("^\\s*Database\\s+\"(?<path>[^\"]*)\"")]
    private static partial Regex DatabaseLine();

    // Nombre y valor separados por tabuladores (o por dos espacios o más, si el servidor los convirtió).
    [GeneratedRegex("^\\s*(?<key>[A-Za-z][^\\t]*?):?(?:\\t+|\\s{2,})(?<value>\\S.*)$")]
    private static partial Regex EntryLine();
}
