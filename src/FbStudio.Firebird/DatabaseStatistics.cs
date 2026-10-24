using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Statistics;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird;

/// <summary>Qué estadísticas pedir: solo el encabezado (gstat -h) o además las páginas de datos e índices (gstat -a).</summary>
public enum StatisticsScope
{
    Header,
    Full,
}

/// <summary>Lo que devolvió el servidor: el texto completo, el encabezado ya leído y cuánto tardó.</summary>
public sealed record StatisticsResult(IReadOnlyList<string> Lines, DatabaseHeader Header, TimeSpan Elapsed);

/// <summary>
/// Estadísticas de la base por la API de servicios (lo mismo que gstat, sin instalarlo). El encabezado lee una sola
/// página; la completa recorre todas las páginas de datos e índices (en una base grande tarda y carga el disco), o solo
/// las de las tablas elegidas (gstat -t). No abre transacciones: no afecta la OAT.
/// </summary>
public static class DatabaseStatistics
{
    /// <param name="tables">Con la completa, solo estas tablas (y sus índices); null o vacía, todas.</param>
    public static async Task<StatisticsResult> ReadAsync(ConnectionProfile profile, string password, StatisticsScope scope,
        Action<string>? onLine = null, CancellationToken ct = default, IReadOnlyList<string>? tables = null)
    {
        var lines = new List<string>();
        var service = new FbStatistical(FbConnectionFactory.ConnectionString(profile, password, "estadísticas"))
        {
            Options = scope == StatisticsScope.Header
                ? FbStatisticalFlags.HeaderPages
                : FbStatisticalFlags.DataPages | FbStatisticalFlags.IndexPages | FbStatisticalFlags.RecordVersionStatistics,
        };
        // Las primeras líneas dicen dónde está la base («Database "/db/clinica.fdb"»): desde ahí, la ruta se cambia por
        // la base como la nombra el registro, también en lo que se va avisando (D-54).
        string? path = null;
        service.ServiceOutput += (_, e) =>
        {
            foreach (var raw in (e.Message ?? "").Replace("\r", "").Split('\n'))
            {
                path ??= DatabaseHeader.Parse([raw]).Database;
                var line = ServerPaths.Hide(raw, path, profile.Database);
                lines.Add(line);
                onLine?.Invoke(line);
            }
        };
        var clock = Stopwatch.StartNew();
        if (scope == StatisticsScope.Full && tables is { Count: > 0 }) await TablesRequest.ExecuteAsync(service, tables, ct);
        else await service.ExecuteAsync(ct);
        return new StatisticsResult(lines, DatabaseHeader.Parse(lines), clock.Elapsed);
    }

    /// <summary>
    /// gstat -t por la API de servicios. Firebird lo acepta (isc_spb_sts_table, una vez por tabla) pero FirebirdClient no
    /// tiene cómo pedirlo: se arma el mismo pedido que <see cref="FbStatistical.ExecuteAsync"/> con sus piezas internas
    /// más las tablas. Si una versión nueva de FirebirdClient las cambia, falla con un aviso claro (no recorre la base
    /// entera sin decirlo) y lo detecta la prueba en Docker.
    /// </summary>
    private static class TablesRequest
    {
        private const int ActionDbStats = 11;   // isc_action_svc_db_stats
        private const int SpbDbName = 106;      // isc_spb_dbname
        private const int SpbOptions = 108;     // isc_spb_options
        private const int SpbStsTable = 64;     // isc_spb_sts_table

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static async Task ExecuteAsync(FbStatistical service, IReadOnlyList<string> tables, CancellationToken ct)
        {
            MethodInfo open, start, process, close;
            Func<object> spb, output;
            try
            {
                var asm = typeof(FbService).Assembly;
                var spbBase = asm.GetType("FirebirdSql.Data.Common.ServiceParameterBufferBase", throwOnError: true)!;
                var bufferType = asm.GetType("FirebirdSql.Data.Common.ServiceParameterBuffer2", throwOnError: true)!;
                open = Method(typeof(FbService), "OpenAsync", typeof(CancellationToken));
                start = Method(typeof(FbService), "StartTaskAsync", spbBase, typeof(CancellationToken));
                process = Method(typeof(FbService), "ProcessServiceOutputAsync", spbBase, typeof(CancellationToken));
                close = Method(typeof(FbService), "CloseAsync", typeof(CancellationToken));
                var append = Method(bufferType, "Append", typeof(int));
                var appendInt = Method(bufferType, "Append", typeof(int), typeof(int));
                var appendString = Method(bufferType, "Append2", typeof(int), typeof(string));
                var manager = Property(typeof(FbService), "Service");
                var options = Property(typeof(FbService), "ConnectionStringOptions");
                // El administrador de servicios existe recién después de abrir: los búferes se arman entonces.
                object Buffer()
                {
                    var m = manager.GetValue(service) ?? throw new MissingMemberException();
                    return Activator.CreateInstance(bufferType, Property(m.GetType(), "ParameterBufferEncoding").GetValue(m))!;
                }
                spb = () =>
                {
                    var buffer = Buffer();
                    var o = options.GetValue(service) ?? throw new MissingMemberException();
                    append.Invoke(buffer, [ActionDbStats]);
                    appendString.Invoke(buffer, [SpbDbName, (string)Property(o.GetType(), "Database").GetValue(o)!]);
                    appendInt.Invoke(buffer, [SpbOptions, (int)service.Options]);
                    foreach (var table in tables) appendString.Invoke(buffer, [SpbStsTable, table]);
                    return buffer;
                };
                output = Buffer;
            }
            catch (Exception ex) when (IsMissing(ex))
            {
                throw new NotSupportedException(CoreStrings.StatsTablesUnsupported, ex);
            }
            await Invoke(open, service, ct);
            try
            {
                object request, response;
                try
                {
                    request = spb();
                    response = output();
                }
                catch (Exception ex) when (IsMissing(ex))
                {
                    throw new NotSupportedException(CoreStrings.StatsTablesUnsupported, ex);
                }
                await Invoke(start, service, request, ct);
                await Invoke(process, service, response, ct);
            }
            finally
            {
                await Invoke(close, service, CancellationToken.None);
            }
        }

        private static MethodInfo Method(Type type, string name, params Type[] parameters) =>
            type.GetMethod(name, Flags, parameters) ?? throw new MissingMethodException(type.Name, name);

        private static PropertyInfo Property(Type type, string name) =>
            type.GetProperty(name, Flags) ?? throw new MissingMemberException(type.Name, name);

        private static bool IsMissing(Exception ex) =>
            ex is MissingMemberException or TypeLoadException or ArgumentException or InvalidCastException or TargetInvocationException;

        /// <summary>Llama al método interno y espera su tarea; si falla, sale la excepción de Firebird, no la de la reflexión.</summary>
        private static async Task Invoke(MethodInfo method, object target, params object[] args)
        {
            Task task;
            try
            {
                task = (Task)method.Invoke(target, args)!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Throw(inner);
                throw;
            }
            await task;
        }
    }
}
