#:package SkiaSharp@3.119.4
// Uso: copiar este archivo FUERA del repo (adentro choca con Directory.Packages.props) y correr
//   dotnet run icons.cs -- <carpeta-de-láminas> <repo>/src/FbStudio.App/Assets
// Escribe fbstudio.ico (Windows), fbstudio.png (Avalonia) y fbstudio.icns (macOS).
#:property PublishAot=false

// Tres opciones de ícono propio para FbStudio (diseño propio, sin el logo de Firebird) y una lámina para comparar.
using SkiaSharp;

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

var options = new (string Name, Action<SKCanvas> Draw)[]
{
    ("A-base-y-rayo", DrawA),
    ("B-monograma", DrawB),
    ("C-base-y-consola", DrawC),
};

foreach (var (name, draw) in options)
{
    using var bmp = Render(draw, 256);
    Save(bmp, Path.Combine(outDir, $"icono-{name}.png"));
}

// Lámina: cada opción en 256, 64, 32 y 16 px, sobre fondo claro y sobre barra oscura.
const int col = 300, rowH = 420;
using var sheet = new SKBitmap(col * options.Length, rowH);
using (var c = new SKCanvas(sheet))
{
    c.Clear(new SKColor(245, 246, 248));
    using var label = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 18);
    using var dark = new SKPaint { Color = new SKColor(32, 33, 36) };
    using var text = new SKPaint { Color = new SKColor(40, 40, 40), IsAntialias = true };
    for (var i = 0; i < options.Length; i++)
    {
        var x = i * col + 22;
        c.DrawText(options[i].Name[..1] + "  " + options[i].Name[2..].Replace('-', ' '), x, 28, label, text);
        using (var big = Render(options[i].Draw, 256)) c.DrawBitmap(big, x, 40);
        c.DrawRect(i * col + 10, 310, col - 20, 90, dark);
        var sx = x;
        foreach (var s in new[] { 64, 32, 16 })
        {
            using var small = Render(options[i].Draw, s);
            c.DrawBitmap(small, sx, 355 - s / 2);
            sx += s + 24;
        }
    }
}
Save(sheet, Path.Combine(outDir, "iconos-comparacion.png"));

// Ícono elegido (A): .ico con varios tamaños (PNG adentro, Windows Vista+) y un PNG de 256 para Avalonia.
if (args.Length > 1)
{
    var assets = args[1];
    Directory.CreateDirectory(assets);
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var images = sizes.Select(s => { using var b = Render(DrawA, s); using var d = b.Encode(SKEncodedImageFormat.Png, 100); return d.ToArray(); }).ToList();
    using (var ico = new BinaryWriter(File.Create(Path.Combine(assets, "fbstudio.ico"))))
    {
        ico.Write((short)0); ico.Write((short)1); ico.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            ico.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); ico.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            ico.Write((byte)0); ico.Write((byte)0); ico.Write((short)1); ico.Write((short)32);
            ico.Write(images[i].Length); ico.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) ico.Write(img);
    }
    using (var big = Render(DrawA, 256)) Save(big, Path.Combine(assets, "fbstudio.png"));

    // macOS: .icns con PNG adentro (de 16 a 1024 px) para el paquete FbStudio.app.
    (string Type, int Size)[] icns = [("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024)];
    var entries = icns.Select(e => { using var b = Render(DrawA, e.Size); using var d = b.Encode(SKEncodedImageFormat.Png, 100); return (e.Type, Data: d.ToArray()); }).ToList();
    using (var mac = new BinaryWriter(File.Create(Path.Combine(assets, "fbstudio.icns"))))
    {
        void BigEndian(int v) { mac.Write((byte)(v >> 24)); mac.Write((byte)(v >> 16)); mac.Write((byte)(v >> 8)); mac.Write((byte)v); }
        mac.Write("icns"u8);
        BigEndian(8 + entries.Sum(e => 8 + e.Data.Length));
        foreach (var (type, data) in entries)
        {
            mac.Write(System.Text.Encoding.ASCII.GetBytes(type));
            BigEndian(8 + data.Length);
            mac.Write(data);
        }
    }
    Console.WriteLine("ícono: " + assets);
}
Console.WriteLine("listo: " + outDir);

static SKBitmap Render(Action<SKCanvas> draw, int size)
{
    var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Transparent);
    c.Scale(size / 256f);
    draw(c);
    return bmp;
}

