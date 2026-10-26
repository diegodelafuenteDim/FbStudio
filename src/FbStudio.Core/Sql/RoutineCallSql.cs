namespace FbStudio.Core.Sql;

/// <summary>
/// «Generar SQL» de un procedimiento o una función desde el árbol: la llamada con un parámetro «:NOMBRE» (F-25) por cada
/// parámetro de entrada, que se piden al ejecutar; los de una UDF, que no tienen nombre, van como «?».
/// </summary>
public static class RoutineCallSql
{
    /// <summary>«SELECT * FROM P(:A, :B)» (seleccionable) o «EXECUTE PROCEDURE P(:A, :B)»; sin parámetros, sin paréntesis.</summary>
    public static string Procedure(RoutineSignature signature, bool selectable)
    {
        var call = Name(signature.Name) + Arguments(signature);
        return selectable ? $"SELECT *\nFROM {call}" : $"EXECUTE PROCEDURE {call}";
    }

    /// <summary>«SELECT F(:X) FROM RDB$DATABASE».</summary>
    public static string Function(RoutineSignature signature) =>
        $"SELECT {Name(signature.Name)}({string.Join(", ", signature.Parameters.Select(Parameter))})\nFROM RDB$DATABASE";

    private static string Arguments(RoutineSignature signature) =>
        signature.Parameters.Count == 0 ? "" : $"({string.Join(", ", signature.Parameters.Select(Parameter))})";

    /// <summary>«PKG.PROC» con comillas en cada parte si hacen falta.</summary>
    private static string Name(string name) => string.Join('.', name.Split('.').Select(SqlIdentifier.Quote));

    private static string Parameter(RoutineParameter parameter) => parameter.Name.Length == 0 ? "?" : TableSql.Parameter(parameter.Name);
}
