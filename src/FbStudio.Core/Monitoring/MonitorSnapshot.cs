namespace FbStudio.Core.Monitoring;

/// <summary>Una transacción abierta según MON$TRANSACTIONS, con quién la tiene.</summary>
public sealed record MonitorTransaction(long Id, long Attachment, DateTime Start, long Seconds, bool Active, bool ReadOnly, bool IsOat,
    MonitorClient Client);

/// <summary>Una sentencia en curso (ejecutándose o con filas por traer) según MON$STATEMENTS.</summary>
/// <param name="Waiting">Espera un registro bloqueado por otra transacción (MON$RECORD_WAITS &gt; 0).</param>
/// <param name="Sql">El texto tal como lo mandó el programa: puede tener valores, se reemplazan antes de mostrarlo afuera.</param>
public sealed record MonitorStatement(long Id, long Attachment, long? Transaction, DateTime Start, long Seconds, bool Running,
    long? SequentialReads, long? IndexedReads, bool Waiting, MonitorClient Client, string? Sql);

/// <summary>Quién está del otro lado de una conexión: usuario de la base, dirección, equipo y programa.</summary>
public sealed record MonitorClient(string? User, string? Address, string? Host, string? Process, bool System = false)
{
    /// <summary>Solo el nombre del programa, sin la carpeta (la ruta puede traer el usuario de Windows).</summary>
    public string? ProcessName => string.IsNullOrWhiteSpace(Process) ? null : Path.GetFileName(Process.Trim().Replace('\\', '/'));

    /// <summary>La dirección sin el puerto («192.0.2.10/60029» → «192.0.2.10»).</summary>
    public string? Ip => Address?.Split('/')[0].Trim() is { Length: > 0 } ip ? ip : null;
}

/// <summary>Cuántas conexiones tiene cada usuario desde cada equipo y programa.</summary>
public sealed record MonitorConnections(MonitorClient Client, int Count);

/// <summary>
/// Una foto del monitoreo leída de una vez (una transacción: MON$ es una foto al empezarla), para el conector MCP: el
/// resumen, las transacciones más viejas, las sentencias en curso y las conexiones agrupadas.
/// </summary>
/// <param name="OwnTransaction">La transacción con que se leyó la foto (cuenta en el resumen y puede ser la OAT).</param>
public sealed record MonitorSnapshot(MonitorSummary Summary, IReadOnlyList<MonitorTransaction> Transactions,
    IReadOnlyList<MonitorStatement> Statements, IReadOnlyList<MonitorConnections> Connections, long OwnTransaction)
{
    /// <summary>La OAT es la transacción de la propia foto: no hay ninguna otra activa más vieja.</summary>
    public bool OatIsOwn => Summary.OldestActive == OwnTransaction;
}
