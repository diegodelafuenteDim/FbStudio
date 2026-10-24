using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Workspace;

namespace FbStudio.App.ViewModels;

/// <summary>Una plantilla mientras se edita en Opciones.</summary>
public sealed partial class TemplateEdit : ObservableObject
{
    [ObservableProperty] private string _abbreviation = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _text = "";
}

/// <summary>Copia editable de las opciones del sistema: nada cambia hasta «Aceptar».</summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    /// <summary>Letras de ancho fijo conocidas; se ofrecen las que estén instaladas.</summary>
    private static readonly string[] MonospaceFonts =
        ["Cascadia Mono", "Cascadia Code", "Consolas", "JetBrains Mono", "Fira Code", "Source Code Pro", "Courier New", "Lucida Console"];

    public OptionsViewModel(AppSettings current)
    {
        var installed = InstalledFonts();
        Fonts = MonospaceFonts.Where(f => installed is null || installed.Contains(f))
            .Append(current.EditorFontFamily).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Load(current);
    }

    public IReadOnlyList<int> RowLimits => AppSettings.RowLimits;
    public IReadOnlyList<string> Themes { get; } = [Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark];

    /// <summary>En el mismo orden que <see cref="UiLanguage.Codes"/>; cada idioma con su propio nombre.</summary>
    public IReadOnlyList<string> Languages { get; } = [Strings.LanguageSystem, "Español", "English"];

    [ObservableProperty] private int _languageIndex;
    public IReadOnlyList<string> Fonts { get; }

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private decimal? _historyRetentionDays;
    [ObservableProperty] private int _defaultRowLimit;
    [ObservableProperty] private decimal? _lockTimeoutSeconds;
    [ObservableProperty] private string _editorFontFamily = AppSettings.DefaultFont;
    [ObservableProperty] private decimal? _editorFontSize;
    [ObservableProperty] private decimal? _tabSize;
    [ObservableProperty] private bool _convertTabsToSpaces;
    [ObservableProperty] private bool _showLineNumbers;
    [ObservableProperty] private bool _highlightCurrentLine;
    [ObservableProperty] private bool _scrollPastEnd;
    [ObservableProperty] private bool _zoomWithCtrlWheel;
    [ObservableProperty] private bool _formatUppercaseKeywords;

    /// <summary>Plantillas del editor, editables (F-32).</summary>
    public System.Collections.ObjectModel.ObservableCollection<TemplateEdit> Templates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTemplateCommand))]
    private TemplateEdit? _selectedTemplate;

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    [RelayCommand]
    private void AddTemplate()
    {
        var n = 1;
        while (Templates.Any(t => t.Abbreviation == Strings.OptionsTemplateNew + n)) n++;
        var template = new TemplateEdit { Abbreviation = Strings.OptionsTemplateNew + n, Text = FbStudio.Core.Sql.SqlTemplates.Cursor };
        Templates.Add(template);
        SelectedTemplate = template;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTemplate))]
    private void DeleteTemplate()
    {
        if (SelectedTemplate is not { } t) return;
        var index = Templates.IndexOf(t);
        Templates.Remove(t);
        SelectedTemplate = Templates.Count == 0 ? null : Templates[Math.Clamp(index, 0, Templates.Count - 1)];
    }

    [RelayCommand]
    private void DefaultTemplates() => LoadTemplates(FbStudio.Core.Sql.SqlTemplates.Defaults);

    private void LoadTemplates(IEnumerable<FbStudio.Core.Sql.SqlTemplate> templates)
    {
        Templates.Clear();
        foreach (var t in templates) Templates.Add(new TemplateEdit { Abbreviation = t.Abbreviation, Name = t.Name, Text = t.Text });
        SelectedTemplate = Templates.FirstOrDefault();
    }

    /// <summary>Las plantillas como quedaron; null si son las de fábrica.</summary>
    private IReadOnlyList<FbStudio.Core.Sql.SqlTemplate>? TemplatesToSave()
    {
        var list = Templates.Select(t => new FbStudio.Core.Sql.SqlTemplate(t.Abbreviation.Trim(), t.Name.Trim(), t.Text)).ToList();
        return list.SequenceEqual(FbStudio.Core.Sql.SqlTemplates.Defaults) ? null : list;
    }
    [ObservableProperty] private decimal? _oatWarnMinutes;
    [ObservableProperty] private decimal? _oatDangerMinutes;
    [ObservableProperty] private bool _oatIgnoreMaintenance;

    /// <summary>Pestaña Datos: true = mostrar los comandos antes de ejecutar; false = ejecutar directamente.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyDirectly))]
    private bool _previewDataChanges;

    public bool ApplyDirectly
    {
        get => !PreviewDataChanges;
        set => PreviewDataChanges = !value;
    }

    /// <summary>Scripts: true = parar en el primer error; false = seguir con la siguiente sentencia.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScriptContinueOnError))]
    private bool _scriptStopOnError;

    public bool ScriptContinueOnError
    {
        get => !ScriptStopOnError;
        set => ScriptStopOnError = !value;
    }

    private void Load(AppSettings s)
    {
        ThemeIndex = (int)s.Theme;
        LanguageIndex = Math.Max(0, UiLanguage.Codes.ToList().IndexOf(s.Language));
        HistoryRetentionDays = s.HistoryRetentionDays;
        DefaultRowLimit = s.DefaultRowLimit;
        LockTimeoutSeconds = s.LockTimeoutSeconds;
        EditorFontFamily = s.EditorFontFamily;
        EditorFontSize = (decimal)s.EditorFontSize;
        TabSize = s.TabSize;
        ConvertTabsToSpaces = s.ConvertTabsToSpaces;
        ShowLineNumbers = s.ShowLineNumbers;
        HighlightCurrentLine = s.HighlightCurrentLine;
        ScrollPastEnd = s.ScrollPastEnd;
        ZoomWithCtrlWheel = s.ZoomWithCtrlWheel;
        FormatUppercaseKeywords = s.FormatUppercaseKeywords;
        LoadTemplates(s.EffectiveTemplates);
        PreviewDataChanges = s.PreviewDataChanges;
        ScriptStopOnError = s.ScriptStopOnError;
        OatWarnMinutes = s.OatWarnMinutes;
        OatDangerMinutes = s.OatDangerMinutes;
        OatIgnoreMaintenance = s.OatIgnoreMaintenance;
    }

    /// <summary>Un campo numérico vacío vuelve a su valor por defecto.</summary>
    public AppSettings ToSettings()
    {
        var d = new AppSettings();
        return new AppSettings
        {
            Theme = (AppTheme)Math.Clamp(ThemeIndex, 0, Themes.Count - 1),
            Language = UiLanguage.Codes[Math.Clamp(LanguageIndex, 0, UiLanguage.Codes.Count - 1)],
            HistoryRetentionDays = (int?)HistoryRetentionDays ?? d.HistoryRetentionDays,
            DefaultRowLimit = DefaultRowLimit,
            LockTimeoutSeconds = (int?)LockTimeoutSeconds ?? d.LockTimeoutSeconds,
            EditorFontFamily = EditorFontFamily,
            EditorFontSize = (double?)EditorFontSize ?? d.EditorFontSize,
            TabSize = (int?)TabSize ?? d.TabSize,
            ConvertTabsToSpaces = ConvertTabsToSpaces,
            ShowLineNumbers = ShowLineNumbers,
            HighlightCurrentLine = HighlightCurrentLine,
            ScrollPastEnd = ScrollPastEnd,
            ZoomWithCtrlWheel = ZoomWithCtrlWheel,
            FormatUppercaseKeywords = FormatUppercaseKeywords,
            Templates = TemplatesToSave(),
            PreviewDataChanges = PreviewDataChanges,
            ScriptStopOnError = ScriptStopOnError,
            OatWarnMinutes = (int?)OatWarnMinutes ?? d.OatWarnMinutes,
            OatDangerMinutes = (int?)OatDangerMinutes ?? d.OatDangerMinutes,
            OatIgnoreMaintenance = OatIgnoreMaintenance,
        }.Normalized();
    }

    [RelayCommand]
    private void ResetDefaults() => Load(new AppSettings());

    private static HashSet<string>? InstalledFonts()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return null; // sin poder listar las fuentes (p. ej. en pruebas sin ventana), se ofrecen todas
        }
    }
}
