using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

internal static class Transactions
{
    /// <summary>Lecturas: read committed + rec_version, sin esperar locks.</summary>
    public static FbTransactionOptions ReadOnly { get; } = new()
    {
        TransactionBehavior = FbTransactionBehavior.Read | FbTransactionBehavior.ReadCommitted
                              | FbTransactionBehavior.RecVersion | FbTransactionBehavior.NoWait,
    };

    /// <summary>
    /// Escrituras desde el editor: read committed + rec_version, esperando un registro bloqueado como máximo
    /// <paramref name="lockTimeout"/> (10 s por defecto, opción del sistema): mejor un error claro que una pestaña
    /// colgada detrás de otra transacción.
    /// </summary>
    public static FbTransactionOptions ReadWrite(TimeSpan lockTimeout) => new()
    {
        TransactionBehavior = FbTransactionBehavior.Write | FbTransactionBehavior.ReadCommitted
                              | FbTransactionBehavior.RecVersion | FbTransactionBehavior.Wait,
        WaitTimeout = lockTimeout,
    };

    /// <summary>Snapshot de MON$ (cada lectura de MON$ necesita su propia transacción).</summary>
    public static FbTransactionOptions Snapshot { get; } = new()
    {
        TransactionBehavior = FbTransactionBehavior.Read | FbTransactionBehavior.Concurrency,
    };
}
