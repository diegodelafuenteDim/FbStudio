using System.Text.Json;
using FbStudio.Core.Profiles;
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
    public int HistoryRetentionDays { get; set; } = 90;

    /// <summary>Filas que trae un SELECT de entrada (la barra de herramientas lo cambia para la sesión).</summary>
    public int DefaultRowLimit { get; set; } = 10_000;

    /// <summary>
    /// Pestaña Datos: «Aplicar» muestra antes los comandos (para ejecutarlos, copiarlos o llevarlos a una pestaña de
    /// consulta) en vez de ejecutarlos directamente.
    /// </summary>
    public bool PreviewDataChanges { get; set; } = true;

    /// <summary>
    /// El script de los cambios de la pestaña Datos (para copiarlo o llevarlo a una consulta) termina con COMMIT. Se
    /// recuerda lo último elegido en la ventana de los comandos; de fábrica no, para confirmarlo a mano.
    /// </summary>
    public bool DataScriptWithCommit { get; init; }

    /// <summary>Pestaña Datos: con un filtro o un WHERE, cuenta solas las filas que coinciden (SELECT COUNT en el servidor).</summary>
    public bool DataCountOnFilter { get; init; }

    /// <summary>Un script se detiene en el primer error; false = sigue con la siguiente sentencia.</summary>
    public bool ScriptStopOnError { get; set; } = true;

    /// <summary>Monitoreo: la OAT abierta hace más de estos minutos se pinta en ámbar…</summary>
    public int OatWarnMinutes { get; set; } = 2;

    /// <summary>…y pasados estos, en rojo.</summary>
    public int OatDangerMinutes { get; set; } = 10;

    /// <summary>Si la OAT es de un backup o un sweep (gbak, gfix, nbackup), se informa sin alarma.</summary>
    public bool OatIgnoreMaintenance { get; set; } = true;

    /// <summary>
    /// Una transacción de escritura abierta en una pestaña hace más de estos minutos se marca en ámbar… (con varias
    /// personas, una olvidada traba la OAT y la recolección de basura del servidor).
    /// </summary>
    public int TransactionWarnMinutes { get; set; } = 5;

    /// <summary>…y pasados estos, en rojo, con un aviso.</summary>
    public int TransactionDangerMinutes { get; set; } = 15;

    /// <summary>Cuánto espera una escritura un registro bloqueado por otra transacción antes de dar error.</summary>
    public int LockTimeoutSeconds { get; set; } = 10;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Color propio de cada entorno (F-20), de la paleta; sin entrada (o null), el de siempre.</summary>
    public Dictionary<ProfileEnvironment, string>? EnvironmentColors { get; init; }

    /// <summary>Color propio de cada carpeta del árbol, por su ruta («Clientes/Norte»), sin distinguir mayúsculas; null si ninguna.</summary>
    public Dictionary<string, string>? FolderColors { get; init; }

    /// <summary>Idioma de la interfaz: "" igual que Windows, "es" o "en". Se aplica al volver a abrir FbStudio.</summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// La última versión de FbStudio que se abrió en este equipo (D-85): con una nueva, Ayuda → Novedades se abre sola
    /// una vez. Null en una instalación nueva (o anterior a esto).
    /// </summary>
    public string? LastSeenVersion { get; init; }

    public string EditorFontFamily { get; set; } = DefaultFont;
    public double EditorFontSize { get; set; } = 14;
    public int TabSize { get; set; } = 4;
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightCurrentLine { get; set; } = true;

    /// <summary>Poder bajar más allá de la última línea.</summary>
    public bool ScrollPastEnd { get; set; } = true;

    /// <summary>Ctrl+rueda del mouse agranda o achica la letra del editor (Ctrl+0 la vuelve a la elegida acá).</summary>
    public bool ZoomWithCtrlWheel { get; set; } = true;

    /// <summary>Marcas en el margen para plegar bloques (BEGIN…END, CASE…END, paréntesis y comentarios largos).</summary>
    public bool ShowFolding { get; set; } = true;

    /// <summary>«Formatear SQL» pasa las palabras clave a mayúsculas (si no, quedan como se escribieron).</summary>
    public bool FormatUppercaseKeywords { get; set; } = true;

    /// <summary>Al cerrar, guardar las pestañas de consulta (con su texto y su base) para volver a abrirlas la próxima vez.</summary>
    public bool RestoreTabs { get; set; } = true;

    /// <summary>Largo máximo de cada respuesta del conector MCP (D-114): más que esto se recorta, con un aviso.</summary>
    public int McpResponseLimit { get; set; } = 40_000;

    /// <summary>En el monitoreo del conector MCP, mostrar usuario, equipo y programa de cada conexión (D-118).</summary>
    public bool McpShowClients { get; set; } = true;

    /// <summary>Los últimos archivos .sql abiertos o guardados, el más reciente primero; null si ninguno.</summary>
    public IReadOnlyList<string>? RecentFiles { get; init; }

    /// <summary>Las plantillas del editor; null = las de fábrica (así, si mejoran, se ven sin tocar nada).</summary>
    public IReadOnlyList<FbStudio.Core.Sql.SqlTemplate>? Templates { get; init; }

    /// <summary>Lleva cada valor a un rango razonable (un settings.json editado a mano no puede romper la aplicación).</summary>
    public AppSettings Normalized() => this with
    {
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 3650),
        DefaultRowLimit = RowLimits.Contains(DefaultRowLimit) ? DefaultRowLimit : 10_000,
        LockTimeoutSeconds = Math.Clamp(LockTimeoutSeconds, 1, 600),
        TransactionWarnMinutes = Math.Clamp(TransactionWarnMinutes, 1, 1440),
        TransactionDangerMinutes = Math.Clamp(TransactionDangerMinutes, Math.Clamp(TransactionWarnMinutes, 1, 1440), 10080),
        OatWarnMinutes = Math.Clamp(OatWarnMinutes, 1, 1440),
        OatDangerMinutes = Math.Clamp(OatDangerMinutes, Math.Clamp(OatWarnMinutes, 1, 1440), 10080),
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        // Solo colores de la paleta (un rojo fuera de producción, o uno escrito a mano en el archivo, se descarta) y sin
        // repetir el de siempre.
        EnvironmentColors = NullIfEmpty((EnvironmentColors ?? []).Where(kv => Enum.IsDefined(kv.Key) && ColorPalette.IsAllowed(kv.Key, kv.Value)
                && !string.Equals(kv.Value.Trim(), ColorPalette.Default(kv.Key), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim().ToUpperInvariant())),
        FolderColors = NullIfEmpty((FolderColors ?? []).Where(kv => ProfileFolders.Clean(kv.Key) is not null && ColorPalette.IsAllowed(null, kv.Value))
            .DistinctBy(kv => ProfileFolders.Clean(kv.Key)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => ProfileFolders.Clean(kv.Key)!, kv => kv.Value.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)),
        Language = Language is "es" or "en" ? Language : "",
        EditorFontFamily = string.IsNullOrWhiteSpace(EditorFontFamily) ? DefaultFont : EditorFontFamily.Trim(),
        EditorFontSize = Math.Clamp(EditorFontSize, 8, 40),
        TabSize = Math.Clamp(TabSize, 1, 16),
        McpResponseLimit = Math.Clamp(McpResponseLimit, 5_000, 200_000),
        RecentFiles = RecentFiles?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecentFiles).ToList() is { Count: > 0 } recent ? recent : null,
        Templates = Templates?.Where(t => !string.IsNullOrWhiteSpace(t.Abbreviation) && t.Text is not null)
            .Select(t => t with { Abbreviation = t.Abbreviation.Trim(), Name = t.Name?.Trim() ?? "" })
            .DistinctBy(t => t.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList(),
    };

    public const int MaxRecentFiles = 10;

    // Sin colores propios queda null: así las opciones de fábrica siguen siendo iguales a new AppSettings().
    private static Dictionary<TKey, string>? NullIfEmpty<TKey>(Dictionary<TKey, string> d) where TKey : notnull => d.Count > 0 ? d : null;

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
