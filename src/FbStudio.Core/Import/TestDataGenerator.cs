using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FbStudio.Core.Metadata;

namespace FbStudio.Core.Import;

/// <summary>Qué se pone en una columna al generar datos de prueba.</summary>
public enum TestValueKind
{
    /// <summary>No se escribe (calculada, o se deja vacía o con su DEFAULT).</summary>
    Skip,

    /// <summary>Números seguidos desde el mayor que hay (para una clave primaria).</summary>
    Sequence,

    /// <summary>Uno de los valores que ya están en la tabla referenciada (para una FK).</summary>
    Reference,

    FirstName,
    LastName,
    FullName,
    Email,
    Phone,
    Document,
    Address,
    City,
    Text,
    Code,
    Integer,
    Decimal,
    Date,
    Timestamp,
    Time,
    Boolean,
}

/// <summary>Una columna a llenar: su nombre, su tipo, si acepta NULL y qué se pone.</summary>
public sealed record TestColumn(string Name, string Type, bool NotNull, TestValueKind Kind);

/// <param name="Seed">Con la misma semilla salen los mismos datos (para repetir una prueba).</param>
/// <param name="NullPercent">En las columnas que aceptan NULL, cuántas filas de cada 100 van vacías.</param>
/// <param name="FirstSequence">Desde dónde numeran las columnas <see cref="TestValueKind.Sequence"/>.</param>
/// <param name="References">Los valores que ya están en la tabla referenciada, por columna.</param>
/// <param name="Spanish">Nombres, calles y textos en castellano; si no, en inglés.</param>
public sealed record TestDataOptions(int Seed = 1, int NullPercent = 10, long FirstSequence = 1,
    IReadOnlyDictionary<string, IReadOnlyList<object>>? References = null, bool Spanish = true);

