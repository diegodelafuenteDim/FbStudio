using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class SourceSearchTests
{
    [Fact]
    public void Numera_como_el_DDL_sin_saltos_iniciales_ni_finales()
    {
        const string source = "\r\n\nBEGIN\r\n  SELECT COUNT(*) FROM T;\n  -- otra vez count\nEND\n\n";
        Assert.Equal(4, SourceSearch.LineCount(source));
        Assert.Equal([(2, "SELECT COUNT(*) FROM T;"), (3, "-- otra vez count")], SourceSearch.MatchingLines(source, "count").ToList());
        Assert.Single(SourceSearch.MatchingLines(source, "COUNT", max: 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 3)]
    [InlineData(2, 2)]
    public void El_encabezado_calculado_coincide_con_el_DDL_del_procedimiento(int inputs, int outputs)
    {
        const string source = "BEGIN\n  X = 1; -- buscado\nEND";
        var ddl = DdlWriter.Procedure(new ProcedureDefinition("P",
            Enumerable.Range(1, inputs).Select(i => new FieldDefinition($"A{i}", "INTEGER")).ToList(),
            Enumerable.Range(1, outputs).Select(i => new FieldDefinition($"B{i}", "INTEGER")).ToList(), source));
        var lines = ddl.Split('\n');
        Assert.Contains("buscado", lines[DdlLayout.ProcedureHeader(inputs, outputs) + 2 - 1]); // línea 2 de la fuente
    }

    [Fact]
    public void El_encabezado_calculado_coincide_con_vista_funcion_y_trigger()
    {
        var view = DdlWriter.View(new ViewDefinition("V", [new("A", "INTEGER"), new("B", "INTEGER")], "SELECT 1\nFROM T -- buscado", [])).Split('\n');
        Assert.Contains("buscado", view[DdlLayout.ViewHeader(2) + 2 - 1]);

        var function = DdlWriter.Function(new FunctionDefinition("F", [new("X", "INTEGER")], new("", "INTEGER"), "BEGIN\n  RETURN X; -- buscado\nEND")).Split('\n');
        Assert.Contains("buscado", function[DdlLayout.FunctionHeader(1) + 2 - 1]);

        foreach (var source in new[] { "AS\nBEGIN -- buscado\nEND", "BEGIN\nX = 1; -- buscado\nEND" })
        {
            var trigger = DdlWriter.Trigger(new TriggerDefinition("T", "X", 1, 0, false, source)).Split('\n');
            Assert.Contains("buscado", trigger[DdlLayout.TriggerHeader(source) + 2 - 1]);
        }
    }

    [Fact]
    public void La_linea_de_la_fuente_cae_en_la_misma_linea_del_DDL()
    {
        const string source = "\nDECLARE X INTEGER;\nBEGIN\n  X = 1; -- buscado\nEND\n";
        var ddl = DdlWriter.Procedure(new ProcedureDefinition("P", [new FieldDefinition("A", "INTEGER")], [new FieldDefinition("B", "INTEGER")], source));
        var ddlLines = ddl.Split('\n');
        var (line, _) = SourceSearch.MatchingLines(source, "buscado").Single();
        Assert.Contains("buscado", ddlLines[ddlLines.Length - SourceSearch.LineCount(source) + line - 1]);
    }
}
