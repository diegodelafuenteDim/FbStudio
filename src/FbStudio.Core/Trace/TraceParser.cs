using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Trace;

/// <summary>
/// Lee la salida de la traza de Firebird (la del servicio de trace o la de un log de fbtracemgr) y la convierte en
/// eventos (F-24). Cada evento empieza con un renglón «2026-10-21T22:53:08.9620 (32:0x7f…) EXECUTE_STATEMENT_FINISH» y
/// sigue con la conexión, la transacción, el SQL, el plan, los parámetros, el rendimiento y la grilla de tablas, en el
/// formato de Firebird 3 (probado en Docker). Se le pasan los renglones a medida que llegan: devuelve el evento anterior
/// cuando empieza el siguiente.
/// </summary>
public sealed partial class TraceParser
{
    private readonly List<string> _block = [];
    private Match? _header;

    /// <summary>Un renglón más; si empezó un evento nuevo, devuelve el que terminó.</summary>
    public TraceEvent? Feed(string line)
    {
        line = line.TrimEnd('\r');
        var header = Header().Match(line);
        if (!header.Success)
        {
            if (_header is not null) _block.Add(line);
            return null;
        }
        var done = Build();
        _header = header;
        _block.Clear();
        return done;
    }

    /// <summary>El último evento, cuando ya no llegan más renglones.</summary>
    public TraceEvent? Flush()
    {
        var done = Build();
        _header = null;
        _block.Clear();
        return done;
    }

    public static IReadOnlyList<TraceEvent> Parse(IEnumerable<string> lines)
    {
        var parser = new TraceParser();
        var events = new List<TraceEvent>();
        foreach (var line in lines)
            if (parser.Feed(line) is { } e) events.Add(e);
        if (parser.Flush() is { } last) events.Add(last);
        return events;
    }

    private enum Part { None, Sql, Plan, Tables }

    private TraceEvent? Build()
    {
        if (_header is not { } header) return null;
        var kind = header.Groups["kind"].Value.Trim();
        var failed = false;
        foreach (var prefix in new[] { "FAILED ", "UNAUTHORIZED " })
        {
            if (!kind.StartsWith(prefix, StringComparison.Ordinal)) continue;
            failed = true;
            kind = kind[prefix.Length..];
        }
        DateTime.TryParseExact(header.Groups["time"].Value, "yyyy-MM-dd'T'HH:mm:ss.FFFF", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);

        string? database = null, user = null, role = null, protocol = null, address = null, process = null;
        long? attachment = null, processId = null, transaction = null, statement = null, records = null;
        long? ms = null, reads = null, writes = null, fetches = null, marks = null;
        string? objectSql = null;
        var sql = new StringBuilder();
        var plan = new StringBuilder();
        var parameters = new StringBuilder();
        var errors = new List<string>();
        var tables = new List<TraceTableStat>();
        int[]? columnEnds = null;
        var part = Part.None;

        foreach (var raw in _block)
        {
            var line = raw.TrimEnd();
            // Lo que corta el SQL o el plan: los parámetros, las filas, el rendimiento o la grilla de tablas.
            var isParam = ParamLine().IsMatch(line);
            var recordsMatch = RecordsLine().Match(line);
            var perfMatch = PerfLine().Match(line);
            var isTableHeader = TableHeader().IsMatch(line);
            if (part is Part.Sql or Part.Plan && (isParam || recordsMatch.Success || perfMatch.Success || isTableHeader)) part = Part.None;

            if (part == Part.Sql)
            {
                if (Rule('^').IsMatch(line)) part = Part.Plan;
                else sql.AppendLine(line);
                continue;
            }
            if (part == Part.Plan)
            {
                if (line.Length == 0) part = Part.None;
                else plan.AppendLine(line.Trim());
                continue;
            }
            if (part == Part.Tables)
            {
                if (line.Length == 0) { part = Part.None; continue; }
                if (line.StartsWith('*') || columnEnds is null) continue;
                tables.Add(TableRow(line, columnEnds));
                continue;
            }

            if (attachment is null && raw.StartsWith('\t') && !raw.StartsWith("\t\t") && AttachmentLine().Match(raw) is { Success: true } a)
            {
                database = FileName(a.Groups["db"].Value);
                attachment = Num(a.Groups["att"].Value);
                var parts = a.Groups["rest"].Value.Split(", ");
                if (parts.Length > 0)
                {
                    var userRole = parts[0].Split(':', 2);
                    user = userRole[0];
                    role = userRole.Length > 1 && userRole[1] != "NONE" ? userRole[1] : null;
                }
                if (parts.Length > 2)
                {
                    var remote = parts[^1].Split(':', 2);
                    protocol = remote[0];
                    if (remote.Length > 1) address = remote[1].Split('/')[0];
                }
                continue;
            }
            if (attachment is not null && process is null && raw.StartsWith('\t') && !raw.StartsWith("\t\t") && ProcessLine().Match(raw) is { Success: true } p)
            {
                process = FileName(p.Groups["path"].Value);
                processId = Num(p.Groups["pid"].Value);
                continue;
            }
            if (TransactionLine().Match(line) is { Success: true } t)
            {
                transaction = Num(t.Groups["tra"].Value);
                continue;
            }
            if (StatementLine().Match(line) is { Success: true } s)
            {
                statement = Num(s.Groups["id"].Value);
                continue;
            }
            if (Rule('-').IsMatch(line))
            {
                part = Part.Sql;
                continue;
            }
            if (ObjectLine().Match(line) is { Success: true } o)
            {
                objectSql = o.Groups["what"].Value == "Procedure" ? $"EXECUTE PROCEDURE {o.Groups["name"].Value}" : $"FUNCTION {o.Groups["name"].Value}";
                continue;
            }
            if (isParam)
            {
                parameters.AppendLine(line);
                continue;
            }
            if (recordsMatch.Success)
            {
                records = Num(recordsMatch.Groups["n"].Value);
                continue;
            }
            if (perfMatch.Success)
            {
                ms = Num(perfMatch.Groups["ms"].Value);
                reads = Counter(line, "read");
                writes = Counter(line, "write");
                fetches = Counter(line, "fetch");
                marks = Counter(line, "mark");
                continue;
            }
            if (isTableHeader)
            {
                columnEnds = ColumnEnds(line);
                part = Part.Tables;
                continue;
            }
            if (ErrorLine().Match(line) is { Success: true } err)
            {
                errors.Add(err.Groups["msg"].Value.Trim());
                continue;
            }
            if (objectSql is null && kind.Contains("TRIGGER", StringComparison.Ordinal) && TriggerLine().Match(line) is { Success: true } trig)
                objectSql = $"TRIGGER {trig.Groups["name"].Value} ({trig.Groups["when"].Value.Trim()})";
        }

        static string? Text(StringBuilder b) => b.Length == 0 ? null : b.ToString().Trim();
        return new TraceEvent
        {
            Time = time, Kind = kind, Failed = failed, Database = database, Attachment = attachment, User = user, Role = role,
            Protocol = protocol, Address = address, Process = process, ProcessId = processId, Transaction = transaction,
            Statement = statement, Sql = Text(sql) ?? objectSql, Plan = Text(plan), Parameters = Text(parameters), Records = records,
            Milliseconds = ms, Reads = reads, Writes = writes, Fetches = fetches, Marks = marks, Tables = tables,
            Error = errors.Count == 0 ? null : string.Join(" · ", errors),
        };
    }

