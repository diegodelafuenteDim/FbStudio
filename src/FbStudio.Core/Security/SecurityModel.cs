namespace FbStudio.Core.Security;

/// <summary>Un usuario del servidor (SEC$USERS). Los usuarios son del servidor, no de una base.</summary>
/// <param name="Admin">Tiene el rol de administrador de usuarios (GRANT ADMIN ROLE).</param>
/// <param name="Plugin">El administrador de usuarios donde está (Srp, Legacy_UserManager…).</param>
public sealed record DbUser(string Name, string? FirstName = null, string? MiddleName = null, string? LastName = null,
    bool Active = true, bool Admin = false, string? Description = null, string? Plugin = null)
{
    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
}

/// <summary>Un rol de la base (RDB$ROLES).</summary>
public sealed record DbRole(string Name, string? Owner = null, string? Description = null, bool IsSystem = false);

/// <summary>A quién se le dan permisos: un usuario, un rol o un objeto PSQL (que los usa al ejecutarse).</summary>
public enum GranteeKind
{
    User,
    Role,
    Procedure,
    Function,
    Package,
    Trigger,
    View,
}

/// <summary>Sobre qué es el permiso.</summary>
public enum GrantObjectKind
{
    Table,
    View,
    Procedure,
    Function,
    Package,
    Generator,
    Exception,
    Role,
}

/// <summary>Una fila de RDB$USER_PRIVILEGES: un permiso de alguien sobre un objeto.</summary>
/// <param name="Privilege">S (SELECT), I (INSERT), U (UPDATE), D (DELETE), R (REFERENCES), X (EXECUTE), G (USAGE) o M (ser miembro de un rol).</param>
/// <param name="Field">La columna, si el permiso es solo sobre algunas (UPDATE o REFERENCES).</param>
/// <param name="GrantOption">Puede darlo a otros (WITH GRANT OPTION; en un rol, WITH ADMIN OPTION).</param>
public sealed record DbGrant(string Grantee, GranteeKind GranteeKind, string Object, GrantObjectKind ObjectKind, char Privilege,
    string? Field, bool GrantOption, string Grantor);

/// <summary>Una sentencia para ejecutar y cómo se muestra (sin la contraseña).</summary>
/// <param name="Secret">La contraseña que va en el SQL, para taparla también si aparece en un mensaje de error.</param>
public sealed record SecurityStatement(string Sql, string Shown, string? Secret = null)
{
    public SecurityStatement(string sql) : this(sql, sql)
    {
    }

    /// <summary>El texto sin la contraseña.</summary>
    /// <summary>La contraseña cambiada por asteriscos, también como va escrita en el SQL (con las comillas dobladas).</summary>
    public string Hide(string text) =>
        string.IsNullOrEmpty(Secret) ? text : text.Replace(Secret.Replace("'", "''"), "********").Replace(Secret, "********");
}

/// <summary>Lo que se pide al crear o cambiar un usuario. Password null o vacía: al cambiar, no se toca.</summary>
public sealed record UserEdit(string Name, string? Password, string? FirstName, string? MiddleName, string? LastName,
    bool Active, bool Admin, string? Description, string? Plugin);
