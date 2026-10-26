namespace FbStudio.Core.Tests;

/// <summary>El registro de cambios (CHANGELOG.md, D-81).</summary>
public class ChangeLogTests
{
    private const string Sample = """
        # Changelog · Registro de cambios

        Intro en los dos idiomas: no es una versión.

        ## [Unreleased]

        ### English

        - Hide columns from the **header** menu; the Data tab remembers them
          for each table.
        - Trace to Excel.

        ### Español

        - Ocultar columnas desde el menú del `encabezado`.

        ## [0.1.0] - 2026-10-24

        ### English

        - First public version.

        ### Notas

        - Esto no es de ningún idioma.

        ### Español

        - Primera versión pública.
        """;

    [Fact]
    public void Lee_cada_version_con_sus_cambios_en_los_dos_idiomas()
    {
        var versions = ChangeLog.Parse(Sample);
        Assert.Equal(["Unreleased", "0.1.0"], versions.Select(v => v.Version));
        var next = versions[0];
        Assert.True(next.IsUnreleased);
        Assert.Null(next.Date);
        Assert.Equal(["Hide columns from the header menu; the Data tab remembers them for each table.", "Trace to Excel."], next.English);
        Assert.Equal(["Ocultar columnas desde el menú del encabezado."], next.Spanish);   // sin las marcas de Markdown

        var first = versions[1];
        Assert.Equal(("2026-10-24", false), (first.Date, first.IsUnreleased));
        Assert.Equal(["First public version."], first.English);                          // «Notas» no es un idioma
        Assert.Equal(["Primera versión pública."], first.In("es"));
        Assert.Equal(["First public version."], first.In("en"));
    }

    [Fact]
    public void Si_falta_un_idioma_muestra_el_otro()
    {
        var only = ChangeLog.Parse("## [0.2.0] - 2026-11-01\n\n### English\n\n- Only in English.\n").Single();
        Assert.Equal(["Only in English."], only.In("es"));
        Assert.Empty(ChangeLog.Parse("sin versiones"));
    }

    [Fact]
    public void Cada_cambio_con_su_titulo_para_la_ventana_de_novedades()
    {
        Assert.Equal(new ChangeLogEntry("Pestaña Datos", "«Contar al filtrar» cuenta las filas.", false), ChangeLog.Split("Pestaña Datos: «Contar al filtrar» cuenta las filas."));
        // El «:» de adentro de un nombre entre «…» no corta; el del final del nombre sí.
        Assert.Equal("Consulta → «Ejecutar script: desde archivo…»", ChangeLog.Split("Consulta → «Ejecutar script: desde archivo…»: corre un .sql.").Title);
        // Sin «:» cerca del principio: el nombre entre «…» con que empieza, o nada.
        Assert.Equal(new ChangeLogEntry("«Ver el valor…»", "En la grilla muestra la celda.", false), ChangeLog.Split("«Ver el valor…» en la grilla muestra la celda."));
        Assert.Equal(new ChangeLogEntry(null, "Importar puede actualizar las filas.", false), ChangeLog.Split("Importar puede actualizar las filas."));
        var long_ = ChangeLog.Split("Después de JOIN, el autocompletado sugiere primero las tablas relacionadas por una FK con las que ya están en el FROM, con el ON armado: «T t ON …».");
        Assert.Null(long_.Title);
        // Las correcciones.
        Assert.Equal(new ChangeLogEntry(null, "Las opciones nuevas llegaban apagadas.", true), ChangeLog.Split("Corregido: las opciones nuevas llegaban apagadas."));
        Assert.True(ChangeLog.Split("Fixed: new options came in turned off.").IsFix);
        Assert.Equal("Cambio con negrita", ChangeLog.Split("Cambio con **negrita**").Body);
    }
}
