using System.Text;
using System.Text.RegularExpressions;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird;

/// <param name="ThresholdMs">Solo lo que tarde al menos esto (0 = todo). En producción conviene no bajar de 100 ms.</param>
/// <param name="Procedures">También procedimientos y funciones.</param>
/// <param name="Triggers">También triggers (en una base con muchos, multiplica los eventos).</param>
/// <param name="MaxSqlLength">El SQL se corta en este largo.</param>
/// <param name="IncludeFilter">Solo las sentencias cuyo texto coincida (SIMILAR TO de Firebird, p. ej. «%AGENDA_TURNOS%»).</param>
public sealed record TraceOptions(int ThresholdMs = 100, bool Procedures = true, bool Triggers = false, bool Errors = true,
    int MaxSqlLength = 4000, string? IncludeFilter = null);

/// <summary>
/// Una sesión de traza de Firebird por la API de servicios (F-24): mientras dura, el servidor manda lo que pasa en la
/// base (sentencias que terminaron, con su SQL, plan y rendimiento; procedimientos; errores) y se lo pasa renglón por
/// renglón a <c>onLine</c>, con la ruta de la base cambiada por cómo la nombra el registro (D-54).
/// <list type="bullet">
/// <item>Firebird 3 compara el nombre con que se conectó cada programa: se configura una sección con el nombre del
/// registro (el alias) y otra con la ruta del servidor, para no perder a los que entran de la otra forma.</item>
/// <item>La conexión de servicio va sin rol: Firebird 3 se lo pasa al administrador de trazas como «-role», que no lo
/// conoce, y rechaza la sesión («unknown switch "-role" encountered», visto en un servidor real). Sin SYSDBA puede que se
/// vean solo las conexiones del propio usuario.</item>
/// <item>Si FbStudio se cierra o se corta la red, el servidor termina la sesión solo (vive con la conexión de servicio).</item>
/// </list>
/// </summary>
public sealed partial class TraceSession(ConnectionProfile profile, string password) : IAsyncDisposable
{
    private Task? _run;
    private readonly TaskCompletionSource<int> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConnectionProfile Profile { get; } = profile;

    /// <summary>El número de la sesión en el servidor (para detenerla).</summary>
    public int? SessionId { get; private set; }

    public bool IsRunning => _run is { IsCompleted: false };

    /// <summary>
    /// Termina cuando la sesión deja de correr, por lo que sea (la detuvo el usuario, la cortó el servidor, se cayó la
    /// conexión), con el motivo si se sabe: el error o el último mensaje del servidor.
    /// </summary>
    public Task<string?> Ended => _ended.Task;

    /// <summary>El último mensaje propio del servidor («Trace session ID 5 stopped», «Error creating trace session…»).</summary>
    public string? LastServerMessage { get; private set; }

    /// <summary>
    /// Patrón para la sección «database» de la configuración: cada letra sin distinguir mayúsculas ([Rr][Aa]…) y lo que no
    /// es letra ni número como «_» (cualquier carácter), para no tener que escapar «\», «.» ni «:» del SIMILAR TO.
    /// </summary>
    public static string NamePattern(string name)
    {
        var b = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsAsciiLetter(c)) b.Append('[').Append(char.ToUpperInvariant(c)).Append(char.ToLowerInvariant(c)).Append(']');
            else if (char.IsAsciiDigit(c)) b.Append(c);
            else b.Append('_');
        }
        return b.ToString();
    }

    /// <summary>Arranca la sesión y vuelve cuando el servidor la confirmó (o con el error si no pudo).</summary>
    public async Task StartAsync(TraceOptions options, Action<string> onLine, CancellationToken ct = default)
    {
        if (_run is not null) throw new InvalidOperationException();
        var serverPath = (await DatabaseServices.FileInfoAsync(Profile, password, ct)).Path;
        var trace = new FbTrace(FbTraceVersion.Detect, ServiceConnectionString());
        var events = FbDatabaseTraceEvents.StatementFinish | FbDatabaseTraceEvents.PrintPlan | FbDatabaseTraceEvents.PrintPerf;
        if (options.Procedures) events |= FbDatabaseTraceEvents.ProcedureFinish | FbDatabaseTraceEvents.FunctionFinish;
        if (options.Triggers) events |= FbDatabaseTraceEvents.TriggerFinish;
        if (options.Errors) events |= FbDatabaseTraceEvents.Errors;
        foreach (var name in new[] { Profile.Database, serverPath }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            trace.DatabasesConfigurations.Add(new FbDatabaseTraceConfiguration
            {
                DatabaseName = NamePattern(name),
                Enabled = true,
                Events = events,
                TimeThreshold = TimeSpan.FromMilliseconds(Math.Max(0, options.ThresholdMs)),
                MaxSQLLength = options.MaxSqlLength,
                IncludeFilter = string.IsNullOrWhiteSpace(options.IncludeFilter) ? null : options.IncludeFilter.Trim(),
            });
        }
        trace.ServiceOutput += (_, e) =>
        {
            foreach (var raw in (e.Message ?? "").Replace("\r", "").Split('\n'))
            {
                if (SessionId is null && SessionStarted().Match(raw) is { Success: true } m)
                {
                    SessionId = int.Parse(m.Groups[1].Value);
                    _started.TrySetResult(SessionId.Value);
                }
                var line = ServerPaths.Hide(raw, serverPath, Profile.Database);
                if (ServerMessage().IsMatch(line) && !SessionStarted().IsMatch(line)) LastServerMessage = line.Trim();
                onLine(line);
            }
        };
        var sessionName = $"FbStudio {Environment.UserName} {DateTime.Now:yyyyMMdd-HHmmss}";
        // StartAsync no vuelve hasta que la sesión termina: corre aparte y se espera solo la confirmación.
        _run = Task.Run(async () =>
        {
            string? error = null;
            try { await trace.StartAsync(sessionName, CancellationToken.None); }
            catch (Exception ex)
            {
                error = ServerPaths.Hide(ex.Message.Split('\n', 2)[0].Trim(), serverPath, Profile.Database);
                _started.TrySetException(ex);
            }
            finally
            {
                _started.TrySetException(new InvalidOperationException(CoreStrings.TraceNotStarted));
                _ended.TrySetResult(error ?? LastServerMessage);
            }
        }, CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _started.Task.WaitAsync(timeout.Token);
    }

    /// <summary>Detiene la sesión en el servidor y espera a que deje de llegar la salida.</summary>
    public async Task StopAsync()
    {
        if (SessionId is { } id)
        {
            var stop = new FbTrace(FbTraceVersion.Detect, ServiceConnectionString());
            try { await stop.StopAsync(id, CancellationToken.None); }
            catch (Exception) { /* ya había terminado */ }
        }
        if (_run is { } run)
        {
            try { await run.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { /* la conexión de servicio se cierra sola al final */ }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>La conexión al servicio de trazas, sin el rol del registro (ver arriba).</summary>
    private string ServiceConnectionString() => FbConnectionFactory.ConnectionString(Profile with { Role = null }, password, "trace");

    [GeneratedRegex(@"Trace session ID (\d+) started")]
    private static partial Regex SessionStarted();

    /// <summary>Lo que dice el propio servidor de la sesión (no los eventos: esos empiezan con la hora o con tabulador).</summary>
    [GeneratedRegex(@"^(Trace session|Error|Can ?not|Unable|Session)", RegexOptions.IgnoreCase)]
    private static partial Regex ServerMessage();
}
