using System.Globalization;
using R = FbStudio.Core.Resources.CoreStrings;

namespace FbStudio.Core.Metadata;

/// <summary>
/// «Datos del servidor…» (F-16): lo de <see cref="ServerInfo"/> más la versión completa de la API de servicios. Nunca
/// rutas del servidor (ni el archivo de la base ni la carpeta de Firebird): para eso están los alias (Diego, D-53).
/// </summary>
public sealed record ServerDetails(ServerInfo Info, string? ServerVersion)
{
    /// <summary>Las filas de la ventana, en orden: (qué, valor).</summary>
    public IReadOnlyList<(string Label, string Value)> Rows(string address)
    {
        var info = Info;
        var mode = info.Mode;
        var rows = new List<(string, string)>
        {
            (R.ServerRowAddress, address),
            (R.ServerRowVersion, ServerVersion is null ? info.Display : $"{info.Display} ({ServerVersion})"),
            (R.ServerRowMode, ServerModes.Label(mode)),
            (R.ServerRowMeaning, ServerModes.Description(mode)),
        };
        if (info.ModeEvidence is { } evidence && mode != ServerMode.Unknown) rows.Add((R.ServerRowEvidence, evidence.Reason));
        rows.Add((R.ServerRowPlatform, info.Platform ?? "—"));
        rows.Add((R.ServerRowDatabase, string.Format(R.ServerDatabaseLine, $"{info.OdsMajor}.{info.OdsMinor}", info.Dialect, info.PageSize, info.Charset ?? "NONE")));
        if (info.PageBuffers > 0) rows.Add((R.ServerRowCache, Cache(info)));
        return rows;
    }

    /// <summary>Todo junto, para «Copiar».</summary>
    public string Text(string address) => string.Join(Environment.NewLine, Rows(address).Select(r => $"{r.Label}: {r.Value}"));

    /// <summary>«2.048 páginas (32 MB), compartida» o «256 páginas (4 MB) por conexión».</summary>
    private static string Cache(ServerInfo info)
    {
        var size = Size((long)info.PageBuffers * info.PageSize);
        var format = info.Mode switch
        {
            ServerMode.SuperServer => R.ServerCacheShared,
            ServerMode.Classic or ServerMode.SuperClassic => R.ServerCachePerConnection,
            _ => R.ServerCache,
        };
        return string.Format(format, info.PageBuffers, size);
    }

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} GB", bytes / (double)(1L << 30))
        : bytes >= 1L << 20 ? string.Format(CultureInfo.CurrentCulture, "{0:N0} MB", bytes / (double)(1L << 20))
        : string.Format(CultureInfo.CurrentCulture, "{0:N0} KB", bytes / 1024.0);
}
