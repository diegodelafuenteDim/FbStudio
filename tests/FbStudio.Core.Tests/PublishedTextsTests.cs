using System.Runtime.CompilerServices;

namespace FbStudio.Core.Tests;

/// <summary>
/// Lo que se publica (código, pruebas, manual, README, CHANGELOG y herramientas) no nombra a otras herramientas de
/// Firebird. La lista está escrita al revés para que este archivo, que también se publica, no las nombre.
/// </summary>
public class PublishedTextsTests
{
    private static readonly string[] Reversed =
    [
        "trepxebi", "trepxe bi", "trepxeder", "trepxe der", "trepxebdr", "niboremalf", "revaebd", "hcnebkrow esabatad", "enecspu",
        "erawtfos-kh", "yreuq etucexe", "oidutsrepeekeeb",
    ];

    private static string Root([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    [Fact]
    public void Lo_publicado_no_nombra_otras_herramientas()
    {
        var root = Root();
        var names = Reversed.Select(n => new string(n.Reverse().ToArray())).ToList();
        var files = new[] { "src", "tests", "tools", Path.Combine("docs", "manual") }
            .Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Concat([Path.Combine(root, "README.md"), Path.Combine(root, "CHANGELOG.md")])
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetExtension(f) is ".cs" or ".axaml" or ".resx" or ".md" or ".sh" or ".csproj" or ".props" or ".json" or ".yml" or ".plist")
            .ToList();
        Assert.NotEmpty(files);
        var found = files.SelectMany(f => names.Where(n => File.ReadAllText(f).Contains(n, StringComparison.OrdinalIgnoreCase))
            .Select(n => $"{Path.GetRelativePath(root, f)}: «{n}»")).ToList();
        Assert.True(found.Count == 0, "Nombran otras herramientas:\n" + string.Join("\n", found));
    }
}
