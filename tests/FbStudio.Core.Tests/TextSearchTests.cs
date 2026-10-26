using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Buscar y reemplazar en el editor.</summary>
public class TextSearchTests
{
    private const string Sql = "SELECT T.ID, T_DET.ID FROM TURNOS T JOIN TURNOS_DET T_DET ON T_DET.TURNO = T.ID WHERE turnos_det.NOTA = 'turnos'";

    [Fact]
    public void Sin_distinguir_mayusculas_con_palabra_entera_y_expresiones_regulares()
    {
        Assert.Equal(4, TextSearch.FindAll(Sql, "turnos", new(), out _).Count);                     // TURNOS, TURNOS(_DET) ×2, 'turnos'
        Assert.Equal(2, TextSearch.FindAll(Sql, "turnos", new(MatchCase: true), out _).Count);       // turnos_det y 'turnos'
        Assert.Equal(2, TextSearch.FindAll(Sql, "TURNOS", new(WholeWord: true), out _).Count);       // TURNOS y 'turnos', no TURNOS_DET
        Assert.Equal(3, TextSearch.FindAll(Sql, "T", new(WholeWord: true, MatchCase: true), out _).Count);      // el alias T, no T_DET
        Assert.Equal(["T_DET.ID", "T_DET.TURNO"], TextSearch.FindAll(Sql, @"T_DET\.\w+", new(Regex: true, MatchCase: true), out _)
            .Select(m => Sql.Substring(m.Start, m.Length)));
        Assert.Empty(TextSearch.FindAll(Sql, "([", new(Regex: true), out var error));
        Assert.NotNull(error);
        Assert.Empty(TextSearch.FindAll(Sql, "", new(), out _));
        Assert.Empty(TextSearch.FindAll("aaa", "x*", new(Regex: true), out _));                     // sin coincidencias vacías
    }

    [Fact]
    public void Siguiente_y_anterior_dan_la_vuelta_y_el_reemplazo_usa_los_grupos()
    {
        var matches = TextSearch.FindAll("ab ab ab", "ab", new(), out _);
        Assert.Equal(3, TextSearch.Next(matches, 0)!.Value.Start);
        Assert.Equal(0, TextSearch.Next(matches, 6)!.Value.Start);                                   // vuelve al principio
        Assert.Equal(6, TextSearch.Previous(matches, 0)!.Value.Start);                               // y al final
        var text = "WHERE A.ID = 1 AND B.ID = 2";
        var groups = TextSearch.FindAll(text, @"(\w)\.ID", new(Regex: true), out _);
        Assert.Equal("ID_A", TextSearch.Replacement(text, groups[0], @"(\w)\.ID", "ID_$1", new(Regex: true)));
        Assert.Equal("ID_$1", TextSearch.Replacement(text, groups[0], "A.ID", "ID_$1", new()));     // sin expresión regular, tal cual
    }
}
