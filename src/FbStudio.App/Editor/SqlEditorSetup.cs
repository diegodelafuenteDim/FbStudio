using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using FbStudio.App.Resources;
using FbStudio.Core.Sql;
using TextMateSharp.Grammars;

namespace FbStudio.App.Editor;

/// <summary>
/// Resaltado SQL con TextMate + autocompletado (palabras clave, tablas, columnas por alias) + plantillas (F-32): la
/// abreviatura y Tab, o Ctrl+J para elegir de la lista.
/// </summary>
public sealed class SqlEditorSetup
{
    private readonly TextEditor _editor;
    private readonly Func<MetadataCatalog> _catalog;
    private readonly Func<IReadOnlyList<SqlTemplate>> _templates;
    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;
    private CompletionWindow? _window;
    private OverloadInsightWindow? _insight;
    private readonly SignatureProvider _signature = new();
    private FoldingManager? _folding;
    private Avalonia.Threading.DispatcherTimer? _foldingTimer;

    public SqlEditorSetup(TextEditor editor, Func<MetadataCatalog> catalog, Func<IReadOnlyList<SqlTemplate>>? templates = null)
    {
        _editor = editor;
        _catalog = catalog;
        _templates = templates ?? (() => SqlTemplates.Defaults);

        // El editor todavía no está en la ventana: su tema es el de la aplicación.
        Theme = ThemeFor(Avalonia.Application.Current?.ActualThemeVariant);
        _registry = new RegistryOptions(Theme);
        _textMate = editor.InstallTextMate(_registry);
        _textMate.SetGrammar(_registry.GetScopeByLanguageId("sql"));
        // Claro / oscuro al vuelo (Opciones → Tema, o el modo de Windows cambiando).
        editor.ActualThemeVariantChanged += (_, _) => ApplyTheme(ThemeFor(editor.ActualThemeVariant));

        editor.TextArea.TextEntered += OnTextEntered;
        editor.TextArea.KeyDown += OnKeyDown;
        // La ayuda de parámetros sigue al cursor: cambia de parámetro con las comas y se va al salir de los paréntesis.
        editor.TextArea.Caret.PositionChanged += (_, _) => UpdateSignature(open: false);
        // En túnel sobre el editor: corre antes que la lista de autocompletado procese Enter o Tab.
        editor.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Plegar bloques (BEGIN…END, CASE…END, paréntesis y comentarios de varios renglones) con las marcas del margen. Los
    /// bloques se recalculan medio segundo después de dejar de escribir.
    /// </summary>
    public bool Folding
    {
        get => _folding is not null;
        set
        {
            if (value == Folding) return;
            if (value)
            {
                _folding = FoldingManager.Install(_editor.TextArea);
                _foldingTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(500), Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
                {
                    _foldingTimer!.Stop();
                    UpdateFoldings();
                });
                _editor.Document.TextChanged += OnTextChangedForFolding;
                _editor.TextArea.DocumentChanged += OnDocumentChangedForFolding;
                UpdateFoldings();
            }
            else
            {
                _editor.Document.TextChanged -= OnTextChangedForFolding;
                _editor.TextArea.DocumentChanged -= OnDocumentChangedForFolding;
                _foldingTimer?.Stop();
                FoldingManager.Uninstall(_folding!);
                _folding = null;
            }
        }
    }

    /// <summary>Recalcula los bloques ya (sin esperar): lo usa el temporizador, y las pruebas.</summary>
    public void UpdateFoldings() =>
        _folding?.UpdateFoldings(SqlFolding.Find(_editor.Document.Text).Select(r => new NewFolding(r.Start, r.End)), -1);

    /// <summary>Los bloques que hay ahora (para las pruebas).</summary>
    public IReadOnlyList<(int Start, int End, bool Folded)> Foldings =>
        _folding?.AllFoldings.Select(f => (f.StartOffset, f.EndOffset, f.IsFolded)).ToList() ?? [];

    private void OnTextChangedForFolding(object? sender, EventArgs e)
    {
        _foldingTimer?.Stop();
        _foldingTimer?.Start();
    }

    // Otro documento en el editor: el administrador de plegado es de un documento, se vuelve a instalar.
    private void OnDocumentChangedForFolding(object? sender, EventArgs e)
    {
        Folding = false;
        Folding = true;
    }

    /// <summary>Tema de colores de TextMate en uso (Light+ o Dark+, los de VS Code).</summary>
    public ThemeName Theme { get; private set; }

    private static ThemeName ThemeFor(ThemeVariant? variant) => variant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus;

