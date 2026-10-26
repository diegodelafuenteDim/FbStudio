using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Una fila «en vertical».</summary>
public class RowRecordTests
{
    [Fact]
    public void Un_renglon_por_columna_con_las_fechas_segun_su_tipo()
    {
        ColumnInfo[] columns = [new(0, "ID", "INTEGER"), new(1, "DIA", "DATE"), new(2, "HORA", "TIMESTAMP"), new(3, "OBS", "VARCHAR"), new(4, "FOTO", "BLOB")];
        var photo = new byte[] { 1, 2 };
        var rows = RowRecord.Build(columns, [7, new DateTime(2026, 10, 24), new DateTime(2026, 10, 24, 9, 5, 0), null, photo]);
        Assert.Equal(5, rows.Count);
        Assert.Equal(["ID", "INTEGER", 7], rows[0]);
        Assert.Equal(["DIA", "DATE", "2026-10-24"], rows[1]);
        Assert.Equal("2026-10-24 09:05:00", rows[2][2]);
        Assert.Equal(DBNull.Value, rows[3][2]);
        Assert.Same(photo, rows[4][2]);
    }
}
