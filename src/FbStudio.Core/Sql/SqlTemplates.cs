namespace FbStudio.Core.Sql;

/// <summary>Una plantilla del editor (F-32): se escribe la abreviatura y Tab, o se elige con Ctrl+J.</summary>
/// <param name="Text">El texto; <see cref="SqlTemplates.Cursor"/> marca dónde queda el cursor.</param>
public sealed record SqlTemplate(string Abbreviation, string Name, string Text);

public static class SqlTemplates
{
    public const string Cursor = "${cursor}";

    /// <summary>Las de fábrica: consultas de todos los días y los esqueletos de PSQL de Firebird.</summary>
    public static IReadOnlyList<SqlTemplate> Defaults { get; } =
    [
        new("sel", "SELECT * FROM", "SELECT *\nFROM ${cursor}"),
        new("self", "SELECT FIRST 100 * FROM", "SELECT FIRST 100 *\nFROM ${cursor}"),
        new("selc", "SELECT COUNT(*) FROM", "SELECT COUNT(*)\nFROM ${cursor}"),
        new("selw", "SELECT * FROM … WHERE", "SELECT *\nFROM ${cursor}\nWHERE "),
        new("ins", "INSERT INTO … VALUES", "INSERT INTO ${cursor} ()\nVALUES ()"),
        new("upd", "UPDATE … SET … WHERE", "UPDATE ${cursor}\nSET \nWHERE "),
        new("del", "DELETE FROM … WHERE", "DELETE FROM ${cursor}\nWHERE "),
        new("uoi", "UPDATE OR INSERT … MATCHING", "UPDATE OR INSERT INTO ${cursor} ()\nVALUES ()\nMATCHING ()"),
        new("gen", "Valor de un generador", "SELECT GEN_ID(${cursor}, 0)\nFROM RDB$DATABASE"),
        new("eb", "EXECUTE BLOCK", "EXECUTE BLOCK\nRETURNS (RESULTADO VARCHAR(100))\nAS\nDECLARE VARIABLE I INTEGER;\nBEGIN\n  ${cursor}\n  SUSPEND;\nEND"),
        new("proc", "CREATE OR ALTER PROCEDURE", "CREATE OR ALTER PROCEDURE ${cursor} (\n  P INTEGER)\nRETURNS (\n  R INTEGER)\nAS\nBEGIN\n  \n  SUSPEND;\nEND"),
        new("func", "CREATE OR ALTER FUNCTION", "CREATE OR ALTER FUNCTION ${cursor} (\n  P INTEGER)\nRETURNS INTEGER\nAS\nBEGIN\n  RETURN P;\nEND"),
        new("trg", "CREATE OR ALTER TRIGGER", "CREATE OR ALTER TRIGGER ${cursor} FOR TABLA\nACTIVE BEFORE INSERT OR UPDATE POSITION 0\nAS\nBEGIN\n  \nEND"),
        new("tab", "CREATE TABLE", "CREATE TABLE ${cursor} (\n  ID INTEGER NOT NULL,\n  CONSTRAINT PK_ PRIMARY KEY (ID))"),
    ];

    /// <summary>La plantilla de esa abreviatura (sin distinguir mayúsculas).</summary>
    public static SqlTemplate? Find(IEnumerable<SqlTemplate> templates, string abbreviation) =>
        templates.FirstOrDefault(t => string.Equals(t.Abbreviation, abbreviation, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// El texto a insertar y dónde queda el cursor: los renglones siguientes llevan la sangría del renglón donde se
    /// inserta, y el fin de renglón el del documento.
    /// </summary>
    public static (string Text, int Caret) Expand(SqlTemplate template, string lineIndent = "", string newLine = "\n")
    {
        var text = template.Text.Replace("\r\n", "\n").Replace("\n", newLine + lineIndent);
        var caret = text.IndexOf(Cursor, StringComparison.Ordinal);
        if (caret < 0) return (text, text.Length);
        return (text.Remove(caret, Cursor.Length), caret);
    }

    /// <summary>La palabra justo antes del cursor (lo que se compara con las abreviaturas).</summary>
    public static (string Word, int Start) WordBefore(string text, int caret)
    {
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '$')) start--;
        return (text[start..caret], start);
    }
}
