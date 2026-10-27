using Avalonia;
using FbStudio.Core.Profiles;
using FbStudio.Workspace;

namespace FbStudio.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // FbStudio --import-mcp <config.json> <servidor> [charset] [Desarrollo|Prueba|Produccion]
        // Crea un perfil (de solo lectura) a partir de la configuración de un MCP de Firebird, sin mostrar la contraseña.
        if (args.Length >= 3 && args[0] == "--import-mcp")
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var env = args.Length > 4 ? Enum.Parse<ProfileEnvironment>(args[4], ignoreCase: true) : ProfileEnvironment.Prueba;
            var (profile, password) = McpConfigImporter.Read(args[1], args[2], args.Length > 3 ? args[3] : "ISO8859_1", env);
            var store = AppServices.CreateProfileStore();
            var existing = store.Load().FirstOrDefault(p => p.Name == profile.Name);
            if (existing is not null) profile = profile with { Id = existing.Id };
            store.Upsert(profile, password);
            Console.WriteLine($"Perfil {(existing is null ? "creado" : "actualizado")}: {profile.Display} · {profile.Environment} · solo lectura: {profile.ReadOnly}");
            if (!store.SavesPasswords)
                Console.WriteLine("Aviso: este equipo no tiene llavero del sistema, así que la contraseña no quedó guardada: FbStudio la va a pedir al conectar.");
            return 0;
        }

        // FbStudio mcp [--data <carpeta>]
        // El conector MCP (D-114): lo lanza un asistente de IA y le habla por la entrada y la salida estándar, sin
        // ventanas. --data usa otra carpeta de datos en lugar de la de siempre (pruebas, o FbStudio portable).
        if (args.Length >= 1 && args[0] == "mcp")
        {
            var folder = args.Length >= 3 && args[1] == "--data" ? args[2] : ProfileStore.DefaultFolder;
            var profiles = AppServices.CreateProfileStore(folder);
            var areas = AppServices.CreateAreaStore(folder);
            var settings = AppServices.CreateSettingsStore(folder).Current;
            var log = AppServices.CreateMcpCallLog(folder, settings.HistoryRetentionDays);
            log.Purge();
            var context = new FbStudio.Mcp.ConnectorContext(profiles.Load, areas.Areas, profiles.GetPassword, log, settings.McpResponseLimit,
                settings.McpShowClients);
            return FbStudio.Mcp.McpHost.RunAsync(context, folder, Console.Error).GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
