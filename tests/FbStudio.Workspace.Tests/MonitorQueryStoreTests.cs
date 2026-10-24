namespace FbStudio.Workspace.Tests;

/// <summary>Las consultas propias del monitoreo se corren cada pocos segundos: solo se aceptan de lectura.</summary>
public class MonitorQueryStoreTests
{
    [Theory]
    [InlineData("SELECT * FROM MON$ATTACHMENTS", true)]
    [InlineData("  select * from mon$transactions;  ", true)]
    [InlineData("SELECT 1 FROM RDB$DATABASE; DELETE FROM T", false)]
    [InlineData("DELETE FROM MON$ATTACHMENTS", false)]
    [InlineData("SELECT GEN_ID(G, 1) FROM RDB$DATABASE", false)]
    [InlineData("EXECUTE BLOCK AS BEGIN END", false)]
    public void Una_consulta_propia_es_un_solo_SELECT(string sql, bool accepted) =>
        Assert.Equal(accepted, MonitorQueryStore.IsSingleSelect(sql));
}
