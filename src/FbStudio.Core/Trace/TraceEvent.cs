namespace FbStudio.Core.Trace;

/// <summary>Lo que hizo una sentencia en una tabla (la grilla «Table / Natural / Index / Update…» de la traza).</summary>
public sealed record TraceTableStat(string Table, long Natural, long Index, long Update, long Insert, long Delete, long Backout, long Purge, long Expunge)
{
    /// <summary>Recorrida completa de la tabla: lecturas sin índice.</summary>
    public bool FullScan => Natural > 0;
}

/// <summary>
/// Un evento de la traza de Firebird (F-24): una sentencia, un procedimiento o una función que terminó, un error, una
/// conexión… Lo que no trae el evento queda en null.
/// </summary>
public sealed record TraceEvent
{
    public DateTime Time { get; init; }

    /// <summary>Como lo escribe el servidor: EXECUTE_STATEMENT_FINISH, EXECUTE_PROCEDURE_FINISH, ERROR AT …</summary>
    public required string Kind { get; init; }

    /// <summary>El servidor lo marcó FAILED (falló) o UNAUTHORIZED (sin permiso).</summary>
    public bool Failed { get; init; }

    /// <summary>La base como la nombró el programa al conectarse, sin carpetas (las rutas no se muestran, D-54).</summary>
    public string? Database { get; init; }

    public long? Attachment { get; init; }
    public string? User { get; init; }
    public string? Role { get; init; }
    public string? Protocol { get; init; }

    /// <summary>IP del cliente, sin el puerto.</summary>
    public string? Address { get; init; }

    /// <summary>El programa que se conectó (solo el nombre del archivo).</summary>
    public string? Process { get; init; }

    public long? ProcessId { get; init; }
    public long? Transaction { get; init; }
    public long? Statement { get; init; }

    /// <summary>El SQL, o «EXECUTE PROCEDURE NOMBRE» / «FUNCTION NOMBRE» / el trigger.</summary>
    public string? Sql { get; init; }

    public string? Plan { get; init; }

    /// <summary>Los parámetros con su valor («param0 = integer, "5"»), uno por renglón.</summary>
    public string? Parameters { get; init; }

    public long? Records { get; init; }
    public long? Milliseconds { get; init; }
    public long? Reads { get; init; }
    public long? Writes { get; init; }
    public long? Fetches { get; init; }
    public long? Marks { get; init; }

    public IReadOnlyList<TraceTableStat> Tables { get; init; } = [];

    /// <summary>El mensaje de un evento de error.</summary>
    public string? Error { get; init; }

    /// <summary>Terminó de ejecutarse algo con tiempo: lo que entra en el análisis por consulta.</summary>
    public bool IsExecution => Milliseconds is not null && Kind.Contains("_FINISH", StringComparison.Ordinal);

    public bool IsError => Kind.StartsWith("ERROR", StringComparison.Ordinal) || Failed;

    /// <summary>Alguna tabla se recorrió entera.</summary>
    public bool HasFullScan => Tables.Any(t => t.FullScan);
}