    /// <summary>«/db/bases/clinica.fdb» → «clinica.fdb»: las carpetas del servidor no se muestran.</summary>
    private static string FileName(string path)
    {
        var trimmed = path.Trim();
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }

    private static long? Counter(string perf, string name) =>
        Regex.Match(perf, $@"(\d+) {name}") is { Success: true } m && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Dónde termina cada columna de números en el encabezado «Table  Natural  Index  Update…».</summary>
    private static int[] ColumnEnds(string header) =>
        Regex.Matches(header, @"\S+").Skip(1).Select(m => m.Index + m.Length).ToArray();

    private static TraceTableStat TableRow(string row, int[] ends)
    {
        long Cell(int i)
        {
            var start = i == 0 ? ends[0] - 10 : ends[i - 1];
            if (start >= row.Length) return 0;
            var text = row[start..Math.Min(ends[i], row.Length)].Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        var name = row[..Math.Min(Math.Max(ends[0] - 10, 0), row.Length)].Trim();
        long C(int i) => i < ends.Length ? Cell(i) : 0;
        return new TraceTableStat(name, C(0), C(1), C(2), C(3), C(4), C(5), C(6), C(7));
    }

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{1,4})\s+\(\d+:0x[0-9A-Fa-f]+\)\s+(?<kind>\S.*)$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^\t(?<db>.+?) \(ATT_(?<att>\d+), (?<rest>.*)\)\s*$")]
    private static partial Regex AttachmentLine();

    [GeneratedRegex(@"^\t(?<path>.+):(?<pid>\d+)\s*$")]
    private static partial Regex ProcessLine();

    [GeneratedRegex(@"^\s*\(TRA_(?<tra>\d+)")]
    private static partial Regex TransactionLine();

    [GeneratedRegex(@"^Statement (?<id>\d+):$")]
    private static partial Regex StatementLine();

    [GeneratedRegex(@"^(?<what>Procedure|Function) (?<name>.+):$")]
    private static partial Regex ObjectLine();

    [GeneratedRegex(@"^\s*(?<name>\S+) FOR (?<table>\S+) \((?<when>[^)]*)\)")]
    private static partial Regex TriggerLine();

    [GeneratedRegex(@"^param\d+ = ")]
    private static partial Regex ParamLine();

    [GeneratedRegex(@"^(?<n>\d+) records? fetched$")]
    private static partial Regex RecordsLine();

    [GeneratedRegex(@"^\s+(?<ms>\d+) ms\b")]
    private static partial Regex PerfLine();

    [GeneratedRegex(@"^Table\s+Natural\s+Index")]
    private static partial Regex TableHeader();

    [GeneratedRegex(@"^\d+ : (?<msg>.*)$")]
    private static partial Regex ErrorLine();

    private static Regex Rule(char c) => c == '^' ? CaretRule() : DashRule();

    [GeneratedRegex(@"^\^{20,}$")]
    private static partial Regex CaretRule();

    [GeneratedRegex(@"^-{20,}$")]
    private static partial Regex DashRule();

    /// <summary>Un número del log; uno que no entra en un long (un log roto o armado a mano) cuenta como 0.</summary>
    private static long Num(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
