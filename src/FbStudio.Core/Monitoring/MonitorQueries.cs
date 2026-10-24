using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using R = FbStudio.Core.Resources.CoreStrings;

namespace FbStudio.Core.Monitoring;

/// <summary>
/// Una consulta del monitoreo (F-1). Los ordinales dicen en qué columna está el número de conexión, de sentencia o el
/// texto SQL, para «Cortar conexión», «Cancelar sentencia» y el detalle de abajo. Así se pueden sumar consultas nuevas
/// sin tocar la pantalla.
/// </summary>
public sealed record MonitorPreset(string Key, string Name, string Sql, int? AttachmentOrdinal = null, int? StatementOrdinal = null, int? SqlOrdinal = null)
{
    /// <summary>En las consultas propias, las columnas se dicen por nombre (vacío: se detecta sola).</summary>
    public string? AttachmentColumn { get; init; }
    public string? StatementColumn { get; init; }
    public string? SqlColumn { get; init; }

    /// <summary>Consulta armada por el usuario (se puede editar y borrar); null en las que vienen con FbStudio.</summary>
    public Guid? CustomId { get; init; }

    public bool IsCustom => CustomId is not null;
}

/// <summary>En qué columna del resultado está el número de conexión, el de sentencia y el texto SQL.</summary>
public sealed record MonitorColumnMap(int? Attachment, int? Statement, int? Sql)
{
    public static MonitorColumnMap None { get; } = new(null, null, null);
}

public static class MonitorColumns
{
    /// <summary>Nombres con que se reconoce cada columna si la consulta propia no la elige (como salen de MON$ o con alias comunes).</summary>
    public static readonly string[] AttachmentNames = ["MON$ATTACHMENT_ID", "ATTACHMENT_ID", "ATT_ID", "CONEXION", "CONEXIÓN", "CONNECTION", "ATTACHMENT"];
    public static readonly string[] StatementNames = ["MON$STATEMENT_ID", "STATEMENT_ID", "STMT_ID", "SENTENCIA", "STATEMENT"];
    public static readonly string[] SqlNames = ["MON$SQL_TEXT", "SQL_TEXT", "SQL"];

    public static MonitorColumnMap Resolve(MonitorPreset preset, IReadOnlyList<ColumnInfo> columns) => new(
        preset.AttachmentOrdinal ?? Find(columns, preset.AttachmentColumn, AttachmentNames),
        preset.StatementOrdinal ?? Find(columns, preset.StatementColumn, StatementNames),
        preset.SqlOrdinal ?? Find(columns, preset.SqlColumn, SqlNames));

    /// <summary>
    /// La columna elegida por nombre o, si no se eligió, la primera que se llame como las conocidas. Se comparan sin
    /// espacios, guiones bajos ni $: «Attachment ID», «ATTACHMENT_ID» y «MON$ATTACHMENT_ID» se reconocen igual.
    /// </summary>
    public static int? Find(IReadOnlyList<ColumnInfo> columns, string? chosen, IReadOnlyList<string> known)
    {
        int? IndexOf(string name)
        {
            var wanted = Normalize(name);
            for (var i = 0; i < columns.Count; i++)
                if (Normalize(columns[i].Name) == wanted) return i;
            return null;
        }
        if (!string.IsNullOrWhiteSpace(chosen)) return IndexOf(chosen);
        foreach (var name in known)
            if (IndexOf(name) is { } i) return i;
        return null;
    }

