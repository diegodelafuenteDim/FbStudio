using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public sealed class SchemaAreasTests
{
    private static DbObject T(string name) => new(DbObjectKind.Table, name);

    private static SchemaArea Area(string name, string[] rules, params string[] exclude) =>
        new() { Name = name, Rules = rules, Exclude = exclude };

    [Theory]
    [InlineData("TUR_*", "TUR_AGENDA", true)]
    [InlineData("tur_*", "TUR_AGENDA", true)]
    [InlineData("TUR_*", "TURNOS", false)]
    [InlineData("TUR%", "TURNOS", true)]
    [InlineData("*_LOG", "TUR_LOG", true)]
    [InlineData("*_LOG", "TUR_LOGS", false)]
    [InlineData("BASE_TURNOS", "BASE_TURNOS", true)]
    [InlineData("BASE_TURNOS", "BASE_TURNOS_HIST", false)]
    [InlineData("T?R*", "TUR_X", true)]
    [InlineData("T?R*", "TR", false)]
    [InlineData("*", "CUALQUIERA", true)]
    [InlineData("*A*B*", "XAXXBX", true)]
    [InlineData("*A*B", "XAXXBX", false)]
    [InlineData("", "X", false)]
    public void Patrones_con_comodines_sin_distinguir_mayusculas(string pattern, string name, bool expected) =>
        Assert.Equal(expected, SchemaAreas.Matches(pattern, name));

    [Fact]
    public void Las_excepciones_ganan_y_un_objeto_puede_estar_en_varias_areas()
    {
        var turnos = Area("Turnos", ["TUR_*", "BASE_TURNOS"], "TUR_LOG*");
        var maestros = Area("Base", ["BASE_*"]);
        var objects = new[] { T("TUR_AGENDA"), T("TUR_LOG"), T("TUR_LOG_2024"), T("BASE_TURNOS"), T("BASE_PACIENTES"), T("FACTURAS") };

        Assert.Equal(["TUR_AGENDA", "BASE_TURNOS"], SchemaAreas.Members(turnos, objects).Select(o => o.Name));
        Assert.Equal(["Turnos", "Base"], SchemaAreas.AreasOf([turnos, maestros], T("BASE_TURNOS")));
        Assert.Equal(["TUR_LOG", "TUR_LOG_2024", "FACTURAS"], SchemaAreas.Unassigned([turnos, maestros], objects).Select(o => o.Name));
    }

    [Fact]
    public void Triggers_e_indices_van_con_su_tabla()
    {
        var turnos = Area("Turnos", ["TUR_*"], "TUR_LOG");
        Assert.True(SchemaAreas.Contains(turnos, new DbObject(DbObjectKind.Trigger, "BI_AGENDA", Owner: "TUR_AGENDA")));
        Assert.True(SchemaAreas.Contains(turnos, new DbObject(DbObjectKind.Index, "IDX_FECHA", Owner: "TUR_AGENDA")));
        // La excepción de la tabla deja afuera también a sus triggers.
        Assert.False(SchemaAreas.Contains(turnos, new DbObject(DbObjectKind.Trigger, "TUR_LOG_BI", Owner: "TUR_LOG")));
        Assert.False(SchemaAreas.Contains(turnos, new DbObject(DbObjectKind.Trigger, "AUDITORIA", Owner: null)));
    }

    [Fact]
    public void Propone_areas_por_prefijo_con_las_tablas_sin_area()
    {
        var tables = new[] { "TUR_A", "TUR_B", "TUR_C", "TUR_D", "FAC_A", "FAC_B", "FAC_C", "PAC_A", "PAC_B", "X_UNO", "X_DOS", "X_TRES", "SUELTA" };
        var existing = new[] { Area("Facturación", ["FAC_*"]) };

        var suggestions = SchemaAreas.SuggestByPrefix(tables, existing);

        // FAC ya tiene área; PAC junta solo 2; «X» es un prefijo demasiado corto; SUELTA no tiene _.
        var s = Assert.Single(suggestions);
        Assert.Equal(("TUR", 4), (s.Name, s.Count));
        Assert.Equal(["TUR_*"], s.Rules);
        Assert.Empty(SchemaAreas.SuggestByPrefix(tables, [Area("TUR", ["NADA"])], minCount: 4));
    }

    [Fact]
    public void Los_prefijos_que_empiezan_con_otro_se_proponen_juntos()
    {
        var tables = new[] { "CLI_A", "CLI_B", "CLIENTES_A", "CLIENTES_B", "PROV_X", "PROVEEDORES_A", "PROVEEDORES_B",
                             "TUR_A", "TURNOS_A", "TURNOS_B", "MEDICO_X", "MEDICOS_A", "LAB_A" };

        var suggestions = SchemaAreas.SuggestByPrefix(tables, []);

        // Cada familia lleva el nombre del más corto; MEDICO_ suelto (1) junta con MEDICOS_ (1): no llegan a 3. LAB_ solo, tampoco.
        Assert.Equal(["CLI", "PROV", "TUR"], suggestions.Select(x => x.Name));
        Assert.Equal(["CLI_*", "CLIENTES_*"], suggestions[0].Rules);
        Assert.Equal(4, suggestions[0].Count);
        Assert.Equal("CLI_, CLIENTES_", suggestions[0].Prefixes);
        Assert.Equal(["PROV_*", "PROVEEDORES_*"], suggestions[1].Rules);
        Assert.Equal(["TUR_*", "TURNOS_*"], suggestions[2].Rules);
    }

    private static DbObject P(string name) => new(DbObjectKind.Procedure, name);

    [Fact]
    public void Suma_lo_que_usa_solo_sus_tablas_si_el_area_lo_pide()
    {
        var objects = new[]
        {
            T("LAB_MUESTRAS"), T("LAB_ANALISIS"), T("USUARIOS"),
            P("SP_GUARDAR_MUESTRA"),                                   // usa solo LAB_MUESTRAS
            P("SP_MUESTRA_USUARIO"),                                    // usa LAB_ y USUARIOS: no
            P("SP_SIN_TABLAS"),                                        // no usa tablas: no
            new DbObject(DbObjectKind.View, "V_LAB"),                // vista de tablas LAB_
            P("SP_DESDE_VISTA"),                                      // usa la vista: entra porque la vista entró
            new DbObject(DbObjectKind.Trigger, "AUDIT_LAB", Owner: "USUARIOS"), // usa LAB_ pero es de USUARIOS: no
            P("SP_EXCLUIDO"),
        };
        var uses = new TableUses([
            (DbObjectKind.Procedure, "SP_GUARDAR_MUESTRA", "LAB_MUESTRAS"),
            (DbObjectKind.Procedure, "SP_MUESTRA_USUARIO", "LAB_ANALISIS"), (DbObjectKind.Procedure, "SP_MUESTRA_USUARIO", "USUARIOS"),
            (DbObjectKind.View, "V_LAB", "LAB_MUESTRAS"), (DbObjectKind.View, "V_LAB", "LAB_ANALISIS"),
            (DbObjectKind.Procedure, "SP_DESDE_VISTA", "V_LAB"),
            (DbObjectKind.Trigger, "AUDIT_LAB", "LAB_ANALISIS"),
            (DbObjectKind.Procedure, "SP_EXCLUIDO", "LAB_ANALISIS"),
        ]);
        var lab = Area("Laboratorio", ["LAB_*"], "SP_EXCLUIDO") with { IncludeDependents = true };
        var soloReglas = lab with { Name = "Solo reglas", IncludeDependents = false };

        var membership = new AreaMembership([lab, soloReglas], objects, uses);

        Assert.Equal(["LAB_MUESTRAS", "LAB_ANALISIS", "SP_GUARDAR_MUESTRA", "V_LAB", "SP_DESDE_VISTA"], membership.Members(0).Select(o => o.Name));
        Assert.True(membership.IsByUse(0, P("SP_GUARDAR_MUESTRA")));
        Assert.False(membership.IsByUse(0, T("LAB_ANALISIS")));
        Assert.Equal(["LAB_MUESTRAS", "LAB_ANALISIS"], membership.Members(1).Select(o => o.Name));
        Assert.Equal(["Laboratorio"], membership.AreasOf(P("SP_DESDE_VISTA")));
        Assert.Equal(["USUARIOS", "SP_MUESTRA_USUARIO", "SP_SIN_TABLAS", "AUDIT_LAB", "SP_EXCLUIDO"], membership.Unassigned.Select(o => o.Name));

        // Recalcular reusando lo anterior da lo mismo (cambia solo el área tocada).
        var narrower = lab with { Rules = ["LAB_MUESTRAS"] };
        var again = new AreaMembership([narrower, soloReglas], objects, uses, membership);
        Assert.Equal(["LAB_MUESTRAS", "SP_GUARDAR_MUESTRA"], again.Members(0).Select(o => o.Name));
        Assert.Equal(membership.Members(1), again.Members(1));
        Assert.Equal(new AreaMembership([narrower, soloReglas], objects, uses).Unassigned, again.Unassigned);

        // Sacar a mano lo que entró por lo que usa agrega una excepción.
        Assert.Equal(["SP_EXCLUIDO", "SP_GUARDAR_MUESTRA"], SchemaAreas.Remove(lab, "SP_GUARDAR_MUESTRA", byUse: true).Exclude);
    }

    [Fact]
    public void Reglas_escritas_una_por_renglon_o_con_comas()
    {
        Assert.Equal(["TUR_*", "BASE_TURNOS", "pac_*"], SchemaAreas.ParseRules("TUR_*\r\n  BASE_TURNOS , pac_*;\n\ntur_*"));
        Assert.Empty(SchemaAreas.ParseRules(null));
    }

    [Fact]
    public void Agregar_y_quitar_a_mano()
    {
        var area = Area("Turnos", ["TUR_*"]);

        var withMaster = SchemaAreas.Add(area, "BASE_TURNOS");
        Assert.Equal(["TUR_*", "BASE_TURNOS"], withMaster.Rules);

        // Lo toma un patrón: se saca con una excepción, y al volver a agregarlo se quita la excepción.
        var withoutLog = SchemaAreas.Remove(withMaster, "TUR_LOG");
        Assert.Equal(["TUR_LOG"], withoutLog.Exclude);
        Assert.False(SchemaAreas.Contains(withoutLog, T("TUR_LOG")));
        Assert.Empty(SchemaAreas.Add(withoutLog, "tur_log").Exclude);

        // Estaba por su nombre: se quita la regla.
        Assert.Equal(["TUR_*"], SchemaAreas.Remove(withMaster, "BASE_TURNOS").Rules);
    }
}
