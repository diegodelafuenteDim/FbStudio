using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using FbStudio.App.ResultGrid;
using FbStudio.Core.Results;
using Xunit.Abstractions;

namespace FbStudio.App.Tests;

/// <summary>
/// Los cuadros de edición que la grilla pone sobre lo dibujado (celda de Datos y fila de filtros): no dejan ver el texto
/// de abajo y escriben donde la grilla dibuja, en tema claro y oscuro. Se mira la imagen dibujada, como la ve el usuario.
/// </summary>
[Collection(nameof(UiCollection))]
public sealed class CellEditorTests(HeadlessFixture ui, ITestOutputHelper output)
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Fuente mínima que deja editar todo y anota lo que se guarda.</summary>
    private sealed class Source : IGridDataSource
    {
        public List<string> Saved { get; } = [];
        public void ViewChanged(ResultViewSpec spec) { }
        public void NearEnd() { }
        public string FooterText => "";
        public bool CanEdit(int column) => true;
        public bool TryGetPending(object[] row, int column, out object? value) { value = null; return false; }
        public bool IsDeleted(object[] row) => false;
        public bool IsChanged(object[] row) => false;
        public string EditText(object[] row, int column) => row[column]?.ToString() ?? "";
        public string? SetValue(object[] row, int column, string text) { Saved.Add(text); return null; }
        public void SetNull(object[] row, int column) { }
        public bool IsNew(object[] row) => false;
        public object[]? AddRow() => null;
        public void ToggleDelete(IReadOnlyList<object[]> rows) { }
        public void Revert(IReadOnlyList<object[]> rows) { }
        public bool CanListValues(int column) => false;
        public Task<IReadOnlyList<ValueCount>> DistinctValuesAsync(int column, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ValueCount>>([]);
    }

    private static readonly ColumnInfo[] Columns = [new(0, "ID", "INTEGER"), new(1, "VERSION", "VARCHAR"), new(2, "PROYECTO", "VARCHAR")];

    private static (Window W, DrawnGrid Grid, Source Source) Open(string theme, int rows = 5)
    {
        var grid = new DrawnGrid();
        var w = new Window
        {
            Content = grid, Width = 700, Height = 360,
            RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        w.Show();
        var data = new AvaloniaList<object[]>();
        for (var i = 0; i < rows; i++) data.Add([i + 1, i == 0 ? "v26.11.5" : $"v26.9.{i}", "Sistema web"]);
        grid.SetData(Columns, data);
        var source = new Source();
        grid.Source = source;
        Pump();
        return (w, grid, source);
    }

    private static Rect InWindow(Control control, Window w) => new(control.TranslatePoint(default, w)!.Value, control.Bounds.Size);

    private static void Near(Point expected, Point actual) =>
        Assert.True(Math.Abs(expected.X - actual.X) <= 1 && Math.Abs(expected.Y - actual.Y) <= 1, $"Se esperaba {expected} y quedó en {actual}.");

    /// <summary>Lo de adentro del cuadro, sin el borde ni el cursor (que con el cuadro vacío queda a la izquierda).</summary>
    private static Rect Inside(Rect box) => new(box.X + 10, box.Y + 3, box.Width - 13, box.Height - 6);

    /// <summary>Los píxeles de la ventana dibujada (sin escala en el modo sin pantalla: un píxel por unidad).</summary>
    private sealed class Frame
    {
        private readonly byte[] _bytes;
        private readonly int _stride;

        public Frame(Window w)
        {
            Pump();
            using var bitmap = w.CaptureRenderedFrame()!;
            using var buffer = bitmap.Lock();
            _stride = buffer.RowBytes;
            _bytes = new byte[_stride * buffer.Size.Height];
            Marshal.Copy(buffer.Address, _bytes, 0, _bytes.Length);
        }

        private int Pixel(int x, int y) => BitConverter.ToInt32(_bytes, y * _stride + x * 4);

        private static int Distance(int a, int b) =>
            Math.Abs((a & 0xFF) - (b & 0xFF)) + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) + Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));

        /// <summary>Los puntos con letra: los que se apartan del color de fondo (el más repetido) del recuadro.</summary>
        public HashSet<(int X, int Y)> Ink(Rect area)
        {
            var (x0, y0, x1, y1) = ((int)Math.Ceiling(area.X), (int)Math.Ceiling(area.Y), (int)Math.Floor(area.Right), (int)Math.Floor(area.Bottom));
            var counts = new Dictionary<int, int>();
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    counts[Pixel(x, y)] = counts.GetValueOrDefault(Pixel(x, y)) + 1;
            var background = counts.MaxBy(c => c.Value).Key;
            var ink = new HashSet<(int, int)>();
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                    if (Distance(Pixel(x, y), background) > 120) ink.Add((x - x0, y - y0));
            return ink;
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task El_editor_de_celda_tapa_el_texto_dibujado_y_escribe_donde_lo_dibuja_la_grilla(string theme) => ui.Session.DispatchAsync(() =>
    {
        var (w, grid, _) = Open(theme);
        grid.SelectCell(0, 1);                                                   // VERSION de la primera fila: «v26.11.5»
        Pump();
        var cell = grid.CellBounds(0, 1).Translate((Vector)grid.TranslatePoint(default, w)!.Value);
        var letters = new Rect(cell.X + 1, cell.Y + 2, 34, cell.Height - 4);    // las primeras letras, lejos del cursor
        var drawn = new Frame(w).Ink(letters);
        Assert.NotEmpty(drawn);

        // Vacío: dentro del cuadro no se ve nada del valor de la celda (antes se veía debajo, corrido y con otra letra).
        Assert.True(grid.OpenEditorAtSelection(""));
        Pump();
        var box = InWindow(grid.CellEditor!, w);
        Near(cell.Position, box.Position);                                      // el cuadro, justo sobre la celda
        Assert.Equal(cell.Height, box.Height);
        Assert.Empty(new Frame(w).Ink(Inside(box)));

        // Con lo mismo que tenía: las letras quedan donde estaban, con la misma letra y el mismo tamaño.
        grid.CancelCellEdit();
        Assert.True(grid.OpenEditorAtSelection("v26.11.5"));
        var typed = new Frame(w).Ink(letters);
        var same = drawn.Intersect(typed).Count();
        var union = drawn.Union(typed).Count();
        output.WriteLine($"{theme}: {drawn.Count} puntos dibujados, {typed.Count} escritos, {same} en común de {union}");
        Assert.True(same >= 0.8 * union, $"Las letras del cuadro no caen sobre las de la celda: {same} en común de {union}.");
        grid.CancelCellEdit();
        w.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task El_editor_de_filtro_tampoco_deja_ver_el_filtro_dibujado(string theme) => ui.Session.DispatchAsync(async () =>
    {
        var (w, grid, _) = Open(theme);
        grid.SetFilter(1, "v26");
        await grid.PendingBuild;
        Pump();
        grid.OpenFilterEditor(1);
        var editor = grid.FilterEditor!;
        editor.Text = " ";                                                      // en blanco (vacío mostraría la ayuda): no se ve «v26» debajo
        editor.CaretIndex = 0;
        Assert.Empty(new Frame(w).Ink(Inside(InWindow(editor, w))));
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task El_editor_de_celda_acompana_a_su_celda_al_desplazar_y_guarda_lo_escrito_si_sale_de_la_vista() => ui.Session.DispatchAsync(() =>
    {
        var (w, grid, source) = Open("Light", rows: 40);
        grid.SelectCell(4, 1);
        Assert.True(grid.OpenEditorAtSelection("nuevo"));
        Pump();
        var editor = grid.CellEditor!;
        Near(grid.CellBounds(4, 1).Position, editor.TranslatePoint(default, grid)!.Value);

        grid.Offset = new Vector(0, 2 * grid.RowHeight);                        // rueda o barra: dos filas para abajo
        Pump();
        Assert.True(grid.IsEditingCell);
        Near(grid.CellBounds(4, 1).Position, editor.TranslatePoint(default, grid)!.Value);
        Assert.Empty(source.Saved);

        grid.Offset = new Vector(0, 10 * grid.RowHeight);                       // la celda quedó arriba, fuera de la vista
        Pump();
        Assert.False(grid.IsEditingCell);                                       // no queda un cuadro tapando otra celda
        Assert.Equal("nuevo", Assert.Single(source.Saved));                     // y lo escrito no se pierde
        w.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);
}
