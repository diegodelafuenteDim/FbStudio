namespace FbStudio.Core.Metadata;

/// <summary>
/// Cuántas líneas ocupa el encabezado que <see cref="DdlWriter"/> pone antes de la fuente PSQL. Con eso, una línea de
/// la fuente guardada en RDB$ se traslada a la línea que se ve en el editor (búsqueda en metadatos).
/// </summary>
public static class DdlLayout
{
    /// <summary>CREATE … ( + una línea por entrada + [RETURNS ( + una por salida] + AS.</summary>
    public static int ProcedureHeader(int inputs, int outputs) => 1 + inputs + (outputs > 0 ? 1 + outputs : 0) + 1;

    /// <summary>CREATE … ( + una línea por entrada + RETURNS … + AS.</summary>
    public static int FunctionHeader(int inputs) => 1 + inputs + 1 + 1;

    /// <summary>CREATE OR ALTER VIEW … ( + una línea por columna + AS.</summary>
    public static int ViewHeader(int columns) => 1 + columns + 1;

    /// <summary>CREATE OR ALTER TRIGGER … + ACTIVE …; la fuente de un trigger suele empezar con AS (si no, se agrega).</summary>
    public static int TriggerHeader(string? source) =>
        2 + (source is not null && SourceSearch.Normalize(source).StartsWith("AS", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
}
