using System.Runtime.CompilerServices;

namespace FbStudio.Mcp.Tests;

/// <summary>
/// D-114: el conector no tiene ninguna puerta para ejecutar SQL, leer filas ni cambiar nada. Si alguna de estas llamadas
/// aparece en su código, es que se abrió una; la prueba falla aunque la herramienta "solo mire".
/// </summary>
public sealed class CodeGuardTests
{
    private static readonly string[] Forbidden =
    [
        "ExecuteAsync", "ExecuteReader", "ExecuteNonQuery", "ExecuteScalar", "FbCommand", "QuerySession", "TableDataReader",
        "TableDataSession", "DataImporter", "TestDataQueries", "KillAttachment", "CancelStatement", "DistinctAsync",
        "ReadPageAsync", "CountRowsAsync", "SecurityManager", "DatabaseServices", "ApplyAsync",
        // El monitoreo de la interfaz corre consultas propias y corta conexiones: el conector usa la foto fija del explorador.
        "MonitorSession", "TraceSession",
    ];

    private static string Root([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    [Fact]
    public void El_conector_no_ejecuta_SQL_ni_lee_filas()
    {
        var folder = Path.Combine(Root(), "src", "FbStudio.Mcp");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);
        var found = files.SelectMany(f => Forbidden.Where(name => File.ReadAllText(f).Contains(name, StringComparison.Ordinal))
            .Select(name => $"{Path.GetFileName(f)}: {name}")).ToList();
        Assert.Empty(found);
    }
}
