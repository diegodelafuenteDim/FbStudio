using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class ServerInfoTests
{
    [Theory]
    [InlineData("3.0.11", 3)]
    [InlineData("4.0.2", 4)]
    [InlineData("5.0.1", 5)]
    [InlineData("?", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Saca_la_version_mayor(string? version, int major) => Assert.Equal(major, ServerInfo.MajorOf(version));

    [Fact]
    public void Avisa_si_no_es_Firebird_3()
    {
        var fb3 = new ServerInfo("3.0.11", 12, 0, 3, 16384, "NONE");
        Assert.Equal("Firebird 3.0.11", fb3.Display);
        Assert.Null(fb3.Warning);
        Assert.Equal("Firebird 3.0.11 · ODS 12.0 · dialecto 3 · página de 16.384 bytes · charset NONE",
            fb3.Summary.Replace(",", ".")); // separador de miles según la cultura de la PC

        var fb4 = new ServerInfo("4.0.2", 13, 0, 3, 8192, null);
        Assert.False(fb4.IsTested);
        Assert.Contains("Firebird 4.0.2", fb4.Warning);
        Assert.EndsWith("charset NONE", fb4.Summary);
    }
}
