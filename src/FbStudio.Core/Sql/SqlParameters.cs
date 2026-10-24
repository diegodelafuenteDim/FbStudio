using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Sql;

/// <summary>
/// Una sentencia con parámetros (F-25): el SQL listo para mandar (cada «:nombre» cambiado por «?»), qué parámetro va en
/// cada «?», en orden, y los nombres distintos en el orden en que aparecen. Los «?» sin nombre se llaman #1, #2…
/// </summary>
public sealed record SqlParameterSet(string Sql, IReadOnlyList<string> Positions, IReadOnlyList<string> Names)
{
    public bool IsEmpty => Positions.Count == 0;

    /// <summary>Los valores en el orden de los «?», a partir de lo elegido para cada nombre (un nombre repetido va repetido).</summary>
    public IReadOnlyList<object?> Values(IReadOnlyDictionary<string, object?> byName) =>
        Positions.Select(p => byName.GetValueOrDefault(p)).ToList();
}

/// <summary>
/// Parámetros en las consultas del editor (F-25): «?» y «:nombre», como en otras herramientas. Firebird solo entiende
/// «?»: los «:nombre» se cambian por «?» y el mismo nombre repetido recibe el mismo valor. No se buscan dentro de textos
/// ('…', q'{…}'), identificadores entre comillas, comentarios ni en PSQL (procedimientos, triggers, EXECUTE BLOCK),
/// donde «:variable» es una variable y no un parámetro.
/// </summary>
public static partial class SqlParameters
{
    /// <summary>Si en esta clase de sentencia «?» y «:nombre» son parámetros (no en DDL ni PSQL).</summary>
    public static bool Applies(StatementInfo statement) =>
        statement.Kind is StatementKind.Select or StatementKind.Dml
        || (statement.Kind == StatementKind.Execute && !statement.Keyword.Contains("BLOCK", StringComparison.OrdinalIgnoreCase));

    public static SqlParameterSet Find(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var positions = new List<string>();
        var names = new List<string>();
        var unnamed = 0;
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            // Lo que no se mira: comentarios, textos, textos q'…' e identificadores entre comillas.
            if (c == '-' && next == '-')
            {
                var end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                output.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                output.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if ((c is 'q' or 'Q') && next == '\'' && i + 2 < sql.Length && (i == 0 || !IsIdentifierChar(sql[i - 1])))
            {
                var close = sql[i + 2] switch { '(' => ')', '[' => ']', '{' => '}', '<' => '>', var d => d };
                var end = sql.IndexOf(close + "'", i + 3, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                output.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c is '\'' or '"')
            {
                var end = i + 1;
                while (end < sql.Length)
                {
                    if (sql[end] == c)
                    {
                        if (end + 1 < sql.Length && sql[end + 1] == c) { end += 2; continue; } // '' o "" dentro del texto
                        end++;
                        break;
                    }
                    end++;
                }
                output.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c == '?')
            {
                var name = $"#{++unnamed}";
                positions.Add(name);
                names.Add(name);
                output.Append('?');
                i++;
                continue;
            }
            if (c == ':' && IsIdentifierStart(next) && (i == 0 || !IsIdentifierChar(sql[i - 1])))
            {
                var end = i + 1;
                while (end < sql.Length && IsIdentifierChar(sql[end])) end++;
                var name = sql[(i + 1)..end];
                var known = names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (known is null) names.Add(name);
                positions.Add(known ?? name);
                output.Append('?');
                i = end;
                continue;
            }
            output.Append(c);
            i++;
        }
        return new SqlParameterSet(output.ToString(), positions, names);
    }

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$';

    /// <summary>
    /// El valor que se manda por lo que se escribió: null (NULL) si se marcó así; una fecha escrita como se escribe en
    /// la PC («22/10/2026», «22/10/2026 14:30») como fecha; un número con coma decimal («12,5») con punto. Todo lo demás
    /// va como texto y Firebird lo convierte al tipo de la columna (así «00123» sigue siendo «00123» si es VARCHAR).
    /// </summary>
    public static object? ToValue(string? text, bool isNull, CultureInfo culture)
    {
        if (isNull) return null;
        var value = text ?? "";
        var trimmed = value.Trim();
        if (DecimalComma().IsMatch(trimmed) && culture.NumberFormat.NumberDecimalSeparator == ",") return trimmed.Replace(',', '.');
        if (SlashDate().IsMatch(trimmed) && DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var date)) return date;
        return value;
    }

    [GeneratedRegex(@"^-?\d+,\d+$")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{2,4}(\s+\d{1,2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex SlashDate();
}
