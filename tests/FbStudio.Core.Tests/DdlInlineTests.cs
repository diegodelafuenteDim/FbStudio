using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class DdlInlineTests
{
    [Fact]
    public void Un_comentario_de_linea_guardado_en_un_default_pasa_a_comentario_de_bloque()
    {
        // Así quedó en una base de prueba: «DEFAULT 0 -- Contador de fallos consecutivos». Escrito tal cual, la coma de la
        // columna siguiente quedaba comentada y el CREATE TABLE no compilaba.
        var column = DdlWriter.Column(new FieldDefinition("INTENTOS_FALLIDOS", "SMALLINT", Default: "DEFAULT 0             -- Contador de fallos consecutivos"));
        Assert.Equal("INTENTOS_FALLIDOS SMALLINT DEFAULT 0             /* Contador de fallos consecutivos */", column);
        Assert.Equal("'a -- b' /* c */", DdlWriter.InlineSafe("'a -- b' -- c"));               // dentro de comillas no es comentario
        Assert.Equal("1 /* x * / y */\n+ 2", DdlWriter.InlineSafe("1 -- x */ y\n+ 2"));
        Assert.Equal("/* ya */ 1", DdlWriter.InlineSafe("/* ya */ 1"));
        Assert.Equal("CHECK (A > 0) /* positivo */", DdlWriter.InlineSafe("CHECK (A > 0) -- positivo"));
    }

    [Fact]
    public void Los_permisos_huerfanos_se_cuentan_y_no_se_escriben()
    {
        var script = SchemaScriptWriter.Write(new DatabaseSchema { OrphanPrivileges = 5 }, new SchemaScriptOptions());
        Assert.Contains("-- 5 permiso(s) dados a procedimientos, triggers, vistas o funciones que ya no existen", script);
    }
}
