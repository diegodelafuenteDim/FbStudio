using System.Globalization;
using System.Text;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Metadata;

/// <summary>
/// Escribe el DDL de un objeto con sintaxis estándar de Firebird 3.
/// Procedimientos, triggers, vistas y funciones salen como UNA sentencia CREATE OR ALTER sin terminador: es lo que se
/// edita y se compila en su pestaña. Tablas y paquetes salen como script (con SET TERM para los bloques PSQL).
/// </summary>
public static class DdlWriter
{
    private const string Indent = "    ";

    private static string Q(string name) => SqlIdentifier.Quote(name);
    private static string Cols(IEnumerable<string> columns) => string.Join(", ", columns.Select(Q));
    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Table(TableDefinition t)
    {
        var sb = new StringBuilder();
        var temporary = t.RelationType is 4 or 5;
        sb.Append(temporary ? "CREATE GLOBAL TEMPORARY TABLE " : "CREATE TABLE ").Append(Q(t.Name));
        if (t.RelationType == 2 && t.ExternalFile is { } file) sb.Append(" EXTERNAL FILE ").Append(SqlIdentifier.Literal(file));
        sb.Append(" (\n");
        var width = t.Columns.Count == 0 ? 0 : t.Columns.Max(c => Q(c.Name).Length);
        for (var i = 0; i < t.Columns.Count; i++)
            sb.Append(Indent).Append(Column(t.Columns[i], width)).Append(i < t.Columns.Count - 1 ? ",\n" : "\n");
        sb.Append(')');
        if (temporary) sb.Append(t.RelationType == 4 ? "\nON COMMIT PRESERVE ROWS" : "\nON COMMIT DELETE ROWS");
        sb.Append(";\n");

        Section(sb, t.Constraints.Where(c => c.Kind is ConstraintKind.PrimaryKey or ConstraintKind.Unique).Select(c => Constraint(t.Name, c)));
        Section(sb, t.Constraints.Where(c => c.Kind == ConstraintKind.ForeignKey).Select(c => Constraint(t.Name, c)));
        Section(sb, t.Constraints.Where(c => c.Kind == ConstraintKind.Check).Select(c => Constraint(t.Name, c)));
        Section(sb, t.Indices.Select(Index));

        var comments = new List<string>();
        if (Comment("TABLE", Q(t.Name), t.Description) is { } tc) comments.Add(tc);
        foreach (var c in t.Columns)
            if (Comment("COLUMN", $"{Q(t.Name)}.{Q(c.Name)}", c.Description) is { } cc) comments.Add(cc);
        foreach (var i in t.Indices)
            if (Comment("INDEX", Q(i.Name), i.Description) is { } ic) comments.Add(ic);
        Section(sb, comments);

        AppendTriggers(sb, t.Triggers);
        return sb.ToString();
    }

    public static string View(ViewDefinition v)
    {
        var sb = new StringBuilder("CREATE OR ALTER VIEW ").Append(Q(v.Name));
        if (v.Columns.Count > 0)
        {
            sb.Append(" (\n");
            sb.Append(string.Join(",\n", v.Columns.Select(c => Indent + Q(c.Name))));
            sb.Append(')');
        }
        sb.Append("\nAS\n").Append(Source(v.Source, $"SELECT 1 FROM RDB$DATABASE /* {Resources.CoreStrings.DdlViewSourceNotStored} */"));
        return sb.ToString();
    }

    public static string Procedure(ProcedureDefinition p)
    {
        var sb = new StringBuilder("CREATE OR ALTER PROCEDURE ").Append(Q(p.Name));
        AppendParameters(sb, p.Inputs, " (\n");
        if (p.Outputs.Count > 0) AppendParameters(sb, p.Outputs, "\nRETURNS (\n");
        sb.Append("\nAS\n").Append(Source(p.Source, MissingBody));
        return sb.ToString();
    }

    public static string Function(FunctionDefinition f)
    {
        if (f.ModuleName is not null) return ExternalFunction(f);
        var sb = new StringBuilder("CREATE OR ALTER FUNCTION ").Append(Q(f.Name));
        AppendParameters(sb, f.Inputs, " (\n");
        sb.Append("\nRETURNS ").Append(f.Returns.Type);
        if (f.Returns.CharacterSet is { } cs) sb.Append(" CHARACTER SET ").Append(Q(cs));
        if (f.Returns.NotNull) sb.Append(" NOT NULL");
        if (f.Returns.Collation is { } co) sb.Append(" COLLATE ").Append(Q(co));
        if (f.Deterministic) sb.Append(" DETERMINISTIC");
        sb.Append("\nAS\n").Append(Source(f.Source, MissingBody));
        return sb.ToString();
    }

