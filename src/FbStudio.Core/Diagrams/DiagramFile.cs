using FbStudio.Core.Metadata;

namespace FbStudio.Core.Diagrams;

/// <summary>Dónde quedó una tabla en el diagrama.</summary>
public sealed record DiagramPosition(double X, double Y);

/// <summary>
/// Un diagrama guardado (.fbsdiagram, F-8): de qué tabla o área sale, las opciones, las tablas sumadas, sacadas y
/// comodines mostradas, el camino resaltado y dónde quedó cada tabla. No guarda la estructura de la base: al abrirlo se
/// vuelve a leer, así que una tabla nueva o una FK que cambió aparecen como están ahora.
/// </summary>
public sealed record DiagramFile
{
    public const string Extension = ".fbsdiagram";

    public int Version { get; set; } = 1;

    /// <summary>La base donde se armó (solo para mostrarlo: se abre en la que se elija).</summary>
    public string? Database { get; init; }

    /// <summary>La tabla, o el nombre del área.</summary>
    public string Source { get; set; } = "";

    /// <summary>La definición del área, si el diagrama es de un área (como estaba al guardarlo).</summary>
    public SchemaArea? Area { get; init; }

    public int Hops { get; set; } = 1;
    public bool HideHubs { get; set; } = true;
    public bool ShowLogical { get; set; } = true;

    /// <summary>0 solo claves, 1 todas, 2 ninguna.</summary>
    public int Columns { get; init; }

    public bool ShowOutside { get; init; }

    /// <summary>0 automático, 1 en capas, 2 por fuerzas, 3 por distancias.</summary>
    public int Layout { get; init; }

    public IReadOnlyList<string> Added { get; set; } = [];
    public IReadOnlyList<string> Removed { get; set; } = [];
    public IReadOnlyList<string> ShownHubs { get; set; } = [];
    public IReadOnlyList<string> Path { get; set; } = [];
    public IReadOnlyDictionary<string, DiagramPosition> Positions { get; set; } = new Dictionary<string, DiagramPosition>();
}
