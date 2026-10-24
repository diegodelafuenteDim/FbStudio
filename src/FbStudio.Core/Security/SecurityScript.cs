using FbStudio.Core.Sql;

namespace FbStudio.Core.Security;

/// <summary>
/// El SQL de usuarios, roles y permisos de Firebird 3 (F-28). La contraseña va en <see cref="SecurityStatement.Sql"/>
/// y nunca en <see cref="SecurityStatement.Shown"/>, que es lo que se muestra y se anota.
/// </summary>
public static class SecurityScript
{
    public const string HiddenPassword = "'********'";

    /// <summary>Las letras de permisos que se manejan, en el orden en que se escriben.</summary>
    public const string Privileges = "SIUDRXG";

    /// <summary>Qué permisos tienen sentido para cada tipo de objeto.</summary>
    public static string Applicable(GrantObjectKind kind) => kind switch
    {
        GrantObjectKind.Table => "SIUDR",
        GrantObjectKind.View => "SIUD",
        GrantObjectKind.Procedure or GrantObjectKind.Function or GrantObjectKind.Package => "X",
        GrantObjectKind.Generator or GrantObjectKind.Exception => "G",
        _ => "",
    };

    public static string PrivilegeName(char privilege) => privilege switch
    {
        'S' => "SELECT",
        'I' => "INSERT",
        'U' => "UPDATE",
        'D' => "DELETE",
        'R' => "REFERENCES",
        'X' => "EXECUTE",
        'G' => "USAGE",
        _ => throw new ArgumentOutOfRangeException(nameof(privilege), privilege, null),
    };

