using System.Text.Json.Serialization;

namespace FbStudio.Core.Profiles;

public enum ProfileEnvironment
{
    Desarrollo,
    Prueba,
    Produccion,
}

/// <summary>Nombre del entorno para mostrar (el valor del enum queda en español porque es lo que se guarda en profiles.json).</summary>
public static class ProfileEnvironments
{
    public static string Label(ProfileEnvironment environment) => environment switch
    {
        ProfileEnvironment.Produccion => Resources.CoreStrings.EnvProduction,
        ProfileEnvironment.Prueba => Resources.CoreStrings.EnvTest,
        _ => Resources.CoreStrings.EnvDevelopment,
    };
}

public enum WireCryptMode
{
    Enabled,
    Required,
    Disabled,
}

/// <summary>
/// Perfil de conexión sin secretos (la contraseña vive en el almacén cifrado del SO, por <see cref="Id"/>).
/// </summary>
public sealed record ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Host { get; init; }
    public int Port { get; set; } = 3050;
    public required string Database { get; init; }
    public required string User { get; init; }
    public string? Role { get; init; }

    /// <summary>
    /// Muchas bases viejas tienen sus columnas de texto en ISO8859_1.
    /// Si alguna base tuviera columnas NONE con bytes Windows-1252, usar WIN1252: el servidor no translitera
    /// NONE y el cliente decodifica como 1252.
    /// </summary>
    public string Charset { get; set; } = "ISO8859_1";

    public ProfileEnvironment Environment { get; set; } = ProfileEnvironment.Desarrollo;

    /// <summary>Solo permite consultas: DML, DDL y EXECUTE se bloquean antes de llegar al servidor.</summary>
    public bool ReadOnly { get; init; }

    public WireCryptMode WireCrypt { get; set; } = WireCryptMode.Enabled;

    /// <summary>Guardar las sentencias ejecutadas en el historial local (cifrado). Se puede apagar por perfil.</summary>
    public bool SaveHistory { get; set; } = true;

    public string? Folder { get; init; }

    /// <summary>
    /// Archivo de áreas de la base (F-9) cuando no es el propio de FbStudio: p. ej. uno compartido en una carpeta de
    /// red. Null = el de esta PC.
    /// </summary>
    public string? AreasFile { get; init; }

    [JsonIgnore]
    public string Display => $"{Name}  ·  {User}@{Host}:{Port}/{Database}";

    [JsonIgnore]
    public bool IsProduction => Environment == ProfileEnvironment.Produccion;
}
