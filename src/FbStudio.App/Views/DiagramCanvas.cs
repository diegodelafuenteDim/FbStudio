using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using FbStudio.Core.Diagrams;

namespace FbStudio.App.Views;

/// <summary>Colores del diagrama; el PNG sale siempre con los claros (va a documentos y mails).</summary>
public sealed record DiagramPalette(IBrush Background, IBrush Box, IBrush Header, IBrush ViewHeader, IBrush OutsideBox, IBrush Border, IBrush Text,
    IBrush Muted, IBrush Marker, IBrush Line, IBrush Selected)
{
    public static DiagramPalette Light { get; } = new(Brushes.White, Brush("#FFFFFF"), Brush("#E2E8F0"), Brush("#D9F0EE"), Brush("#F8FAFC"),
        Brush("#64748B"), Brush("#1F2937"), Brush("#64748B"), Brush("#B45309"), Brush("#475569"), Brush("#1E88E5"));

    public static DiagramPalette Dark { get; } = new(Brush("#1E1F22"), Brush("#2B2D31"), Brush("#3A3F47"), Brush("#2F4A48"), Brush("#26282C"),
        Brush("#7C8796"), Brush("#E5E7EB"), Brush("#9AA5B1"), Brush("#FFB74D"), Brush("#9AA5B1"), Brush("#64B5F6"));

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color)).ToImmutable();
}

/// <summary>Dibuja una escena de diagrama (pantalla y PNG): las líneas primero y las cajas encima.</summary>
public static class DiagramPainter
{
    private static readonly Typeface Face = new("Segoe UI, Inter, sans-serif");
    private static readonly Typeface FaceBold = new("Segoe UI, Inter, sans-serif", FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Ancho de un texto con la letra del diagrama (true = el nombre de la tabla, en negrita).</summary>
    public static double Measure(string text, bool bold) =>
        Text(text, bold ? FaceBold : Face, bold ? DiagramMetrics.HeaderFontSize : DiagramMetrics.FontSize, Brushes.Black).Width;

    private static FormattedText Text(string text, Typeface face, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush);

    public static void Paint(DrawingContext ctx, DiagramScene scene, DiagramPalette palette, DiagramNode? selected = null)
    {
        var line = new Pen(palette.Line, 1.2);
        var dashed = new Pen(palette.Line, 1.2, new DashStyle([5, 3.5], 0));
        var highlight = new Pen(palette.Selected, 2);
        var highlightDashed = new Pen(palette.Selected, 2, new DashStyle([4, 3], 0));
        foreach (var e in scene.Edges)
        {
            var touches = selected is not null && (ReferenceEquals(e.From, selected) || ReferenceEquals(e.To, selected));
            var pen = touches ? e.Link.IsLogical ? highlightDashed : highlight : e.Link.IsLogical ? dashed : line;
            var points = DiagramScene.Route(e);
            for (var i = 1; i < points.Count; i++)
                ctx.DrawLine(pen, new Point(points[i - 1].X, points[i - 1].Y), new Point(points[i].X, points[i].Y));
            Arrow(ctx, touches ? palette.Selected : palette.Line, points[^2], points[^1]);
        }

        var border = new Pen(palette.Border, 1);
        var outsideBorder = new Pen(palette.Border, 1, new DashStyle([3, 2], 0));
        var selectedBorder = new Pen(palette.Selected, 2.5);
        foreach (var n in scene.Nodes)
        {
            var rect = new Rect(n.X, n.Y, n.Width, n.Height);
            ctx.DrawRectangle(n.IsOutside ? palette.OutsideBox : palette.Box, null, rect, 4, 4);
            var head = new Rect(n.X, n.Y, n.Width, Math.Min(DiagramMetrics.HeaderHeight, n.Height));
            ctx.DrawRectangle(n.IsView ? palette.ViewHeader : palette.Header, null, head, 4, 4);
            if (n.Rows.Count > 0 || n.HiddenColumns > 0)
                ctx.DrawLine(border, new Point(n.X, n.Y + DiagramMetrics.HeaderHeight), new Point(n.X + n.Width, n.Y + DiagramMetrics.HeaderHeight));
            ctx.DrawRectangle(null, ReferenceEquals(n, selected) ? selectedBorder : n.IsOutside ? outsideBorder : border, rect, 4, 4);
            ctx.DrawText(Text(n.Name, FaceBold, DiagramMetrics.HeaderFontSize, n.IsOutside ? palette.Muted : palette.Text),
                new Point(n.X + DiagramMetrics.PaddingX, n.Y + 5));

            var y = n.Y + DiagramMetrics.HeaderHeight + 1;
            foreach (var r in n.Rows)
            {
                if (r.Marker.Length > 0)
                    ctx.DrawText(Text(r.Marker, FaceBold, DiagramMetrics.MarkerFontSize, palette.Marker), new Point(n.X + DiagramMetrics.PaddingX, y + 2.5));
                ctx.DrawText(Text(r.Name, Face, DiagramMetrics.FontSize, palette.Text), new Point(n.X + DiagramMetrics.PaddingX + DiagramMetrics.MarkerWidth, y + 1));
                var type = Text(r.Type, Face, DiagramMetrics.FontSize, palette.Muted);
                ctx.DrawText(type, new Point(n.X + n.Width - DiagramMetrics.PaddingX - type.Width, y + 1));
                y += DiagramMetrics.RowHeight;
            }
            if (n.HiddenColumns > 0)
                ctx.DrawText(Text(DiagramScene.MoreText(n.HiddenColumns), Face, DiagramMetrics.FontSize, palette.Muted), new Point(n.X + DiagramMetrics.PaddingX, y + 1));
        }
    }

    /// <summary>Punta de flecha en <paramref name="to"/>, apuntando desde <paramref name="from"/>.</summary>
    private static void Arrow(DrawingContext ctx, IBrush brush, (double X, double Y) from, (double X, double Y) to)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        (dx, dy) = (dx / len, dy / len);
        const double size = 8, half = 4;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(to.X, to.Y), true);
            g.LineTo(new Point(to.X - dx * size - dy * half, to.Y - dy * size + dx * half));
            g.LineTo(new Point(to.X - dx * size + dy * half, to.Y - dy * size - dx * half));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geometry);
    }

    /// <summary>
    /// El diagrama entero como PNG, con fondo blanco. Se dibuja al doble de resolución para que se lea bien impreso,
    /// salvo que quede demasiado grande.
    /// </summary>
    public static void SavePng(DiagramScene scene, string path, double scale = 2)
    {
        var (bx, by, bw, bh) = scene.Bounds();
        scale = Math.Min(scale, Math.Min(16000 / bw, 16000 / bh));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(bw * scale), (int)Math.Ceiling(bh * scale)), new Vector(96 * scale, 96 * scale));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, bw, bh));
            using (ctx.PushTransform(Matrix.CreateTranslation(-bx, -by)))
                Paint(ctx, scene, DiagramPalette.Light);
        }
