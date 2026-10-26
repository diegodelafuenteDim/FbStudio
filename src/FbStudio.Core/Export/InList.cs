using System.Text;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Export;

/// <summary>
/// Los valores de una columna como condición para pegar en otra consulta: «COL IN (1, 2, 3)», sin repetidos ni NULL, con
/// los literales de Firebird (textos entre comillas, fechas como DATE '…'). Firebird acepta hasta 1.500 valores por IN:
/// con más, se arman varios unidos con OR.
/// </summary>
public static class InList
{
    public const int MaxPerIn = 1_500;

    /// <summary>Null si no hay ningún valor (todos NULL o ninguna fila).</summary>
    public static string? Format(ColumnInfo column, IEnumerable<object?> values)
    {
        var literals = values.Where(v => !CellFormatter.IsNull(v)).Select(v => SqlLiteral.Format(v, column.DataTypeName)).Distinct(StringComparer.Ordinal).ToList();
        if (literals.Count == 0) return null;
        var name = SqlIdentifier.Quote(column.Name);
        var sb = new StringBuilder();
        for (var start = 0; start < literals.Count; start += MaxPerIn)
        {
            if (start > 0) sb.Append("\n   OR ");
            sb.Append(name).Append(" IN (");
            var line = (start > 0 ? 6 : 0) + name.Length + 5; // «   OR » + «COL IN (»
            foreach (var (literal, i) in literals.Skip(start).Take(MaxPerIn).Select((l, i) => (l, i)))
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (line + literal.Length + 2 > 100)
                    {
                        sb.Append("\n    ");
                        line = 4;
                    }
                    else
                    {
                        sb.Append(' ');
                        line++;
                    }
                }
                sb.Append(literal);
                line += literal.Length + 1;
            }
            sb.Append(')');
        }
        return sb.ToString();
    }
}
