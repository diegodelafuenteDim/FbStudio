using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Security;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>Usuarios del servidor, roles y permisos de la base, como se leyeron.</summary>
/// <param name="CurrentUser">Con quién se conectó (CURRENT_USER).</param>
/// <param name="CanManageUsers">SYSDBA o conectado con el rol RDB$ADMIN: puede crear, cambiar y borrar usuarios.</param>
public sealed record SecuritySnapshot(IReadOnlyList<DbUser> Users, IReadOnlyList<DbRole> Roles, IReadOnlyList<DbGrant> Grants,
    string CurrentUser, bool CanManageUsers);

/// <summary>
/// Una sentencia de usuarios o permisos que Firebird rechazó: ninguna se aplicó. El mensaje es el de Firebird sin la
/// contraseña (y sin la excepción original, que podría traerla).
/// </summary>
public sealed class SecurityStatementException(int index, SecurityStatement statement, Exception inner)
    : Exception(statement.Hide(Summary(inner.Message)))
{
    /// <summary>La sentencia que falló; -1 si fue al confirmar (lo que va a la base de seguridad del servidor).</summary>
    public int Index { get; } = index;
    public SecurityStatement Statement { get; } = statement;

    // «add record error» solo no dice nada: el segundo renglón de Firebird dice qué fue.
    private static string Summary(string message) =>
        string.Join(" · ", message.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2));
}