/// <summary>
/// Genera filas de prueba para una tabla: valores verosímiles según el tipo y el nombre de cada columna (nombres,
/// apellidos, mails, teléfonos, documentos, direcciones, montos, fechas del último año), las FK con valores que ya
/// existen y la clave numerada desde el mayor que hay. Todo inventado: no sale de ninguna base real.
/// </summary>
public static partial class TestDataGenerator
{
    private static readonly string[] FirstNamesEs = ["Juan", "María", "Carlos", "Ana", "Luis", "Laura", "Jorge", "Sofía", "Diego", "Lucía",
        "Martín", "Paula", "Pablo", "Valeria", "Sergio", "Carolina", "Andrés", "Florencia", "Gustavo", "Natalia"];
    private static readonly string[] LastNamesEs = ["González", "Rodríguez", "Gómez", "Fernández", "López", "Díaz", "Martínez", "Pérez",
        "García", "Sánchez", "Romero", "Sosa", "Álvarez", "Torres", "Ruiz", "Ramírez", "Flores", "Benítez", "Acosta", "Medina"];
    private static readonly string[] StreetsEs = ["San Martín", "Belgrano", "Rivadavia", "Sarmiento", "Mitre", "Moreno", "Las Heras", "Colón", "Perú", "Italia"];
    private static readonly string[] CitiesEs = ["Mendoza", "Córdoba", "Rosario", "La Plata", "Salta", "Neuquén", "San Juan", "Tucumán", "Mar del Plata", "Santa Fe"];
    private static readonly string[] WordsEs = ["control", "de", "rutina", "sin", "novedades", "pendiente", "revisión", "pedido", "entregado",
        "a", "confirmar", "con", "el", "cliente", "nuevo", "turno", "listo", "para", "facturar", "observado"];
    private static readonly string[] FirstNamesEn = ["James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda", "David", "Susan",
        "William", "Karen", "Richard", "Lisa", "Thomas", "Nancy", "Daniel", "Emma", "Paul", "Olivia"];
    private static readonly string[] LastNamesEn = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Miller", "Davis", "Wilson", "Taylor", "Clark",
        "Lewis", "Walker", "Hall", "Allen", "Young", "King", "Wright", "Scott", "Green", "Baker"];
    private static readonly string[] StreetsEn = ["Main St", "Oak Ave", "Maple Dr", "Cedar Ln", "Elm St", "Park Ave", "Pine St", "Lake Rd", "Hill St", "River Rd"];
    private static readonly string[] CitiesEn = ["Springfield", "Riverside", "Fairview", "Franklin", "Greenville", "Bristol", "Clinton", "Salem", "Madison", "Georgetown"];
    private static readonly string[] WordsEn = ["routine", "check", "with", "no", "news", "pending", "review", "order", "delivered", "to",
        "confirm", "for", "the", "customer", "new", "appointment", "ready", "to", "invoice", "noted"];

    /// <summary>Lo que conviene poner en una columna, por su tipo y su nombre.</summary>
    public static TestValueKind Suggest(DbColumn column, bool isForeignKey)
    {
        if (column.IsComputed) return TestValueKind.Skip;
        var type = column.Type.ToUpperInvariant();
        var name = Plain(column.Name);
        if (isForeignKey) return TestValueKind.Reference;
        if (column.IsPrimaryKey && IsInteger(type)) return TestValueKind.Sequence;
        if (type == "BOOLEAN") return TestValueKind.Boolean;
        if (type == "DATE") return TestValueKind.Date;
        if (type.StartsWith("TIMESTAMP", StringComparison.Ordinal)) return TestValueKind.Timestamp;
        if (type.StartsWith("TIME", StringComparison.Ordinal)) return TestValueKind.Time;
        if (IsInteger(type)) return name.Contains("DNI") || name.Contains("DOCUMENTO") ? TestValueKind.Document : TestValueKind.Integer;
        if (IsDecimal(type)) return TestValueKind.Decimal;
        if (!IsText(type)) return TestValueKind.Skip;
        if (Length(type) is { } n && n <= 2) return TestValueKind.Code;
        bool Has(params string[] parts) => parts.Any(name.Contains);
        if (Has("EMAIL", "MAIL")) return TestValueKind.Email;
        if (Has("TELEFONO", "CELULAR", "MOVIL", "PHONE", "TEL")) return TestValueKind.Phone;
        if (Has("DNI", "DOCUMENTO", "CUIT", "CUIL")) return TestValueKind.Document;
        if (Has("NOMBRECOMPLETO", "APELLIDOYNOMBRE", "APELLIDONOMBRE", "RAZONSOCIAL", "FULLNAME", "PACIENTE", "CLIENTE", "PERSONA", "CONTACTO", "CUSTOMER")) return TestValueKind.FullName;
        if (Has("APELLIDO", "LASTNAME", "SURNAME")) return TestValueKind.LastName;
        if (Has("NOMBRE", "NAME")) return TestValueKind.FirstName;
        if (Has("DIRECCION", "DOMICILIO", "CALLE", "ADDRESS")) return TestValueKind.Address;
        if (Has("CIUDAD", "LOCALIDAD", "PROVINCIA", "CITY")) return TestValueKind.City;
        if (Has("CODIGO", "CODE") || name.StartsWith("COD", StringComparison.Ordinal)) return TestValueKind.Code;
        return TestValueKind.Text;
    }

    /// <summary>Las filas, una por vez (no se arman todas en memoria): los valores en el orden de <paramref name="columns"/>.</summary>
    public static IEnumerable<object?[]> Rows(IReadOnlyList<TestColumn> columns, int count, TestDataOptions options)
    {
        var random = new Random(options.Seed);
        var today = DateTime.Today;
        for (var row = 0; row < count; row++)
        {
            var values = new object?[columns.Count];
            var first = Pick(random, options.Spanish ? FirstNamesEs : FirstNamesEn);
            var last = Pick(random, options.Spanish ? LastNamesEs : LastNamesEn);
            for (var i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var nullable = !c.NotNull && c.Kind is not (TestValueKind.Skip or TestValueKind.Sequence);
                if (nullable && random.Next(100) < options.NullPercent)
                {
                    values[i] = null;
                    continue;
                }
                values[i] = Value(c, row, random, options, today, first, last);
            }
            yield return values;
        }
    }

    private static object? Value(TestColumn c, int row, Random random, TestDataOptions o, DateTime today, string first, string last)
    {
        var type = c.Type.ToUpperInvariant();
        object? value = c.Kind switch
        {
            TestValueKind.Skip => null,
            TestValueKind.Sequence => o.FirstSequence + row,
            TestValueKind.Reference => o.References?.GetValueOrDefault(c.Name) is { Count: > 0 } refs ? refs[random.Next(refs.Count)] : null,
            TestValueKind.FirstName => first,
            TestValueKind.LastName => last,
            TestValueKind.FullName => o.Spanish ? $"{last}, {first}" : $"{first} {last}",
            TestValueKind.Email => $"{Ascii(first)}.{Ascii(last)}{random.Next(1, 999)}@example.com",
            TestValueKind.Phone => $"{random.Next(11, 3900)}-{random.Next(1000, 9999)}-{random.Next(1000, 9999)}",
            TestValueKind.Document => IsInteger(type) ? random.Next(20_000_000, 45_000_000) : random.Next(20_000_000, 45_000_000).ToString(CultureInfo.InvariantCulture),
            TestValueKind.Address => $"{Pick(random, o.Spanish ? StreetsEs : StreetsEn)} {random.Next(1, 3000)}",
            TestValueKind.City => Pick(random, o.Spanish ? CitiesEs : CitiesEn),
            TestValueKind.Text => Sentence(random, o.Spanish ? WordsEs : WordsEn),
            TestValueKind.Code => Code(random, Length(type) ?? 6),
            TestValueKind.Integer => type switch
            {
                "SMALLINT" => (object)(short)random.Next(0, 1000),
                "BIGINT" => (long)random.Next(1, 1_000_000),
                _ => random.Next(1, 100_000),
            },
            TestValueKind.Decimal => DecimalValue(random, type),
            TestValueKind.Date => today.AddDays(-random.Next(0, 365)),
            TestValueKind.Timestamp => today.AddDays(-random.Next(0, 365)).AddMinutes(random.Next(7 * 60, 21 * 60)),
            TestValueKind.Time => TimeSpan.FromMinutes(random.Next(7 * 60, 21 * 60)),
            TestValueKind.Boolean => random.Next(2) == 1,
            _ => null,
        };
        // Un texto no pasa del largo de la columna.
        return value is string s && Length(type) is { } max && s.Length > max ? s[..max] : value;
    }

    private static object DecimalValue(Random random, string type)
    {
        var m = Precision().Match(type);
        var scale = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 2;
        var precision = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 18;
        var maxInt = (decimal)Math.Min(Math.Pow(10, Math.Max(1, precision - scale)) - 1, 100_000);
        var value = Math.Round((decimal)random.NextDouble() * maxInt, Math.Min(scale, 4));
        return type.StartsWith("DOUBLE", StringComparison.Ordinal) || type is "FLOAT" or "REAL" ? (object)(double)value : value;
    }

    private static string Pick(Random random, string[] list) => list[random.Next(list.Length)];

    private static string Sentence(Random random, string[] words)
    {
        var sb = new StringBuilder();
        var n = random.Next(3, 9);
        for (var i = 0; i < n; i++) sb.Append(i == 0 ? "" : " ").Append(words[random.Next(words.Length)]);
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    private static string Code(Random random, int length)
    {
        const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
        var len = Math.Clamp(length, 1, 8);
        return new string(Enumerable.Range(0, len).Select(i => len <= 2 ? Chars[random.Next(24)] : Chars[random.Next(Chars.Length)]).ToArray());
    }

    /// <summary>«José Pérez» → «jose», «perez»: sin acentos ni espacios, para un mail.</summary>
    private static string Ascii(string text) =>
        new string(text.Normalize(NormalizationForm.FormD).Where(ch => char.IsAsciiLetter(ch)).ToArray()).ToLowerInvariant();

    private static string Plain(string name) => new string(name.ToUpperInvariant().Normalize(NormalizationForm.FormD).Where(char.IsAsciiLetterOrDigit).ToArray());

    private static bool IsInteger(string type) => type is "SMALLINT" or "INTEGER" or "BIGINT" or "INT128";

    private static bool IsDecimal(string type) =>
        type.StartsWith("NUMERIC", StringComparison.Ordinal) || type.StartsWith("DECIMAL", StringComparison.Ordinal)
        || type.StartsWith("DOUBLE", StringComparison.Ordinal) || type is "FLOAT" or "REAL" || type.StartsWith("DECFLOAT", StringComparison.Ordinal);

    private static bool IsText(string type) =>
        type.StartsWith("VARCHAR", StringComparison.Ordinal) || type.StartsWith("CHAR", StringComparison.Ordinal) || type.StartsWith("BLOB SUB_TYPE TEXT", StringComparison.Ordinal)
        || type.StartsWith("BLOB SUB_TYPE 1", StringComparison.Ordinal);

    private static int? Length(string type) => Size().Match(type) is { Success: true } m && IsText(type) ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex Size();

    [GeneratedRegex(@"\((\d+)(?:\s*,\s*(\d+))?\)")]
    private static partial Regex Precision();
}
