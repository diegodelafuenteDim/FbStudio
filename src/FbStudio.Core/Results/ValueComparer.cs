using System.Globalization;

namespace FbStudio.Core.Results;

/// <summary>
/// Compara valores de una columna para ordenar y agrupar: NULL primero, números entre sí aunque sean de
/// distinto tipo (INTEGER contra NUMERIC), textos según la cultura sin distinguir mayúsculas.
/// </summary>
public sealed class ValueComparer(CultureInfo culture) : IComparer<object?>
{
    public static ValueComparer Default { get; } = new(CultureInfo.CurrentCulture);

    private readonly CompareInfo _compare = culture.CompareInfo;

    public int Compare(object? a, object? b)
    {
        var aNull = CellFormatter.IsNull(a);
        var bNull = CellFormatter.IsNull(b);
        if (aNull || bNull) return aNull == bNull ? 0 : aNull ? -1 : 1;
        object x = a!, y = b!;

        if (x is string sa && y is string sb)
            return _compare.Compare(sa.TrimEnd(), sb.TrimEnd(), CompareOptions.IgnoreCase);
        if (IsNumber(x) && IsNumber(y))
            return Convert.ToDecimal(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(y, CultureInfo.InvariantCulture));
        if (x.GetType() == y.GetType() && x is IComparable cx)
            return cx.CompareTo(y);
        return string.CompareOrdinal(x.ToString(), y.ToString());
    }

    /// <summary>Igualdad para agrupar: la misma que para ordenar (sin el relleno de los CHAR, sin mayúsculas).</summary>
    public bool SameGroup(object? a, object? b) => Compare(a, b) == 0;

    public static bool IsNumber(object? v) => v is byte or short or int or long or decimal or double or float or sbyte or ushort or uint or ulong;
}
