namespace FbStudio.Core.Metadata;

/// <summary>Convierte los códigos de RDB$FIELDS (RDB$FIELD_TYPE, SUB_TYPE, PRECISION, SCALE…) en el tipo SQL legible.</summary>
public static class FieldTypeFormatter
{
    public static string Format(int type, int? subType, int? length, int? charLength, int? precision, int? scale)
    {
        var s = -(scale ?? 0);
        string Exact(string integer)
        {
            // Los NUMERIC/DECIMAL se guardan como enteros con escala negativa.
            if (s > 0 || subType is 1 or 2)
                return $"{(subType == 2 ? "DECIMAL" : "NUMERIC")}({(precision is > 0 ? precision : DefaultPrecision(type))},{s})";
            return integer;
        }

        return type switch
        {
            7 => Exact("SMALLINT"),
            8 => Exact("INTEGER"),
            16 => Exact("BIGINT"),
            26 => Exact("INT128"),
            10 => "FLOAT",
            27 => s > 0 ? $"NUMERIC({precision ?? 15},{s})" : "DOUBLE PRECISION",
            24 => "DECFLOAT(16)",
            25 => "DECFLOAT(34)",
            12 => "DATE",
            13 => "TIME",
            35 => "TIMESTAMP",
            28 => "TIME WITH TIME ZONE",
            29 => "TIMESTAMP WITH TIME ZONE",
            23 => "BOOLEAN",
            14 => $"CHAR({charLength ?? length})",
            37 => $"VARCHAR({charLength ?? length})",
            40 => $"CSTRING({charLength ?? length})",
            261 => subType switch
            {
                1 => "BLOB SUB_TYPE TEXT",
                0 or null => "BLOB SUB_TYPE BINARY",
                _ => $"BLOB SUB_TYPE {subType}",
            },
            _ => $"TIPO {type}",
        };
    }

    private static int DefaultPrecision(int type) => type switch { 7 => 4, 8 => 9, 16 => 18, 26 => 38, _ => 18 };
}
