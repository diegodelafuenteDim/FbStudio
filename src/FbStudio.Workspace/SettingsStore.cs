using System.Text.Json;
using FbStudio.Core.Resources;

namespace FbStudio.Workspace;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Opciones del sistema (no de cada base). Los valores por defecto son los que FbStudio usaba fijos, así que quien
/// nunca abre la ventana de opciones sigue igual.
/// </summary>
public sealed record AppSettings
{
    public const string DefaultFont = "Cascadia Mono";

    public static IReadOnlyList<int> RowLimits { get; } = [1_000, 10_000, 100_000, 1_000_000];

    /// <summary>Días que se guardan las sentencias en el historial (se purga al abrir y al cambiar la opción).</summary>
    public int HistoryRetentionDays { get; init; } = 90;

    /// <summary>Filas que trae un SELECT de entrada (la barra de herramientas lo cambia para la sesión).</summary>
    public int DefaultRowLimit { get; init; } = 10_000;

    /// <summary>
    /// Pestaña Datos: «Aplicar» muestra antes los comandos (para ejecutarlos, copiarlos o llevarlos a una pestaña de
    /// consulta) en vez de ejecutarlos directamente.
    /// </summary>
    public bool PreviewDataChanges { get; init; } = true;

    /// <summary>Un script se detiene en el primer error; false = sigue con la siguiente sentencia.</summary>
    public bool ScriptStopOnError { get; init; } = true;

    /// <summary>Monitoreo: la OAT abierta hace más de estos minutos se pinta en ámbar…</summary>
    public int OatWarnMinutes { get; init; } = 2;

    /// <summary>…y pasados estos, en rojo.</summary>
    public int OatDangerMinutes { get; init; } = 10;

    /// <summary>Si la OAT es de un backup o un sweep (gbak, gfix, nbackup), se informa sin alarma.</summary>
    public bool OatIgnoreMaintenance { get; init; } = true;

    /// <summary>Cuánto espera una escritura un registro bloqueado por otra transacción antes de dar error.</summary>
    public int LockTimeoutSeconds { get; init; } = 10;

    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Idioma de la interfaz: "" igual que Windows, "es" o "en". Se aplica al volver a abrir FbStudio.</summary>
    public string Language { get; init; } = "";

    public string EditorFontFamily { get; init; } = DefaultFont;
    public double EditorFontSize { get; init; } = 14;
    public int TabSize { get; init; } = 4;
    public bool ConvertTabsToSpaces { get; init; } = true;
    public bool ShowLineNumbers { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;

    /// <summary>Poder bajar más allá de la última línea.</summary>
    public bool ScrollPastEnd { get; init; } = true;

    /// <summary>Ctrl+rueda del mouse agranda o achica la letra del editor (Ctrl+0 la vuelve a la elegida acá).</summary>
    public bool ZoomWithCtrlWheel { get; init; } = true;

    /// <summary>«Formatear SQL» pasa las palabras clave a mayúsculas (si no, quedan como se escribieron).</summary>
    public bool FormatUppercaseKeywords { get; init; } = true;

    /// <summary>Las plantillas del editor; null = las de fábrica (así, si mejoran, se ven sin tocar nada).</summary>
    public IReadOnlyList<FbStudio.Core.Sql.SqlTemplate>? Templates { get; init; }

    /// <summary>Lleva cada valor a un rango razonable (un settings.json editado a mano no puede romper la aplicación).</summary>
    public AppSettings Normalized() => this with
    {
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 3650),
        DefaultRowLimit = RowLimits.Contains(DefaultRowLimit) ? DefaultRowLimit : 10_000,
        LockTimeoutSeconds = Math.Clamp(LockTimeoutSeconds, 1, 600),
        OatWarnMinutes = Math.Clamp(OatWarnMinutes, 1, 1440),
        OatDangerMinutes = Math.Clamp(OatDangerMinutes, Math.Clamp(OatWarnMinutes, 1, 1440), 10080),
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        Language = Language is "es" or "en" ? Language : "",
        EditorFontFamily = string.IsNullOrWhiteSpace(EditorFontFamily) ? DefaultFont : EditorFontFamily.Trim(),
        EditorFontSize = Math.Clamp(EditorFontSize, 8, 40),
        TabSize = Math.Clamp(TabSize, 1, 16),
        Templates = Templates?.Where(t => !string.IsNullOrWhiteSpace(t.Abbreviation) && t.Text is not null)
            .Select(t => t with { Abbreviation = t.Abbreviation.Trim(), Name = t.Name?.Trim() ?? "" })
            .DistinctBy(t => t.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList(),
    };

    /// <summary>Las plantillas que valen: las guardadas o, si no hay, las de fábrica.</summary>
    public IReadOnlyList<FbStudio.Core.Sql.SqlTemplate> EffectiveTemplates => Templates ?? FbStudio.Core.Sql.SqlTemplates.Defaults;
}

/// <summary>Opciones del sistema en %APPDATA%\FbStudio\settings.json.</summary>
public sealed class SettingsStore
{
    private readonly string _folder;

    public SettingsStore(string folder)
    {
        _folder = folder;
        Current = Load();
    }

    public string FilePath => Path.Combine(_folder, "settings.json");

    public AppSettings Current { get; private set; }

    /// <summary>Si el archivo existía pero no se pudo leer (se usan los valores por defecto y se reescribe al guardar).</summary>
    public string? LoadError { get; private set; }

    /// <summary>Se dispara después de guardar, con las opciones ya normalizadas.</summary>
    public event Action<AppSettings>? Changed;

    private AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllText(FilePath), WorkspaceJsonContext.Default.AppSettings) ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = string.Format(CoreStrings.SettingsLoadFailed, e.Message);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings = settings.Normalized();
        PrivateFiles.EnsureFolder(_folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, WorkspaceJsonContext.Default.AppSettings));
        PrivateFiles.Restrict(tmp);
        File.Move(tmp, FilePath, overwrite: true);
        Current = settings;
        LoadError = null;
        Changed?.Invoke(settings);
    }
}
