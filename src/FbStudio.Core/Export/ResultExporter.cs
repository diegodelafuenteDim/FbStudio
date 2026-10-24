using FbStudio.Core.Results;

namespace FbStudio.Core.Export;

/// <summary>
/// Escribe un resultado a un archivo. Las filas llegan como secuencia: pueden ser las cargadas en la grilla o un
/// stream de la base (exportar todo), así que el exportador no debe materializarlas.
/// </summary>
public interface IResultExporter
{
    /// <summary>Nombre para los mensajes ("CSV", "XLSX", "Script INSERT").</summary>
    string Name { get; }

    /// <summary>Extensión sin punto, para el diálogo de guardar.</summary>
    string Extension { get; }

    /// <summary>Devuelve la cantidad de filas escritas.</summary>
    long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows);
}

public sealed class CsvExporter(CsvOptions options) : IResultExporter
{
    public string Name => "CSV";
    public string Extension => "csv";

    public long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        using var w = new StreamWriter(path, false, options.Encoding);
        CsvWriter.WriteHeader(w, columns, options);
        long count = 0;
        foreach (var row in rows)
        {
            CsvWriter.WriteRow(w, row, columns, options);
            count++;
        }
        return count;
    }
}
