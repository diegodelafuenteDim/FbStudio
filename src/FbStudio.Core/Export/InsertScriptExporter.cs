using System.Globalization;
using System.Text;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Export;

/// <summary>
/// Script de INSERT para Firebird (uno por fila, COMMIT cada <paramref name="commitEvery"/> filas), escrito en streaming.
/// Se puede ejecutar en FbStudio, isql u otra herramienta de Firebird.
/// </summary>
/// <param name="table">Tabla destino tal como la escribe el usuario: sin comillas se pasa a mayúsculas.</param>
public sealed class InsertScriptExporter(string table, int commitEvery = 1_000) : IResultExporter
{
    public string Name => Resources.CoreStrings.InsertScriptName;
    public string Extension => "sql";

    public string Table { get; } = RowScript.QuoteTable(table);

    public long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true), 1 << 16);
        w.Write("-- " + string.Format(Resources.CoreStrings.InsertScriptHeader, DateTime.Now) + "\n");
        w.Write("-- " + string.Format(Resources.CoreStrings.InsertScriptTarget, Table, columns.Count, commitEvery) + "\n\n");
        var prefix = $"INSERT INTO {Table} ({string.Join(", ", columns.Select(c => SqlIdentifier.Quote(c.Name)))}) VALUES (";
        long count = 0;
        foreach (var row in rows)
        {
            w.Write(prefix);
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) w.Write(", ");
                w.Write(SqlLiteral.Format(row[columns[i].Ordinal], columns[i].DataTypeName));
            }
            w.Write(");\n");
            count++;
            if (commitEvery > 0 && count % commitEvery == 0) w.Write("COMMIT WORK;\n");
        }
        if (commitEvery <= 0 || count % commitEvery != 0 || count == 0) w.Write("COMMIT WORK;\n");
        return count;
    }
}

/// <summary>Valores como literales SQL de Firebird.</summary>
public static class SqlLiteral
{
    public static string Format(object? value, string dataTypeName) => value switch
    {
        null or DBNull => "NULL",
        string s => SqlIdentifier.Literal(dataTypeName == "CHAR" ? s.TrimEnd() : s),
        bool b => b ? "TRUE" : "FALSE",
        DateTime d when dataTypeName == "DATE" => "DATE '" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'",
        DateTime d when dataTypeName == "TIME" => "TIME '" + d.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture) + "'",
        DateTime d => "TIMESTAMP '" + d.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture) + "'",
        TimeSpan t => "TIME '" + t.ToString(@"hh\:mm\:ss\.ffff", CultureInfo.InvariantCulture) + "'",
        TimeOnly t => "TIME '" + t.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture) + "'",
        byte[] bytes => "x'" + Convert.ToHexString(bytes) + "'",
        double d when double.IsNaN(d) || double.IsInfinity(d) => "NULL",
        float f when float.IsNaN(f) || float.IsInfinity(f) => "NULL",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => SqlIdentifier.Literal(value.ToString() ?? ""),
    };
}
