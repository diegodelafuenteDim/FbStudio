using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class DdlWriterTests
{
    [Fact]
    public void Juego_de_caracteres_e_intercalacion_con_un_nombre_raro_van_entre_comillas() =>
        Assert.Equal("NOTA VARCHAR(10) CHARACTER SET UTF8 COLLATE \"mi_orden\"",
            DdlWriter.Parameter(new FieldDefinition("NOTA", "VARCHAR(10)", CharacterSet: "UTF8", Collation: "mi_orden")));

    [Fact]
    public void Un_comentario_de_una_linea_no_se_corta() =>
        Assert.Equal("-- dueño: A B C", "-- " + SqlIdentifier.OneLine("dueño: A\r\nB\nC"));

    [Theory]
    [InlineData("TURNOS", "TURNOS")]
    [InlineData("CLINICA_TURNOS", "CLINICA_TURNOS")]
    [InlineData("RDB$DATABASE", "RDB$DATABASE")]
    [InlineData("Turnos", "\"Turnos\"")]
    [InlineData("MI TABLA", "\"MI TABLA\"")]
    [InlineData("1A", "\"1A\"")]
    [InlineData("POSITION", "\"POSITION\"")]  // palabra reservada
    [InlineData("VALUE", "\"VALUE\"")]
    [InlineData("AÑO", "\"AÑO\"")]            // sin comillas Firebird solo acepta A-Z
    [InlineData("A\"B", "\"A\"\"B\"")]
    public void Pone_comillas_solo_cuando_hace_falta(string name, string expected) =>
        Assert.Equal(expected, SqlIdentifier.Quote(name));

    [Theory]
    [InlineData(1, "BEFORE INSERT")]
    [InlineData(2, "AFTER INSERT")]
    [InlineData(3, "BEFORE UPDATE")]
    [InlineData(4, "AFTER UPDATE")]
    [InlineData(5, "BEFORE DELETE")]
    [InlineData(6, "AFTER DELETE")]
    [InlineData(17, "BEFORE INSERT OR UPDATE")]
    [InlineData(18, "AFTER INSERT OR UPDATE")]
    [InlineData(25, "BEFORE INSERT OR DELETE")]
    [InlineData(27, "BEFORE UPDATE OR DELETE")]
    [InlineData(113, "BEFORE INSERT OR UPDATE OR DELETE")]
    [InlineData(114, "AFTER INSERT OR UPDATE OR DELETE")]
    [InlineData(8192, "ON CONNECT")]
    [InlineData(8196, "ON TRANSACTION ROLLBACK")]
    public void Decodifica_el_tipo_de_trigger(long type, string expected) =>
        Assert.Equal(expected, TriggerTypeFormatter.Describe(type));

    [Fact]
    public void Tabla_con_constraints_indices_comentarios_y_triggers()
    {
        var table = new TableDefinition(
            "TURNOS_DET",
            [
                new FieldDefinition("ID", "INTEGER", NotNull: true),
                new FieldDefinition("TURNO", "INTEGER", NotNull: true),
                new FieldDefinition("CANTIDAD", "D_CANTIDAD"),
                new FieldDefinition("TOTAL", "BIGINT", Computed: "(CANTIDAD * 2)"),
                new FieldDefinition("NOTA", "VARCHAR(30)", Default: "'-'", CharacterSet: "UTF8", Collation: "UNICODE_CI", Description: "Nota del día"),
            ],
            [
                new ConstraintDefinition("INTEG_5", ConstraintKind.PrimaryKey, ["ID"], IndexName: "RDB$PRIMARY5"),
                new ConstraintDefinition("FK_DET_TURNO", ConstraintKind.ForeignKey, ["TURNO"], IndexName: "IX_DET_TURNO",
                    ReferencedTable: "TURNOS", ReferencedColumns: ["ID"], OnUpdate: "RESTRICT", OnDelete: "CASCADE"),
                new ConstraintDefinition("CHK_NOTA", ConstraintKind.Check, [], CheckSource: "CHECK (NOTA <> '')"),
            ],
            [new IndexDefinition("IX_DET_NOTA", "TURNOS_DET", ["NOTA", "ID"], Descending: true, Inactive: true)],
            [new TriggerDefinition("TURNOS_DET_BIU", "TURNOS_DET", 17, 5, false, "AS\nBEGIN\n  NEW.NOTA = UPPER(NEW.NOTA);\nEND")],
            Description: "Detalle de 'turnos'");

        Assert.Equal("""
            CREATE TABLE TURNOS_DET (
                ID       INTEGER NOT NULL,
                TURNO    INTEGER NOT NULL,
                CANTIDAD D_CANTIDAD,
                TOTAL    COMPUTED BY (CANTIDAD * 2),
                NOTA     VARCHAR(30) CHARACTER SET UTF8 DEFAULT '-' COLLATE UNICODE_CI
            );

            ALTER TABLE TURNOS_DET ADD PRIMARY KEY (ID);

            ALTER TABLE TURNOS_DET ADD CONSTRAINT FK_DET_TURNO FOREIGN KEY (TURNO) REFERENCES TURNOS (ID) ON DELETE CASCADE USING INDEX IX_DET_TURNO;

            ALTER TABLE TURNOS_DET ADD CONSTRAINT CHK_NOTA CHECK (NOTA <> '');

            CREATE DESCENDING INDEX IX_DET_NOTA ON TURNOS_DET (NOTA, ID);
            ALTER INDEX IX_DET_NOTA INACTIVE;

            COMMENT ON TABLE TURNOS_DET IS 'Detalle de ''turnos''';
            COMMENT ON COLUMN TURNOS_DET.NOTA IS 'Nota del día';

            SET TERM ^ ;

            CREATE OR ALTER TRIGGER TURNOS_DET_BIU FOR TURNOS_DET
            ACTIVE BEFORE INSERT OR UPDATE POSITION 5
            AS
            BEGIN
              NEW.NOTA = UPPER(NEW.NOTA);
            END^

            SET TERM ; ^

            """.ReplaceLineEndings("\n"), DdlWriter.Table(table));
    }

    [Fact]
    public void El_script_de_una_tabla_se_puede_partir_en_sentencias()
    {
        var table = new TableDefinition("T", [new FieldDefinition("ID", "INTEGER")], [], [],
            [new TriggerDefinition("T_BI", "T", 1, 0, false, "AS BEGIN NEW.ID = 1; END")]);
        var parts = SqlScriptSplitter.Split(DdlWriter.Table(table)).Select(p => p.Text).ToList();
        Assert.Equal(2, parts.Count);
        Assert.StartsWith("CREATE TABLE T", parts[0]);
        Assert.Equal("CREATE OR ALTER TRIGGER T_BI FOR T\nACTIVE BEFORE INSERT POSITION 0\nAS BEGIN NEW.ID = 1; END", parts[1]);
    }

    [Fact]
    public void Procedimiento_con_parametros_defaults_y_salida_es_una_sentencia_compilable()
    {
        var proc = new ProcedureDefinition("P_TURNOS",
            [
                new FieldDefinition("DESDE", "INTEGER"),
                new FieldDefinition("HASTA", "INTEGER", Default: "= 10"),
                new FieldDefinition("NOMBRE", "TYPE OF COLUMN TURNOS.PACIENTE", Default: "'X'"),
            ],
            [new FieldDefinition("ID", "INTEGER", NotNull: true)],
            "\r\nBEGIN\n  SUSPEND;\nEND\n");
        var ddl = DdlWriter.Procedure(proc);
        Assert.Equal("""
            CREATE OR ALTER PROCEDURE P_TURNOS (
                DESDE  INTEGER,
                HASTA  INTEGER = 10,
                NOMBRE TYPE OF COLUMN TURNOS.PACIENTE = 'X')
            RETURNS (
                ID INTEGER NOT NULL)
            AS
            BEGIN
              SUSPEND;
            END
            """.ReplaceLineEndings("\n"), ddl);
        Assert.Single(SqlScriptSplitter.Split(ddl));
        Assert.Equal(StatementKind.Ddl, StatementClassifier.Classify(ddl).Kind);
    }

    [Fact]
    public void Trigger_de_base_vista_y_funcion()
    {
        Assert.Equal("CREATE OR ALTER TRIGGER T_CONEXION\nINACTIVE ON CONNECT POSITION 0\nAS\nBEGIN END",
            DdlWriter.Trigger(new TriggerDefinition("T_CONEXION", null, 8192, 0, true, "BEGIN END")));

        Assert.Equal("CREATE OR ALTER VIEW V_TURNOS (\n    ID,\n    FECHA)\nAS\nSELECT ID, FECHA FROM TURNOS",
            DdlWriter.View(new ViewDefinition("V_TURNOS", [new("ID", "INTEGER"), new("FECHA", "TIMESTAMP")], "\nSELECT ID, FECHA FROM TURNOS", [])));

        Assert.Equal("CREATE OR ALTER FUNCTION F_DOBLE (\n    X INTEGER)\nRETURNS INTEGER DETERMINISTIC\nAS\nBEGIN RETURN X * 2; END",
            DdlWriter.Function(new FunctionDefinition("F_DOBLE", [new("X", "INTEGER")], new("", "INTEGER"), "BEGIN RETURN X * 2; END", Deterministic: true)));
    }

    [Fact]
    public void UDF_dominio_generador_excepcion_e_indice()
    {
        Assert.Equal("DECLARE EXTERNAL FUNCTION STRLEN\n    CSTRING(32767)\nRETURNS INTEGER BY VALUE\nENTRY_POINT 'IB_UDF_strlen' MODULE_NAME 'ib_udf';\n",
            DdlWriter.Function(new FunctionDefinition("STRLEN", [new("", "CSTRING(32767)")], new("", "INTEGER"), null,
                ModuleName: "ib_udf", EntryPoint: "IB_UDF_strlen", ReturnMechanism: 0)));

        Assert.Equal("CREATE DOMAIN D_CANTIDAD AS SMALLINT\n    DEFAULT 1\n    NOT NULL\n    CHECK (VALUE > 0);\n",
            DdlWriter.Domain(new DomainDefinition(new FieldDefinition("D_CANTIDAD", "SMALLINT", NotNull: true, Default: "DEFAULT 1"), "CHECK (VALUE > 0)")));

        Assert.Equal("CREATE SEQUENCE G_TURNOS;\n-- Valor actual: 50000 (para fijarlo: ALTER SEQUENCE G_TURNOS RESTART WITH 50000;)\n",
            DdlWriter.Generator(new GeneratorDefinition("G_TURNOS", 50_000)));
        Assert.Contains("sin permiso", DdlWriter.Generator(new GeneratorDefinition("G", null)));

        Assert.Equal("CREATE OR ALTER EXCEPTION E_TURNO 'Turno ''inválido''';\n",
            DdlWriter.Exception(new ExceptionDefinition("E_TURNO", "Turno 'inválido'")));

        Assert.Equal("CREATE UNIQUE INDEX IX_UPPER ON T COMPUTED BY (UPPER(NOMBRE));",
            DdlWriter.Index(new IndexDefinition("IX_UPPER", "T", [], Unique: true, Expression: "UPPER(NOMBRE)")));
    }
}
