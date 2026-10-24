namespace FbStudio.Core.Results;

public sealed record ColumnInfo(int Ordinal, string Name, string DataTypeName)
{
    public bool IsNumeric => DataTypeName is "INTEGER" or "BIGINT" or "SMALLINT" or "NUMERIC" or "DECIMAL"
        or "DOUBLE PRECISION" or "FLOAT" or "INT128" or "DECFLOAT";
}

/// <summary>Lecturas de registros de una tabla durante una ejecución (de MON$TABLE_STATS).</summary>
public sealed record TableReads(string Table, long Indexed, long NonIndexed);

public sealed class QueryOutcome
{
    public ColumnInfo[] Columns { get; set; } = [];
    public string? Plan { get; set; }
    public bool PlanIsExplained { get; set; }
    public long RowsFetched { get; set; }
    /// <summary>Filas afectadas por INSERT/UPDATE/DELETE/MERGE (-1 si no aplica).</summary>
    public int RecordsAffected { get; set; } = -1;
    public bool Cancelled { get; set; }

    /// <summary>Se confirmó sola al terminar (autocommit, D-84).</summary>
    public bool AutoCommitted { get; set; }
    public bool LimitReached { get; set; }
    public TimeSpan PrepareTime { get; set; }
    public TimeSpan? FirstRowTime { get; set; }
    public TimeSpan TotalTime { get; set; }
    /// <summary>Desde que se pidió cancelar hasta que el lector dejó de entregar filas.</summary>
    public TimeSpan? CancelLatency { get; set; }
    public IReadOnlyList<TableReads> Reads { get; set; } = [];
    public long Fetches { get; set; }
    public long PageReads { get; set; }
    public long PageWrites { get; set; }
    /// <summary>La ejecución dejó (o usó) una transacción de escritura abierta que hay que confirmar o deshacer.</summary>
    public bool TransactionOpen { get; set; }
    public string? Error { get; set; }
    public int? ErrorCode { get; set; }
}
