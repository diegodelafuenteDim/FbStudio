using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.Editor;
using FbStudio.App.ViewModels;
using FbStudio.Core.Sql;
using FbStudio.Workspace;

namespace FbStudio.App.Views;

/// <summary>
/// Editor + resultados de una pestaña. La ventana crea una vista por pestaña y la mantiene viva mientras la pestaña
/// exista (solo cambia la visibilidad), así no se pierden el deshacer del editor ni el estado de la grilla.
/// </summary>
public partial class EditorTabView : UserControl
{
    private SqlEditorSetup? _editorSetup;
    private MainWindowViewModel? _owner;
    private double _fontSize = 14; // el elegido en Opciones (Ctrl+0 vuelve a este)
    private bool _zoomWithWheel = true;

    public EditorTabView()
    {
        InitializeComponent();
        FindBar = new FindReplaceBar(Editor);
        FindHost.Content = FindBar;
        UsesList.DoubleTapped += async (_, _) => { if (UsesList.SelectedItem is DependencyListItem { Row: { } row } && Tab is { } t) await t.OpenDependencyAsync(row); };
        UsedByList.DoubleTapped += async (_, _) => { if (UsedByList.SelectedItem is DependencyListItem { Row: { } row } && Tab is { } t) await t.OpenDependencyAsync(row); };
        // Ctrl+rueda: zoom de la letra del editor (en túnel: antes de que el editor lo use para desplazarse).
        Editor.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!_zoomWithWheel || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0) return;
            Editor.FontSize = Math.Clamp(Editor.FontSize + (e.Delta.Y > 0 ? 1 : -1), 8, 40);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Editor.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key is Key.D0 or Key.NumPad0 && e.KeyModifiers == KeyModifiers.Control)
            {
                Editor.FontSize = _fontSize;
                e.Handled = true;
            }
            else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                await GoToDefinitionAtCaretAsync();
            }
            else if (e.Key == Key.F3 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
            {
                e.Handled = true;
                FindBar.Go(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            }
            else if (e.Key == Key.Escape && FindBar.IsVisible && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                FindBar.Close();
            }
            // Ctrl+/ (o la tecla de esa posición, «-» en el teclado español) comenta; Ctrl+D duplica; Ctrl+U y
            // Ctrl+Shift+U, minúsculas y mayúsculas; Alt+↑ y Alt+↓ mueven los renglones.
            else if (e.KeyModifiers == KeyModifiers.Control && (e.Key is Key.OemQuestion or Key.Divide || e.PhysicalKey is PhysicalKey.Slash or PhysicalKey.NumPadDivide))
            {
                e.Handled = true;
                ToggleComment();
            }
            else if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                DuplicateLine();
            }
            else if (e.Key == Key.U && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
            {
                e.Handled = true;
                ChangeCase(upper: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
            else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
            {
                e.Handled = true;
                MoveLines(up: e.Key == Key.Up);
            }
        }, RoutingStrategies.Tunnel);
        // Ctrl+clic sobre un nombre abre el objeto (F-32); con Ctrl apretado, el puntero lo anticipa con la manito.
        Editor.TextArea.AddHandler(PointerPressedEvent, async (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.GetCurrentPoint(Editor.TextArea).Properties.IsLeftButtonPressed
                || Tab is not { } tab || OffsetAt(e.GetPosition(Editor)) is not { } offset || SqlNavigation.NameAt(Editor.Text, offset) is null) return;
            e.Handled = true;
            await tab.GoToDefinitionAsync(Editor.Text, offset);
        }, RoutingStrategies.Tunnel);
        Editor.TextArea.PointerMoved += (_, e) =>
        {
            var link = e.KeyModifiers.HasFlag(KeyModifiers.Control) && OffsetAt(e.GetPosition(Editor)) is { } offset && SqlNavigation.NameAt(Editor.Text, offset) is not null;
            Editor.TextArea.TextView.Cursor = link ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Ibeam);
        };
        DataWhere.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Tab?.Data is { } data)
            {
                e.Handled = true;
                await data.ReloadAsync();
            }
        };
        SaveLogButton.Click += async (_, _) => await SaveLogAsync();
        DataNewRow.Click += (_, _) => DataGrid.AddRowAndEdit();
        CopyLogButton.Click += async (_, _) =>
        {
            if (Tab is not { } tab || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(tab.LogForFile(DateTime.Now));
            tab.Status = Strings.LogCopied;
        };
        HistoryList.DoubleTapped += (_, _) =>
        {
            if (HistoryList.SelectedItem is not HistoryEntry h) return;
            Editor.Text = h.Sql;
            Editor.CaretOffset = h.Sql.Length;
            Editor.Focus();
        };
    }

    public EditorTabViewModel? Tab => DataContext as EditorTabViewModel;

    /// <summary>«Guardar registro…»: los mensajes de la última ejecución (un script, por ejemplo) en un .txt.</summary>
    private async Task SaveLogAsync()
    {
        if (Tab is not { } tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Strings.SaveLogTitle,
            SuggestedFileName = $"{Strings.LogFileName}-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(Strings.LogFileType) { Patterns = ["*.txt", "*.log"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, tab.LogForFile(DateTime.Now), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            tab.Status = string.Format(Strings.LogSaved, path);
        }
        catch (Exception ex)
        {
            tab.Status = string.Format(Strings.ErrorStatus, ex.Message);
        }
    }

    /// <summary>«Copiar como INSERT / UPDATE»: las filas como se ven, con la tabla y la clave que resuelve la pestaña.</summary>
    private async Task CopyAsScriptAsync(ResultGrid.DrawnGrid grid, IReadOnlyList<int> rows, bool update, bool fromData)
    {
        if (Tab is not { } tab) return;
        var (columns, values) = grid.DisplayedRows(rows);
        var (text, message) = await tab.RowScriptAsync(columns, values, update, fromData);
        if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        tab.Status = message;
    }

    /// <summary>El autocompletado y la ayuda de parámetros de este editor (para las pruebas).</summary>
    internal SqlEditorSetup? EditorSetup => _editorSetup;

    /// <summary>Tema del resaltado (para las pruebas: tiene que seguir al claro/oscuro).</summary>
    public string? EditorTheme => _editorSetup?.Theme.ToString();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Tab is not { } tab) return;
        Editor.Document = tab.Document;
        Editor.IsReadOnly = tab.IsReadOnlyObject; // el DDL de una tabla se ve y se copia, no se edita ni se ejecuta
        tab.ColumnsChanged += cols => Results.SetData(cols, tab.Rows);
        Results.CopyAsScript = (rows, update) => CopyAsScriptAsync(Results, rows, update, fromData: false);
        DataGrid.CopyAsScript = (rows, update) => CopyAsScriptAsync(DataGrid, rows, update, fromData: true);
        tab.GoToRequested += GoTo;
        _editorSetup ??= new SqlEditorSetup(Editor, () => tab.Catalog, () => tab.Owner.Options.EffectiveTemplates);
        if (tab.Data is { } data && DataGrid.Source is null)
        {
            DataGrid.Source = data;
            data.ColumnsChanged += cols =>
            {
                DataGrid.SetData(cols, data.Rows);
                DataGrid.ApplyColumnLayout(data.SavedLayout()); // como quedó la última vez en esta tabla
            };
            DataGrid.ColumnLayoutChanged += () => data.SaveLayout(DataGrid.ColumnLayout());
            data.Redraw += DataGrid.RefreshCells;
        }
        if (!ReferenceEquals(_owner, tab.Owner))
        {
            if (_owner is not null) _owner.SettingsChanged -= ApplySettings;
            _owner = tab.Owner;
            _owner.SettingsChanged += ApplySettings;
        }
        ApplySettings(tab.Owner.Options);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // La pestaña se cerró: que la ventana no siga avisándole a esta vista.
        if (_owner is not null) _owner.SettingsChanged -= ApplySettings;
        _owner = null;
    }

    /// <summary>Buscar y reemplazar en este editor (Ctrl+F, Ctrl+H, F3).</summary>
    public FindReplaceBar FindBar { get; }

    /// <summary>Ctrl+F o Ctrl+H (Consulta → Buscar / Reemplazar).</summary>
    public void OpenFind(bool replace) => FindBar.Open(replace);

    /// <summary>Letra, tabulación y demás opciones del editor (al abrir la pestaña y al aceptar Opciones).</summary>
    public void ApplySettings(AppSettings s)
    {
        Editor.FontFamily = new FontFamily($"{s.EditorFontFamily},Cascadia Mono,Consolas,Menlo,monospace");
        Editor.FontSize = _fontSize = s.EditorFontSize;
        Editor.ShowLineNumbers = s.ShowLineNumbers;
        Editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;
        Editor.Options.IndentationSize = s.TabSize;
        Editor.Options.ConvertTabsToSpaces = s.ConvertTabsToSpaces;
        Editor.Options.AllowScrollBelowDocument = s.ScrollPastEnd;
        Editor.Options.EnableTextDragDrop = true;  // una tabla o una columna arrastradas del árbol escriben su nombre donde se sueltan
        _zoomWithWheel = s.ZoomWithCtrlWheel;
        if (_editorSetup is not null) _editorSetup.Folding = s.ShowFolding;
    }

    private int? OffsetAt(Point point) =>
        Editor.GetPositionFromPoint(point) is { } position ? Editor.Document.GetOffset(position.Location) : null;

    /// <summary>F12 o Consulta → «Abrir el objeto»: el nombre donde está el cursor.</summary>
    public Task<bool> GoToDefinitionAtCaretAsync() =>
        Tab is { } tab ? tab.GoToDefinitionAsync(Editor.Text, Editor.CaretOffset) : Task.FromResult(false);

    /// <summary>Ctrl+J o Consulta → «Insertar plantilla…».</summary>
    public void ShowTemplates()
    {
        if (Tab is { } tab && tab.Owner.Options.EffectiveTemplates.Count == 0)
        {
            tab.Status = Strings.TemplateNone;
            return;
        }
        Editor.Focus();
        _editorSetup?.ShowTemplates();
    }

    /// <summary>Tab después de una abreviatura (para las pruebas: lo mismo que apretar Tab).</summary>
    public bool ExpandTemplateAtCaret() => _editorSetup?.ExpandTemplateAtCaret() == true;

    /// <summary>
    /// Ctrl+Shift+L o Consulta → «Formatear SQL» (F-32): la selección o, sin selección, todo el editor. Solo cambian las
    /// consultas; se deshace con Ctrl+Z.
    /// </summary>
    public void FormatSql()
    {
        if (Tab is not { } tab) return;
        var selection = Editor.SelectionLength > 0;
        var start = selection ? Editor.SelectionStart : 0;
        var length = selection ? Editor.SelectionLength : Editor.Document.TextLength;
        var original = Editor.Document.GetText(start, length);
        var newLine = AvaloniaEdit.Document.TextUtilities.GetNewLineFromDocument(Editor.Document, 1);
        var formatted = SqlFormatter.Format(original, new SqlFormatOptions(tab.Owner.Options.FormatUppercaseKeywords))
            .Replace("\r\n", "\n").Replace("\n", newLine);
        if (formatted == original)
        {
            tab.Status = Strings.FormatNothing;
            return;
        }
        var caret = Editor.CaretOffset;
        Editor.Document.Replace(start, length, formatted);
        if (selection) Editor.Select(start, formatted.Length);
        else Editor.CaretOffset = Math.Min(caret, Editor.Document.TextLength);
        tab.Status = Strings.FormatDone;
    }

    // ── Comandos de edición (el cálculo, en LineEdits; acá se aplica de una vez: un solo Ctrl+Z) ──

    public void ToggleComment() => Apply(LineEdits.ToggleComment(Editor.Text, Editor.SelectionStart, Editor.SelectionLength));

    public void DuplicateLine() => Apply(LineEdits.DuplicateLine(Editor.Text, Editor.SelectionStart, Editor.SelectionLength));

    public void MoveLines(bool up) => Apply(LineEdits.MoveLines(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, up));

    public void ChangeCase(bool upper) => Apply(LineEdits.ChangeCase(Editor.Text, Editor.SelectionStart, Editor.SelectionLength, upper));

    private void Apply(TextEdit? edit)
    {
        if (edit is not { } e || Editor.IsReadOnly) return;
        Editor.Document.Replace(e.Start, e.Length, e.Text);
        if (e.SelectionLength > 0) Editor.Select(e.SelectionStart, e.SelectionLength);
        else
        {
            Editor.SelectionLength = 0;
            Editor.CaretOffset = e.SelectionStart;
        }
        Editor.TextArea.Caret.BringCaretToView();
    }

    public void InsertAtCaret(string text)
    {
        Editor.Document.Insert(Editor.CaretOffset, text);
        Editor.Focus();
    }

    /// <summary>Lleva el cursor a una línea y columna (1-based) y la deja a la vista.</summary>
    public void GoTo(int line, int column)
    {
        line = Math.Clamp(line, 1, Editor.Document.LineCount);
        var docLine = Editor.Document.GetLineByNumber(line);
        // Sin seleccionar: en una pestaña de objeto, F9 con selección ejecutaría solo eso en vez de compilar.
        Editor.SelectionLength = 0;
        Editor.CaretOffset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
        Dispatcher.UIThread.Post(() =>
        {
            Editor.ScrollTo(line, Math.Max(1, column));
            Editor.Focus();
        });
    }
}
