namespace FbStudio.Core.Metadata;

/// <summary>
/// Las rutas del servidor no se muestran (D-54, pedido de Diego: para eso están los alias). Lo que llega del servidor con
/// la ruta de la base (gstat, gbak, la validación, los errores) se muestra con la base como la nombra el registro.
/// </summary>
public static class ServerPaths
{
    /// <summary>Cambia la ruta de la base por <paramref name="shownAs"/> (sin distinguir mayúsculas: en Windows da igual).</summary>
    public static string Hide(string text, string? serverPath, string shownAs) =>
        string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(serverPath) || serverPath == shownAs
            ? text
            : text.Replace(serverPath, shownAs, StringComparison.OrdinalIgnoreCase);
}
