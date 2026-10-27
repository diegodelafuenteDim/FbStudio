using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Mcp;

namespace FbStudio.Mcp.Tests;

public sealed class DatabaseListTests
{
    private static ConnectionProfile Profile(string name, bool enabled, ProfileEnvironment env = ProfileEnvironment.Desarrollo, bool readOnly = false) =>
        new() { Name = name, Host = "srv-secreto", Database = "/datos/secreta.fdb", User = "SYSDBA", Environment = env, ReadOnly = readOnly, AssistantAccess = enabled };

    private static IReadOnlyList<SchemaArea> NoAreas(ConnectionProfile _) => [];

    [Fact]
    public void Solo_lista_los_habilitados_sin_servidor_ruta_ni_usuario()
    {
        var text = DatabaseList.Describe(
            [Profile("Ventas", enabled: true, ProfileEnvironment.Produccion), Profile("Archivo", enabled: false), Profile("Pruebas", enabled: true, ProfileEnvironment.Prueba, readOnly: true)],
            NoAreas);

        Assert.Equal(
            "Databases available through FbStudio (2):\n" +
            "- Pruebas · environment: Test · read-only registration\n" +
            "- Ventas · environment: Production\n" +
            "1 more registration not enabled. " + DatabaseList.HowToEnable,
            text);
        Assert.DoesNotContain("Archivo", text);                                  // ni el nombre de los que no están habilitados
        foreach (var secret in new[] { "srv-secreto", "secreta.fdb", "/datos", "SYSDBA" }) Assert.DoesNotContain(secret, text);
    }

    [Fact]
    public void Muestra_las_areas_y_avisa_si_no_se_pudieron_leer()
    {
        var text = DatabaseList.Describe([Profile("Lab", enabled: true), Profile("Red", enabled: true)], p => p.Name == "Lab"
            ? [new SchemaArea { Name = "LAB", Rules = ["LAB_*"] }, new SchemaArea { Name = "TURNOS", Rules = ["TURNOS*"] }]
            : throw new IOException("la carpeta de red no responde"));

        Assert.Contains("- Lab · environment: Development\n  areas: LAB, TURNOS\n", text);
        Assert.Contains("- Red · environment: Development\n  areas: could not be read", text);
        Assert.DoesNotContain("carpeta de red", text);                           // el error no sale
    }

    [Fact]
    public void Sin_habilitados_dice_donde_se_habilitan()
    {
        Assert.Equal("No database is enabled for assistants yet (3 registrations in FbStudio).\n" + DatabaseList.HowToEnable,
            DatabaseList.Describe([Profile("A", false), Profile("B", false), Profile("C", false)], NoAreas));
        Assert.Equal("No database is enabled for assistants yet (FbStudio has no registrations).\n" + DatabaseList.HowToEnable,
            DatabaseList.Describe([], NoAreas));
    }
}
