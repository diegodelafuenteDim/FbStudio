using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using FbStudio.Core.Results;

namespace FbStudio.App.ResultGrid;

public enum ChartKind
{
    Bars,
    Line,
}

/// <summary>
/// El gráfico rápido de un resultado: una serie, barras o línea, un solo eje. Barras finas con el extremo redondeado y
/// apoyadas en la base (2 px entre una y otra), línea de 2 px con puntos de 8 px si son pocos, grilla y ejes discretos,
/// los textos con el color del texto (no el de la serie) y, al pasar el mouse, la etiqueta y el valor del punto.
/// </summary>
public sealed class ChartControl : Control
{
    private const double Left = 64, Right = 16, Top = 16, Bottom = 44;

    private IReadOnlyList<ChartPoint> _points = [];
    private ChartKind _kind;
    private int _hover = -1;

    public IReadOnlyList<ChartPoint> Points
    {
        get => _points;
        set
        {
            _points = value;
            _hover = -1;
            InvalidateVisual();
        }
    }

    public ChartKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            InvalidateVisual();
        }
    }

    /// <summary>Barras o puntos dibujados en el último cuadro (para las pruebas).</summary>
    public int MarksDrawnLastFrame { get; private set; }

    /// <summary>El punto debajo del mouse, o -1.</summary>
    public int Hover => _hover;

    public ChartControl()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    private bool Dark => ActualThemeVariant == ThemeVariant.Dark;

    // La serie (azul de la paleta de referencia, cada tema con su paso) y las tintas de texto, grilla y fondo.
    private IBrush Series => new SolidColorBrush(Dark ? Color.Parse("#3987e5") : Color.Parse("#2a78d6"));
    private IBrush Ink => new SolidColorBrush(Dark ? Color.Parse("#f0efec") : Color.Parse("#0b0b0b"));
    private IBrush Muted => new SolidColorBrush(Dark ? Color.Parse("#c3c2b7") : Color.Parse("#52514e"));
    private IBrush Surface => new SolidColorBrush(Dark ? Color.Parse("#1a1a19") : Color.Parse("#fcfcfb"));
    private Pen GridPen => new(new SolidColorBrush(Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)), 1);

    private static readonly Typeface Face = new("Segoe UI,Inter,Helvetica,Arial,sans-serif");

    private FormattedText Text(string text, IBrush brush, double size = 11) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);

    private Rect Plot => new(Left, Top, Math.Max(10, Bounds.Width - Left - Right), Math.Max(10, Bounds.Height - Top - Bottom));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Surface, new Rect(Bounds.Size));
        MarksDrawnLastFrame = 0;
        if (_points.Count == 0) return;
        var plot = Plot;
        var ticks = ChartData.Ticks(_points.Min(p => p.Value), _points.Max(p => p.Value));
        double min = ticks[0], max = ticks[^1];
        double Y(double value) => plot.Bottom - (value - min) / (max - min) * plot.Height;

        // Grilla y valores del eje (discretos).
        foreach (var t in ticks)
        {
            var y = Math.Round(Y(t)) + 0.5;
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(ChartData.Format(t), Muted);
            context.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        var slot = plot.Width / _points.Count;
        double X(int i) => plot.Left + slot * (i + 0.5);
        var baseline = Y(Math.Clamp(0, min, max));
        if (_kind == ChartKind.Bars)
        {
            var width = Math.Max(1, Math.Min(48, slot - 2)); // 2 px de aire entre barras
            for (var i = 0; i < _points.Count; i++)
            {
                var y = Y(_points[i].Value);
                var rect = new Rect(X(i) - width / 2, Math.Min(y, baseline), width, Math.Max(1, Math.Abs(baseline - y)));
                var radius = Math.Min(4, Math.Min(width / 2, rect.Height / 2));
                var brush = _hover >= 0 && _hover != i ? new SolidColorBrush(((ISolidColorBrush)Series).Color, 0.55) : Series;
                // Redondeada en el extremo del dato, recta sobre la base (una sola forma: con transparencia no se pisa).
                var corners = _points[i].Value >= 0 ? new CornerRadius(radius, radius, 0, 0) : new CornerRadius(0, 0, radius, radius);
                context.DrawRectangle(brush, null, new RoundedRect(rect, corners));
                MarksDrawnLastFrame++;
            }
        }
        else
        {
            var pen = new Pen(Series, 2, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(X(0), Y(_points[0].Value)), false);
                for (var i = 1; i < _points.Count; i++) g.LineTo(new Point(X(i), Y(_points[i].Value)));
                g.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
            if (_points.Count <= 60)
                for (var i = 0; i < _points.Count; i++)
                {
                    // Punto de 8 px con un anillo del color del fondo.
                    context.DrawEllipse(Series, new Pen(Surface, 2), new Point(X(i), Y(_points[i].Value)), 4, 4);
                    MarksDrawnLastFrame++;
                }
            if (_hover >= 0) context.DrawLine(GridPen, new Point(X(_hover), plot.Top), new Point(X(_hover), plot.Bottom));
        }
        context.DrawLine(new Pen(Muted, 1), new Point(plot.Left, Math.Round(baseline) + 0.5), new Point(plot.Right, Math.Round(baseline) + 0.5));

        // Etiquetas de X: las que entran sin pisarse.
        var every = Math.Max(1, (int)Math.Ceiling(80 / slot));
        for (var i = 0; i < _points.Count; i += every)
        {
            var text = _points[i].Label.Length > 14 ? _points[i].Label[..13] + "…" : _points[i].Label;
            var label = Text(text, Muted);
            context.DrawText(label, new Point(Math.Clamp(X(i) - label.Width / 2, 0, Math.Max(0, Bounds.Width - label.Width)), plot.Bottom + 6));
        }

        if (_hover >= 0 && _hover < _points.Count) DrawTooltip(context, plot, X(_hover), Y(_points[_hover].Value), _points[_hover]);
    }

    private void DrawTooltip(DrawingContext context, Rect plot, double x, double y, ChartPoint point)
    {
        var label = Text(point.Label, Muted);
        var value = Text(ChartData.Format(point.Value), Ink, 13);
        var width = Math.Max(label.Width, value.Width) + 16;
        var height = label.Height + value.Height + 12;
        var left = Math.Clamp(x + 10, 0, Math.Max(0, Bounds.Width - width));
        var top = Math.Clamp(y - height - 8, 0, Math.Max(0, Bounds.Height - height));
        var box = new Rect(left, top, width, height);
        context.DrawRectangle(Surface, new Pen(GridPen.Brush, 1), box, 6, 6);
        context.DrawText(label, new Point(left + 8, top + 6));
        context.DrawText(value, new Point(left + 8, top + 6 + label.Height));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        HoverAt(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>Elige el punto más cercano a esa posición (toda la franja de cada barra cuenta, no solo la barra).</summary>
    public void HoverAt(Point position)
    {
        var plot = Plot;
        var hover = _points.Count == 0 || position.X < plot.Left || position.X > plot.Right
            ? -1
            : Math.Clamp((int)((position.X - plot.Left) / (plot.Width / _points.Count)), 0, _points.Count - 1);
        if (hover == _hover) return;
        _hover = hover;
        InvalidateVisual();
    }
}
