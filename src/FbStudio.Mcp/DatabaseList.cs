using System.Text;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;

namespace FbStudio.Mcp;

/// <summary>
/// Lo que devuelve <c>list_databases</c>: los registros habilitados para asistentes, con su entorno y sus áreas. Nunca
/// el servidor, la ruta, el usuario ni nada de la contraseña (D-54, D-114). De los que no están habilitados solo se dice
/// cuántos son, para que la persona sepa dónde habilitarlos.
/// </summary>
public static class DatabaseList
{
    /// <param name="areas">Las áreas de un registro; puede fallar (un archivo compartido en una carpeta de red caída).</param>
    public static string Describe(IReadOnlyList<ConnectionProfile> profiles, Func<ConnectionProfile, IReadOnlyList<SchemaArea>> areas)
    {
        var enabled = profiles.Where(p => p.AssistantAccess).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var others = profiles.Count - enabled.Count;
        var sb = new StringBuilder();
        if (enabled.Count == 0)
        {
            sb.Append("No database is enabled for assistants yet");
            sb.Append(others == 0 ? " (FbStudio has no registrations)." : $" ({others} registration{Plural(others)} in FbStudio).");
            sb.Append('\n').Append(HowToEnable);
            return sb.ToString();
        }

        sb.Append($"Databases available through FbStudio ({enabled.Count}):\n");
        foreach (var p in enabled)
        {
            sb.Append("- ").Append(p.Name).Append(" · environment: ").Append(Environment(p.Environment));
            if (p.ReadOnly) sb.Append(" · read-only registration");
            sb.Append('\n');
            var names = AreaNames(p, areas);
            if (names is null) sb.Append("  areas: could not be read\n");
            else if (names.Count > 0) sb.Append("  areas: ").Append(string.Join(", ", names)).Append('\n');
        }
        if (others > 0) sb.Append($"{others} more registration{Plural(others)} not enabled. ").Append(HowToEnable);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Dónde se habilita un registro, con los textos de la interfaz en inglés (el modelo se lo cuenta a la persona).</summary>
    public const string HowToEnable =
        "To enable one, edit the registration in FbStudio and tick \"Available to AI assistants (MCP)\".";

    /// <summary>El entorno con nombre fijo en inglés: lo lee el modelo, no depende del idioma de la interfaz.</summary>
    public static string Environment(ProfileEnvironment environment) => environment switch
    {
        ProfileEnvironment.Produccion => "Production",
        ProfileEnvironment.Prueba => "Test",
        _ => "Development",
    };

    private static List<string>? AreaNames(ConnectionProfile profile, Func<ConnectionProfile, IReadOnlyList<SchemaArea>> areas)
    {
        try
        {
            return areas(profile).Select(a => a.Name).ToList();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Plural(int count) => count == 1 ? "" : "s";
}
