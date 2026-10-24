using FbStudio.Core.Monitoring;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

public class MonitorColumnsTests
{
    private static ColumnInfo[] Cols(params string[] names) => names.Select((n, i) => new ColumnInfo(i, n, "INTEGER")).ToArray();

    [Fact]
    public void Reconoce_las_columnas_de_las_consultas_importadas_aunque_cambie_el_alias()
    {
        var custom = new MonitorPreset("c", "Mis conexiones", "SELECT ...") { CustomId = Guid.NewGuid() };
        // «Mis conexiones»: "Attachment ID" con espacio; «Connections by IP»: ATTACHMENT_ID; MON$ sin alias.
        Assert.Equal(0, MonitorColumns.Resolve(custom, Cols("Attachment ID", "Server PID", "State")).Attachment);
        Assert.Equal(1, MonitorColumns.Resolve(custom, Cols("STARTED_AT", "ATTACHMENT_ID")).Attachment);
        var full = MonitorColumns.Resolve(custom, Cols("MON$STATEMENT_ID", "MON$ATTACHMENT_ID", "MON$SQL_TEXT"));
        Assert.Equal((1, 0, 2), (full.Attachment, full.Statement, full.Sql));
        Assert.Equal(MonitorColumnMap.None, MonitorColumns.Resolve(custom, Cols("A", "B")));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void El_SQL_de_las_predefinidas_solo_usa_caracteres_que_viajan_en_ISO8859_1(string language)
    {
        // Con una conexión ISO8859_1 (o NONE), lo que no entra vuelve como «?»: así le apareció «? es la OAT» a Diego.
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            foreach (var preset in MonitorQueries.Presets)
                Assert.Empty(preset.Sql.Where(c => c > 'ÿ')); // lo que no entra en ISO8859_1
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData(@"C:\Program Files\Firebird\Firebird_3_0\gbak.exe", false, true)]
    [InlineData("/opt/firebird/bin/gfix", false, true)]
    [InlineData("NBACKUP.EXE", false, true)]
    [InlineData(null, true, true)]                                            // proceso interno del servidor (sweep)
    [InlineData(@"C:\Apps\Agenda\AgendaApp.exe", false, false)]
    [InlineData(null, false, false)]
    public void Reconoce_backups_y_sweeps(string? process, bool system, bool expected) =>
        Assert.Equal(expected, MonitorQueries.IsMaintenance(process, system));

    [Fact]
    public void La_columna_elegida_manda_y_las_predefinidas_usan_su_posicion()
    {
        var chosen = new MonitorPreset("c", "x", "SELECT ...") { AttachmentColumn = "Conexión del cliente", CustomId = Guid.NewGuid() };
        Assert.Equal(2, MonitorColumns.Resolve(chosen, Cols("ATTACHMENT_ID", "B", "CONEXION DEL CLIENTE")).Attachment);
        var builtIn = MonitorQueries.Presets.Single(p => p.Key == "statements");
        var map = MonitorColumns.Resolve(builtIn, Cols("ID", "Conexión", "Tx", "Usuario", "IP", "Equipo", "Desde", "Segundos", "Estado", "a", "b", "SQL"));
        Assert.Equal((1, 0, 11), (map.Attachment, map.Statement, map.Sql));
    }
}
