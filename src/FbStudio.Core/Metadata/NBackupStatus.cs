namespace FbStudio.Core.Metadata;

/// <summary>
/// En qué está la base respecto de nbackup (MON$DATABASE.MON$BACKUP_STATE): normal, «stalled» (en modo backup: lo que se
/// escribe va al archivo delta) o fusionando el delta al terminar.
/// </summary>
public enum BackupState
{
    Normal = 0,
    Stalled = 1,
    Merge = 2,
}

public static class BackupStates
{
    public static BackupState From(int value) => value switch
    {
        1 => BackupState.Stalled,
        2 => BackupState.Merge,
        _ => BackupState.Normal,
    };

    /// <summary>El aviso para mostrar (null si está normal).</summary>
    public static string? Warning(BackupState state) => state switch
    {
        BackupState.Stalled => Resources.CoreStrings.BackupStalled,
        BackupState.Merge => Resources.CoreStrings.BackupMerge,
        _ => null,
    };

    /// <summary>Lo corto, para el árbol: «⚠ modo backup».</summary>
    public static string? Short(BackupState state) => state == BackupState.Normal ? null : Resources.CoreStrings.BackupShort;
}

/// <summary>Un backup de nbackup anotado en RDB$BACKUP_HISTORY.</summary>
/// <param name="FileName">Solo el nombre del archivo: la carpeta del servidor no se muestra (D-54).</param>
public sealed record NBackupEntry(int Id, DateTime Timestamp, int Level, string? Guid, long? Scn, string FileName)
{
    public string LevelText => string.Format(Level == 0 ? Resources.CoreStrings.NBackupLevelFull : Resources.CoreStrings.NBackupLevelIncremental, Level);

    /// <summary>El nombre del archivo de una ruta del servidor (Windows o Linux).</summary>
    public static string NameOf(string path) => path.Trim()[(path.Trim().LastIndexOfAny(['/', '\\']) + 1)..];
}

/// <summary>Lo que se sabe de nbackup en una base (F-29, paso 1: solo mirar).</summary>
/// <param name="CustomDeltaFile">El delta va a un archivo definido en la base (ALTER DATABASE ADD DIFFERENCE FILE), no al lado de la base.</param>
/// <param name="History">Lo último primero.</param>
public sealed record NBackupStatus(BackupState State, bool CustomDeltaFile, IReadOnlyList<NBackupEntry> History);
