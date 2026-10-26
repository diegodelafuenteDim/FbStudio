namespace FbStudio.Core.Results;

/// <summary>
/// Una fila «en vertical»: un renglón por columna con su nombre, su tipo y su valor. Las fechas y horas van ya como
/// texto (cada una con el formato de su tipo, porque en la columna «Valor» se mezclan tipos); los textos, los números,
/// los binarios y NULL quedan tal cual (así un blob se puede ver entero con «Ver el valor»).
/// </summary>
public static class RowRecord
{
    public static List<object[]> Build(IReadOnlyList<ColumnInfo> columns, IReadOnlyList<object?> row) =>
        columns.Select(c =>
        {
            var value = row[c.Ordinal];
            object shown = value switch
            {
                null => DBNull.Value,
                DateTime or TimeSpan => CellFormatter.Format(value, c.DataTypeName),
                _ => value,
            };
            return new object[] { c.Name, c.DataTypeName, shown };
        }).ToList();
}
