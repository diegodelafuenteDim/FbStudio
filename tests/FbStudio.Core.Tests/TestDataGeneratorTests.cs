using FbStudio.Core.Import;
using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

/// <summary>Datos de prueba inventados para una tabla.</summary>
public class TestDataGeneratorTests
{
    private static DbColumn C(string name, string type, bool notNull = false, bool pk = false, bool computed = false) =>
        new(name, type, notNull, pk, null, null, computed);

    [Fact]
    public void Elige_por_el_tipo_y_el_nombre_de_cada_columna()
    {
        Assert.Equal(TestValueKind.Sequence, TestDataGenerator.Suggest(C("ID", "INTEGER", true, pk: true), false));
        Assert.Equal(TestValueKind.Reference, TestDataGenerator.Suggest(C("ID_CLIENTE", "INTEGER"), isForeignKey: true));
        Assert.Equal(TestValueKind.Skip, TestDataGenerator.Suggest(C("TOTAL", "NUMERIC(12,2)", computed: true), false));
        Assert.Equal(TestValueKind.FirstName, TestDataGenerator.Suggest(C("NOMBRE", "VARCHAR(40)"), false));
        Assert.Equal(TestValueKind.LastName, TestDataGenerator.Suggest(C("APELLIDO", "VARCHAR(40)"), false));
        Assert.Equal(TestValueKind.FullName, TestDataGenerator.Suggest(C("RAZON_SOCIAL", "VARCHAR(80)"), false));
        Assert.Equal(TestValueKind.Email, TestDataGenerator.Suggest(C("E_MAIL", "VARCHAR(80)"), false));
        Assert.Equal(TestValueKind.Phone, TestDataGenerator.Suggest(C("TELEFONO", "VARCHAR(30)"), false));
        Assert.Equal(TestValueKind.Document, TestDataGenerator.Suggest(C("DNI", "INTEGER"), false));
        Assert.Equal(TestValueKind.Address, TestDataGenerator.Suggest(C("DOMICILIO", "VARCHAR(80)"), false));
        Assert.Equal(TestValueKind.City, TestDataGenerator.Suggest(C("LOCALIDAD", "VARCHAR(40)"), false));
        Assert.Equal(TestValueKind.Code, TestDataGenerator.Suggest(C("ESTADO", "CHAR(1)"), false));
        Assert.Equal(TestValueKind.Text, TestDataGenerator.Suggest(C("OBSERVACIONES", "VARCHAR(200)"), false));
        Assert.Equal(TestValueKind.Decimal, TestDataGenerator.Suggest(C("IMPORTE", "NUMERIC(12,2)"), false));
        Assert.Equal(TestValueKind.Date, TestDataGenerator.Suggest(C("FECHA", "DATE"), false));
        Assert.Equal(TestValueKind.Timestamp, TestDataGenerator.Suggest(C("ALTA", "TIMESTAMP"), false));
        Assert.Equal(TestValueKind.Boolean, TestDataGenerator.Suggest(C("ACTIVO", "BOOLEAN"), false));
    }

    [Fact]
    public void Con_la_misma_semilla_salen_los_mismos_datos_que_respetan_tipos_y_largos()
    {
        TestColumn[] columns =
        [
            new("ID", "INTEGER", true, TestValueKind.Sequence), new("NOMBRE", "VARCHAR(5)", true, TestValueKind.FullName),
            new("ID_CLIENTE", "INTEGER", true, TestValueKind.Reference), new("IMPORTE", "NUMERIC(6,2)", false, TestValueKind.Decimal),
            new("MAIL", "VARCHAR(80)", false, TestValueKind.Email), new("ESTADO", "CHAR(1)", true, TestValueKind.Code),
        ];
        var options = new TestDataOptions(Seed: 7, NullPercent: 0, FirstSequence: 101,
            References: new Dictionary<string, IReadOnlyList<object>> { ["ID_CLIENTE"] = [5, 9] });
        var rows = TestDataGenerator.Rows(columns, 50, options).ToList();
        Assert.Equal(rows.Select(r => string.Join("|", r)), TestDataGenerator.Rows(columns, 50, options).Select(r => string.Join("|", r)));
        Assert.Equal(Enumerable.Range(101, 50).Select(i => (long)i), rows.Select(r => (long)r[0]!));
        Assert.All(rows, r => Assert.True(((string)r[1]!).Length <= 5));                 // no pasa del largo de la columna
        Assert.All(rows, r => Assert.Contains(r[2], new object[] { 5, 9 }));             // solo valores que existen
        Assert.All(rows, r => Assert.InRange((decimal)r[3]!, 0m, 9999.99m));             // entra en NUMERIC(6,2)
        Assert.All(rows, r => Assert.EndsWith("@example.com", (string)r[4]!));
        Assert.All(rows, r => Assert.Single((string)r[5]!));
        Assert.All(rows, r => Assert.DoesNotContain(null, r));                         // 0 % vacías

        // Con 50 % de vacías, las opcionales a veces van NULL; las obligatorias y la clave nunca.
        var half = TestDataGenerator.Rows(columns, 200, options with { NullPercent = 50 }).ToList();
        Assert.Contains(half, r => r[3] is null);
        Assert.All(half, r => Assert.NotNull(r[0]));
        Assert.All(half, r => Assert.NotNull(r[1]));
    }
}
