using System.Text;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>«Ver el valor» de una celda: qué es y cómo se muestra.</summary>
public class CellContentTests
{
    [Fact]
    public void Texto_JSON_XML_y_RTF()
    {
        Assert.Equal(CellContentKind.Null, CellContents.Of(DBNull.Value, "VARCHAR").Kind);
        var plain = CellContents.Of("línea 1\nlínea 2", "BLOB SUB_TYPE TEXT");
        Assert.Equal((CellContentKind.Text, null, null, ".txt", 15L), (plain.Kind, plain.Format, plain.Formatted, plain.Extension, plain.Size));

        var json = CellContents.Of("{\"nombre\":\"Peña\",\"items\":[1,2]}", "BLOB SUB_TYPE TEXT");
        Assert.Equal(("JSON", ".json"), (json.Format, json.Extension));
        Assert.Equal("{\n  \"nombre\": \"Peña\",\n  \"items\": [\n    1,\n    2\n  ]\n}", json.Formatted!.Replace("\r\n", "\n"));
        Assert.Null(CellContents.Of("[no es json", "VARCHAR").Format);

        var xml = CellContents.Of("<a><b x=\"1\">t</b></a>", "BLOB SUB_TYPE TEXT");
        Assert.Equal("XML", xml.Format);
        Assert.Equal("<a>\n  <b x=\"1\">t</b>\n</a>", xml.Formatted!.Replace("\r\n", "\n"));
        // Sin DTD: una entidad externa no se resuelve (no se lee nada de afuera).
        Assert.Null(CellContents.Of("<!DOCTYPE a [<!ENTITY e SYSTEM \"file:///c:/x\">]><a>&e;</a>", "VARCHAR").Format);

        const string rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl;\red0\green0\blue0;}{\*\generator Riched20;}" +
                           @"\pard\f0\fs20 Informe de ecograf\'eda\par Paciente: Mar\u237?a\tab Edad 40\par\b Conclusi\'f3n:\b0  normal \{ok\}}";
        var content = CellContents.Of(rtf, "BLOB SUB_TYPE TEXT");
        Assert.Equal(("RTF", ".rtf"), (content.Format, content.Extension));
        Assert.Equal("Informe de ecografía\nPaciente: María\tEdad 40\nConclusión: normal {ok}", content.Formatted);
    }

    [Fact]
    public void Imagenes_binarios_y_texto_guardado_como_binario()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
        var image = CellContents.Of(png, "BLOB");
        Assert.Equal((CellContentKind.Image, "PNG", ".png", 12L), (image.Kind, image.Format, image.Extension, image.Size));
        Assert.StartsWith("00000000  89 50 4E 47 0D 0A 1A 0A 00 00 00 0D", image.Text);
        Assert.EndsWith(".PNG........", image.Text);

        var pdf = CellContents.Of(Encoding.ASCII.GetBytes("%PDF-1.4\n\0\0"), "BLOB");
        Assert.Equal((CellContentKind.Binary, "PDF", ".pdf"), (pdf.Kind, pdf.Format, pdf.Extension));
        Assert.Equal((CellContentKind.Binary, ".bin"), (CellContents.Of(new byte[] { 0, 1, 2, 3 }, "BLOB").Kind, CellContents.Of(new byte[] { 0, 1, 2, 3 }, "BLOB").Extension));

        // Texto en un blob binario: UTF-8, o Latin-1 (charset NONE); conserva los bytes para guardarlo tal cual.
        var utf8 = CellContents.Of(Encoding.UTF8.GetBytes("Señal ok"), "BLOB");
        Assert.Equal((CellContentKind.Text, "Señal ok"), (utf8.Kind, utf8.Text));
        Assert.Equal(9L, utf8.Size);
        Assert.Equal("Señal", CellContents.Of(Encoding.Latin1.GetBytes("Señal"), "BLOB").Text);
        Assert.Equal("RTF", CellContents.Of(Encoding.Latin1.GetBytes(@"{\rtf1 Hola\par}"), "BLOB").Format);

        // Un binario grande se muestra hasta 64 KB.
        var big = CellContents.HexDump(new byte[CellContents.HexLimit + 10]);
        Assert.Contains("65.536 de 65.546 bytes", big.Replace(",", "."));
        Assert.Equal(CellContents.HexLimit / 16 + 2, big.Split('\n').Length);

        // Otros tipos: como en la grilla.
        Assert.Equal(CellContentKind.Text, CellContents.Of(12.5m, "NUMERIC").Kind);
    }
}