#pragma warning disable CS0618 // la sobrecarga con BitmapEncoderOptions no trae nada que haga falta para un PNG
        bitmap.Save(path);
#pragma warning restore CS0618
    }
}

/// <summary>
/// El lienzo del diagrama: arrastrar una tabla la mueve, arrastrar el fondo mueve la vista, la rueda acerca y aleja (en
/// el lugar del puntero), un clic elige una tabla y el doble clic la abre.
/// </summary>
public sealed class DiagramCanvas : Control
{
    public const double MinZoom = 0.1, MaxZoom = 3;

    private DiagramScene? _scene;
    private DiagramNode? _dragNode;
    private Point _grab;
    private Point? _panFrom;
    private bool _moved;

    public DiagramCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public DiagramScene? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            Selected = null;
            InvalidateVisual();
        }
    }

    public double Zoom { get; private set; } = 1;
    public Vector Offset { get; private set; }
    public DiagramNode? Selected { get; private set; }

    /// <summary>Se eligió una tabla (o ninguna, con un clic en el fondo).</summary>
    public event Action<DiagramNode?>? SelectionChanged;

    /// <summary>Doble clic en una tabla.</summary>
    public event Action<DiagramNode>? NodeActivated;

    public void Select(DiagramNode? node)
    {
        Selected = node;
        SelectionChanged?.Invoke(node);
        InvalidateVisual();
    }

    /// <summary>Todo el diagrama a la vista (sin agrandarlo más del tamaño real).</summary>
    public void FitToView()
    {
        if (_scene is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var (bx, by, bw, bh) = _scene.Bounds();
        Zoom = Math.Clamp(Math.Min(1, Math.Min(Bounds.Width / bw, Bounds.Height / bh)), MinZoom, MaxZoom);
        Offset = new Vector((Bounds.Width - bw * Zoom) / 2 - bx * Zoom, (Bounds.Height - bh * Zoom) / 2 - by * Zoom);
        InvalidateVisual();
    }

    /// <summary>Acerca o aleja manteniendo quieto el punto <paramref name="at"/> de la pantalla.</summary>
    public void ZoomAt(Point at, double factor)
    {
        var zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        Offset = new Vector(at.X - (at.X - Offset.X) * zoom / Zoom, at.Y - (at.Y - Offset.Y) * zoom / Zoom);
        Zoom = zoom;
        InvalidateVisual();
    }

    private Point ToScene(Point p) => new((p.X - Offset.X) / Zoom, (p.Y - Offset.Y) / Zoom);

    public override void Render(DrawingContext context)
    {
        var palette = ActualThemeVariant == ThemeVariant.Dark ? DiagramPalette.Dark : DiagramPalette.Light;
        context.FillRectangle(palette.Background, new Rect(Bounds.Size));
        if (_scene is null) return;
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Offset.X, Offset.Y)))
            DiagramPainter.Paint(context, _scene, palette, Selected);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_scene is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var at = ToScene(e.GetPosition(this));
        var node = _scene.NodeAt(at.X, at.Y);
        if (node is not null && e.ClickCount == 2)
        {
            NodeActivated?.Invoke(node);
            e.Handled = true;
            return;
        }
        Select(node);
        _moved = false;
        if (node is not null)
        {
            _dragNode = node;
            _grab = new Point(at.X - node.X, at.Y - node.Y);
        }
        else
        {
            _panFrom = e.GetPosition(this);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragNode is { } node)
        {
            var at = ToScene(e.GetPosition(this));
            (node.X, node.Y) = (Math.Round(at.X - _grab.X), Math.Round(at.Y - _grab.Y));
            _moved = true;
            InvalidateVisual();
        }
        else if (_panFrom is { } from)
        {
            var p = e.GetPosition(this);
            Offset += p - from;
            _panFrom = p;
            InvalidateVisual();
        }
        Cursor = _scene?.NodeAt(ToScene(e.GetPosition(this)).X, ToScene(e.GetPosition(this)).Y) is not null
            ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragNode is not null && _moved) NodeMoved?.Invoke(_dragNode);
        _dragNode = null;
        _panFrom = null;
        e.Pointer.Capture(null);
    }

    /// <summary>Se terminó de mover una tabla.</summary>
    public event Action<DiagramNode>? NodeMoved;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }
}
