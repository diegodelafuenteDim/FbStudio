using CommunityToolkit.Mvvm.ComponentModel;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

public enum SearchItemKind
{
    /// <summary>Encabezado de un tipo de objeto: «Procedimientos · 148 objetos · 160 coincidencias».</summary>
    Group,
    /// <summary>Un objeto con su primera coincidencia; se despliega si tiene varias.</summary>
    Object,
    /// <summary>Una coincidencia de un objeto con varias.</summary>
    Line,
}

/// <summary>
/// Fila de la lista de resultados de la búsqueda, agrupada por tipo y por objeto (lista plana con sangría, como el
/// árbol). El fragmento viene partido en tres para resaltar el texto buscado.
/// </summary>
public sealed partial class SearchListItem : ObservableObject
{
    public required SearchItemKind Kind { get; init; }
    public required MaterialIconKind Icon { get; init; }
    public required string Caption { get; init; }
    public string? Count { get; init; }
    public string? Where { get; init; }
    public string Before { get; init; } = "";
    public string Match { get; init; } = "";
    public string After { get; init; } = "";

    /// <summary>La coincidencia que se abre con doble clic (en un objeto, la primera).</summary>
    public MetadataSearchHit? Hit { get; init; }

    public IReadOnlyList<SearchListItem> Children { get; init; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private bool _isExpanded;

    public bool IsGroup => Kind == SearchItemKind.Group;
    public Avalonia.Media.FontWeight CaptionWeight => Kind switch
    {
        SearchItemKind.Group => Avalonia.Media.FontWeight.Bold,
        SearchItemKind.Object => Avalonia.Media.FontWeight.SemiBold,
        _ => Avalonia.Media.FontWeight.Normal,
    };
    public bool HasIcon => Kind != SearchItemKind.Line;
    public bool HasSnippet => Kind != SearchItemKind.Group;
    public string Glyph => Children.Count == 0 ? "" : IsExpanded ? "▾" : "▸";
    public double Indent => Kind switch { SearchItemKind.Group => 0, SearchItemKind.Object => 18, _ => 54 };

    /// <summary>Arma los grupos (por tipo de objeto) con sus objetos y coincidencias.</summary>
    public static IReadOnlyList<SearchListItem> Build(IReadOnlyList<MetadataSearchHit> hits, string term) =>
        hits.GroupBy(h => h.Kind)
            .OrderBy(g => g.Key)
            .Select(group =>
            {
                var objects = group.GroupBy(h => h.Name, StringComparer.Ordinal)
                    .OrderBy(o => o.Key, StringComparer.Ordinal)
                    .Select(o => ObjectItem(o.Key, o.OrderBy(h => h.Area).ThenBy(h => h.Line ?? 0).ToList(), term))
                    .ToList();
                var total = group.Count();
                return new SearchListItem
                {
                    Kind = SearchItemKind.Group,
                    Icon = DatabaseTreeViewModel.IconFor(group.Key),
                    Caption = DbObjectKinds.Label(group.Key),
                    Count = (objects.Count == 1 ? Strings.SearchOneObject : string.Format(Strings.SearchObjects, objects.Count))
                            + " · " + (total == 1 ? Strings.SearchOneMatch : string.Format(Strings.SearchMatches, total)),
                    Children = objects,
                    IsExpanded = true,
                };
            })
            .ToList();

    private static SearchListItem ObjectItem(string name, List<MetadataSearchHit> hits, string term)
    {
        var first = hits[0];
        var (before, match, after) = Split(first.Snippet, term);
        return new SearchListItem
        {
            Kind = SearchItemKind.Object,
            Icon = DatabaseTreeViewModel.IconFor(first.Kind),
            Caption = name,
            Count = hits.Count > 1 ? string.Format(Strings.SearchMatches, hits.Count) : null,
            Where = WhereOf(first),
            Before = before,
            Match = match,
            After = after,
            Hit = first,
            Children = hits.Count > 1 ? hits.Select(h => LineItem(h, term)).ToList() : [],
        };
    }

    private static SearchListItem LineItem(MetadataSearchHit hit, string term)
    {
        var (before, match, after) = Split(hit.Snippet, term);
        return new SearchListItem
        {
            Kind = SearchItemKind.Line,
            Icon = MaterialIconKind.ChevronRight,
            Caption = "",
            Where = WhereOf(hit),
            Before = before,
            Match = match,
            After = after,
            Hit = hit,
        };
    }

    public static string WhereOf(MetadataSearchHit hit) => hit.Area switch
    {
        SearchArea.Source => hit.Line is { } line ? string.Format(Strings.SearchAtLine, line) : Strings.SearchAreaSource,
        SearchArea.Name => Strings.SearchAreaName,
        SearchArea.Column => Strings.SearchAreaColumn,
        _ => Strings.SearchAreaDescription,
    };

    /// <summary>Parte el fragmento en antes / texto buscado / después (primera aparición, sin distinguir mayúsculas).</summary>
    public static (string Before, string Match, string After) Split(string snippet, string term)
    {
        var i = term.Length == 0 ? -1 : snippet.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? (snippet, "", "") : (snippet[..i], snippet.Substring(i, term.Length), snippet[(i + term.Length)..]);
    }
}