    public static string Trigger(TriggerDefinition t)
    {
        var sb = new StringBuilder("CREATE OR ALTER TRIGGER ").Append(Q(t.Name));
        if (t.Table is { } table) sb.Append(" FOR ").Append(Q(table));
        sb.Append('\n').Append(t.Inactive ? "INACTIVE " : "ACTIVE ").Append(TriggerTypeFormatter.Describe(t.Type))
          .Append(" POSITION ").Append(t.Position).Append('\n');
        var source = Source(t.Source, "AS\n" + MissingBody);
        sb.Append(StartsWithWord(source, "AS") ? source : "AS\n" + source);
        return sb.ToString();
    }

    public static string Package(PackageDefinition p)
    {
        var sb = new StringBuilder("SET TERM ^ ;\n\n");
        sb.Append("CREATE OR ALTER PACKAGE ").Append(Q(p.Name)).Append("\nAS\n").Append(Source(p.Header, "BEGIN\nEND")).Append("^\n");
        if (p.Body is not null)
            sb.Append("\nRECREATE PACKAGE BODY ").Append(Q(p.Name)).Append("\nAS\n").Append(Source(p.Body, "BEGIN\nEND")).Append("^\n");
        sb.Append("\nSET TERM ; ^\n");
        if (Comment("PACKAGE", Q(p.Name), p.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    public static string Generator(GeneratorDefinition g)
    {
        var sb = new StringBuilder("CREATE SEQUENCE ").Append(Q(g.Name));
        if (g.InitialValue != 0) sb.Append(" START WITH ").Append(N(g.InitialValue));
        if (g.Increment != 1) sb.Append(" INCREMENT BY ").Append(N(g.Increment));
        sb.Append(";\n");
        sb.Append(g.CurrentValue is { } v
            ? "-- " + string.Format(Resources.CoreStrings.DdlGeneratorValue, N(v), $"ALTER SEQUENCE {Q(g.Name)} RESTART WITH {N(v)};") + "\n"
            : "-- " + Resources.CoreStrings.DdlGeneratorNoPermission + "\n");
        if (Comment("SEQUENCE", Q(g.Name), g.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    public static string Exception(ExceptionDefinition e)
    {
        var sb = new StringBuilder("CREATE OR ALTER EXCEPTION ").Append(Q(e.Name)).Append(' ').Append(SqlIdentifier.Literal(e.Message)).Append(";\n");
        if (Comment("EXCEPTION", Q(e.Name), e.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    public static string Domain(DomainDefinition d)
    {
        var f = d.Field;
        var sb = new StringBuilder("CREATE DOMAIN ").Append(Q(f.Name)).Append(" AS ").Append(f.Type);
        if (f.CharacterSet is { } cs) sb.Append(" CHARACTER SET ").Append(Q(cs));
        if (f.Default is { } def) sb.Append('\n').Append(Indent).Append(DefaultClause(def, parameter: false));
        if (f.NotNull) sb.Append('\n').Append(Indent).Append("NOT NULL");
        if (d.Check is { } check) sb.Append('\n').Append(Indent).Append(InlineSafe(check.Trim()));
        if (f.Collation is { } co) sb.Append('\n').Append(Indent).Append("COLLATE ").Append(Q(co));
        sb.Append(";\n");
        if (Comment("DOMAIN", Q(f.Name), f.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    public static string Index(IndexDefinition i)
    {
        var sb = new StringBuilder("CREATE ");
        if (i.Unique) sb.Append("UNIQUE ");
        if (i.Descending) sb.Append("DESCENDING ");
        sb.Append("INDEX ").Append(Q(i.Name)).Append(" ON ").Append(Q(i.Table));
        sb.Append(i.Expression is { } e ? " COMPUTED BY " + Parenthesize(InlineSafe(e)) : $" ({Cols(i.Columns)})").Append(';');
        if (i.Inactive) sb.Append("\nALTER INDEX ").Append(Q(i.Name)).Append(" INACTIVE;");
        return sb.ToString();
    }

    public static string Role(RoleDefinition r)
    {
        var sb = new StringBuilder("CREATE ROLE ").Append(Q(r.Name)).Append(";\n");
        if (r.Owner is { } owner) sb.Append("-- ").Append(Sql.SqlIdentifier.OneLine(string.Format(Resources.CoreStrings.DdlOwner, owner))).Append('\n');
        if (Comment("ROLE", Q(r.Name), r.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    public static string Constraint(string table, ConstraintDefinition c)
    {
        var sb = new StringBuilder("ALTER TABLE ").Append(Q(table)).Append(" ADD ");
        // Los nombres INTEG_n los pone el servidor: no se repiten (se vuelven a generar al crearla).
        if (!c.Name.StartsWith("INTEG_", StringComparison.Ordinal)) sb.Append("CONSTRAINT ").Append(Q(c.Name)).Append(' ');
        switch (c.Kind)
        {
            case ConstraintKind.PrimaryKey:
                sb.Append("PRIMARY KEY (").Append(Cols(c.Columns)).Append(')');
                break;
            case ConstraintKind.Unique:
                sb.Append("UNIQUE (").Append(Cols(c.Columns)).Append(')');
                break;
            case ConstraintKind.ForeignKey:
                sb.Append("FOREIGN KEY (").Append(Cols(c.Columns)).Append(") REFERENCES ").Append(Q(c.ReferencedTable ?? "?"))
                  .Append(" (").Append(Cols(c.ReferencedColumns ?? [])).Append(')');
                if (Rule(c.OnUpdate) is { } onUpdate) sb.Append(" ON UPDATE ").Append(onUpdate);
                if (Rule(c.OnDelete) is { } onDelete) sb.Append(" ON DELETE ").Append(onDelete);
                break;
            case ConstraintKind.Check:
                sb.Append(c.CheckSource is { } check ? InlineSafe(check.Trim()) : $"CHECK (1 = 1) /* {Resources.CoreStrings.DdlCheckSourceMissing} */");
                break;
        }
        if (c.Kind != ConstraintKind.Check && c.IndexName is { } index && index != c.Name && !index.StartsWith("RDB$", StringComparison.Ordinal))
            sb.Append(" USING ").Append(c.IndexDescending ? "DESCENDING " : "").Append("INDEX ").Append(Q(index));
        return sb.Append(';').ToString();
    }

    /// <summary>Definición de una columna de tabla: NOMBRE TIPO [CHARACTER SET] [DEFAULT] [NOT NULL] [COLLATE].</summary>
    public static string Column(FieldDefinition f, int width = 0)
    {
        var sb = new StringBuilder(Q(f.Name).PadRight(width));
        if (f.Computed is { } expression) return sb.Append(" COMPUTED BY ").Append(Parenthesize(InlineSafe(expression))).ToString();
        sb.Append(' ').Append(f.Type);
        if (f.CharacterSet is { } cs) sb.Append(" CHARACTER SET ").Append(Q(cs));
        if (f.Identity) sb.Append(" GENERATED BY DEFAULT AS IDENTITY");
        if (f.Default is { } def) sb.Append(' ').Append(DefaultClause(def, parameter: false));
        if (f.NotNull) sb.Append(" NOT NULL");
        if (f.Collation is { } co) sb.Append(" COLLATE ").Append(Q(co));
        return sb.ToString();
    }

    /// <summary>Parámetro de procedimiento o función: NOMBRE TIPO [NOT NULL] [COLLATE] [= valor].</summary>
    public static string Parameter(FieldDefinition f, int width = 0)
    {
        var sb = new StringBuilder(Q(f.Name).PadRight(width)).Append(' ').Append(f.Type);
        if (f.CharacterSet is { } cs) sb.Append(" CHARACTER SET ").Append(Q(cs));
        if (f.NotNull) sb.Append(" NOT NULL");
        if (f.Collation is { } co) sb.Append(" COLLATE ").Append(Q(co));
        if (f.Default is { } def) sb.Append(' ').Append(DefaultClause(def, parameter: true));
        return sb.ToString();
    }

    /// <summary>
    /// Un fragmento guardado en RDB$ que va en medio de una sentencia (default, cálculo, CHECK) puede traer un comentario
    /// de línea («DEFAULT 0 -- contador»): lo que siga en ese renglón (la coma, el punto y coma) quedaría comentado y la
    /// sentencia no compilaría. El comentario se pasa a /* … */; los textos entre comillas no se tocan.
    /// </summary>
    public static string InlineSafe(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c is '\'' or '"')
            {
                var end = i + 1;
                while (end < sql.Length && (sql[end] != c || (end + 1 < sql.Length && sql[end + 1] == c && ++end > 0))) end++;
                end = Math.Min(end, sql.Length - 1);
                sb.Append(sql, i, end - i + 1);
                i = end;
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                sb.Append(sql, i, end - i);
                i = end - 1;
            }
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOfAny(['\r', '\n'], i);
                if (end < 0) end = sql.Length;
                sb.Append("/* ").Append(sql[(i + 2)..end].Trim().Replace("*/", "* /")).Append(" */");
                i = end - 1;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string? Comment(string objectType, string quotedName, string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : $"COMMENT ON {objectType} {quotedName} IS {SqlIdentifier.Literal(description)};";

    private static string MissingBody => $"BEGIN\n  /* {Resources.CoreStrings.DdlSourceNotStored} */\nEND";

    private static string ExternalFunction(FunctionDefinition f)
    {
        var sb = new StringBuilder("DECLARE EXTERNAL FUNCTION ").Append(Q(f.Name));
        if (f.Inputs.Count > 0) sb.Append('\n').Append(Indent).Append(string.Join(", ", f.Inputs.Select(i => i.Type)));
        sb.Append("\nRETURNS ").Append(f.Returns.Type);
        sb.Append(f.ReturnMechanism switch
        {
            0 => " BY VALUE",
            2 => " BY DESCRIPTOR",
            < 0 => " FREE_IT",
            _ => "",
        });
        sb.Append("\nENTRY_POINT ").Append(SqlIdentifier.Literal(f.EntryPoint ?? "")).Append(" MODULE_NAME ").Append(SqlIdentifier.Literal(f.ModuleName ?? "")).Append(";\n");
        if (Comment("EXTERNAL FUNCTION", Q(f.Name), f.Description) is { } c) sb.Append('\n').Append(c).Append('\n');
        return sb.ToString();
    }

    private static void AppendParameters(StringBuilder sb, IReadOnlyList<FieldDefinition> parameters, string opening)
    {
        if (parameters.Count == 0) return;
        var width = parameters.Max(p => Q(p.Name).Length);
        sb.Append(opening);
        sb.Append(string.Join(",\n", parameters.Select(p => Indent + Parameter(p, width))));
        sb.Append(')');
    }

    private static void AppendTriggers(StringBuilder sb, IReadOnlyList<TriggerDefinition> triggers)
    {
        if (triggers.Count == 0) return;
        sb.Append("\nSET TERM ^ ;\n");
        foreach (var t in triggers) sb.Append('\n').Append(Trigger(t)).Append("^\n");
        sb.Append("\nSET TERM ; ^\n");
        Section(sb, triggers.Select(t => Comment("TRIGGER", Q(t.Name), t.Description)).OfType<string>());
    }

    private static void Section(StringBuilder sb, IEnumerable<string> statements)
    {
        var first = true;
        foreach (var s in statements)
        {
            if (first) sb.Append('\n');
            first = false;
            sb.Append(s).Append('\n');
        }
    }

    private static string? Rule(string? rule) => rule?.Trim().ToUpperInvariant() switch
    {
        null or "" or "RESTRICT" or "NO ACTION" => null,
        var r => r,
    };

    private static string DefaultClause(string stored, bool parameter)
    {
        var s = InlineSafe(stored.Trim());
        if (StartsWithWord(s, "DEFAULT") || s.StartsWith('=')) return s;
        return (parameter ? "= " : "DEFAULT ") + s;
    }

    /// <summary>Fuente guardada en RDB$ sin los saltos de línea del principio ni los espacios del final.</summary>
    private static string Source(string? source, string missing) =>
        string.IsNullOrWhiteSpace(source) ? missing : source.TrimStart('\r', '\n').TrimEnd();

    private static string Parenthesize(string expression)
    {
        var e = expression.Trim();
        return IsWrapped(e) ? e : "(" + e + ")";
    }

    private static bool IsWrapped(string e)
    {
        if (e.Length < 2 || e[0] != '(' || e[^1] != ')') return false;
        var depth = 0;
        for (var i = 0; i < e.Length; i++)
        {
            if (e[i] == '(') depth++;
            else if (e[i] == ')' && --depth == 0 && i < e.Length - 1) return false;
        }
        return depth == 0;
    }

    private static bool StartsWithWord(string s, string word) =>
        s.StartsWith(word, StringComparison.OrdinalIgnoreCase) && (s.Length == word.Length || !char.IsLetterOrDigit(s[word.Length]) && s[word.Length] != '_');
}
