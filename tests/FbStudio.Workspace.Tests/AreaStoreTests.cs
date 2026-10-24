using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;

namespace FbStudio.Workspace.Tests;

public sealed class AreaStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-areas-" + Guid.NewGuid().ToString("N"));

    private static ConnectionProfile Profile(string? areasFile = null) =>
        new() { Name = "base", Host = "localhost", Database = "x.fdb", User = "u", AreasFile = areasFile };

    private static SchemaAreaFile File1(params SchemaArea[] areas) => new() { Areas = areas };

    [Fact]
    public void Guarda_en_el_archivo_propio_de_la_base_y_lo_vuelve_a_leer()
    {
        var store = new AreaStore(_folder);
        var profile = Profile();
        var path = store.PathFor(profile)!;
        Assert.Equal(Path.Combine(_folder, "areas", $"{profile.Id:N}.json"), path);
        var first = store.Load(profile);
        Assert.Null(first.Stamp);
        Assert.Empty(first.File.Areas);

        var changed = 0;
        store.Changed += () => changed++;
        var saved = store.Save(path, profile.Id, File1(new SchemaArea { Name = " Turnos ", Rules = ["TUR_*", "", "tur_*"], Description = " " }), first.Stamp);

        Assert.False(saved.Conflict);
        Assert.Equal(1, changed);
        var again = new AreaStore(_folder).Load(profile);
        var area = Assert.Single(again.File.Areas);
        Assert.Equal("Turnos", area.Name);
        Assert.Equal(["TUR_*"], area.Rules);
        Assert.Null(area.Description);
        Assert.Equal(saved.Stamp, again.Stamp);
        Assert.Equal(["Turnos"], store.Areas(profile).Select(a => a.Name));
    }

    [Fact]
    public void El_color_de_un_area_se_guarda_y_un_rojo_se_descarta()
    {
        var store = new AreaStore(_folder);
        var profile = Profile();
        store.Save(store.PathFor(profile), profile.Id, File1(
            new SchemaArea { Name = "Laboratorio", Color = "#00796b" },
            new SchemaArea { Name = "Roja", Color = "#C62828" },                 // los rojos son de producción
            new SchemaArea { Name = "Rara", Color = "violeta" }), null);
        var areas = new AreaStore(_folder).Load(profile).File.Areas;
        Assert.Equal(["#00796B", null, null], areas.Select(a => a.Color));
    }

    [Fact]
    public void Un_archivo_compartido_no_se_pisa_si_otro_lo_cambio()
    {
        var shared = Path.Combine(_folder, "red", "areas-clinica.json");
        var mine = Profile(shared);
        var other = Profile(shared);
        var store = new AreaStore(_folder);
        var otherStore = new AreaStore(Path.Combine(_folder, "otra-pc"));

        var loaded = store.Load(mine);
        var otherLoaded = otherStore.Load(other);
        var first = otherStore.Save(otherStore.PathFor(other), other.Id, File1(new SchemaArea { Name = "Pacientes", Rules = ["PAC_*"] }), otherLoaded.Stamp);
        Assert.False(first.Conflict);

        // Lo leí antes de que el otro guardara: no lo piso sin preguntar.
        var conflict = store.Save(shared, mine.Id, File1(new SchemaArea { Name = "Turnos", Rules = ["TUR_*"] }), loaded.Stamp);
        Assert.True(conflict.Conflict);
        Assert.Equal("Pacientes", Assert.Single(store.Load(mine).File.Areas).Name);

        var forced = store.Save(shared, mine.Id, File1(new SchemaArea { Name = "Turnos", Rules = ["TUR_*"] }), loaded.Stamp, force: true);
        Assert.False(forced.Conflict);
        Assert.Equal("Turnos", Assert.Single(otherStore.Load(other).File.Areas).Name);
    }

    [Fact]
    public void Un_archivo_roto_se_informa_y_sin_carpeta_viven_en_memoria()
    {
        var broken = Path.Combine(_folder, "rota.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(broken, "{ esto no es json");
        var load = new AreaStore(_folder).Load(Profile(broken));
        Assert.NotNull(load.Error);
        Assert.Empty(load.File.Areas);

        // Escrito a mano con nulls: se limpia.
        File.WriteAllText(broken, """{ "Areas": [ { "Name": "A", "Rules": null }, { "Name": null }, null ] }""");
        Assert.Equal(["A"], new AreaStore(_folder).Load(Profile(broken)).File.Areas.Select(a => a.Name));

        var memory = new AreaStore(null);
        var profile = Profile();
        Assert.Null(memory.PathFor(profile));
        memory.Save(null, profile.Id, File1(new SchemaArea { Name = "Turnos", Rules = ["TUR_*"] }), null);
        Assert.Equal(["Turnos"], memory.Areas(profile).Select(a => a.Name));
        Assert.Empty(memory.Areas(Profile()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
