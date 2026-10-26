namespace FbStudio.Core.Metadata;

public enum DiffKind
{
    Same,

    /// <summary>Solo en el primer texto (en la comparación de bases, el origen: lo que se agrega al destino).</summary>
    OnlyInA,

    /// <summary>Solo en el segundo (el destino: lo que se le saca).</summary>
    OnlyInB,
}

/// <param name="LineA">Renglón en el primer texto (desde 0); -1 si no está.</param>
/// <param name="LineB">Renglón en el segundo; -1 si no está.</param>
public readonly record struct DiffLine(DiffKind Kind, int LineA, int LineB);

/// <summary>
/// Diferencias renglón por renglón entre dos textos (el algoritmo de Myers, el de diff y git), para marcar en «Comparar
/// bases» qué cambia en el DDL del origen y del destino. Los espacios del final no cuentan. Con textos enormes o casi
/// todo distinto (más de 3.000 cambios) no busca el mínimo: marca todo como distinto.
/// </summary>
public static class LineDiff
{
    private const int MaxEdits = 3_000;

    public static IReadOnlyList<DiffLine> Compute(string a, string b) => Compute(Lines(a), Lines(b));

    public static IReadOnlyList<DiffLine> Compute(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = a.Count, m = b.Count, max = n + m;
        if (max == 0) return [];
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var edits = -1;
        for (var d = 0; d <= Math.Min(max, MaxEdits) && edits < 0; d++)
        {
            // Lo que había antes de este paso, para k en [-d-1, d+1]: con eso se vuelve atrás.
            trace.Add(v[(offset - d - 1)..(offset + d + 2)]);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal)) { x++; y++; }
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    edits = d;
                    break;
                }
            }
        }
        if (edits < 0)
            return Enumerable.Range(0, n).Select(i => new DiffLine(DiffKind.OnlyInA, i, -1))
                .Concat(Enumerable.Range(0, m).Select(j => new DiffLine(DiffKind.OnlyInB, -1, j))).ToList();

        var result = new List<DiffLine>();
        int cx = n, cy = m;
        for (var d = edits; d >= 0; d--)
        {
            var snap = trace[d];
            int V(int k) => snap[k + d + 1];
            var k = cx - cy;
            var previousK = k == -d || (k != d && V(k - 1) < V(k + 1)) ? k + 1 : k - 1;
            var previousX = V(previousK);
            var previousY = previousX - previousK;
            while (cx > previousX && cy > previousY)
            {
                result.Add(new DiffLine(DiffKind.Same, cx - 1, cy - 1));
                cx--;
                cy--;
            }
            if (d > 0) result.Add(cx == previousX ? new DiffLine(DiffKind.OnlyInB, -1, cy - 1) : new DiffLine(DiffKind.OnlyInA, cx - 1, -1));
            cx = previousX;
            cy = previousY;
        }
        result.Reverse();
        return result;
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToArray();
}
