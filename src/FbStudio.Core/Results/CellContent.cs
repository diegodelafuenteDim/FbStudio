using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FbStudio.Core.Results;

public enum CellContentKind
{
    Null,
    Text,
    Image,
    Binary,
}

/// <summary>
/// Lo que hay en una celda, para verlo entero («Ver el valor»): texto (con su versión formateada si es JSON o XML, y el
/// texto sin formato si es RTF), una imagen o un binario (que se muestra en hexadecimal).
/// </summary>
/// <param name="Text">El texto para mostrar: el valor, o el binario decodificado o en hexadecimal.</param>
/// <param name="Bytes">El binario tal cual (para guardarlo o dibujarlo); null si el valor es texto.</param>
/// <param name="Extension">La extensión para guardarlo («.png», «.pdf», «.txt»…).</param>
/// <param name="Format">Qué es, para mostrar: «PNG», «PDF», «RTF», «JSON»…; null si es texto común o no se reconoce.</param>
/// <param name="Formatted">El JSON o XML con sangría, o el texto de un RTF; null si no hay otra forma de verlo.</param>
public sealed record CellContent(CellContentKind Kind, string Text, byte[]? Bytes, string Extension, string? Format, string? Formatted = null)
{
    /// <summary>Caracteres de un texto o bytes de un binario.</summary>
    public long Size => Bytes?.LongLength ?? Text.Length;
}

public static class CellContents
{
    /// <summary>Hasta cuántos bytes se muestran en hexadecimal (se guarda entero igual).</summary>
    public const int HexLimit = 64 * 1024;

    private static readonly (byte[] Magic, string Extension, string Format, bool Image)[] Signatures =
    [
        ([0x89, 0x50, 0x4E, 0x47], ".png", "PNG", true),
        ([0xFF, 0xD8, 0xFF], ".jpg", "JPEG", true),
        ([0x47, 0x49, 0x46, 0x38], ".gif", "GIF", true),
        ([0x42, 0x4D], ".bmp", "BMP", true),
        ([0x25, 0x50, 0x44, 0x46], ".pdf", "PDF", false),
        ([0x50, 0x4B, 0x03, 0x04], ".zip", "ZIP", false),
        ([0x7B, 0x5C, 0x72, 0x74, 0x66], ".rtf", "RTF", false),
    ];

    public static CellContent Of(object? value, string dataTypeName)
    {
        switch (value)
        {
            case null or DBNull:
                return new CellContent(CellContentKind.Null, "", null, ".txt", null);
            case byte[] bytes:
            {
                var known = Signatures.FirstOrDefault(s => bytes.AsSpan().StartsWith(s.Magic));
                if (known.Image) return new CellContent(CellContentKind.Image, HexDump(bytes), bytes, known.Extension, known.Format);
                if (known.Format is not ("RTF" or null)) return new CellContent(CellContentKind.Binary, HexDump(bytes), bytes, known.Extension, known.Format);
                // Texto guardado como binario (o con charset NONE): UTF-8 si es válido, si no Latin-1, si no hay controles raros.
                if (Decode(bytes) is { } decoded)
                {
                    var text = OfText(decoded);
                    return text with { Bytes = bytes };
                }
                return new CellContent(CellContentKind.Binary, HexDump(bytes), bytes, ".bin", null);
            }
            case string s:
                return OfText(s);
            default:
                return new CellContent(CellContentKind.Text, CellFormatter.Format(value, dataTypeName), null, ".txt", null);
        }
    }

