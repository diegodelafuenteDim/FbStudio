namespace FbStudio.Core.Results;

/// <summary>
/// Cómo se ven las columnas de una grilla: el orden (por nombre), el ancho de cada una y las que están ocultas. Es solo la
/// vista: no cambia la consulta ni el orden, los filtros o los totales. Por nombre, así sobrevive a columnas nuevas o
/// borradas en la tabla.
/// </summary>
public sealed record GridColumnLayout(List<string> Order, Dictionary<string, double> Widths, List<string>? Hidden = null);
