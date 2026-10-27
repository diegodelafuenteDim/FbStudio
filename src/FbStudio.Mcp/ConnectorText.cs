using System.Text.RegularExpressions;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;

namespace FbStudio.Mcp;

/// <summary>Lo que el conector le devuelve al asistente: recortado si es muy largo y con los errores sin datos internos.</summary>
public static partial class ConnectorText
{
    /// <summary>Largo máximo de una respuesta (D-114): más que esto se recorta, con un aviso de cómo pedir menos.</summary>
    public const int DefaultLimit = 40_000;

    public static string Truncate(string text, int limit = DefaultLimit)
    {
        if (text.Length <= limit) return text;
        var cut = text.LastIndexOf('\n', limit);
        if (cut < limit / 2) cut = limit;
        return text[..cut] + $"\n[Truncated: {text.Length - cut} more characters. Ask for less: an area, a kind, a name pattern or the next offset.]";
    }

    /// <summary>
    /// Un error de Firebird o de la conexión, sin el servidor, la ruta de la base ni el usuario (D-54), y con los textos
    /// entre comillas simples y los valores de una clave repetida cambiados por «?» (D-114).
    /// </summary>
    public static string Error(Exception ex, ConnectionProfile? profile)
    {
        var text = ex.Message.Trim();
        if (profile is not null)
        {
            text = ServerPaths.Hide(text, profile.Database, profile.Name);
            if (profile.Host.Length > 0) text = text.Replace(profile.Host, "the server", StringComparison.OrdinalIgnoreCase);
            if (profile.User.Length > 2) text = Regex.Replace(text, $@"\b{Regex.Escape(profile.User)}\b", "the user", RegexOptions.IgnoreCase);
        }
        text = KeyValue().Replace(text, "${1}?)");
        text = ConversionValue().Replace(text, "${1}?\"");
        text = Quoted().Replace(text, "'?'");
        return text;
    }

    [GeneratedRegex(@"(Problematic key value is \()[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"(conversion error from string "")[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex ConversionValue();

    [GeneratedRegex(@"'(?:[^']|'')*'")]
    private static partial Regex Quoted();
}
