using System.Text.RegularExpressions;

namespace FbStudio.Core.Security;

/// <summary>
/// Las contraseñas escritas en SQL («CREATE USER X PASSWORD 'secreto'», «ALTER USER X SET PASSWORD '…'») cambiadas por
/// asteriscos, para lo que queda guardado (el historial). Lo que se ejecuta no cambia.
/// </summary>
public static partial class SqlSecrets
{
    public const string Masked = "'********'";

    [GeneratedRegex(@"(\bPASSWORD\s+)'(?:[^']|'')*'", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordLiteral();

    public static string Mask(string text) =>
        text.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ? PasswordLiteral().Replace(text, "$1" + Masked) : text;
}
