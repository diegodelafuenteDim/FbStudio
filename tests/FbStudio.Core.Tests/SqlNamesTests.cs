using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SqlNamesTests
{
    [Fact]
    public void Lee_la_lista_como_la_guarda_Firebird()
    {
        Assert.Equal(["AGENDA_TURNOS", "PACIENTES", "Mixta Rara", "X\"Y", "LOG"],
            SqlNames.Parse("agenda_turnos, PACIENTES;\n\"Mixta Rara\"  \"X\"\"Y\" log, Log"));
        Assert.Empty(SqlNames.Parse("  , ;  "));
        Assert.Empty(SqlNames.Parse(null));
    }

    [Fact]
    public void El_patron_reconoce_solo_esos_nombres()
    {
        Assert.Equal("AGENDA[_]TURNOS|A$B|[%]X|Mixta[ ]Rara", SqlNames.SimilarTo(["AGENDA_TURNOS", "A$B", "%X", "Mixta Rara"]));
        Assert.Equal("", SqlNames.SimilarTo([]));
    }
}