/// <summary>
/// Lee y cambia usuarios, roles y permisos (F-28) con SQL de Firebird 3: SEC$USERS, RDB$ROLES y RDB$USER_PRIVILEGES
/// para leer; CREATE/ALTER/DROP USER, CREATE/DROP ROLE, GRANT y REVOKE para cambiar. Un usuario sin permisos de
/// administrador ve en SEC$USERS solo a sí mismo.
/// </summary>
public static class SecurityManager
{
    public static async Task<SecuritySnapshot> LoadAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "usuarios");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);

        async Task<List<object?[]>> Rows(string sql)
        {
            await using var cmd = new FbCommand(sql, connection, tx);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<object?[]>();
            while (await reader.ReadAsync(ct))
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(values);
            }
            return rows;
        }
        static string? S(object? v) => v is null ? null : Convert.ToString(v)!.Trim() is { Length: > 0 } s ? s : null;
        static bool B(object? v) => v is bool b ? b : v is not null && Convert.ToInt32(v) != 0;

        var me = (await Rows("SELECT TRIM(CURRENT_USER), TRIM(CURRENT_ROLE) FROM RDB$DATABASE"))[0];
        var currentUser = S(me[0]) ?? profile.User;
        var canManage = string.Equals(currentUser, "SYSDBA", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(S(me[1]), "RDB$ADMIN", StringComparison.OrdinalIgnoreCase);

        var users = (await Rows("""
                SELECT SEC$USER_NAME, SEC$FIRST_NAME, SEC$MIDDLE_NAME, SEC$LAST_NAME, SEC$ACTIVE, SEC$ADMIN, SEC$DESCRIPTION, SEC$PLUGIN
                FROM SEC$USERS ORDER BY SEC$USER_NAME
                """))
            .Select(r => new DbUser(S(r[0])!, S(r[1]), S(r[2]), S(r[3]), B(r[4]), B(r[5]), S(r[6]), S(r[7])))
            .ToList();

        var roles = (await Rows("SELECT RDB$ROLE_NAME, RDB$OWNER_NAME, RDB$DESCRIPTION, RDB$SYSTEM_FLAG FROM RDB$ROLES ORDER BY RDB$ROLE_NAME"))
            .Select(r => new DbRole(S(r[0])!, S(r[1]), S(r[2]), B(r[3])))
            .ToList();

        // Las vistas figuran como relaciones (tipo 0): se distinguen por su BLR. Los permisos sobre objetos del sistema
        // (los que Firebird da a PUBLIC) no se muestran; ser miembro de RDB$ADMIN, sí.
        var grants = new List<DbGrant>();
        foreach (var r in await Rows("""
                     SELECT p.RDB$USER, p.RDB$USER_TYPE, p.RDB$RELATION_NAME, p.RDB$OBJECT_TYPE, p.RDB$PRIVILEGE, p.RDB$FIELD_NAME,
                            p.RDB$GRANT_OPTION, p.RDB$GRANTOR,
                            (SELECT 1 FROM RDB$RELATIONS v WHERE v.RDB$RELATION_NAME = p.RDB$RELATION_NAME AND v.RDB$VIEW_BLR IS NOT NULL),
                            (SELECT 1 FROM RDB$RELATIONS s WHERE s.RDB$RELATION_NAME = p.RDB$RELATION_NAME AND s.RDB$SYSTEM_FLAG = 1)
                     FROM RDB$USER_PRIVILEGES p
                     """))
        {
            if (r[9] is not null) continue;
            if (GranteeKindOf(Convert.ToInt32(r[1])) is not { } granteeKind) continue;
            if (ObjectKindOf(Convert.ToInt32(r[3]), r[8] is not null) is not { } objectKind) continue;
            var obj = S(r[2])!;
            if (objectKind == GrantObjectKind.Metadata ? !SecurityScript.DdlClasses.Any(c => c.Name == obj)
                : IsSystemName(obj) && !(objectKind == GrantObjectKind.Role && obj == "RDB$ADMIN")) continue;
            var privilege = S(r[4]) is { Length: 1 } p ? p[0] : '?';
            var known = objectKind switch
            {
                GrantObjectKind.Role => privilege == 'M',
                GrantObjectKind.Metadata => SecurityScript.DdlPrivileges.Contains(privilege),
                _ => SecurityScript.Privileges.Contains(privilege),
            };
            if (!known) continue;
            grants.Add(new DbGrant(S(r[0])!, granteeKind, obj, objectKind, privilege, S(r[5]), B(r[6]), S(r[7]) ?? ""));
        }
        await tx.CommitAsync(ct);
        return new SecuritySnapshot(users, roles, grants, currentUser, canManage);
    }

    private static bool IsSystemName(string name) =>
        name.StartsWith("RDB$", StringComparison.Ordinal) || name.StartsWith("MON$", StringComparison.Ordinal) || name.StartsWith("SEC$", StringComparison.Ordinal)
        || name.StartsWith("SQL$", StringComparison.Ordinal);

    // Tipos de objeto de Firebird (obj_relation 0, obj_view 1, obj_trigger 2, obj_procedure 5, obj_exception 7, obj_user 8,
    // obj_sql_role 13, obj_generator 14, obj_udf 15, obj_package_header 18).
    private static GranteeKind? GranteeKindOf(int type) => type switch
    {
        8 => GranteeKind.User,
        13 => GranteeKind.Role,
        5 => GranteeKind.Procedure,
        15 => GranteeKind.Function,
        18 => GranteeKind.Package,
        2 => GranteeKind.Trigger,
        1 => GranteeKind.View,
        _ => null,
    };

    private static GrantObjectKind? ObjectKindOf(int type, bool isView) => type switch
    {
        0 => isView ? GrantObjectKind.View : GrantObjectKind.Table,
        1 => GrantObjectKind.View,
        5 => GrantObjectKind.Procedure,
        15 => GrantObjectKind.Function,
        18 => GrantObjectKind.Package,
        14 => GrantObjectKind.Generator,
        7 => GrantObjectKind.Exception,
        13 => GrantObjectKind.Role,
        // Permisos de estructura de Firebird 3: la clase de objetos (obj_database 20 … obj_filters 32).
        >= 20 and <= 32 => GrantObjectKind.Metadata,
        _ => null,
    };

    /// <summary>
    /// Ejecuta las sentencias en orden, en una sola transacción: si una falla, no queda ninguna aplicada y se dice cuál
    /// fue. Los usuarios se graban recién al confirmar (en la base de seguridad del servidor): si ahí falla (p. ej. un
    /// usuario que ya existe), se deshace todo y se dice que fue al confirmar. Con un registro de solo lectura no se
    /// ejecuta nada.
    /// </summary>
    public static async Task ExecuteAsync(ConnectionProfile profile, string password, IReadOnlyList<SecurityStatement> statements,
        CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        if (statements.Count == 0) return;
        await using var connection = FbConnectionFactory.Create(profile, password, "usuarios");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadWrite(TimeSpan.FromSeconds(10)), ct);
        for (var i = 0; i < statements.Count; i++)
        {
            try
            {
                await using var cmd = new FbCommand(statements[i].Sql, connection, tx);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (FbException ex)
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw new SecurityStatementException(i, statements[i], ex);
            }
        }
        try
        {
            await tx.CommitAsync(ct);
        }
        catch (FbException ex)
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception) { /* ya no estaba activa */ }
            var secret = statements.Select(st => st.Secret).FirstOrDefault(s => s is not null);
            throw new SecurityStatementException(-1, new SecurityStatement("COMMIT", "COMMIT", secret), ex);
        }
    }
}
