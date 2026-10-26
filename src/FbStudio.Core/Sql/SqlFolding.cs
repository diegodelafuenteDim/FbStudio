namespace FbStudio.Core.Sql;

/// <summary>Un bloque que se puede plegar: lo que queda escondido, [Start, End).</summary>
public readonly record struct FoldRegion(int Start, int End);

/// <summary>
/// Los bloques plegables del editor: BEGIN…END y CASE…END (PSQL largo), los paréntesis (subconsultas, listas de
/// columnas) y los comentarios /* … */, cuando ocupan más de un renglón. Se esconde lo de adentro: quedan a la vista
/// «BEGIN … END». Los textos y los comentarios no cuentan (un «END» dentro de un texto no cierra nada).
/// </summary>
public static class SqlFolding
{
    public static IReadOnlyList<FoldRegion> Find(string text)
    {
        var regions = new List<FoldRegion>();
        var open = new Stack<SqlToken>();
        void Add(int start, int end)
        {
            if (end > start && text.AsSpan(start, end - start).Contains('\n')) regions.Add(new FoldRegion(start, end));
        }
        foreach (var token in SqlLexer.Tokenize(text, comments: true))
        {
            switch (token)
            {
                case { Kind: SqlTokenKind.Comment } when token.Text.StartsWith("/*", StringComparison.Ordinal) && token.Length >= 4:
                    Add(token.Start + 2, token.End - 2);
                    break;
                case { Kind: SqlTokenKind.Symbol, Text: "(" }:
                    open.Push(token);
                    break;
                case { Kind: SqlTokenKind.Symbol, Text: ")" }:
                    if (open.Count > 0 && open.Peek().Text == "(") Add(open.Pop().End, token.Start);
                    break;
                case { Kind: SqlTokenKind.Word } when token.IsWord("BEGIN") || token.IsWord("CASE"):
                    open.Push(token);
                    break;
                case { Kind: SqlTokenKind.Word } when token.IsWord("END"):
                    // Un paréntesis sin cerrar adentro (se está escribiendo) no se lleva el END.
                    while (open.Count > 0 && open.Peek().Text == "(") open.Pop();
                    if (open.Count > 0) Add(open.Pop().End, token.Start);
                    break;
            }
        }
        regions.Sort((a, b) => a.Start.CompareTo(b.Start));
        return regions;
    }
}
