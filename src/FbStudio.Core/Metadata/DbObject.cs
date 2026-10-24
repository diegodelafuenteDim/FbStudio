namespace FbStudio.Core.Metadata;

public enum DbObjectKind
{
    Table,
    View,
    Procedure,
    Function,
    Package,
    Trigger,
    Generator,
    Exception,
    Domain,
    Index,
    Role,
}

/// <param name="Detail">Dato secundario para mostrar al lado (tabla de un trigger o índice, tipo de un dominio, etc.).</param>
/// <param name="Inactive">Trigger o índice desactivado.</param>
/// <param name="Owner">Tabla o vista de un trigger o índice (para las áreas, F-9); null en los demás.</param>
public sealed record DbObject(DbObjectKind Kind, string Name, string? Detail = null, bool Inactive = false, string? Owner = null);

/// <param name="Description">Comentario de la columna (algunas bases guardan ahí metadata, p. ej. #FK_%TABLA,%CODIGO,%NOMBRE#).</param>
/// <param name="IsComputed">Columna calculada (COMPUTED BY): se muestra pero no se edita.</param>
public sealed record DbColumn(string Name, string Type, bool NotNull, bool IsPrimaryKey, string? Domain, string? Description, bool IsComputed = false)
{
    public string Flags => (IsPrimaryKey ? "PK " : "") + (NotNull ? "NOT NULL" : "");
}

public static class DbObjectKinds
{
    public static IReadOnlyList<DbObjectKind> All { get; } = Enum.GetValues<DbObjectKind>();

    public static string Label(DbObjectKind kind) => kind switch
    {
        DbObjectKind.Table => Resources.CoreStrings.KindTablePlural,
        DbObjectKind.View => Resources.CoreStrings.KindViewPlural,
        DbObjectKind.Procedure => Resources.CoreStrings.KindProcedurePlural,
        DbObjectKind.Function => Resources.CoreStrings.KindFunctionPlural,
        DbObjectKind.Package => Resources.CoreStrings.KindPackagePlural,
        DbObjectKind.Trigger => Resources.CoreStrings.KindTriggerPlural,
        DbObjectKind.Generator => Resources.CoreStrings.KindGeneratorPlural,
        DbObjectKind.Exception => Resources.CoreStrings.KindExceptionPlural,
        DbObjectKind.Domain => Resources.CoreStrings.KindDomainPlural,
        DbObjectKind.Index => Resources.CoreStrings.KindIndexPlural,
        DbObjectKind.Role => Resources.CoreStrings.KindRolePlural,
        _ => kind.ToString(),
    };

    /// <summary>Nombre en singular, para títulos ("Procedimiento P_CONTAR").</summary>
    public static string Singular(DbObjectKind kind) => kind switch
    {
        DbObjectKind.Table => Resources.CoreStrings.KindTable,
        DbObjectKind.View => Resources.CoreStrings.KindView,
        DbObjectKind.Procedure => Resources.CoreStrings.KindProcedure,
        DbObjectKind.Function => Resources.CoreStrings.KindFunction,
        DbObjectKind.Package => Resources.CoreStrings.KindPackage,
        DbObjectKind.Trigger => Resources.CoreStrings.KindTrigger,
        DbObjectKind.Generator => Resources.CoreStrings.KindGenerator,
        DbObjectKind.Exception => Resources.CoreStrings.KindException,
        DbObjectKind.Domain => Resources.CoreStrings.KindDomain,
        DbObjectKind.Index => Resources.CoreStrings.KindIndex,
        DbObjectKind.Role => Resources.CoreStrings.KindRole,
        _ => kind.ToString(),
    };

    /// <summary>Se editan y compilan desde su pestaña con CREATE OR ALTER.</summary>
    public static bool IsCompilable(DbObjectKind kind) =>
        kind is DbObjectKind.Procedure or DbObjectKind.Trigger or DbObjectKind.View or DbObjectKind.Function;

    /// <summary>Tablas y vistas tienen columnas para mostrar y se pueden consultar con SELECT.</summary>
    public static bool IsRelation(DbObjectKind kind) => kind is DbObjectKind.Table or DbObjectKind.View;
}
