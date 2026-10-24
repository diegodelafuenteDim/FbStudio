using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;

namespace FbStudio.Firebird.Tests;

/// <summary>DDL armado desde RDB$ contra los objetos del seed (Firebird 3 en Docker).</summary>
[Collection(nameof(FirebirdCollection))]
public class DdlScriptTests(FirebirdFixture fb)
{
    private async Task<ObjectScript> Script(DbObjectKind kind, string name)
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var script = await ex.ScriptAsync(new DbObject(kind, name));
        return script with { Ddl = script.Ddl.ReplaceLineEndings("\n") };
    }

    [Fact]
    public async Task Tabla_con_PK_indices_comentarios_y_trigger()
    {
        var s = await Script(DbObjectKind.Table, "TURNOS");

        Assert.Equal("Turnos de prueba", s.Description);
        Assert.Contains("CREATE TABLE TURNOS (\n    ID       INTEGER NOT NULL,\n    FECHA    TIMESTAMP,\n    PACIENTE VARCHAR(60),", s.Ddl);
        Assert.Contains("    IMPORTE  D_IMPORTE\n);", s.Ddl);
        Assert.Contains("ALTER TABLE TURNOS ADD PRIMARY KEY (ID);", s.Ddl);  // nombre INTEG_n: no se repite
        Assert.Contains("CREATE INDEX TURNOS_FECHA ON TURNOS (FECHA);", s.Ddl);
        Assert.Contains("CREATE INDEX TURNOS_PACIENTE ON TURNOS (PACIENTE);\nALTER INDEX TURNOS_PACIENTE INACTIVE;", s.Ddl);
        Assert.Contains("COMMENT ON TABLE TURNOS IS 'Turnos de prueba';", s.Ddl);
        Assert.Contains("COMMENT ON COLUMN TURNOS.PACIENTE IS '#FK_%PACIENTES,%CODIGO,%NOMBRE#';", s.Ddl);
        Assert.Contains("SET TERM ^ ;\n\nCREATE OR ALTER TRIGGER TURNOS_BI FOR TURNOS\nACTIVE BEFORE INSERT POSITION 0\nAS BEGIN END^", s.Ddl);
    }

    [Fact]
    public async Task Tabla_con_FK_CHECK_columna_calculada_dominio_y_collation()
    {
        var s = await Script(DbObjectKind.Table, "TURNOS_DET");

        Assert.Contains("    CANTIDAD D_CANTIDAD,", s.Ddl);
        Assert.Contains("    TOTAL    COMPUTED BY (CANTIDAD * 2),", s.Ddl);
        Assert.Contains("    NOTA     VARCHAR(30) CHARACTER SET ISO8859_1 DEFAULT '-' COLLATE ES_ES", s.Ddl);
        Assert.Contains("ALTER TABLE TURNOS_DET ADD CONSTRAINT PK_TURNOS_DET PRIMARY KEY (ID);", s.Ddl);
        Assert.Contains("ALTER TABLE TURNOS_DET ADD CONSTRAINT FK_DET_TURNO FOREIGN KEY (TURNO) REFERENCES TURNOS (ID) ON DELETE CASCADE;", s.Ddl);
        Assert.Contains("ALTER TABLE TURNOS_DET ADD CONSTRAINT CHK_NOTA CHECK (NOTA <> '');", s.Ddl);
        Assert.Contains("CREATE OR ALTER TRIGGER TURNOS_DET_BIU FOR TURNOS_DET\nACTIVE BEFORE INSERT OR UPDATE POSITION 5", s.Ddl);
        Assert.DoesNotContain("CREATE INDEX", s.Ddl); // los índices de PK y FK salen con sus constraints
    }

    [Fact]
    public async Task Procedimiento_con_parametros_default_y_acentos_es_una_sentencia()
    {
        var s = await Script(DbObjectKind.Procedure, "P_TURNOS_DESDE");

        Assert.Equal("Turnos entre dos ID", s.Description);
        Assert.StartsWith("CREATE OR ALTER PROCEDURE P_TURNOS_DESDE (\n    DESDE INTEGER,\n    HASTA INTEGER = 10)\nRETURNS (\n    ID    INTEGER,\n    FECHA TIMESTAMP)\nAS\nDECLARE VARIABLE N INTEGER;", s.Ddl);
        Assert.Contains("(año, señal)", s.Ddl);
        Assert.EndsWith("END", s.Ddl);
        Assert.Single(SqlScriptSplitter.Split(s.Ddl));
    }

    [Fact]
    public async Task Vista_trigger_funcion_y_paquete()
    {
        Assert.Equal("CREATE OR ALTER VIEW V_TURNOS (\n    ID,\n    FECHA)\nAS\nSELECT ID, FECHA FROM TURNOS", (await Script(DbObjectKind.View, "V_TURNOS")).Ddl);
        Assert.Equal("CREATE OR ALTER TRIGGER TURNOS_BI FOR TURNOS\nACTIVE BEFORE INSERT POSITION 0\nAS BEGIN END", (await Script(DbObjectKind.Trigger, "TURNOS_BI")).Ddl);
        Assert.Equal("CREATE OR ALTER FUNCTION F_DOBLE (\n    X INTEGER)\nRETURNS INTEGER DETERMINISTIC\nAS\nBEGIN RETURN X * 2; END",
            (await Script(DbObjectKind.Function, "F_DOBLE")).Ddl);

        var package = (await Script(DbObjectKind.Package, "PKG_TURNOS")).Ddl;
        Assert.Contains("CREATE OR ALTER PACKAGE PKG_TURNOS\nAS\nBEGIN FUNCTION CANTIDAD RETURNS INTEGER; END^", package);
        Assert.Contains("RECREATE PACKAGE BODY PKG_TURNOS\nAS\nBEGIN FUNCTION CANTIDAD RETURNS INTEGER AS BEGIN RETURN (SELECT COUNT(*) FROM TURNOS); END END^", package);
        Assert.Equal(2, SqlScriptSplitter.Split(package).Count);
    }

    [Fact]
    public async Task Generador_excepcion_dominio_indices_y_rol()
    {
        Assert.StartsWith("CREATE SEQUENCE G_TURNOS;\n-- Valor actual: 0", (await Script(DbObjectKind.Generator, "G_TURNOS")).Ddl);
        Assert.Equal("CREATE OR ALTER EXCEPTION E_TURNO 'Turno inválido';\n", (await Script(DbObjectKind.Exception, "E_TURNO")).Ddl);
        Assert.Equal("CREATE DOMAIN D_CANTIDAD AS SMALLINT\n    DEFAULT 1\n    NOT NULL\n    CHECK (VALUE > 0);\n", (await Script(DbObjectKind.Domain, "D_CANTIDAD")).Ddl);
        Assert.Equal("CREATE INDEX TURNOS_PACIENTE ON TURNOS (PACIENTE);\nALTER INDEX TURNOS_PACIENTE INACTIVE;\n", (await Script(DbObjectKind.Index, "TURNOS_PACIENTE")).Ddl);

        var pkIndex = (await Script(DbObjectKind.Index, "PK_TURNOS_DET")).Ddl;
        Assert.Contains("respalda la constraint PK_TURNOS_DET", pkIndex);
        Assert.Contains("ALTER TABLE TURNOS_DET ADD CONSTRAINT PK_TURNOS_DET PRIMARY KEY (ID);", pkIndex);

        Assert.StartsWith("CREATE ROLE R_LECTURA;\n-- Dueño: ", (await Script(DbObjectKind.Role, "R_LECTURA")).Ddl);
    }

    [Fact]
    public async Task Objeto_inexistente_da_un_error_claro()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Script(DbObjectKind.Procedure, "NO_EXISTE"));
        Assert.Contains("NO_EXISTE", error.Message);
    }
}
