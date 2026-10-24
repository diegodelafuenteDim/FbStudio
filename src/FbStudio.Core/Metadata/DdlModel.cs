namespace FbStudio.Core.Metadata;

// Definiciones leídas de RDB$, ya resueltas a texto SQL (tipos, defaults, fuentes). DdlWriter las convierte en DDL.

/// <summary>Columna, parámetro, argumento o dominio.</summary>
/// <param name="Type">Tipo SQL ("VARCHAR(60)", "NUMERIC(12,2)"), nombre de dominio o "TYPE OF COLUMN T.C".</param>
/// <param name="Default">Tal como se guardó ("DEFAULT 0", "= 10"); el escritor completa la palabra si falta.</param>
/// <param name="Computed">Expresión de una columna calculada, con sus paréntesis.</param>
/// <param name="CharacterSet">Solo cuando difiere del charset por defecto de la base.</param>
/// <param name="Collation">Solo cuando no es la de por defecto (o la del dominio).</param>
public sealed record FieldDefinition(
    string Name,
    string Type,
    bool NotNull = false,
    string? Default = null,
    string? Computed = null,
    string? CharacterSet = null,
    string? Collation = null,
    bool Identity = false,
    string? Description = null);

public enum ConstraintKind
{
    PrimaryKey,
    Unique,
    ForeignKey,
    Check,
}

/// <param name="IndexName">Índice que respalda la constraint (se escribe USING INDEX si tiene otro nombre).</param>
/// <param name="CheckSource">Fuente completa de un CHECK, p. ej. "CHECK (CANTIDAD > 0)".</param>
public sealed record ConstraintDefinition(
    string Name,
    ConstraintKind Kind,
    IReadOnlyList<string> Columns,
    string? IndexName = null,
    bool IndexDescending = false,
    string? ReferencedTable = null,
    IReadOnlyList<string>? ReferencedColumns = null,
    string? OnUpdate = null,
    string? OnDelete = null,
    string? CheckSource = null);

public sealed record IndexDefinition(
    string Name,
    string Table,
    IReadOnlyList<string> Columns,
    bool Unique = false,
    bool Descending = false,
    bool Inactive = false,
    string? Expression = null,
    string? Description = null);

/// <param name="Table">Null en los triggers de base de datos (ON CONNECT, etc.).</param>
/// <param name="Type">RDB$TRIGGER_TYPE.</param>
/// <param name="Source">RDB$TRIGGER_SOURCE (empieza con AS).</param>
public sealed record TriggerDefinition(string Name, string? Table, long Type, int Position, bool Inactive, string? Source, string? Description = null);

/// <param name="RelationType">RDB$RELATION_TYPE: 0 persistente, 2 externa, 4 GTT preserve rows, 5 GTT delete rows.</param>
public sealed record TableDefinition(
    string Name,
    IReadOnlyList<FieldDefinition> Columns,
    IReadOnlyList<ConstraintDefinition> Constraints,
    IReadOnlyList<IndexDefinition> Indices,
    IReadOnlyList<TriggerDefinition> Triggers,
    string? Description = null,
    int RelationType = 0,
    string? ExternalFile = null);

public sealed record ViewDefinition(string Name, IReadOnlyList<FieldDefinition> Columns, string? Source, IReadOnlyList<TriggerDefinition> Triggers, string? Description = null);

public sealed record ProcedureDefinition(string Name, IReadOnlyList<FieldDefinition> Inputs, IReadOnlyList<FieldDefinition> Outputs, string? Source, string? Description = null);

/// <param name="ModuleName">UDF: biblioteca externa (DECLARE EXTERNAL FUNCTION).</param>
/// <param name="ReturnMechanism">UDF: RDB$MECHANISM del retorno (0 por valor, 1 por referencia, 2 por descriptor; negativo = FREE_IT).</param>
public sealed record FunctionDefinition(
    string Name,
    IReadOnlyList<FieldDefinition> Inputs,
    FieldDefinition Returns,
    string? Source,
    bool Deterministic = false,
    string? ModuleName = null,
    string? EntryPoint = null,
    int? ReturnMechanism = null,
    string? Description = null);

public sealed record PackageDefinition(string Name, string? Header, string? Body, string? Description = null);

/// <param name="CurrentValue">Null si el usuario no tiene permiso para leerlo (USAGE).</param>
public sealed record GeneratorDefinition(string Name, long? CurrentValue, long InitialValue = 0, int Increment = 1, string? Description = null);

public sealed record ExceptionDefinition(string Name, string Message, string? Description = null);

/// <param name="Check">Fuente del CHECK del dominio, p. ej. "CHECK (VALUE > 0)".</param>
public sealed record DomainDefinition(FieldDefinition Field, string? Check);

public sealed record RoleDefinition(string Name, string? Owner, string? Description = null);
