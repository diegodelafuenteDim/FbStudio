using System.Text;
using FbStudio.Core.Profiles;

namespace FbStudio.Workspace.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-ws-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_folder, "history.db");

    // En Windows, DPAPI; en Linux y macOS, AES con una clave fija de prueba (la real va al llavero del sistema).
    private static readonly IDataProtector Protector = OperatingSystem.IsWindows() ? new DpapiDataProtector() : new AesDataProtector(new byte[32]);

    private HistoryStore NewHistory(TimeSpan? retention = null) => new(Db, Protector, retention ?? TimeSpan.FromDays(90));

    private static HistoryEntry Entry(Guid profile, string sql, DateTimeOffset? at = null, string? error = null) =>
        new(0, profile, "base-prueba", at ?? DateTimeOffset.Now, "SELECT", sql, 120, 10, -1, false, error, "PLAN (T NATURAL)");

    [Fact]
    public void Historial_guarda_y_devuelve_lo_mas_reciente_primero()
    {
        var h = NewHistory();
        var p = Guid.NewGuid();
        h.Add(Entry(p, "SELECT 1 FROM RDB$DATABASE"));
        h.Add(Entry(p, "SELECT * FROM CLINICA_PACIENTES WHERE DNI = '12345678'"));

        var recent = h.Recent(p);
        Assert.Equal(2, recent.Count);
        Assert.Contains("12345678", recent[0].Sql);
        Assert.Equal("PLAN (T NATURAL)", recent[0].Plan);
    }

    [Fact]
    public void El_SQL_y_los_errores_no_quedan_en_claro_en_el_archivo()
    {
        var h = NewHistory();
        h.Add(Entry(Guid.NewGuid(), "SELECT * FROM CLINICA_PACIENTES WHERE APELLIDO = 'GONZALEZ_SECRETO'", error: "violación: DNI_SECRETO"));
        var bytes = File.ReadAllBytes(Db);
        Assert.DoesNotContain("GONZALEZ_SECRETO", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("DNI_SECRETO", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("GONZALEZ_SECRETO", Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void Filtra_por_texto_y_por_perfil()
    {
        var h = NewHistory();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        h.Add(Entry(a, "SELECT * FROM CLINICA_TURNOS"));
        h.Add(Entry(a, "UPDATE T SET X = 1", error: "lock conflict on no wait transaction"));
        h.Add(Entry(b, "SELECT * FROM CLINICA_TURNOS"));

        Assert.Single(h.Recent(a, "clinica_turnos"));
        Assert.Equal(2, h.Recent(null, "CLINICA_TURNOS").Count);
        Assert.Single(h.Recent(a, "lock conflict")); // también busca en el error
    }

    [Fact]
    public void Purga_lo_mas_viejo_que_la_retencion_al_abrir()
    {
        var p = Guid.NewGuid();
        var h = NewHistory();
        h.Add(Entry(p, "SELECT VIEJO FROM T", DateTimeOffset.Now.AddDays(-120)));
        h.Add(Entry(p, "SELECT NUEVO FROM T"));

        var reopened = NewHistory(TimeSpan.FromDays(90));
        Assert.Equal(["SELECT NUEVO FROM T"], reopened.Recent(p).Select(e => e.Sql));
    }

    [Fact]
    public void Perfiles_sin_contrasena_en_el_JSON_y_con_contrasena_cifrada()
    {
        ISecretStore secrets = OperatingSystem.IsWindows() ? new DpapiSecretStore(Path.Combine(_folder, "secrets")) : new MemorySecretStore();
        var store = new ProfileStore(_folder, secrets);
        var profile = new ConnectionProfile { Name = "prod", Host = "srv", Database = "sucursal", User = "U", Environment = ProfileEnvironment.Produccion };
        store.Upsert(profile, "clave-super-secreta");

        Assert.Equal(profile, Assert.Single(store.Load()));
        Assert.Equal("clave-super-secreta", store.GetPassword(profile.Id));
        Assert.DoesNotContain("clave-super-secreta", File.ReadAllText(Path.Combine(_folder, "profiles.json")));
        Assert.Contains("\"Produccion\"", File.ReadAllText(Path.Combine(_folder, "profiles.json"))); // enums como texto

        store.Upsert(profile with { Name = "prod2" }, null); // null = conservar la contraseña
        Assert.Equal("clave-super-secreta", store.GetPassword(profile.Id));
        store.Delete(profile.Id);
        Assert.Empty(store.Load());
        Assert.Null(store.GetPassword(profile.Id));
    }

    [Fact]
    public void Opciones_sin_archivo_son_los_valores_de_siempre()
    {
        var store = new SettingsStore(_folder);
        Assert.Equal(new AppSettings(), store.Current);
        Assert.Equal((90, 10_000, 10, AppTheme.System), (store.Current.HistoryRetentionDays, store.Current.DefaultRowLimit, store.Current.LockTimeoutSeconds, store.Current.Theme));
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void Colores_propios_solo_de_la_paleta_y_las_carpetas_sin_distinguir_mayusculas()
    {
        var settings = new AppSettings
        {
            EnvironmentColors = new()
            {
                [ProfileEnvironment.Desarrollo] = "#1565c0",
                [ProfileEnvironment.Prueba] = "#C62828",                              // un rojo fuera de producción: no
                [ProfileEnvironment.Produccion] = "#C62828",                          // el de siempre: no se guarda
            },
            FolderColors = new() { ["Clientes/Norte/"] = "#00796B", ["Rojas"] = "#B71C1C", [" "] = "#1565C0" },
        }.Normalized();
        Assert.Equal(new Dictionary<ProfileEnvironment, string> { [ProfileEnvironment.Desarrollo] = "#1565C0" }, settings.EnvironmentColors);
        Assert.Equal("#00796B", Assert.Single(settings.FolderColors!).Value);
        Assert.Equal(new AppSettings(), new AppSettings { EnvironmentColors = new() { [ProfileEnvironment.Produccion] = "#C62828" } }.Normalized());

        var store = new SettingsStore(_folder);
        store.Save(settings);
        var again = new SettingsStore(_folder).Current;
        Assert.Equal("#1565C0", again.EnvironmentColors![ProfileEnvironment.Desarrollo]);
        Assert.Equal("#00796B", again.FolderColors!["CLIENTES/NORTE"]);              // sin distinguir mayúsculas, también al releer
    }

    [Fact]
    public void Opciones_se_guardan_y_se_vuelven_a_leer()
    {
        var store = new SettingsStore(_folder);
        AppSettings? notified = null;
        store.Changed += s => notified = s;
        store.Save(new AppSettings { HistoryRetentionDays = 30, DefaultRowLimit = 1_000, LockTimeoutSeconds = 3, Theme = AppTheme.Dark, EditorFontSize = 16 });

        Assert.Equal(16, notified?.EditorFontSize);
        Assert.Contains("\"Dark\"", File.ReadAllText(store.FilePath)); // enums como texto: el archivo se puede leer y editar
        var again = new SettingsStore(_folder).Current;
        Assert.Equal((30, 1_000, 3, AppTheme.Dark, 16.0), (again.HistoryRetentionDays, again.DefaultRowLimit, again.LockTimeoutSeconds, again.Theme, again.EditorFontSize));
    }

    [Fact]
    public void Opciones_fuera_de_rango_o_archivo_roto_no_rompen_la_aplicacion()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "HistoryRetentionDays": -5, "DefaultRowLimit": 123, "LockTimeoutSeconds": 99999, "EditorFontSize": 2, "Desconocida": 1 }""");
        var clamped = new SettingsStore(_folder).Current;
        Assert.Equal((1, 10_000, 600, 8.0), (clamped.HistoryRetentionDays, clamped.DefaultRowLimit, clamped.LockTimeoutSeconds, clamped.EditorFontSize));

        File.WriteAllText(path, "{ esto no es json");
        var broken = new SettingsStore(_folder);
        Assert.Equal(new AppSettings(), broken.Current);
        Assert.NotNull(broken.LoadError);
    }

    [Fact]
    public void Consultas_propias_del_monitoreo_se_guardan_exportan_e_importan()
    {
        var store = new MonitorQueryStore(_folder);
        var changes = 0;
        store.Changed += () => changes++;
        var mini = new CustomMonitorQuery(Guid.NewGuid(), "Transacciones por IP", "SELECT 1 FROM MON$TRANSACTIONS");
        store.Upsert(mini);
        store.Upsert(new CustomMonitorQuery(Guid.NewGuid(), "Mis conexiones", "SELECT 2 FROM MON$ATTACHMENTS", AttachmentColumn: "Attachment ID"));
        store.Upsert(mini with { Sql = "SELECT 3 FROM MON$TRANSACTIONS" });                 // mismo Id: reemplaza
        Assert.Equal(["Mis conexiones", "Transacciones por IP"], new MonitorQueryStore(_folder).Load().Select(q => q.Name));
        Assert.Equal("Attachment ID", store.Load()[0].AttachmentColumn);
        Assert.Equal(3, changes);

        // Compartir con el equipo: exportar, borrar e importar.
        var file = Path.Combine(_folder, "equipo.json");
        store.Export(file, store.Load());
        store.Delete(mini.Id);
        Assert.Single(store.Load());
        Assert.Equal(2, store.Import(file));
        Assert.Equal(2, store.Load().Count);                                                  // la que ya estaba no se duplica
        Assert.Equal("SELECT 3 FROM MON$TRANSACTIONS", store.Load().Single(q => q.Id == mini.Id).Sql);

        // Sin carpeta (pruebas): en memoria.
        var memory = new MonitorQueryStore(null);
        memory.Upsert(mini);
        Assert.Single(memory.Load());
        Assert.Null(memory.FilePath);
    }

    [Fact]
    public void El_archivo_de_consultas_de_ejemplo_se_puede_importar()
    {
        var store = new MonitorQueryStore(_folder);
        Assert.Equal(2, store.Import(Path.Combine(AppContext.BaseDirectory, "monitor-queries.json"))); // docs/examples, copiado al compilar
        Assert.Equal(["Connections (example)", "Open transactions (example)"], store.Load().Select(q => q.Name));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
