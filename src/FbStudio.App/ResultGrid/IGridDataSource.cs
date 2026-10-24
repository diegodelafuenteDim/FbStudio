using FbStudio.Core.Results;

namespace FbStudio.App.ResultGrid;

/// <summary>
/// Fuente «en el servidor» de la grilla (pestaña Datos, F-3). Con una fuente, la grilla no ordena ni filtra lo cargado:
/// avisa el orden y los filtros elegidos y la fuente vuelve a leer del servidor. Pide más filas al llegar al final, y
/// permite editar celdas y marcar filas para borrar: los cambios quedan pendientes en la fuente hasta «Aplicar».
/// Las filas se identifican por su arreglo de valores (no por posición), así un cambio no se pierde al reordenar.
/// </summary>
public interface IGridDataSource
{
    /// <summary>Cambió el orden o un filtro (los grupos no se usan en este modo).</summary>
    void ViewChanged(ResultViewSpec spec);

    /// <summary>Se ven las últimas filas cargadas: si hay más, que las traiga.</summary>
    void NearEnd();

    /// <summary>Texto del pie («1.500 filas cargadas · bajá para traer más»).</summary>
    string FooterText { get; }

    bool CanEdit(int column);
    bool TryGetPending(object[] row, int column, out object? value);
    bool IsDeleted(object[] row);
    bool IsChanged(object[] row);

    /// <summary>El valor (pendiente o leído) como se edita.</summary>
    string EditText(object[] row, int column);

    /// <summary>Interpreta lo escrito; devuelve el error (y no cambia nada) si no se entiende.</summary>
    string? SetValue(object[] row, int column, string text);

    void SetNull(object[] row, int column);

    /// <summary>Fila nueva (sin aplicar): se marca en el número de fila.</summary>
    bool IsNew(object[] row);

    /// <summary>Agrega una fila nueva arriba de todo (Insert); null si no se puede agregar.</summary>
    object[]? AddRow();
    void ToggleDelete(IReadOnlyList<object[]> rows);
    void Revert(IReadOnlyList<object[]> rows);

    /// <summary>Si la ventanita de filtro puede traer los valores de esta columna (los blobs no se agrupan).</summary>
    bool CanListValues(int column);

    /// <summary>Los valores de una columna para tildar en su ventanita de filtro, del servidor (con los demás filtros).</summary>
    Task<IReadOnlyList<ValueCount>> DistinctValuesAsync(int column, int limit, CancellationToken ct);
}
