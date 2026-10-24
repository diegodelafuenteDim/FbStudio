using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public static class FbConnectionFactory
{
    public static string ConnectionString(ConnectionProfile profile, string password, string purpose = "editor") =>
        new FbConnectionStringBuilder
        {
            DataSource = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            UserID = profile.User,
            Password = password,
            Role = profile.Role,
            Charset = profile.Charset,
            WireCrypt = profile.WireCrypt switch
            {
                WireCryptMode.Required => FbWireCrypt.Required,
                WireCryptMode.Disabled => FbWireCrypt.Disabled,
                _ => FbWireCrypt.Enabled,
            },
            // Compresión desactivada a propósito: FirebirdClient 10.3.4 / 11.0.0-alpha1 pierde el sincronismo
            // del protocolo con ciertos paquetes comprimidos (ver docs/decisiones.md, D-03).
            Compression = false,
            // Cada pestaña es una sesión con su propia transacción: sin pool para que nada quede compartido.
            Pooling = false,
            ApplicationName = $"FbStudio ({purpose})",
        }.ConnectionString;

    public static FbConnection Create(ConnectionProfile profile, string password, string purpose = "editor") =>
        new(ConnectionString(profile, password, purpose));
}
