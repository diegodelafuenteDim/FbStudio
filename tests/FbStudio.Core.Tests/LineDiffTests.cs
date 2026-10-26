using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

/// <summary>Diferencias renglón por renglón (para marcar el DDL en «Comparar bases»).</summary>
public class LineDiffTests
{
    private static string Show(IReadOnlyList<DiffLine> diff) =>
        string.Concat(diff.Select(d => d.Kind switch { DiffKind.Same => "=", DiffKind.OnlyInA => "-", _ => "+" }));

    [Fact]
    public void Lo_minimo_que_cambia()
    {
        const string source = "CREATE PROCEDURE P\nAS\nBEGIN\n  SELECT COUNT(*) FROM T INTO :N;\n  SUSPEND;\nEND";
        const string target = "CREATE PROCEDURE P\nAS\nBEGIN\n  N = 1;\n  SUSPEND;   \nEND";
        var diff = LineDiff.Compute(source, target);
        Assert.Equal("===-+==", Show(diff));                                    // los espacios del final no cuentan
        Assert.Equal(new DiffLine(DiffKind.OnlyInA, 3, -1), diff[3]);
        Assert.Equal(new DiffLine(DiffKind.OnlyInB, -1, 3), diff[4]);
        Assert.Equal(new DiffLine(DiffKind.Same, 5, 5), diff[6]);

        Assert.Equal("==+=", Show(LineDiff.Compute("a\nb\nc", "a\nb\nx\nc")));
        Assert.Equal("=-=", Show(LineDiff.Compute("a\nb\nc", "a\nc")));
        Assert.Equal("+++", Show(LineDiff.Compute("", "a\nb\nc")));
        Assert.Empty(LineDiff.Compute("", ""));

        // Todo distinto: cada renglón de un lado y del otro.
        var all = LineDiff.Compute("a\nb", "c\nd");
        Assert.Equal(2, all.Count(d => d.Kind == DiffKind.OnlyInA));
        Assert.Equal(2, all.Count(d => d.Kind == DiffKind.OnlyInB));
    }

    [Fact]
    public void Textos_largos_con_pocos_cambios()
    {
        var a = Enumerable.Range(0, 5_000).Select(i => $"renglón {i}").ToList();
        var b = a.ToList();
        b[100] = "cambiado";
        b.Insert(2_000, "nuevo");
        b.RemoveAt(4_000);
        var diff = LineDiff.Compute(a, b);
        Assert.Equal(2, diff.Count(d => d.Kind == DiffKind.OnlyInA));            // el 100 y el que se sacó
        Assert.Equal(2, diff.Count(d => d.Kind == DiffKind.OnlyInB));            // el cambiado y el nuevo
        Assert.Equal(4_998, diff.Count(d => d.Kind == DiffKind.Same));
    }
}
