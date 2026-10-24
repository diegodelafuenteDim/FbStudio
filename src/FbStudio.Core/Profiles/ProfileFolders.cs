namespace FbStudio.Core.Profiles;

/// <summary>
/// Carpetas propias del árbol (p. ej. «Clinica», o «Clinica/Producción» para una carpeta dentro de otra): cada
/// registro guarda la ruta de su carpeta; sin carpeta, la base va al grupo de su ambiente. Las rutas se comparan sin
/// importar mayúsculas ni espacios alrededor de cada nivel.
/// </summary>
public static class ProfileFolders
{
    public const char Separator = '/';

    /// <summary>«  clinica /  prod » → «clinica/prod»; null si queda vacía.</summary>
    public static string? Clean(string? folder)
    {
        var parts = (folder ?? "").Split(Separator).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        return parts.Length == 0 ? null : string.Join(Separator, parts);
    }

    public static bool Same(string? a, string? b) =>
        Clean(a) is { } x && Clean(b) is { } y && string.Equals(x, y, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>La carpeta es <paramref name="path"/> o está adentro (a cualquier profundidad).</summary>
    public static bool IsInside(string? folder, string path)
    {
        if (Clean(folder) is not { } f || Clean(path) is not { } p) return false;
        return string.Equals(f, p, StringComparison.CurrentCultureIgnoreCase)
               || f.StartsWith(p + Separator, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>El último nivel: «Clinica/Producción» → «Producción».</summary>
    public static string Name(string path) => Clean(path)?.Split(Separator)[^1] ?? "";

    /// <summary>La carpeta que la contiene, o null si está en la raíz.</summary>
    public static string? Parent(string path) =>
        Clean(path) is { } p && p.LastIndexOf(Separator) is var i and > 0 ? p[..i] : null;

    /// <summary>Carpetas propias en uso (también las que solo contienen otras carpetas), ordenadas, sin repetir.</summary>
    public static IReadOnlyList<string> All(IEnumerable<ConnectionProfile> profiles)
    {
        var all = new List<string>();
        foreach (var folder in profiles.Select(p => Clean(p.Folder)).OfType<string>())
        {
            var parts = folder.Split(Separator);
            for (var i = 1; i <= parts.Length; i++) all.Add(string.Join(Separator, parts[..i]));
        }
        return all.Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Ruta a guardar: limpia, null si queda vacía y, nivel por nivel, con la grafía de una carpeta que ya existe
    /// («clinica/test» → «Clinica/test» si ya está «Clinica»).
    /// </summary>
    public static string? Normalize(string? folder, IEnumerable<ConnectionProfile> existing)
    {
        if (Clean(folder) is not { } clean) return null;
        var known = All(existing);
        var parts = clean.Split(Separator);
        // La carpeta existente más larga que coincide define la grafía de esos niveles (y la de sus padres).
        for (var i = parts.Length; i >= 1; i--)
        {
            var prefix = string.Join(Separator, parts[..i]);
            if (known.FirstOrDefault(k => string.Equals(k, prefix, StringComparison.CurrentCultureIgnoreCase)) is not { } spelled) continue;
            spelled.Split(Separator).CopyTo(parts, 0);
            break;
        }
        return string.Join(Separator, parts);
    }
}