    public static string Grantee(GranteeKind kind, string name) => kind switch
    {
        GranteeKind.User => SqlIdentifier.Quote(name),
        GranteeKind.Role => "ROLE " + SqlIdentifier.Quote(name),
        GranteeKind.Procedure => "PROCEDURE " + SqlIdentifier.Quote(name),
        GranteeKind.Function => "FUNCTION " + SqlIdentifier.Quote(name),
        GranteeKind.Package => "PACKAGE " + SqlIdentifier.Quote(name),
        GranteeKind.Trigger => "TRIGGER " + SqlIdentifier.Quote(name),
        GranteeKind.View => "VIEW " + SqlIdentifier.Quote(name),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string Object(GrantObjectKind kind, string name) => kind switch
    {
        GrantObjectKind.Table or GrantObjectKind.View => SqlIdentifier.Quote(name),
        GrantObjectKind.Procedure => "PROCEDURE " + SqlIdentifier.Quote(name),
        GrantObjectKind.Function => "FUNCTION " + SqlIdentifier.Quote(name),
        GrantObjectKind.Package => "PACKAGE " + SqlIdentifier.Quote(name),
        GrantObjectKind.Generator => "GENERATOR " + SqlIdentifier.Quote(name),
        GrantObjectKind.Exception => "EXCEPTION " + SqlIdentifier.Quote(name),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Los GRANT y REVOKE para que <paramref name="grantee"/> pase de lo que tiene sobre el objeto (<paramref name="current"/>:
    /// sus permisos de toda la tabla, sin los de algunas columnas) a <paramref name="wanted"/>. «Con GRANT OPTION» vale para
    /// todos los permisos de la fila. Lo que dio otro usuario se quita con GRANTED BY (eso lo puede hacer un administrador).
    /// Primero los REVOKE, después los GRANT.
    /// </summary>
    public static IReadOnlyList<SecurityStatement> Changes(GranteeKind granteeKind, string grantee, GrantObjectKind objectKind, string obj,
        IReadOnlyCollection<DbGrant> current, string wanted, bool grantOption, string currentUser)
    {
        var has = current.Where(g => g.Field is null).ToList();
        var target = Privileges.Where(p => wanted.Contains(p) && Applicable(objectKind).Contains(p)).ToList();
        var on = Object(objectKind, obj);
        var to = Grantee(granteeKind, grantee);
        var statements = new List<SecurityStatement>();

        string List(IEnumerable<char> privileges) => string.Join(", ", privileges.Distinct().OrderBy(p => Privileges.IndexOf(p)).Select(PrivilegeName));
        string By(string grantor) => string.Equals(grantor, currentUser, StringComparison.OrdinalIgnoreCase) ? "" : " GRANTED BY " + SqlIdentifier.Quote(grantor);

        foreach (var group in has.Where(g => !target.Contains(g.Privilege)).GroupBy(g => g.Grantor))
            statements.Add(new($"REVOKE {List(group.Select(g => g.Privilege))} ON {on} FROM {to}{By(group.Key)}"));
        if (!grantOption)
        {
            foreach (var group in has.Where(g => target.Contains(g.Privilege) && g.GrantOption).GroupBy(g => g.Grantor))
                statements.Add(new($"REVOKE GRANT OPTION FOR {List(group.Select(g => g.Privilege))} ON {on} FROM {to}{By(group.Key)}"));
        }
        var grant = target.Where(p => !has.Any(g => g.Privilege == p) || (grantOption && !has.Any(g => g.Privilege == p && g.GrantOption))).ToList();
        if (grant.Count > 0) statements.Add(new($"GRANT {List(grant)} ON {on} TO {to}{(grantOption ? " WITH GRANT OPTION" : "")}"));
        return statements;
    }

    /// <summary>
    /// El nombre como lo va a guardar Firebird: uno común («pepe») va en mayúsculas, como si se escribiera sin comillas
    /// (si no, quedaría un usuario «pepe» que solo entra escribiéndolo entre comillas). Con espacios u otros signos se
    /// respeta tal cual.
    /// </summary>
    public static string StoredName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0 && char.IsAsciiLetter(trimmed[0]) && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
            ? trimmed.ToUpperInvariant()
            : trimmed;
    }

    public static SecurityStatement GrantRole(string role, string user) => new($"GRANT {SqlIdentifier.Quote(role)} TO {SqlIdentifier.Quote(user)}");

    public static SecurityStatement RevokeRole(string role, string user, string grantor, string currentUser) =>
        new($"REVOKE {SqlIdentifier.Quote(role)} FROM {SqlIdentifier.Quote(user)}"
            + (string.Equals(grantor, currentUser, StringComparison.OrdinalIgnoreCase) ? "" : " GRANTED BY " + SqlIdentifier.Quote(grantor)));

    public static SecurityStatement CreateRole(string name) => new($"CREATE ROLE {SqlIdentifier.Quote(name)}");

    public static SecurityStatement DropRole(string name) => new($"DROP ROLE {SqlIdentifier.Quote(name)}");

    /// <summary>CREATE USER con todo lo pedido; el comentario va aparte (COMMENT ON USER).</summary>
    public static IReadOnlyList<SecurityStatement> CreateUser(UserEdit user)
    {
        if (string.IsNullOrEmpty(user.Password)) throw new ArgumentException("Falta la contraseña", nameof(user));
        var options = UserOptions(user.FirstName, user.MiddleName, user.LastName, user.Admin, user.Active, user.Plugin);
        var head = $"CREATE USER {SqlIdentifier.Quote(user.Name)} PASSWORD ";
        List<SecurityStatement> statements = [new(head + SqlIdentifier.Literal(user.Password) + options, head + HiddenPassword + options, user.Password)];
        if (!string.IsNullOrWhiteSpace(user.Description)) statements.Add(Comment(user.Name, user.Description));
        return statements;
    }

    /// <summary>ALTER USER solo con lo que cambió (nada si no cambió nada); el comentario va aparte.</summary>
    public static IReadOnlyList<SecurityStatement> AlterUser(DbUser before, UserEdit after)
    {
        var statements = new List<SecurityStatement>();
        static bool Changed(string? a, string? b) => (a ?? "").Trim() != (b ?? "").Trim();
        var parts = new List<string>();
        if (Changed(before.FirstName, after.FirstName)) parts.Add("FIRSTNAME " + SqlIdentifier.Literal(after.FirstName?.Trim() ?? ""));
        if (Changed(before.MiddleName, after.MiddleName)) parts.Add("MIDDLENAME " + SqlIdentifier.Literal(after.MiddleName?.Trim() ?? ""));
        if (Changed(before.LastName, after.LastName)) parts.Add("LASTNAME " + SqlIdentifier.Literal(after.LastName?.Trim() ?? ""));
        if (before.Admin != after.Admin) parts.Add(after.Admin ? "GRANT ADMIN ROLE" : "REVOKE ADMIN ROLE");
        if (before.Active != after.Active) parts.Add(after.Active ? "ACTIVE" : "INACTIVE");
        var plugin = Plugin(before.Plugin);
        var head = $"ALTER USER {SqlIdentifier.Quote(before.Name)}";
        var rest = (parts.Count > 0 ? " " + string.Join(" ", parts) : "") + plugin;
        if (!string.IsNullOrEmpty(after.Password))
            statements.Add(new($"{head} PASSWORD {SqlIdentifier.Literal(after.Password)}{rest}", $"{head} PASSWORD {HiddenPassword}{rest}", after.Password));
        else if (parts.Count > 0)
            statements.Add(new(head + rest));
        if (Changed(before.Description, after.Description)) statements.Add(Comment(before.Name, after.Description));
        return statements;
    }

    public static SecurityStatement DropUser(DbUser user) => new($"DROP USER {SqlIdentifier.Quote(user.Name)}{Plugin(user.Plugin)}");

    private static SecurityStatement Comment(string user, string? description) =>
        new($"COMMENT ON USER {SqlIdentifier.Quote(user)} IS {(string.IsNullOrWhiteSpace(description) ? "NULL" : SqlIdentifier.Literal(description.Trim()))}");

    private static string UserOptions(string? first, string? middle, string? last, bool admin, bool active, string? plugin) =>
        (string.IsNullOrWhiteSpace(first) ? "" : " FIRSTNAME " + SqlIdentifier.Literal(first.Trim()))
        + (string.IsNullOrWhiteSpace(middle) ? "" : " MIDDLENAME " + SqlIdentifier.Literal(middle.Trim()))
        + (string.IsNullOrWhiteSpace(last) ? "" : " LASTNAME " + SqlIdentifier.Literal(last.Trim()))
        + (admin ? " GRANT ADMIN ROLE" : "")
        + (active ? "" : " INACTIVE")
        + Plugin(plugin);

    // Los nombres de plugin se escriben como están (Srp, Legacy_UserManager): entre comillas solo si tienen algo raro.
    private static string Plugin(string? plugin) =>
        string.IsNullOrWhiteSpace(plugin) ? ""
        : " USING PLUGIN " + (plugin.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? plugin.Trim() : SqlIdentifier.Quote(plugin.Trim()));
}
