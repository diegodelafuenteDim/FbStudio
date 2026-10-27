using System.Text.Json.Nodes;
using FbStudio.App.ViewModels;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

/// <summary>La pestaña «Asistentes (MCP)» de Opciones (D-117).</summary>
public sealed class McpOptionsTests
{
    [Fact]
    public void Muestra_la_configuracion_para_copiar_las_bases_habilitadas_y_las_llamadas()
    {
        var calls = new[]
        {
            new McpCall(new DateTime(2026, 10, 27, 10, 15, 2), "explain_query", "Base de pruebas", "SELECT *\nFROM T", 120, true, 2547),
            new McpCall(new DateTime(2026, 10, 27, 10, 14, 0), "get_schema", "Otra", "area=LAB", 30, false, 90, "There is no enabled database called \"Otra\"."),
        };
        var vm = new OptionsViewModel(new AppSettings(), new AssistantsInfo(["Base de pruebas", "Pruebas"], @"C:\Program Files\FbStudio\FbStudio.exe", ["mcp"], calls));

        // El JSON estándar, con la ruta bien escapada y sin nada de la conexión.
        var config = JsonNode.Parse(vm.AssistantsConfig)!["mcpServers"]!["fbstudio"]!;
        Assert.Equal(@"C:\Program Files\FbStudio\FbStudio.exe", config["command"]!.GetValue<string>());
        Assert.Equal("mcp", Assert.Single(config["args"]!.AsArray())!.GetValue<string>());
        Assert.Equal("Base de pruebas, Pruebas", vm.AssistantsDatabases);

        Assert.True(vm.HasAssistantsCalls);
        Assert.StartsWith("27/10 10:15:02  Base de pruebas  explain_query  120 ms", vm.AssistantsCalls[0]);
        Assert.EndsWith("\n    SELECT * FROM T", vm.AssistantsCalls[0]);                              // en un renglón
        Assert.EndsWith("\n    There is no enabled database called \"Otra\".", vm.AssistantsCalls[1]);   // el error, no lo pedido
    }

    [Fact]
    public void Sin_datos_dice_que_no_hay_nada_y_el_largo_maximo_se_guarda()
    {
        var vm = new OptionsViewModel(new AppSettings());
        Assert.False(vm.HasAssistantsCalls);
        Assert.Equal(FbStudio.App.Resources.Strings.AssistantsNoneEnabled, vm.AssistantsDatabases);
        Assert.Equal(40_000, vm.McpResponseLimit);

        vm.McpResponseLimit = 20_000;
        Assert.Equal(20_000, vm.ToSettings().McpResponseLimit);
        vm.McpResponseLimit = 1;                                                                // fuera de rango
        Assert.Equal(5_000, vm.ToSettings().McpResponseLimit);
        Assert.True(vm.McpShowClients);
        vm.McpShowClients = false;
        Assert.False(vm.ToSettings().McpShowClients);
        vm.McpResponseLimit = null;                                                             // vacío: el de fábrica
        Assert.Equal(40_000, vm.ToSettings().McpResponseLimit);
    }

    [Fact]
    public void La_configuracion_lanza_este_mismo_FbStudio()
    {
        var (command, arguments) = McpSetup.Launch();
        Assert.Equal("mcp", arguments[^1]);
        Assert.True(File.Exists(command) || command == "dotnet");
    }
}
