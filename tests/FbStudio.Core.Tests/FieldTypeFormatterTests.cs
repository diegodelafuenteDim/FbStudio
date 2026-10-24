using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class FieldTypeFormatterTests
{
    [Theory]
    [InlineData(8, null, 4, null, 0, 0, "INTEGER")]
    [InlineData(7, null, 2, null, 0, 0, "SMALLINT")]
    [InlineData(16, null, 8, null, 0, 0, "BIGINT")]
    [InlineData(8, 1, 4, null, 9, -2, "NUMERIC(9,2)")]
    [InlineData(16, 2, 8, null, 18, -4, "DECIMAL(18,4)")]
    [InlineData(16, null, 8, null, 0, -2, "NUMERIC(18,2)")]   // sin precisión guardada (bases viejas)
    [InlineData(27, null, 8, null, null, 0, "DOUBLE PRECISION")]
    [InlineData(35, null, 8, null, null, 0, "TIMESTAMP")]
    [InlineData(12, null, 4, null, null, 0, "DATE")]
    [InlineData(37, null, 2000, 2000, null, 0, "VARCHAR(2000)")]
    [InlineData(37, null, 400, 100, null, 0, "VARCHAR(100)")]   // UTF8: 4 bytes por carácter
    [InlineData(14, null, 1, 1, null, 0, "CHAR(1)")]
    [InlineData(261, 1, 8, null, null, 0, "BLOB SUB_TYPE TEXT")]
    [InlineData(261, 0, 8, null, null, 0, "BLOB SUB_TYPE BINARY")]
    [InlineData(23, null, 1, null, null, 0, "BOOLEAN")]
    public void Traduce_los_codigos_de_RDB_FIELDS(int type, int? sub, int? len, int? charLen, int? prec, int? scale, string expected) =>
        Assert.Equal(expected, FieldTypeFormatter.Format(type, sub, len, charLen, prec, scale));
}