static void Save(SKBitmap bmp, string path)
{
    using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}

static void Background(SKCanvas c, SKColor top, SKColor bottom)
{
    using var paint = new SKPaint
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 256), [top, bottom], SKShaderTileMode.Clamp),
    };
    c.DrawRoundRect(new SKRoundRect(new SKRect(8, 8, 248, 248), 52), paint);
}

/// <summary>Tres discos apilados: la "base de datos" clásica.</summary>
static void Database(SKCanvas c, float left, float right, float top, float discH, float gap, float ry, SKColor side, SKColor lid, SKColor? stroke = null, float strokeW = 0)
{
    using var sidePaint = new SKPaint { IsAntialias = true, Color = side, Style = stroke is null ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = strokeW };
    using var lidPaint = new SKPaint { IsAntialias = true, Color = lid, Style = stroke is null ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = strokeW };
    for (var i = 2; i >= 0; i--)
    {
        var y0 = top + i * (discH + gap);
        using var body = new SKPath();
        body.MoveTo(left, y0);
        body.LineTo(left, y0 + discH);
        body.ArcTo(new SKRect(left, y0 + discH - ry, right, y0 + discH + ry), 180, -180, false);
        body.LineTo(right, y0);
        body.Close();
        c.DrawPath(body, sidePaint);
        c.DrawOval(new SKRect(left, y0 - ry, right, y0 + ry), lidPaint);
    }
}

static void DrawA(SKCanvas c)
{
    Background(c, new SKColor(37, 99, 179), new SKColor(18, 52, 110));
    Database(c, 62, 182, 66, 34, 10, 20, new SKColor(226, 236, 250), SKColors.White);
    using var bolt = new SKPath();
    bolt.MoveTo(206, 124); bolt.LineTo(160, 190); bolt.LineTo(190, 190); bolt.LineTo(172, 238);
    bolt.LineTo(226, 166); bolt.LineTo(196, 166); bolt.LineTo(214, 124); bolt.Close();
    using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(255, 160, 0) };
    using var edge = new SKPaint { IsAntialias = true, Color = new SKColor(18, 52, 110), Style = SKPaintStyle.Stroke, StrokeWidth = 7, StrokeJoin = SKStrokeJoin.Round };
    c.DrawPath(bolt, edge);
    c.DrawPath(bolt, fill);
}

static void DrawB(SKCanvas c)
{
    Background(c, new SKColor(20, 150, 136), new SKColor(10, 92, 86));
    using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 118);
    using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
    var width = font.MeasureText("FS");
    c.DrawText("FS", 128 - width / 2, 162, font, white);
    using var bar = new SKPaint { IsAntialias = true, Color = new SKColor(255, 196, 61) };
    c.DrawRoundRect(new SKRoundRect(new SKRect(70, 188, 186, 204), 8), bar);
}

static void DrawC(SKCanvas c)
{
    Background(c, new SKColor(44, 56, 74), new SKColor(22, 28, 38));
    Database(c, 48, 160, 58, 30, 12, 18, new SKColor(56, 189, 248), new SKColor(125, 211, 252), SKColors.White, 9);
    using var panel = new SKPaint { IsAntialias = true, Color = new SKColor(250, 250, 250) };
    using var border = new SKPaint { IsAntialias = true, Color = new SKColor(22, 28, 38), Style = SKPaintStyle.Stroke, StrokeWidth = 8 };
    var rect = new SKRoundRect(new SKRect(118, 140, 236, 226), 16);
    c.DrawRoundRect(rect, border);
    c.DrawRoundRect(rect, panel);
    using var font = new SKFont(SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold), 56);
    using var green = new SKPaint { IsAntialias = true, Color = new SKColor(22, 163, 74) };
    c.DrawText(">_", 134, 203, font, green);
}
