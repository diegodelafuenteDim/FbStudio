using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Comentar, duplicar, mover renglones y mayúsculas en el editor.</summary>
public class LineEditsTests
{
    /// <summary>El texto después de aplicar el cambio, con la selección marcada con [ ] (o el cursor con |).</summary>
    private static string Apply(string text, TextEdit? edit)
    {
        if (edit is not { } e) return "sin cambios";
        var result = text.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
        return e.SelectionLength == 0
            ? result.Insert(e.SelectionStart, "|")
            : result.Insert(e.SelectionStart + e.SelectionLength, "]").Insert(e.SelectionStart, "[");
    }

    /// <summary>Corre el comando sobre un texto con la selección marcada con [ ] (o el cursor con |).</summary>
    private static string Run(string marked, Func<string, int, int, TextEdit?> command)
    {
        int start, length;
        string text;
        if (marked.Contains('|'))
        {
            start = marked.IndexOf('|');
            length = 0;
            text = marked.Remove(start, 1);
        }
        else
        {
            start = marked.IndexOf('[');
            length = marked.IndexOf(']') - start - 1;
            text = marked.Replace("[", "").Replace("]", "");
        }
        return Apply(text, command(text, start, length));
    }

    [Fact]
    public void Comentar_y_descomentar_renglones()
    {
        // Sin selección, el renglón del cursor; el cursor queda en su lugar del texto.
        Assert.Equal("SELECT 1\n-- FROM T|X", Run("SELECT 1\nFROM T|X", LineEdits.ToggleComment));
        Assert.Equal("SELECT 1\nFROM T|X", Run("SELECT 1\n-- FROM T|X", LineEdits.ToggleComment));
        // Varios renglones, a la altura del menos sangrado, sin tocar los que están en blanco.
        Assert.Equal("[  -- WHERE A = 1\n\n  --   AND B = 2]\nORDER BY 1", Run("[  WHERE A = 1\n\n    AND B = 2]\nORDER BY 1", LineEdits.ToggleComment));
        Assert.Equal("[  WHERE A = 1\n\n    AND B = 2]\nORDER BY 1", Run("[  -- WHERE A = 1\n\n  --   AND B = 2]\nORDER BY 1", LineEdits.ToggleComment));
        // Si alguno no está comentado, se comentan todos; «--» sin espacio también se saca.
        Assert.Equal("[-- -- A\n-- B]", Run("[-- A\nB]", LineEdits.ToggleComment));
        Assert.Equal("[A\nB]", Run("[--A\n-- B]", LineEdits.ToggleComment));
        // Renglones enteros elegidos (hasta el comienzo del siguiente): el siguiente no cuenta. Con \r\n.
        Assert.Equal("[-- A\r\n-- B\r\n]C", Run("[A\r\nB\r\n]C", LineEdits.ToggleComment));
        // Un renglón vacío recibe el «-- » para empezar a escribir el comentario.
        Assert.Equal("A\n-- |", Run("A\n|", LineEdits.ToggleComment));
    }

    [Fact]
    public void Duplicar_mover_y_cambiar_mayusculas()
    {
        Assert.Equal("A\nSELECT 1\nSELECT |1\nB", Run("A\nSELECT |1\nB", LineEdits.DuplicateLine));
        Assert.Equal("SELECT 1, 1[, 1]", Run("SELECT 1[, 1]", LineEdits.DuplicateLine));
        Assert.Equal("A\r\nA|\r\nB", Run("A|\r\nB", LineEdits.DuplicateLine));

        Assert.Equal("B|\nA\nC", Run("A\nB|\nC", (t, s, l) => LineEdits.MoveLines(t, s, l, up: true)));
        Assert.Equal("A\nC\nB|", Run("A\nB|\nC", (t, s, l) => LineEdits.MoveLines(t, s, l, up: false)));
        Assert.Equal("[B\r\nC]\r\nA", Run("A\r\n[B\r\nC]", (t, s, l) => LineEdits.MoveLines(t, s, l, up: true)));
        Assert.Equal("sin cambios", Run("A|\nB", (t, s, l) => LineEdits.MoveLines(t, s, l, up: true)));
        Assert.Equal("sin cambios", Run("A\nB|", (t, s, l) => LineEdits.MoveLines(t, s, l, up: false)));

        Assert.Equal("SELECT [NOMBRE_COMPLETO] FROM t", Run("SELECT [nombre_completo] FROM t", (t, s, l) => LineEdits.ChangeCase(t, s, l, upper: true)));
        Assert.Equal("select MAESTRO|_TURNOS", Run("select maestro|_turnos", (t, s, l) => LineEdits.ChangeCase(t, s, l, upper: true)));
        Assert.Equal("select maestro|_turnos", Run("select MAESTRO|_TURNOS", (t, s, l) => LineEdits.ChangeCase(t, s, l, upper: false)));
        Assert.Equal("sin cambios", Run("SELECT |", (t, s, l) => LineEdits.ChangeCase(t, s, l, upper: true)));
    }
}
