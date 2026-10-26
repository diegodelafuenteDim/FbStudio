using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace FbStudio.App.Editor;

/// <summary>Pinta el fondo de algunos renglones enteros (las diferencias en «Comparar bases»).</summary>
public sealed class LineHighlighter(IBrush brush) : IBackgroundRenderer
{
    private HashSet<int> _lines = [];

    /// <summary>Los renglones marcados (desde 1, como en el editor).</summary>
    public IReadOnlyCollection<int> Lines => _lines;

    public KnownLayer Layer => KnownLayer.Background;

    public void SetLines(TextView view, IEnumerable<int> lines)
    {
        _lines = lines.ToHashSet();
        view.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines.Count == 0 || !textView.VisualLinesValid) return;
        foreach (var line in textView.VisualLines)
        {
            if (!_lines.Contains(line.FirstDocumentLine.LineNumber)) continue;
            var top = line.VisualTop - textView.VerticalOffset;
            drawingContext.FillRectangle(brush, new Rect(0, top, Math.Max(textView.Bounds.Width, 1), line.Height));
        }
    }
}
