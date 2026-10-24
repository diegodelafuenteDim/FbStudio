using FbStudio.Core.Metadata;

namespace FbStudio.Firebird.Tests;

[Collection(nameof(FirebirdCollection))]
public class MetadataSearchTests(FirebirdFixture fb)
{
    private async Task<IReadOnlyList<MetadataSearchHit>> Search(string text, MetadataSearchOptions? options = null)
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        return await ex.SearchAsync(text, options);
    }

    [Fact]
    public async Task Encuentra_en_fuentes_con_linea_y_fragmento()
    {
        var hits = await Search("señal");
        var hit = Assert.Single(hits);
        // Línea 3 de la fuente guardada = línea 10 del DDL (7 de encabezado: CREATE, 2 entradas, RETURNS, 2 salidas, AS).
        Assert.Equal((DbObjectKind.Procedure, "P_TURNOS_DESDE", SearchArea.Source, 10), (hit.Kind, hit.Name, hit.Area, hit.Line));
        Assert.Equal("/* Devuelve los turnos del rango (año, señal) */", hit.Snippet);
    }

    [Fact]
    public async Task Sin_distinguir_mayusculas_en_procedimientos_triggers_vistas_funciones_y_paquetes()
    {
        var hits = await Search("count(*)", new MetadataSearchOptions(Names: false, Columns: false, Descriptions: false));
        Assert.Contains(hits, h => h is { Kind: DbObjectKind.Procedure, Name: "P_CONTAR", Line: 5 });
        Assert.Contains(hits, h => h is { Kind: DbObjectKind.Package, Name: "PKG_TURNOS", Line: null });

        Assert.Contains(await Search("new.nota"), h => h is { Kind: DbObjectKind.Trigger, Name: "TURNOS_DET_BIU", Area: SearchArea.Source });
        Assert.Contains(await Search("SELECT ID, FECHA"), h => h is { Kind: DbObjectKind.View, Name: "V_TURNOS", Area: SearchArea.Source });
        Assert.Contains(await Search("RETURN X"), h => h is { Kind: DbObjectKind.Function, Name: "F_DOBLE", Area: SearchArea.Source });
    }

    [Fact]
    public async Task Encuentra_nombres_columnas_y_descripciones()
    {
        var names = await Search("turnos_d", new MetadataSearchOptions(Sources: false, Columns: false, Descriptions: false));
        Assert.Contains(names, h => h is { Kind: DbObjectKind.Table, Name: "TURNOS_DET", Area: SearchArea.Name });
        Assert.Contains(names, h => h is { Kind: DbObjectKind.Procedure, Name: "P_TURNOS_DESDE", Area: SearchArea.Name });
        Assert.Contains(names, h => h is { Kind: DbObjectKind.Trigger, Name: "TURNOS_DET_BIU", Area: SearchArea.Name });

        var columns = await Search("pacien", new MetadataSearchOptions(Sources: false, Names: false, Descriptions: false));
        Assert.Equal(("TURNOS", "PACIENTE"), Assert.Single(columns) is var c ? (c.Name, c.Detail) : default);

        var descriptions = await Search("%PACIENTES", new MetadataSearchOptions(Sources: false, Names: false, Columns: false));
        Assert.Equal(("TURNOS", "PACIENTE"), Assert.Single(descriptions) is var d ? (d.Name, d.Detail) : default);
        Assert.Contains(await Search("entre dos"), h => h is { Kind: DbObjectKind.Procedure, Name: "P_TURNOS_DESDE", Area: SearchArea.Description });
    }

    [Fact]
    public async Task Texto_vacio_o_inexistente_no_devuelve_nada()
    {
        Assert.Empty(await Search("   "));
        Assert.Empty(await Search("NO_EXISTE_EN_NINGUN_LADO"));
    }
}