    /// <summary>Sin acentos, espacios ni signos: «Conexión del cliente» = «CONEXION_DEL_CLIENTE».</summary>
    private static string Normalize(string name) =>
        new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetterOrDigit(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
}

/// <summary>Foto de MON$DATABASE: los contadores de transacciones y cuántas conexiones, transacciones y sentencias hay.</summary>
public sealed record MonitorSummary(
    long NextTransaction, long OldestInteresting, long OldestActive, long OldestSnapshot,
    int Attachments, int Transactions, int RunningStatements, long? OldestActiveSeconds,
    long OwnAttachment, string User, string? Role)
{
    /// <summary>Transacciones desde la OAT: si crece sin parar, algo tiene una transacción abierta hace mucho.</summary>
    public long Gap => NextTransaction - OldestActive;

    /// <summary>Sin RDB$ADMIN, SYSDBA o ser el dueño de la base, MON$ muestra solo las conexiones del propio usuario.</summary>
    public bool SeesEveryone => Role?.Trim() == "RDB$ADMIN" || User.Trim() == "SYSDBA" || IsOwner;

    /// <summary>El usuario es el dueño de la base (también ve todas las conexiones; probado en Docker, D-53).</summary>
    public bool IsOwner { get; init; }

    /// <summary>Hace cuánto empezó la OAT (calculado en el servidor: MON$TIMESTAMP está en la hora del servidor).</summary>
    public TimeSpan? OldestActiveAge => OldestActiveSeconds is { } s ? TimeSpan.FromSeconds(Math.Max(0, s)) : null;

    /// <summary>Quién tiene la OAT (si se ve: sin RDB$ADMIN solo se ven las propias).</summary>
    public string? OatUser { get; init; }
    public string? OatIp { get; init; }

    /// <summary>El nombre del equipo de quien tiene la OAT (lo informa el programa al conectarse; algunos no lo mandan).</summary>
    public string? OatHost { get; init; }

    public string? OatProcess { get; init; }
    public bool OatSystem { get; init; }

    /// <summary>La OAT es de un backup o un sweep (gbak, gfix, nbackup o un proceso interno del servidor): es esperable.</summary>
    public bool OatIsMaintenance => MonitorQueries.IsMaintenance(OatProcess, OatSystem);

    /// <summary>Sentencias activas que esperan un registro bloqueado por otra transacción (MON$RECORD_WAITS &gt; 0).</summary>
    public int WaitingStatements { get; init; }

    /// <summary>Hace cuánto empezó la que más espera.</summary>
    public long? LongestWaitSeconds { get; init; }

    /// <summary>Si la base está en modo backup de nbackup (F-29).</summary>
    public Metadata.BackupState BackupState { get; init; }
}

public static class MonitorQueries
{
    /// <summary>Una espera de bloqueo más larga que esto se pinta en rojo.</summary>
    public static readonly TimeSpan LongWait = TimeSpan.FromSeconds(30);

