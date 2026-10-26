using System.Globalization;
using System.Text;

namespace FbStudio.Core.Results;

/// <summary>
/// Buscar un texto en lo que muestra la grilla (Ctrl+F sobre los resultados): las celdas cuyo texto, como se ve, lo
/// contiene, sin distinguir mayúsculas ni acentos; recorre fila por fila y, en cada una, las columnas en el orden en que
/// se ven. NULL no coincide con nada.
/// </summary>
public static class GridSearch
{
    /// <summary>Con más coincidencias que esto se deja de buscar (y se avisa que hay más).</summary>
    public const int Limit = 100_000;

    /// <param name="rows">Las filas que se ven, con su posición en la grilla.</param>
    /// <param name="columns">Las columnas que se ven, en orden; se devuelve su <see cref="ColumnInfo.Ordinal"/>.</param>
    public static IReadOnlyList<(int Display, int Column)> Find(IEnumerable<(int Display, IReadOnlyList<object?> Row)> rows,
        IReadOnlyList<ColumnInfo> columns, string text)
    {
        var needle = Fold(text.Trim());
        var found = new List<(int, int)>();
        if (needle.Length == 0) return found;
        foreach (var (display, row) in rows)
        {
            foreach (var c in columns)
            {
                var value = row[c.Ordinal];
                if (CellFormatter.IsNull(value) || !Fold(CellFormatter.Format(value, c.DataTypeName)).Contains(needle, StringComparison.Ordinal)) continue;
                found.Add((display, c.Ordinal));
                if (found.Count >= Limit) return found;
            }
        }
        return found;
    }

    /// <summary>Sin acentos y en minúsculas: «Peña» y «PENA» son lo mismo.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
