using FbStudio.Core.Diagrams;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Después de JOIN, las tablas relacionadas por FK con lo que está en el FROM, con el ON armado.</summary>
public class SqlCompletionJoinTests
{
    // TURNOS_DET → TURNOS por FOREIGN KEY; TURNOS → PACIENTES por un comentario #FK_; PACIENTES → PACIENTES (titular).
    private static readonly MetadataCatalog Catalog = new(
        [("TURNOS", "ID"), ("TURNOS", "PACIENTE"), ("TURNOS_DET", "ID"), ("TURNOS_DET", "TURNO"), ("PACIENTES", "CODIGO"), ("PACIENTES", "TITULAR"),
         ("INSUMOS_NOMENC", "ID"), ("INSUMOS_NOMENC", "TURNO")])
    {
        ForeignKeys =
        [
            new DiagramLink("FK_DET_TURNO", "TURNOS_DET", ["TURNO"], "TURNOS", ["ID"]),
            new DiagramLink("FK_PAC_TITULAR", "PACIENTES", ["TITULAR"], "PACIENTES", ["CODIGO"]),
            new DiagramLink("FK_INS_TURNO", "INSUMOS_NOMENC", ["TURNO"], "TURNOS", ["ID"]),
            LogicalForeignKeys.Parse("TURNOS", "PACIENTE", "#FK_%PACIENTES,%CODIGO,%NOMBRE,I#")!,
            new DiagramLink("FK_A_OTRA_BASE", "TURNOS", ["X"], "NO_EXISTE", ["ID"]),
        ],
    };

    private static List<CompletionItem> Joins(string sqlWithCaret)
    {
        var caret = sqlWithCaret.IndexOf('|');
        return SqlCompletion.Suggest(sqlWithCaret.Remove(caret, 1), caret, Catalog).Items.Where(i => i.Kind == CompletionKind.Join).ToList();
    }

    [Fact]
    public void Primero_a_las_que_apunta_y_despues_las_que_la_apuntan()
    {
        var joins = Joins("SELECT * FROM TURNOS t JOIN |");
        Assert.Equal(
        [
            "PACIENTES p ON p.CODIGO = t.PACIENTE",
            "TURNOS_DET td ON td.TURNO = t.ID",
            "INSUMOS_NOMENC in2 ON in2.TURNO = t.ID",                                       // «in» es reservada
        ], joins.Select(j => j.Text));
        Assert.Equal("#FK TURNOS.PACIENTE", joins[0].Detail);                               // por qué relación
        Assert.Equal("FK_DET_TURNO", joins[1].Detail);

        // Las sugerencias del JOIN van antes que las tablas, y se filtran por lo que se escribió.
        var caret = "SELECT * FROM TURNOS_DET LEFT JOIN TU".Length;
        var items = SqlCompletion.Suggest("SELECT * FROM TURNOS_DET LEFT JOIN TU", caret, Catalog).Items;
        Assert.Equal(new CompletionItem("TURNOS t ON t.ID = TURNOS_DET.TURNO", CompletionKind.Join, "FK_DET_TURNO"), items[0]);
        Assert.Equal("TURNOS", items[1].Text);
        Assert.DoesNotContain(items, i => i.Text.StartsWith("PACIENTES", StringComparison.Ordinal));
    }

    [Fact]
    public void Con_varias_tablas_primero_la_ultima_y_las_que_ya_estan_al_final()
    {
        var joins = Joins("SELECT * FROM TURNOS_DET td\nJOIN TURNOS t ON t.ID = td.TURNO\nLEFT JOIN |\nWHERE td.ID = 1").Select(j => j.Text).ToList();
        Assert.Equal("PACIENTES p ON p.CODIGO = t.PACIENTE", joins[0]);
        Assert.Equal("INSUMOS_NOMENC in2 ON in2.TURNO = t.ID", joins[1]);
        // Las que ya están en el FROM (otra vez, con otro alias) quedan al final.
        Assert.Equal(["TURNOS_DET td2 ON td2.TURNO = t.ID", "TURNOS t2 ON t2.ID = td.TURNO"], joins[^2..]);

        // Una tabla consigo misma, con un alias que no choca; la que apunta a una tabla que no está, no.
        Assert.Equal(["PACIENTES p2 ON p2.CODIGO = p.TITULAR", "PACIENTES p2 ON p2.TITULAR = p.CODIGO", "TURNOS t ON t.PACIENTE = p.CODIGO"],
            Joins("SELECT * FROM PACIENTES p JOIN |").Select(j => j.Text));

        // Las tablas que siguen después del cursor no cuentan, y sin FK no hay nada.
        Assert.Empty(Joins("SELECT * FROM T JOIN |  JOIN TURNOS t ON 1 = 1"));
        Assert.Empty(SqlCompletion.Suggest("SELECT * FROM TURNOS JOIN ", 26, new MetadataCatalog([("TURNOS", "ID")])).Items.Where(i => i.Kind == CompletionKind.Join));
    }
}
