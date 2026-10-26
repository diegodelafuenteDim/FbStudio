using System.Text;
using FbStudio.Core.Metadata;
using FbStudio.Core.Resources;

namespace FbStudio.Core.Sql;

/// <summary>Qué sentencia arma «Generar SQL» del árbol.</summary>
public enum TableSqlKind
{
    Select,
    Insert,
    Update,
    Delete,
    UpdateOrInsert,
}

/// <summary>
/// «Generar SQL» de una tabla o una vista desde el árbol: un SELECT con todas las columnas y, de una tabla, INSERT, UPDATE
/// y DELETE por la clave primaria y UPDATE OR INSERT. Los valores van como parámetros «:COLUMNA» (F-25), que se piden al
/// ejecutar. Las columnas calculadas no se escriben. Sin clave primaria, la condición queda para completar: así no corre
/// (no actualiza ni borra toda la tabla por un descuido).
/// </summary>
public static class TableSql
{
    /// <summary>Las listas largas de columnas se parten en renglones de hasta este ancho.</summary>
    private const int Width = 100;

    public static string Build(TableSqlKind kind, string table, IReadOnlyList<DbColumn> columns)
    {
        var t = SqlIdentifier.Quote(table);
        var written = columns.Where(c => !c.IsComputed).Select(c => c.Name).ToList();
        var key = columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
        var placeholder = $"/* {CoreStrings.TableSqlNoKey} */";
        var where = "WHERE " + (key.Count > 0 ? string.Join(" AND ", key.Select(k => $"{Q(k)} = {P(k)}")) : placeholder);
        return kind switch
        {
            TableSqlKind.Select => $"SELECT {Wrap(columns.Select(c => Q(c.Name)), 7)}\nFROM {t}",
            TableSqlKind.Insert => $"INSERT INTO {t} ({Wrap(written.Select(Q), 4)})\nVALUES ({Wrap(written.Select(P), 4)})",
            TableSqlKind.Update => $"UPDATE {t}\nSET {string.Join(",\n    ", written.Except(key).Select(c => $"{Q(c)} = {P(c)}"))}\n{where}",
            TableSqlKind.Delete => $"DELETE FROM {t}\n{where}",
            TableSqlKind.UpdateOrInsert => $"UPDATE OR INSERT INTO {t} ({Wrap(written.Select(Q), 4)})\nVALUES ({Wrap(written.Select(P), 4)})\n"
                                           + $"MATCHING ({(key.Count > 0 ? string.Join(", ", key.Select(Q)) : placeholder)})",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string Q(string name) => SqlIdentifier.Quote(name);

    /// <summary>El parámetro «:NOMBRE» de una columna o de un parámetro de procedimiento.</summary>
    public static string Parameter(string name) => P(name);

    /// <summary>El parámetro de una columna: «:FECHA»; lo que no puede ir en un nombre de parámetro («Nro Doc») pasa a «_».</summary>
    private static string P(string column)
    {
        var name = string.Concat(column.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$' ? c : '_'));
        return ":" + (char.IsAsciiLetter(name[0]) || name[0] == '_' ? name : "P_" + name);
    }

    /// <summary>«A, B, C» partido en renglones de hasta <see cref="Width"/> columnas; los que siguen, con sangría.</summary>
    private static string Wrap(IEnumerable<string> items, int indent)
    {
        var sb = new StringBuilder();
        var line = indent;
        foreach (var item in items)
        {
            if (sb.Length > 0 && line + item.Length + 2 > Width)
            {
                sb.Append(",\n").Append(' ', indent);
                line = indent;
            }
            else if (sb.Length > 0)
            {
                sb.Append(", ");
                line += 2;
            }
            sb.Append(item);
            line += item.Length;
        }
        return sb.ToString();
    }
}