    private static CellContent OfText(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith(@"{\rtf", StringComparison.Ordinal))
            return new CellContent(CellContentKind.Text, text, null, ".rtf", "RTF", RtfToText(text));
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                var pretty = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                return new CellContent(CellContentKind.Text, text, null, ".json", "JSON", pretty);
            }
            catch (JsonException)
            {
            }
        }
        if (trimmed.StartsWith('<'))
        {
            try
            {
                // Sin DTD ni entidades externas: solo se lee para darle sangría.
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = System.Xml.XmlReader.Create(new StringReader(text), settings);
                return new CellContent(CellContentKind.Text, text, null, ".xml", "XML", XDocument.Load(reader).ToString());
            }
            catch (System.Xml.XmlException)
            {
            }
        }
        return new CellContent(CellContentKind.Text, text, null, ".txt", null);
    }

    /// <summary>Los bytes como texto, si lo parecen: UTF-8 válido o Latin-1, sin NUL ni otros controles que tabulador y saltos.</summary>
    private static string? Decode(byte[] bytes)
    {
        static bool Readable(string s) => s.All(c => c >= ' ' || c is '\t' or '\r' or '\n') && !s.Any(c => c is >= '\u007F' and < ' ');
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            if (Readable(utf8)) return utf8;
        }
        catch (DecoderFallbackException)
        {
        }
        var latin1 = Encoding.Latin1.GetString(bytes);
        return Readable(latin1) ? latin1 : null;
    }

    /// <summary>«00000000  89 50 4E 47 …  .PNG…», de a 16 bytes por renglón, hasta <see cref="HexLimit"/>.</summary>
    public static string HexDump(byte[] bytes)
    {
        var sb = new StringBuilder();
        var length = Math.Min(bytes.Length, HexLimit);
        for (var offset = 0; offset < length; offset += 16)
        {
            sb.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            var count = Math.Min(16, length - offset);
            for (var i = 0; i < 16; i++)
                sb.Append(i < count ? bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
            sb.Append(' ');
            for (var i = 0; i < count; i++)
            {
                var b = bytes[offset + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.Append('\n');
        }
        if (bytes.Length > length) sb.Append(string.Format(CultureInfo.CurrentCulture, Resources.CoreStrings.HexDumpTruncated, length, bytes.Length));
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// El texto de un RTF, sin formato: saltea las tablas de fuentes, colores y estilos, las imágenes y los grupos «\*»;
    /// \par y \line son saltos, \tab un tabulador, \'hh un carácter (Latin-1, como Windows-1252 en los acentos) y \uN uno Unicode.
    /// </summary>
    public static string RtfToText(string rtf)
    {
        var skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "fonttbl", "colortbl", "stylesheet", "info", "pict", "header", "footer", "headerl", "headerr", "footerl", "footerr",
            "listtable", "listoverridetable", "rsidtbl", "generator", "themedata", "colorschememapping", "latentstyles", "datastore", "xmlnstbl",
        };
        var sb = new StringBuilder();
        var stack = new Stack<(bool Skipping, int UnicodeSkip)>();
        var skipping = false;
        var unicodeSkip = 1;
        var pendingSkip = 0; // caracteres a saltear después de un \uN
        var i = 0;
        void Emit(char c)
        {
            if (pendingSkip > 0) pendingSkip--;
            else if (!skipping) sb.Append(c);
        }
        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '{')
            {
                stack.Push((skipping, unicodeSkip));
                i++;
                if (i + 1 < rtf.Length && rtf[i] == '\\' && rtf[i + 1] == '*') skipping = true;
                continue;
            }
            if (c == '}')
            {
                (skipping, unicodeSkip) = stack.Count > 0 ? stack.Pop() : (false, 1);
                i++;
                continue;
            }
            if (c is '\r' or '\n')
            {
                i++;
                continue;
            }
            if (c != '\\')
            {
                Emit(c);
                i++;
                continue;
            }
            // Una palabra de control: \palabra[-N][espacio], un símbolo (\\ \{ \}), \'hh o \~ \- \_.
            i++;
            if (i >= rtf.Length) break;
            var next = rtf[i];
            if (next is '\\' or '{' or '}')
            {
                Emit(next);
                i++;
                continue;
            }
            if (next == '\'' && i + 2 < rtf.Length)
            {
                if (int.TryParse(rtf.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) Emit(Encoding.Latin1.GetString([(byte)code])[0]);
                i += 3;
                continue;
            }
            if (!char.IsAsciiLetter(next))
            {
                if (next == '~') Emit(' ');
                i++;
                continue;
            }
            var start = i;
            while (i < rtf.Length && char.IsAsciiLetter(rtf[i])) i++;
            var word = rtf[start..i];
            var numberStart = i;
            if (i < rtf.Length && rtf[i] == '-') i++;
            while (i < rtf.Length && char.IsAsciiDigit(rtf[i])) i++;
            int? number = i > numberStart && int.TryParse(rtf.AsSpan(numberStart, i - numberStart), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
            if (i < rtf.Length && rtf[i] == ' ') i++;
            switch (word)
            {
                case "par" or "line" or "sect" or "page" or "row":
                    Emit('\n');
                    break;
                case "tab" or "cell":
                    Emit('\t');
                    break;
                case "uc" when number is { } uc:
                    unicodeSkip = uc;
                    break;
                case "u" when number is { } u:
                    Emit((char)(u < 0 ? u + 65536 : u));
                    pendingSkip = unicodeSkip;
                    break;
                default:
                    if (skip.Contains(word)) skipping = true;
                    break;
            }
        }
        return sb.ToString().Trim();
    }
}
