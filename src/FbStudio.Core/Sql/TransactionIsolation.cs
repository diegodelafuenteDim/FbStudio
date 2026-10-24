namespace FbStudio.Core.Sql;

/// <summary>
/// Aislamiento de la transacción de escritura del editor (D-84): read committed (de fábrica, ve lo que otros confirman),
/// snapshot (la foto del momento en que empezó) o snapshot table stability (además bloquea las tablas que lee). Solo se
/// elige en Desarrollo y Prueba: en producción siempre read committed.
/// </summary>
public enum TransactionIsolation
{
    ReadCommitted,
    Snapshot,
    SnapshotTableStability,
}
