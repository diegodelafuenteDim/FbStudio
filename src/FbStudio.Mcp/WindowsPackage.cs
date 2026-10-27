namespace FbStudio.Mcp;

/// <summary>
/// La copia virtual de %APPDATA% de las apps de Windows instaladas como paquete MSIX (D-114, riesgo 1). Lo que un
/// programa lanzado desde una de esas apps escribe en %APPDATA% queda en la carpeta del paquete, y después los programas
/// que lanza esa app ven esa copia en lugar de lo real (aunque no tengan identidad de paquete: probado, D-115). Si
/// FbStudio está viendo una copia así, sus registros pueden ser viejos.
/// </summary>
public static class WindowsPackage
{
    /// <summary>
    /// El paquete cuya copia de FbStudio se está viendo, o null. Se reconoce porque el profiles.json que se ve es el mismo
    /// archivo (fecha y tamaño) que el de la copia: desde afuera del paquete se vería el real, que no coincide.
    /// </summary>
    public static string? VirtualCopyInUse(string dataFolder) => OperatingSystem.IsWindows()
        ? VirtualCopyInUse(dataFolder, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"))
        : null;

    /// <param name="roaming">%APPDATA%.</param>
    /// <param name="packages">%LOCALAPPDATA%\Packages, donde cada paquete guarda su copia en LocalCache\Roaming.</param>
    internal static string? VirtualCopyInUse(string dataFolder, string roaming, string packages)
    {
        try
        {
            var seen = new FileInfo(Path.Combine(dataFolder, "profiles.json"));
            if (!seen.Exists || !Directory.Exists(packages)) return null;
            roaming = Path.TrimEndingDirectorySeparator(Path.GetFullPath(roaming));
            var data = Path.GetFullPath(dataFolder);
            if (!data.StartsWith(roaming + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null; // --data
            var relative = Path.GetRelativePath(roaming, data);
            foreach (var package in Directory.EnumerateDirectories(packages))
            {
                var copy = new FileInfo(Path.Combine(package, "LocalCache", "Roaming", relative, "profiles.json"));
                if (copy.Exists && copy.Length == seen.Length && copy.LastWriteTimeUtc == seen.LastWriteTimeUtc)
                    return Path.GetFileName(package);
            }
            return null;
        }
        catch (Exception)
        {
            return null; // sin permiso para mirar: no se avisa
        }
    }
}
