namespace FbStudio.Workspace.Tests;

/// <summary>Un settings.json de una versión anterior: las opciones que todavía no tenía toman su valor de fábrica.</summary>
public sealed class SettingsDefaultsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Lo_que_falta_en_el_archivo_queda_con_el_valor_de_fabrica()
    {
        Directory.CreateDirectory(_folder);
        // Como lo dejó una versión vieja: solo algunas opciones, y cambiadas.
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "DefaultRowLimit": 1000, "EditorFontSize": 16 }""");
        var current = new SettingsStore(_folder).Current;
        var factory = new AppSettings();
        Assert.Equal((1000, 16), (current.DefaultRowLimit, current.EditorFontSize));     // lo guardado se respeta
        Assert.Equal(
            (factory.ShowFolding, factory.RestoreTabs, factory.PreviewDataChanges, factory.OatIgnoreMaintenance, factory.OatWarnMinutes,
             factory.OatDangerMinutes, factory.TransactionWarnMinutes, factory.TransactionDangerMinutes, factory.HistoryRetentionDays, factory.ShowLineNumbers),
            (current.ShowFolding, current.RestoreTabs, current.PreviewDataChanges, current.OatIgnoreMaintenance, current.OatWarnMinutes,
             current.OatDangerMinutes, current.TransactionWarnMinutes, current.TransactionDangerMinutes, current.HistoryRetentionDays, current.ShowLineNumbers));
        Assert.True(current.ShowFolding);
        Assert.True(current.RestoreTabs);
    }

    [Fact]
    public void Registros_diagramas_y_areas_viejos_toman_los_valores_de_fabrica()
    {
        Directory.CreateDirectory(_folder);
        // Un registro de antes de que existieran el cifrado, el entorno o «guardar historial».
        File.WriteAllText(Path.Combine(_folder, "profiles.json"),
            """[ { "Id": "6f1b2c3d-0000-0000-0000-000000000001", "Name": "Vieja", "Host": "srv", "Database": "/db/x.fdb", "User": "U" } ]""");
        var profile = Assert.Single(new ProfileStore(_folder, new FbStudio.Workspace.MemorySecretStore()).Load());
        var factory = new FbStudio.Core.Profiles.ConnectionProfile { Name = "x", Host = "h", Database = "d", User = "u" };
        Assert.Equal((factory.Port, factory.Charset, factory.Environment, factory.WireCrypt, factory.SaveHistory),
            (profile.Port, profile.Charset, profile.Environment, profile.WireCrypt, profile.SaveHistory));
        Assert.True(profile.SaveHistory);

        // Un diagrama sin las opciones nuevas: las de fábrica, y las listas vacías (no null).
        var diagram = Path.Combine(_folder, "d.fbsdiagram");
        File.WriteAllText(diagram, """{ "Source": "TURNOS" }""");
        var read = DiagramFiles.Read(diagram);
        Assert.Equal((1, true, true), (read.Hops, read.HideHubs, read.ShowLogical));
        Assert.NotNull(read.Positions);

        // Un área sin «Exclude» (agregado después): lista vacía.
        var areas = Path.Combine(_folder, "areas.json");
        File.WriteAllText(areas, """{ "Areas": [ { "Name": "LAB", "Rules": [ "LAB_" ] } ] }""");
        var area = Assert.Single(new AreaStore(_folder).LoadFile(areas).File.Areas);
        Assert.NotNull(area.Exclude);
        Assert.Empty(area.Exclude);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
