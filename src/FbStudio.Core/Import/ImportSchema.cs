using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Import;

/// <summary>Una columna de la tabla nueva que se arma desde el archivo: su nombre, su tipo y de qué columna del archivo sale.</summary>
public sealed record NewTableColumn(string Name, string Type, int FileIndex);

/// <summary>
/// Importar a una tabla nueva (F-26): el nombre de la tabla y de sus columnas a partir del archivo y el tipo de cada una
/// según lo que trae (números enteros, con decimales, fechas, fecha y hora, sí/no de Excel o texto con su largo).
/// </summary>
public static partial class ImportSchema
{
    /// <summary>Firebird 3 admite nombres de hasta 31 bytes; con letras, números y «_» es lo mismo que 31 caracteres.</summary>
    public const int MaxNameLength = 31;

    /// <summary>Más largo que esto (en caracteres) va como BLOB de texto en vez de VARCHAR.</summary>
    public const int MaxVarchar = 8000;

    /// <summary>
    /// Un nombre de tabla o de columna sin comillas: mayúsculas sin acentos, «_» en lugar de espacios y signos, que
    /// empiece con una letra y de hasta 31 caracteres. <paramref name="fallback"/> si no queda nada.
    /// </summary>
    public static string Identifier(string text, string fallback)
    {
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' ? char.ToUpperInvariant(c) : '_');
        }
        var name = Underscores().Replace(plain.ToString(), "_").Trim('_');
        if (name.Length == 0) name = fallback;
        if (!char.IsLetter(name[0])) name = "C_" + name;
        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd('_') : name;
    }

    /// <summary>El nombre de la tabla nueva a partir del archivo («Pacientes 2026.xlsx» → PACIENTES_2026).</summary>
    public static string TableName(string path) => Identifier(Path.GetFileNameWithoutExtension(path), "IMPORTADA");

    /// <summary>
    /// Las columnas de la tabla nueva: una por columna del archivo, con el nombre del encabezado (sin repetir) y el tipo
    /// que corresponde a todos sus valores.
    /// </summary>
    public static IReadOnlyList<NewTableColumn> Infer(IReadOnlyList<string> headers, IEnumerable<object?[]> rows, CultureInfo culture)
    {
        var guesses = headers.Select(_ => new TypeGuess()).ToArray();
        foreach (var row in rows)
            for (var i = 0; i < guesses.Length && i < row.Length; i++)
                guesses[i].Add(row[i], culture);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<NewTableColumn>();
        for (var i = 0; i < headers.Count; i++)
        {
            var name = Identifier(headers[i], $"COLUMNA_{i + 1}");
            for (var n = 2; !used.Add(name); n++)
            {
                var suffix = $"_{n}";
                var stem = Identifier(headers[i], $"COLUMNA_{i + 1}");
                name = (stem.Length + suffix.Length > MaxNameLength ? stem[..(MaxNameLength - suffix.Length)] : stem) + suffix;
            }
            columns.Add(new NewTableColumn(name, guesses[i].Type, i));
        }
        return columns;
    }

    /// <summary>El tipo para estos valores (para las pruebas y para recalcular una columna).</summary>
    public static string InferType(IEnumerable<object?> values, CultureInfo culture)
    {
        var guess = new TypeGuess();
        foreach (var v in values) guess.Add(v, culture);
        return guess.Type;
    }

    /// <summary>El CREATE TABLE de la tabla nueva (sin clave: se puede agregar después).</summary>
    public static string CreateTableSql(string table, IReadOnlyList<NewTableColumn> columns)
    {
        var width = columns.Max(c => SqlIdentifier.Quote(c.Name).Length);
        var lines = columns.Select(c => $"    {SqlIdentifier.Quote(c.Name).PadRight(width)} {c.Type}");
        return $"CREATE TABLE {SqlIdentifier.Quote(table)} ({Environment.NewLine}{string.Join("," + Environment.NewLine, lines)}{Environment.NewLine})";
    }

    /// <summary>Lo que se va sabiendo de una columna mientras se recorren sus valores.</summary>
    private sealed class TypeGuess
    {
        private bool _any, _notInteger, _notNumber, _notDate, _hasTime, _notBool;
        private long _min, _max;
        private int _scale, _intDigits, _length;

        public void Add(object? value, CultureInfo culture)
        {
            switch (value)
            {
                case null:
                    return;
                case string s when s.Trim().Length == 0:
                    return;
                case bool:
                    Seen(value.ToString()!.Length);
                    _notInteger = _notNumber = _notDate = true;
                    return;
                case DateTime dt:
                    Seen(dt.TimeOfDay == TimeSpan.Zero ? 10 : 19);
                    _notInteger = _notNumber = _notBool = true;
                    if (dt.TimeOfDay != TimeSpan.Zero) _hasTime = true;
                    return;
                case double d:
                    Seen(d.ToString("R", CultureInfo.InvariantCulture).Length);
                    _notDate = _notBool = true;
                    Number(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case string s:
                    var text = s.Trim();
                    Seen(s.Length);
                    _notBool = true;
                    if (!_notNumber || !_notInteger)
                    {
                        // Lo mismo que se manda al importar (coma decimal de la PC → punto).
                        var normalized = ImportMapping.Convert(text, "NUMERIC", culture) as string ?? text;
                        if (NumberText().IsMatch(normalized) && !LeadingZero().IsMatch(normalized)) Number(normalized);
                        else _notInteger = _notNumber = true;
                    }
                    if (!_notDate) Date(text, culture);
                    return;
                default:
                    Seen(value.ToString()?.Length ?? 0);
                    _notInteger = _notNumber = _notDate = _notBool = true;
                    return;
            }
        }

        private void Seen(int length)
        {
            _any = true;
            _length = Math.Max(_length, length);
        }

        private void Number(string invariant)
        {
            if (!decimal.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                _notInteger = _notNumber = true;
                return;
            }
            var plain = value.ToString(CultureInfo.InvariantCulture); // sin exponente y con los decimales escritos («1.50»)
            var dot = plain.IndexOf('.');
            var scale = dot < 0 ? 0 : plain.Length - dot - 1;
            if (value != decimal.Truncate(value) || scale > 0) _notInteger = true;
            else if (value is < long.MinValue or > long.MaxValue) _notInteger = true;
            else
            {
                _min = Math.Min(_min, (long)value);
                _max = Math.Max(_max, (long)value);
            }
            _scale = Math.Max(_scale, scale);
            _intDigits = Math.Max(_intDigits, decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).Length);
        }

        private void Date(string text, CultureInfo culture)
        {
            if (DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            {
                if (iso.TimeOfDay != TimeSpan.Zero || text.Length > 10) _hasTime = true;
                return;
            }
            if (SlashDate().IsMatch(text) && DateTime.TryParse(text, culture, DateTimeStyles.None, out var local))
            {
                if (local.TimeOfDay != TimeSpan.Zero || text.Contains(':')) _hasTime = true;
                return;
            }
            _notDate = true;
        }

        public string Type
        {
            get
            {
                if (!_any) return "VARCHAR(100)";
                if (!_notBool) return "BOOLEAN";
                if (!_notInteger) return _min >= int.MinValue && _max <= int.MaxValue ? "INTEGER" : "BIGINT";
                if (!_notNumber)
                {
                    var scale = Math.Min(_scale, 9);
                    return _intDigits + scale <= 18 ? $"NUMERIC(18,{Math.Max(scale, 2)})" : "DOUBLE PRECISION";
                }
                if (!_notDate) return _hasTime ? "TIMESTAMP" : "DATE";
                return _length > MaxVarchar ? "BLOB SUB_TYPE TEXT" : $"VARCHAR({NiceLength(_length)})";
            }
        }

        private static int NiceLength(int length) => length switch
        {
            <= 10 => 10,
            <= 20 => 20,
            <= 50 => 50,
            <= 100 => 100,
            <= 255 => 255,
            _ => Math.Min(MaxVarchar, (length + 499) / 500 * 500),
        };
    }

    [GeneratedRegex("_+")]
    private static partial Regex Underscores();

    [GeneratedRegex(@"^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$")]
    private static partial Regex NumberText();

    /// <summary>«00123» es un código, no un número: si se guardara como número perdería los ceros.</summary>
    [GeneratedRegex(@"^[+-]?0\d")]
    private static partial Regex LeadingZero();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{2,4}(\s+\d{1,2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex SlashDate();
}
