using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class PlanAnalyzerTests
{
    [Fact]
    public void Detecta_lectura_completa_en_el_plan_detallado()
    {
        const string plan = """
            Select Expression
                -> Filter
                    -> Table "CLINICA_TURNOS" as "T" Full Scan
                -> Nested Loop Join (inner)
                    -> Table "RDB$DATABASE" Full Scan
                    -> Table "CLINICA_MEDICOS" Access By ID
                        -> Bitmap
                            -> Index "PK_CLINICA_MEDICOS" Unique Scan
            """;
        Assert.Equal(["CLINICA_TURNOS"], PlanAnalyzer.FullScans(plan)); // RDB$ no cuenta; la de índice tampoco
    }

    [Theory]
    [InlineData("PLAN (T NATURAL)", "T")]
    [InlineData("PLAN JOIN (A NATURAL, B INDEX (PK_B))", "A")]
    public void Detecta_NATURAL_en_el_plan_clasico(string plan, string expected) =>
        Assert.Equal([expected], PlanAnalyzer.FullScans(plan));

    [Fact]
    public void Con_indices_no_avisa() =>
        Assert.Empty(PlanAnalyzer.FullScans("Select Expression\n    -> Table \"TURNOS\" Access By ID\n        -> Bitmap\n            -> Index \"TURNOS_FECHA\" Range Scan"));

    [Fact]
    public void Lee_la_transaccion_que_tiene_el_bloqueo()
    {
        Assert.Equal(19182, PlanAnalyzer.LockingTransaction("deadlock\nupdate conflicts with concurrent update\nconcurrent transaction number is 19182"));
        Assert.Null(PlanAnalyzer.LockingTransaction("Table unknown"));
    }
}
