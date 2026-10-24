using System.Globalization;

namespace FbStudio.Core.Results;

public static class CellFormatter
{
    public const string Null = "NULL";

    public static string Format(object? value, string dataTypeName) => value switch
    {
        null or DBNull => Null,
        string s => s.TrimEnd(),
        DateTime d when dataTypeName.Equals("DATE", StringComparison.OrdinalIgnoreCase) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        byte[] b => string.Format(Resources.CoreStrings.XlsxBinary, b.Length),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static bool IsNull(object? value) => value is null or DBNull;
}
