using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SqlTemplatesNavigationTests
{
    [Fact]
    public void La_plantilla_lleva_la_sangria_del_renglon_y_deja_el_cursor_donde_dice()
    {
        var template = SqlTemplates.Find(SqlTemplates.Defaults, "SELW")!;
        var (text, caret) = SqlTemplates.Expand(template, "    ", "\r\n");
        Assert.Equal("SELECT *\r\n    FROM \r\n    WHERE ", text);
        Assert.Equal("SELECT *\r\n    FROM ".Length, caret);
        Assert.Equal(("sel", 6), SqlTemplates.WordBefore("  x = sel", 9));
        Assert.Equal(("", 3), SqlTemplates.WordBefore("a (", 3));
        Assert.Null(SqlTemplates.Find(SqlTemplates.Defaults, "nada"));
        Assert.Equal(SqlTemplates.Defaults.Count, SqlTemplates.Defaults.Select(t => t.Abbreviation.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public void El_nombre_bajo_el_cursor_con_su_alias()
    {
        const string Sql = "SELECT t.id, \"Mixta\".x FROM turnos t JOIN \"Pacientes Viejos\" AS p ON p.id = t.paciente WHERE 'turnos' = t.estado";
        Assert.Equal(new SqlName("ID", "T", 9, 2), SqlNavigation.NameAt(Sql, 10));
        Assert.Equal(new SqlName("ID", "T", 9, 2), SqlNavigation.NameAt(Sql, 11));          // justo al final del nombre
        Assert.Equal("Mixta", SqlNavigation.NameAt(Sql, Sql.IndexOf("Mixta", StringComparison.Ordinal))!.Name);
        Assert.Equal("TURNOS", SqlNavigation.NameAt(Sql, Sql.IndexOf("turnos", StringComparison.Ordinal) + 2)!.Name);
        Assert.Null(SqlNavigation.NameAt(Sql, Sql.IndexOf("'turnos'", StringComparison.Ordinal) + 3));   // dentro de un texto
        Assert.Equal("TURNOS", SqlNavigation.TableOfAlias(Sql, 10, "T"));
        Assert.Equal("Pacientes Viejos", SqlNavigation.TableOfAlias(Sql, 10, "P"));
        Assert.Null(SqlNavigation.TableOfAlias(Sql, 10, "ESTADO"));
        Assert.Null(SqlNavigation.TableOfAlias(Sql, 10, "TURNOS"));                         // la tabla no es un alias: se busca por nombre
    }
}
