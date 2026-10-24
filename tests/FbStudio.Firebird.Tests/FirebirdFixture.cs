using System.Threading.Channels;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;
using Testcontainers.FirebirdSql;

namespace FbStudio.Firebird.Tests;

/// <summary>
/// Firebird 3.0 en Docker con una tabla de turnos de 50.000 filas.
/// Los tests NUNCA tocan servidores reales.
/// </summary>
public sealed class FirebirdFixture : IAsyncLifetime
{
    public const int Rows = 50_000;

    private readonly FirebirdSqlContainer _container = new FirebirdSqlBuilder("jacobalberty/firebird:v3.0").Build();

    public ConnectionProfile Profile { get; private set; } = null!;
    public string Password { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var cs = new FbConnectionStringBuilder(_container.GetConnectionString());
        Profile = new ConnectionProfile
        {
            Name = "docker-fb3",
            Host = cs.DataSource,
            Port = cs.Port,
            Database = cs.Database,
            User = cs.UserID,
            Charset = "ISO8859_1",
            Environment = ProfileEnvironment.Desarrollo,
        };
        Password = cs.Password;

        await using var c = FbConnectionFactory.Create(Profile, Password, "seed");
        await c.OpenAsync();
        foreach (var sql in new[]
        {
            "CREATE DOMAIN D_IMPORTE AS NUMERIC(12,2)",
            "CREATE TABLE TURNOS (ID INTEGER NOT NULL PRIMARY KEY, FECHA TIMESTAMP, PACIENTE VARCHAR(60), OBS VARCHAR(2000), IMPORTE D_IMPORTE)",
            "CREATE INDEX TURNOS_FECHA ON TURNOS (FECHA)",
            "CREATE INDEX TURNOS_PACIENTE ON TURNOS (PACIENTE)",
            "ALTER INDEX TURNOS_PACIENTE INACTIVE",
            "COMMENT ON COLUMN TURNOS.PACIENTE IS '#FK_%PACIENTES,%CODIGO,%NOMBRE#'",
            "CREATE VIEW V_TURNOS AS SELECT ID, FECHA FROM TURNOS",
            "CREATE GENERATOR G_TURNOS",
            "CREATE EXCEPTION E_TURNO 'Turno inválido'",
            "CREATE PROCEDURE P_CONTAR RETURNS (N INTEGER) AS BEGIN SELECT COUNT(*) FROM TURNOS INTO :N; SUSPEND; END",
            "CREATE TRIGGER TURNOS_BI FOR TURNOS ACTIVE BEFORE INSERT POSITION 0 AS BEGIN END",
            "CREATE ROLE R_LECTURA",
            // Objetos para probar el DDL: dominio con CHECK, FK, CHECK, columna calculada, charset/collation propios,
            // procedimiento con parámetros, función PSQL, paquete y trigger de varias acciones.
            "CREATE DOMAIN D_CANTIDAD AS SMALLINT DEFAULT 1 NOT NULL CHECK (VALUE > 0)",
            """
            CREATE TABLE TURNOS_DET (
              ID INTEGER NOT NULL, TURNO INTEGER NOT NULL, CANTIDAD D_CANTIDAD, TOTAL COMPUTED BY (CANTIDAD * 2),
              NOTA VARCHAR(30) CHARACTER SET ISO8859_1 DEFAULT '-' COLLATE ES_ES, -- la base de Docker es UTF8 por defecto
              CONSTRAINT PK_TURNOS_DET PRIMARY KEY (ID),
              CONSTRAINT FK_DET_TURNO FOREIGN KEY (TURNO) REFERENCES TURNOS (ID) ON DELETE CASCADE,
              CONSTRAINT CHK_NOTA CHECK (NOTA <> ''))
            """,
            "COMMENT ON TABLE TURNOS IS 'Turnos de prueba'",
            """
            CREATE PROCEDURE P_TURNOS_DESDE (DESDE INTEGER, HASTA INTEGER = 10)
            RETURNS (ID INTEGER, FECHA TIMESTAMP)
            AS
            DECLARE VARIABLE N INTEGER;
            BEGIN
              /* Devuelve los turnos del rango (año, señal) */
              FOR SELECT ID, FECHA FROM TURNOS WHERE ID BETWEEN :DESDE AND :HASTA INTO :ID, :FECHA DO
                SUSPEND;
            END
            """,
            "COMMENT ON PROCEDURE P_TURNOS_DESDE IS 'Turnos entre dos ID'",
            "CREATE FUNCTION F_DOBLE (X INTEGER) RETURNS INTEGER DETERMINISTIC AS BEGIN RETURN X * 2; END",
            "CREATE PACKAGE PKG_TURNOS AS BEGIN FUNCTION CANTIDAD RETURNS INTEGER; END",
            "CREATE PACKAGE BODY PKG_TURNOS AS BEGIN FUNCTION CANTIDAD RETURNS INTEGER AS BEGIN RETURN (SELECT COUNT(*) FROM TURNOS); END END",
            "CREATE TRIGGER TURNOS_DET_BIU FOR TURNOS_DET ACTIVE BEFORE INSERT OR UPDATE POSITION 5 AS BEGIN NEW.NOTA = UPPER(NEW.NOTA); END",
            $"""
            EXECUTE BLOCK AS
            DECLARE I INTEGER = 0;
            BEGIN
              WHILE (I < {Rows}) DO
              BEGIN
                INSERT INTO TURNOS VALUES (:I, DATEADD(:I MINUTE TO TIMESTAMP '2026-01-01 00:00'),
                  'PACIENTE ' || :I, LPAD('', MOD(:I, 500), 'x'), :I / 100.00); -- 0,01 por fila (con 100.0 quedaba un solo decimal)
                I = I + 1;
              END
            END
            """,
        })
        {
            await using var tx = await c.BeginTransactionAsync();
            await using var cmd = new FbCommand(sql, c, tx);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public QuerySession NewSession(bool readOnly = false) => new(Profile with { ReadOnly = readOnly }, Password);

    /// <summary>Ejecuta y junta todas las filas (simula la grilla).</summary>
    public static async Task<(QueryOutcome Outcome, List<object[]> Rows)> RunAsync(
        QuerySession session, string sql, int maxRows = 100_000, CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<object[][]>(64);
        var rows = new List<object[]>();
        var consumer = Task.Run(async () => { await foreach (var b in channel.Reader.ReadAllAsync()) rows.AddRange(b); });
        var outcome = await session.ExecuteAsync(sql, StatementClassifier.Classify(sql), new ExecuteOptions(maxRows), _ => { }, channel.Writer, ct);
        await consumer;
        return (outcome, rows);
    }
}

[CollectionDefinition(nameof(FirebirdCollection))]
public sealed class FirebirdCollection : ICollectionFixture<FirebirdFixture>;
