namespace FbStudio.Workspace;

/// <summary>
/// En Linux y macOS, la carpeta de FbStudio y sus archivos quedan solo para el usuario (0700 y 0600): tienen servidores,
/// bases, usuarios, áreas y el historial. En Windows la carpeta del perfil ya es privada. Un archivo compartido (p. ej.
/// el de áreas en una carpeta de red) no se toca.
/// </summary>
internal static class PrivateFiles
{
    private const UnixFileMode Folder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode File = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void EnsureFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }
        Directory.CreateDirectory(folder, Folder);
        Try(() => System.IO.File.SetUnixFileMode(folder, Folder));
    }

    public static void Restrict(string path)
    {
        if (OperatingSystem.IsWindows() || !System.IO.File.Exists(path)) return;
        Try(() => System.IO.File.SetUnixFileMode(path, File));
    }

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Un sistema de archivos sin permisos de Unix (una unidad FAT montada): queda como está.
        }
    }
}
