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
}