    /// <summary>Herramientas de mantenimiento que tienen una transacción abierta mientras trabajan (backup, sweep).</summary>
    public static bool IsMaintenance(string? process, bool system)
    {
        if (system) return true;
        var name = Path.GetFileNameWithoutExtension((process ?? "").Trim().Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
        return name is "gbak" or "gfix" or "nbackup";
    }

    public const string SummarySql = """
        SELECT d.MON$NEXT_TRANSACTION, d.MON$OLDEST_TRANSACTION, d.MON$OLDEST_ACTIVE, d.MON$OLDEST_SNAPSHOT,
               (SELECT COUNT(*) FROM MON$ATTACHMENTS a WHERE a.MON$SYSTEM_FLAG = 0),
               (SELECT COUNT(*) FROM MON$TRANSACTIONS),
               (SELECT COUNT(*) FROM MON$STATEMENTS s WHERE s.MON$STATE = 1 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION),
               (SELECT DATEDIFF(SECOND FROM t.MON$TIMESTAMP TO CURRENT_TIMESTAMP) FROM MON$TRANSACTIONS t
                WHERE t.MON$TRANSACTION_ID = d.MON$OLDEST_ACTIVE),
               CURRENT_CONNECTION, CURRENT_USER, CURRENT_ROLE,
               oat.MON$USER, IIF(POSITION('/' IN oat.MON$REMOTE_ADDRESS) > 0,
                   LEFT(oat.MON$REMOTE_ADDRESS, POSITION('/' IN oat.MON$REMOTE_ADDRESS) - 1), oat.MON$REMOTE_ADDRESS),
               oat.MON$REMOTE_PROCESS, oat.MON$SYSTEM_FLAG,
               (SELECT COUNT(*) FROM MON$STATEMENTS s JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = s.MON$STAT_ID
                WHERE s.MON$STATE = 1 AND r.MON$RECORD_WAITS > 0 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION),
               (SELECT MAX(DATEDIFF(SECOND FROM s.MON$TIMESTAMP TO CURRENT_TIMESTAMP)) FROM MON$STATEMENTS s
                JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = s.MON$STAT_ID
                WHERE s.MON$STATE = 1 AND r.MON$RECORD_WAITS > 0 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION),
               IIF(CURRENT_USER = (SELECT TRIM(o.RDB$OWNER_NAME) FROM RDB$RELATIONS o WHERE o.RDB$RELATION_NAME = 'RDB$DATABASE'), 1, 0),
               d.MON$BACKUP_STATE, oat.MON$REMOTE_HOST
        FROM MON$DATABASE d
        LEFT JOIN MON$TRANSACTIONS ot ON ot.MON$TRANSACTION_ID = d.MON$OLDEST_ACTIVE
        LEFT JOIN MON$ATTACHMENTS oat ON oat.MON$ATTACHMENT_ID = ot.MON$ATTACHMENT_ID
        """;

    /// <summary>Encabezado de columna (entre comillas si tiene minúsculas, acentos o espacios).</summary>
    private static string A(string alias) => SqlIdentifier.Quote(alias);
    private static string L(string text) => SqlIdentifier.Literal(text);

    /// <summary>IP de la conexión sin el puerto («192.0.2.10/60029» → «192.0.2.10»): para filtrar y agrupar por IP.</summary>
    private const string Ip = "IIF(POSITION('/' IN a.MON$REMOTE_ADDRESS) > 0, LEFT(a.MON$REMOTE_ADDRESS, POSITION('/' IN a.MON$REMOTE_ADDRESS) - 1), a.MON$REMOTE_ADDRESS)";

    /// <summary>La consulta con que abre el monitoreo (pedido de Diego: siempre las transacciones).</summary>
    public const string DefaultKey = "transactions";

    public static MonitorPreset Default => Presets.First(p => p.Key == DefaultKey);

    /// <summary>
    /// Las consultas que vienen con FbStudio, con los encabezados en el idioma de la interfaz. Conexiones y transacciones
    /// son las «Mini» de Diego (lo que más le sirve, en su orden) más la nota («es la OAT», «tiene la OAT», «este
    /// monitoreo») al lado del número y el equipo al lado de la dirección; en transacciones, además, el número de la
    /// transacción y cuántos segundos lleva.
    /// </summary>
    public static IReadOnlyList<MonitorPreset> Presets =>
    [
        new("attachments", R.MonAttachments, $"""
            SELECT a.MON$ATTACHMENT_ID AS {A(R.MonColAttachment)},
                   CASE
                     WHEN a.MON$ATTACHMENT_ID = CURRENT_CONNECTION THEN {L(R.MonThisMonitor)}
                     WHEN EXISTS (SELECT 1 FROM MON$TRANSACTIONS t WHERE t.MON$ATTACHMENT_ID = a.MON$ATTACHMENT_ID
                                  AND t.MON$TRANSACTION_ID = d.MON$OLDEST_ACTIVE) THEN {L(R.MonHasOat)}
                     ELSE ''
                   END AS {A(R.MonColNote)},
                   a.MON$SERVER_PID AS {A(R.MonColServerPid)},
                   IIF(a.MON$STATE = 1, {L(R.MonActive)}, {L(R.MonIdle)}) AS {A(R.MonColState)},
                   a.MON$REMOTE_ADDRESS AS {A(R.MonColAddress)},
                   a.MON$REMOTE_HOST AS {A(R.MonColHost)},
                   a.MON$REMOTE_PID AS {A(R.MonColRemotePid)},
                   a.MON$TIMESTAMP AS {A(R.MonColSince)},
                   a.MON$REMOTE_PROCESS AS {A(R.MonColProcess)},
                   TRIM(a.MON$USER) AS {A(R.MonColUser)}
            FROM MON$ATTACHMENTS a CROSS JOIN MON$DATABASE d
            WHERE a.MON$SYSTEM_FLAG = 0
            ORDER BY a.MON$ATTACHMENT_ID
            """, AttachmentOrdinal: 0),

        // La más vieja arriba: la que tiene la OAT queda primera.
        new("transactions", R.MonTransactions, $"""
            SELECT t.MON$ATTACHMENT_ID AS {A(R.MonColAttachment)},
                   CASE
                     WHEN t.MON$TRANSACTION_ID = d.MON$OLDEST_ACTIVE THEN {L(R.MonIsOat)}
                     WHEN t.MON$ATTACHMENT_ID = CURRENT_CONNECTION THEN {L(R.MonThisMonitor)}
                     ELSE ''
                   END AS {A(R.MonColNote)},
                   t.MON$TRANSACTION_ID AS {A(R.MonColTx)},
                   t.MON$TIMESTAMP AS {A(R.MonColSince)},
                   DATEDIFF(SECOND FROM t.MON$TIMESTAMP TO CURRENT_TIMESTAMP) AS {A(R.MonColSeconds)},
                   a.MON$REMOTE_ADDRESS AS {A(R.MonColAddress)},
                   a.MON$REMOTE_HOST AS {A(R.MonColHost)},
                   a.MON$REMOTE_PROCESS AS {A(R.MonColProcess)},
                   IIF(t.MON$STATE = 1, {L(R.MonActive)}, {L(R.MonIdle)}) AS {A(R.MonColState)},
                   t.MON$TOP_TRANSACTION AS {A(R.MonColTop)},
                   t.MON$OLDEST_TRANSACTION AS {A(R.MonColOit)},
                   t.MON$OLDEST_ACTIVE AS {A(R.MonColOat)},
                   TRIM(a.MON$USER) AS {A(R.MonColUser)}
            FROM MON$TRANSACTIONS t
            JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
            CROSS JOIN MON$DATABASE d
            WHERE a.MON$SYSTEM_FLAG = 0
            ORDER BY t.MON$TIMESTAMP, t.MON$TRANSACTION_ID
            """, AttachmentOrdinal: 0),

        new("statements", R.MonStatements, $"""
            SELECT s.MON$STATEMENT_ID AS {A(R.MonColId)},
                   s.MON$ATTACHMENT_ID AS {A(R.MonColAttachment)},
                   s.MON$TRANSACTION_ID AS {A(R.MonColTx)},
                   TRIM(a.MON$USER) AS {A(R.MonColUser)},
                   {Ip} AS {A(R.MonColIp)},
                   a.MON$REMOTE_HOST AS {A(R.MonColHost)},
                   s.MON$TIMESTAMP AS {A(R.MonColSince)},
                   DATEDIFF(SECOND FROM s.MON$TIMESTAMP TO CURRENT_TIMESTAMP) AS {A(R.MonColSeconds)},
                   IIF(s.MON$STATE = 1, {L(R.MonRunning)}, {L(R.MonFetching)}) AS {A(R.MonColState)},
                   r.MON$RECORD_SEQ_READS AS {A(R.MonColSeqReads)},
                   r.MON$RECORD_IDX_READS AS {A(R.MonColIdxReads)},
                   s.MON$SQL_TEXT AS {A(R.MonColSql)}
            FROM MON$STATEMENTS s
            JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = s.MON$ATTACHMENT_ID
            LEFT JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = s.MON$STAT_ID
            WHERE s.MON$STATE <> 0 AND a.MON$SYSTEM_FLAG = 0 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
            ORDER BY s.MON$TIMESTAMP
            """, AttachmentOrdinal: 1, StatementOrdinal: 0, SqlOrdinal: 11),

        // Bloqueos: quién espera un registro (MON$RECORD_WAITS) y quién lo podría tener. Firebird 3 no dice qué
        // transacción tiene el registro; se proponen las de escritura con cambios que empezaron antes de la espera.
        new("locks", R.MonLocks, $"""
            SELECT CAST({L(R.MonWaiting)} AS VARCHAR(40)) AS {A(R.MonColKind)},
                   s.MON$ATTACHMENT_ID AS {A(R.MonColAttachment)},
                   s.MON$TRANSACTION_ID AS {A(R.MonColTx)},
                   s.MON$STATEMENT_ID AS {A(R.MonColStatement)},
                   TRIM(a.MON$USER) AS {A(R.MonColUser)},
                   {Ip} AS {A(R.MonColIp)},
                   a.MON$REMOTE_HOST AS {A(R.MonColHost)},
                   a.MON$REMOTE_PROCESS AS {A(R.MonColProcess)},
                   s.MON$TIMESTAMP AS {A(R.MonColSince)},
                   DATEDIFF(SECOND FROM s.MON$TIMESTAMP TO CURRENT_TIMESTAMP) AS {A(R.MonColSeconds)},
                   CAST(NULL AS BIGINT) AS {A(R.MonColChanges)},
                   s.MON$SQL_TEXT AS {A(R.MonColSql)}
            FROM MON$STATEMENTS s
            JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = s.MON$ATTACHMENT_ID
            JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = s.MON$STAT_ID
            WHERE s.MON$STATE = 1 AND r.MON$RECORD_WAITS > 0 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
            UNION ALL
            SELECT {L(R.MonPossibleHolder)},
                   t.MON$ATTACHMENT_ID,
                   t.MON$TRANSACTION_ID,
                   CAST(NULL AS BIGINT),
                   TRIM(a.MON$USER),
                   {Ip},
                   a.MON$REMOTE_HOST,
                   a.MON$REMOTE_PROCESS,
                   t.MON$TIMESTAMP,
                   DATEDIFF(SECOND FROM t.MON$TIMESTAMP TO CURRENT_TIMESTAMP),
                   r.MON$RECORD_INSERTS + r.MON$RECORD_UPDATES + r.MON$RECORD_DELETES,
                   (SELECT FIRST 1 s2.MON$SQL_TEXT FROM MON$STATEMENTS s2
                    WHERE s2.MON$TRANSACTION_ID = t.MON$TRANSACTION_ID ORDER BY s2.MON$TIMESTAMP DESC)
            FROM MON$TRANSACTIONS t
            JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
            JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = t.MON$STAT_ID
            WHERE t.MON$READ_ONLY = 0 AND a.MON$SYSTEM_FLAG = 0 AND t.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
              AND r.MON$RECORD_INSERTS + r.MON$RECORD_UPDATES + r.MON$RECORD_DELETES > 0
              AND EXISTS (SELECT 1 FROM MON$STATEMENTS w JOIN MON$RECORD_STATS wr ON wr.MON$STAT_ID = w.MON$STAT_ID
                          WHERE w.MON$STATE = 1 AND wr.MON$RECORD_WAITS > 0 AND w.MON$TRANSACTION_ID <> t.MON$TRANSACTION_ID
                            AND w.MON$TIMESTAMP >= t.MON$TIMESTAMP)
              AND NOT EXISTS (SELECT 1 FROM MON$STATEMENTS w JOIN MON$RECORD_STATS wr ON wr.MON$STAT_ID = w.MON$STAT_ID
                              WHERE w.MON$STATE = 1 AND wr.MON$RECORD_WAITS > 0 AND w.MON$TRANSACTION_ID = t.MON$TRANSACTION_ID)
            ORDER BY 10 DESC
            """, AttachmentOrdinal: 1, StatementOrdinal: 3, SqlOrdinal: 11),
    ];

    public const string KillAttachmentSql = "DELETE FROM MON$ATTACHMENTS WHERE MON$ATTACHMENT_ID = @id";

    /// <summary>Quién es una conexión (para confirmar antes de cortarla con los datos del servidor, no los de la consulta).</summary>
    public const string AttachmentSql =
        "SELECT TRIM(MON$USER), MON$REMOTE_ADDRESS, MON$REMOTE_HOST, MON$REMOTE_PROCESS FROM MON$ATTACHMENTS WHERE MON$ATTACHMENT_ID = @id";
    public const string CancelStatementSql = "DELETE FROM MON$STATEMENTS WHERE MON$STATEMENT_ID = @id";
}