    private void ApplyTheme(ThemeName theme)
    {
        if (theme == Theme) return;
        Theme = theme;
        _textMate.SetTheme(_registry.LoadTheme(theme));
    }

    /// <summary>
    /// AvaloniaEdit inserta solo el elemento elegido, y la lista recién existe cuando se dibuja la ventanita: si se
    /// aprieta Enter o Tab sin haber escrito otra letra, no hay nada elegido y no inserta nada (solo andaba el clic).
    /// Se elige el que mejor coincide justo antes.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && _window is null && _editor.SelectionLength == 0 && ExpandTemplateAtCaret())
        {
            e.Handled = true;
            return;
        }
        if (_window is null || e.Key is not (Key.Enter or Key.Tab) || e.KeyModifiers != KeyModifiers.None) return;
        SelectBestMatch(_window);
    }

    /// <summary>Si lo escrito justo antes del cursor es la abreviatura de una plantilla, la reemplaza por la plantilla.</summary>
    public bool ExpandTemplateAtCaret()
    {
        var caret = _editor.CaretOffset;
        var (word, start) = SqlTemplates.WordBefore(_editor.Document.Text, caret);
        if (word.Length == 0 || SqlTemplates.Find(_templates(), word) is not { } template) return false;
        Insert(template, start, caret - start);
        return true;
    }

    /// <summary>Inserta la plantilla en lugar de [offset, offset + length) con la sangría del renglón, y deja el cursor donde dice.</summary>
    private void Insert(SqlTemplate template, int offset, int length)
    {
        var document = _editor.Document;
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line.Offset, line.Length);
        var indent = lineText[..(lineText.Length - lineText.TrimStart().Length)];
        var newLine = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        var (text, caret) = SqlTemplates.Expand(template, indent, newLine);
        document.Replace(offset, length, text);
        _editor.CaretOffset = offset + caret;
    }

    /// <summary>Ctrl+J: la lista de plantillas; lo que ya se escribió de la abreviatura la filtra.</summary>
    public void ShowTemplates()
    {
        _window?.Close();
        var templates = _templates();
        if (templates.Count == 0) return;
        var (_, start) = SqlTemplates.WordBefore(_editor.Document.Text, _editor.CaretOffset);
        _window = new CompletionWindow(_editor.TextArea) { StartOffset = start };
        foreach (var t in templates) _window.CompletionList.CompletionData.Add(new TemplateCompletionData(t, this));
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        var window = _window;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_window == window) SelectBestMatch(window); }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void SelectBestMatch(CompletionWindow window)
    {
        var list = window.CompletionList;
        if (list.SelectedItem is not null || list.CompletionData.Count == 0) return;
        var caret = _editor.CaretOffset;
        if (caret >= window.StartOffset) list.SelectItem(_editor.Document.GetText(window.StartOffset, caret - window.StartOffset));
        if (list.SelectedItem is null) list.SelectedItem = list.CompletionData[0];
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            ShowCompletion(force: true);
        }
        else if (e.Key == Key.J && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            ShowTemplates();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        var c = e.Text[0];
        if (c is '(' or ',' or ' ') UpdateSignature(open: true); // el espacio, por «EXECUTE PROCEDURE P a, b» sin paréntesis
        if (_window is not null) return;
        if (c == '.' || char.IsLetter(c)) ShowCompletion(force: false);
    }

    /// <summary>
    /// Ayuda de parámetros (F-33): dentro de los paréntesis de un procedimiento o una función del catálogo, muestra su
    /// firma con el parámetro actual resaltado. Se abre al escribir «(» o «,» (o el espacio después del nombre en un
    /// EXECUTE PROCEDURE sin paréntesis); después solo se actualiza o se cierra.
    /// </summary>
    private void UpdateSignature(bool open)
    {
        if (_insight is null && !open) return;
        var help = SqlCompletion.SignatureAt(_editor.Document.Text, _editor.CaretOffset, _catalog());
        if (help is null)
        {
            _insight?.Close();
            return;
        }
        _signature.Show(help);
        if (_insight is not null) return;
        _insight = new OverloadInsightWindow(_editor.TextArea) { Provider = _signature, CloseAutomatically = false, StartOffset = help.OpenParen };
        _insight.Closed += (_, _) => _insight = null;
        _insight.Show();
    }

    /// <summary>La firma que se está mostrando (para las pruebas).</summary>
    public SignatureHelp? CurrentSignature => _insight is null ? null : _signature.Help;

    /// <summary>Lo que muestra la ventanita: «P_X(DESDE DATE, **HASTA DATE = …**)» y si es procedimiento o función.</summary>
    private sealed class SignatureProvider : IOverloadProvider
    {
        public SignatureHelp? Help { get; private set; }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public int SelectedIndex { get; set; }
        public int Count => 1;
        public string? CurrentIndexText => null;
        public object? CurrentHeader { get; private set; }
        public object? CurrentContent { get; private set; }

        public void Show(SignatureHelp help)
        {
            Help = help;
            var s = help.Signature;
            var header = new Avalonia.Controls.TextBlock { FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace") };
            header.Inlines!.Add(new Avalonia.Controls.Documents.Run(s.Name + "("));
            for (var i = 0; i < s.Parameters.Count; i++)
            {
                if (i > 0) header.Inlines.Add(new Avalonia.Controls.Documents.Run(", "));
                header.Inlines.Add(new Avalonia.Controls.Documents.Run(s.Parameters[i].Text) { FontWeight = i == help.Argument ? FontWeight.Bold : FontWeight.Normal });
            }
            header.Inlines.Add(new Avalonia.Controls.Documents.Run(")" + (s.Returns is null ? "" : " RETURNS " + s.Returns)));
            CurrentHeader = header;
            CurrentContent = s.IsFunction ? Strings.SignatureFunction : Strings.SignatureProcedure;
            PropertyChanged?.Invoke(this, new(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentContent)));
        }
    }

    private void ShowCompletion(bool force)
    {
        var text = _editor.Document.Text;
        var caret = _editor.CaretOffset;
        var (start, items) = SqlCompletion.Suggest(text, caret, _catalog());
        if (items.Count == 0 || (!force && caret - start < 2 && !(start > 0 && text[start - 1] == '.'))) return;

        _window = new CompletionWindow(_editor.TextArea) { StartOffset = start };
        foreach (var item in items) _window.CompletionList.CompletionData.Add(new SqlCompletionData(item));
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        // Cuando la lista ya está dibujada, se marca el que se va a insertar (así se ve antes de apretar Enter).
        var window = _window;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_window == window) SelectBestMatch(window); }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private sealed class TemplateCompletionData(SqlTemplate template, SqlEditorSetup setup) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => template.Abbreviation;
        public object Content => $"{template.Abbreviation}   {template.Name}";
        public object? Description => template.Text.Replace(SqlTemplates.Cursor, "|");
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            setup.Insert(template, completionSegment.Offset, completionSegment.Length);
    }

    private sealed class SqlCompletionData(CompletionItem item) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => item.Text;
        public object Content => item.Kind switch
        {
            CompletionKind.Keyword => item.Text,
            CompletionKind.Table when item.Detail == "WITH" => $"{item.Text}   ({Strings.CompletionWith})",
            CompletionKind.Table => $"{item.Text}   ({string.Format(Strings.CompletionTable, item.Detail)})",
            CompletionKind.Procedure or CompletionKind.Function when item.Detail is not null =>
                $"{item.Text}   ({(item.Kind == CompletionKind.Function ? Strings.CompletionFunction : Strings.CompletionProcedure)}, {string.Format(Strings.CompletionInPackage, item.Detail)})",
            CompletionKind.Procedure => $"{item.Text}   ({Strings.CompletionProcedure})",
            CompletionKind.Function => $"{item.Text}   ({Strings.CompletionFunction})",
            CompletionKind.Package => $"{item.Text}   ({Strings.CompletionPackage})",
            CompletionKind.Generator => $"{item.Text}   ({Strings.CompletionGenerator})",
            CompletionKind.Exception => $"{item.Text}   ({Strings.CompletionException})",
            CompletionKind.Join => $"{item.Text}   ({string.Format(Strings.CompletionJoin, item.Detail)})",
            _ => $"{item.Text}   ({item.Detail})",
        };
        public object? Description => item.Kind.ToString();
        public double Priority => item.Kind switch
        {
            CompletionKind.Join => 3,
            CompletionKind.Column => 2,
            CompletionKind.Table or CompletionKind.Procedure or CompletionKind.Function or CompletionKind.Generator or CompletionKind.Exception => 1,
            _ => 0,
        };

        // Los nombres de objetos y columnas van con comillas si hacen falta (minúsculas, palabras reservadas); un JOIN ya viene armado.
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, item.Kind is CompletionKind.Keyword or CompletionKind.Join ? Text : SqlIdentifier.Quote(Text));
    }
}
