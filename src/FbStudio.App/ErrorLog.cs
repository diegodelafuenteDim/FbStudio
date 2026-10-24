using FbStudio.Workspace;

namespace FbStudio.App;

/// <summary>
/// Los errores no previstos, en errors.log junto a los perfiles (lo último arriba de 1 MB se descarta). Sin contraseñas:
/// lo que diga PASSWORD '…' queda enmascarado.
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1_000_000;

    public static string FilePath => Path.Combine(ProfileStore.DefaultFolder, "errors.log");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(ProfileStore.DefaultFolder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {AppInfo.Version}\n"
                                         + Core.Security.SqlSecrets.Mask(ex.ToString()) + "\n\n");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Si ni siquiera se puede escribir el registro, el aviso en la barra de estado alcanza.
        }
    }
}
