using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

/// <summary>La documentación de la base en HTML.</summary>
public class SchemaDocumentationTests
{
    private static readonly DatabaseSchema Schema = new()
    {
        Tables =
        [
            new TableDefinition("TURNOS",
                [
                    new FieldDefinition("ID", "INTEGER", NotNull: true),
                    new FieldDefinition("PACIENTE", "INTEGER", Description: "#Required# #FK_%PACIENTES,%CODIGO,%NOMBRE,I#"),
                    new FieldDefinition("OBS", "VARCHAR(60)", Default: "DEFAULT '-'", Description: "Observación <libre> & \"corta\""),
                ],
                [new ConstraintDefinition("PK_TURNOS", ConstraintKind.PrimaryKey, ["ID"])],
                [new IndexDefinition("TURNOS_OBS", "TURNOS", ["OBS"], Inactive: true)],
                [new TriggerDefinition("TURNOS_BI", "TURNOS", 1, 0, false, "AS BEGIN NEW.OBS = 'x'; END", "Pone la observación")],
                "Turnos de los pacientes"),
            new TableDefinition("TURNOS_DET",
                [new FieldDefinition("ID", "INTEGER", NotNull: true), new FieldDefinition("TURNO", "INTEGER", NotNull: true)],
                [new ConstraintDefinition("FK_DET_TURNO", ConstraintKind.ForeignKey, ["TURNO"], ReferencedTable: "TURNOS", ReferencedColumns: ["ID"], OnDelete: "CASCADE")],
                [], []),
            new TableDefinition("PACIENTES", [new FieldDefinition("CODIGO", "INTEGER", NotNull: true)], [], [], []),
        ],
        Procedures = [new ProcedureDefinition("P_CONTAR", [new FieldDefinition("DESDE", "DATE")], [new FieldDefinition("N", "INTEGER")], "AS BEGIN SUSPEND; END", "Cuenta")],
        Exceptions = [new ExceptionDefinition("E_TURNO", "Turno inválido")],
        Area = "Turnos",
    };

    [Fact]
    public void Tablas_con_claves_FK_enlazadas_y_quien_las_referencia()
    {
        var html = SchemaDocumentation.Html(Schema, "Base de pruebas", new DateTime(2026, 10, 24, 18, 30, 0), includeSources: false);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>Documentación de Base de pruebas</title>", html);
        Assert.Contains("Generada el 2026-10-24 18:30 con FbStudio · Área: Turnos", html);
        // Índice y secciones con ancla; los textos, escapados.
        Assert.Contains("<li><a href=\"#t-TURNOS_DET\">TURNOS_DET</a></li>", html);
        Assert.Contains("<section id=\"t-TURNOS\" data-name=\"turnos\">", html);
        Assert.Contains("<p class=\"desc\">Turnos de los pacientes</p>", html);
        Assert.Contains("Observación &lt;libre&gt; &amp; &quot;corta&quot;", html);
        Assert.DoesNotContain("<libre>", html);
        // PK, FK de verdad y lógica (del comentario), enlazadas; quién referencia a TURNOS y a PACIENTES.
        Assert.Contains("<code>ID</code> <span class=\"pk\">PK</span>", html);
        Assert.Contains("<a class=\"fk\" href=\"#t-TURNOS\">FK → TURNOS</a>", html);
        Assert.Contains("<a class=\"fk logical\" href=\"#t-PACIENTES\"", html);
        Assert.Contains("FOREIGN KEY (TURNO) → <a href=\"#t-TURNOS\">TURNOS</a> (ID) ON DELETE CASCADE", html);
        Assert.Contains("<a href=\"#t-TURNOS_DET\">TURNOS_DET</a> <small>(TURNO)</small>", html);
        Assert.Contains("<a href=\"#t-TURNOS\">TURNOS</a> <small>(PACIENTE, FK lógica (del comentario))</small>", html);
        // Índices, triggers, procedimientos y excepciones; sin el código si no se pide.
        Assert.Contains("<code>TURNOS_OBS</code> (OBS) <small>inactivo</small>", html);
        Assert.Contains("<code>TURNOS_BI</code> BEFORE INSERT POSITION 0 — Pone la observación", html);
        Assert.Contains("<section id=\"p-P_CONTAR\"", html);
        Assert.Contains("Mensaje: <q>Turno inválido</q>", html);
        Assert.DoesNotContain("SUSPEND", html);
        Assert.Contains("SUSPEND", SchemaDocumentation.Html(Schema, "Base de pruebas", DateTime.Now, includeSources: true));
    }
}
