namespace FbStudio.Core.Sql;

/// <summary>Un cambio en el texto del editor: reemplazar [Start, Start + Length) por Text y después elegir esa selección.</summary>
public readonly record struct TextEdit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// Comandos de edición del editor (comentar, duplicar, mover renglones, mayúsculas): calculan el cambio sobre el texto y
/// la selección sin tocar el editor; el editor lo aplica de una vez, así se deshace con un solo Ctrl+Z. Respetan los
/// saltos de línea del texto (\r\n o \n). Null: no hay nada que cambiar.
/// </summary>
public static class LineEdits
{
    /// <summary>
    /// Comenta con «-- » los renglones que toca la selección (o el del cursor), a la altura del menos sangrado; si ya
    /// estaban todos comentados, los descomenta. Los renglones en blanco no se tocan.
    /// </summary>
    public static TextEdit? ToggleComment(string text, int selStart, int selLength)
    {
        var (start, end) = Lines(text, selStart, selLength);
        var lines = text[start..end].Split('\n');
        static int Indent(string line) => line.Length - line.TrimStart(' ', '\t').Length;
        static bool Blank(string line) => line.TrimEnd('\r').Trim().Length == 0;
        var written = lines.Where(l => !Blank(l)).ToList();
        var uncomment = written.Count > 0 && written.All(l => l.AsSpan(Indent(l)).StartsWith("--"));
        var column = written.Count > 0 ? written.Min(Indent) : 0;
        var changes = new List<(int At, int Removed, int Added)>();
        var offset = start;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (uncomment && !Blank(line))
            {
                var at = Indent(line);
                var removed = line.Length > at + 2 && line[at + 2] == ' ' ? 3 : 2;
                lines[i] = line.Remove(at, removed);
                changes.Add((offset + at, removed, 0));
            }
            else if (!uncomment && (!Blank(line) || written.Count == 0))
            {
                var at = Math.Min(column, line.TrimEnd('\r').Length);
                lines[i] = line.Insert(at, "-- ");
                changes.Add((offset + at, 0, 3));
            }
            offset += line.Length + 1;
        }
        // Dónde queda cada posición: corrida por lo agregado o sacado antes; el comienzo de una selección queda antes del
        // «-- » que se agrega justo ahí (así el comentario queda elegido), el cursor solo pasa después.
        int Map(int position, bool beforeInsert)
        {
            var mapped = position;
            foreach (var (at, removed, added) in changes)
            {
                if (position < at || (position == at && removed == 0 && beforeInsert)) break;
                mapped += position >= at + removed ? added - removed : at - position;
            }
            return mapped;
        }
        var newStart = Map(selStart, beforeInsert: selLength > 0);
        var newLength = selLength == 0 ? 0 : Map(selStart + selLength, beforeInsert: false) - newStart;
        return new TextEdit(start, end - start, string.Join('\n', lines), newStart, newLength);
    }

    /// <summary>Duplica lo elegido (la copia queda elegida) o, sin selección, el renglón del cursor (el cursor baja a la copia).</summary>
    public static TextEdit? DuplicateLine(string text, int selStart, int selLength)
    {
        if (selLength > 0)
        {
            var selected = text.Substring(selStart, selLength);
            return new TextEdit(selStart + selLength, 0, selected, selStart + selLength, selLength);
        }
        var (start, end) = Lines(text, selStart, 0);
        var newLine = NewLine(text);
        var line = text[start..end];
        return new TextEdit(end, 0, newLine + line, selStart + line.Length + newLine.Length, 0);
    }

    /// <summary>Sube (o baja) los renglones que toca la selección, intercambiándolos con el de arriba (o el de abajo).</summary>
    public static TextEdit? MoveLines(string text, int selStart, int selLength, bool up)
    {
        var (start, end) = Lines(text, selStart, selLength);
        var block = text[start..end];
        if (up)
        {
            if (start == 0) return null;
            var sepStart = start - 1;
            if (sepStart > 0 && text[sepStart - 1] == '\r') sepStart--;
            var separator = text[sepStart..start];
            var previousStart = sepStart == 0 ? 0 : text.LastIndexOf('\n', sepStart - 1) + 1;
            var previous = text[previousStart..sepStart];
            var shift = previous.Length + separator.Length;
            return new TextEdit(previousStart, end - previousStart, block + separator + previous, selStart - shift, selLength);
        }
        if (end == text.Length) return null;
        var sepEnd = end + (text[end] == '\r' ? 2 : 1);
        var sep = text[end..sepEnd];
        var nextEnd = text.IndexOf('\n', sepEnd);
        if (nextEnd < 0) nextEnd = text.Length;
        else if (nextEnd > sepEnd && text[nextEnd - 1] == '\r') nextEnd--;
        var next = text[sepEnd..nextEnd];
        return new TextEdit(start, nextEnd - start, next + sep + block, selStart + next.Length + sep.Length, selLength);
    }

    /// <summary>Lo elegido (o, sin selección, la palabra del cursor) en mayúsculas o en minúsculas.</summary>
    public static TextEdit? ChangeCase(string text, int selStart, int selLength, bool upper)
    {
        var start = selStart;
        var end = selStart + selLength;
        if (selLength == 0)
        {
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
        }
        if (end == start) return null;
        var part = text[start..end];
        var changed = upper ? part.ToUpperInvariant() : part.ToLowerInvariant();
        return changed == part ? null : new TextEdit(start, end - start, changed, selStart, selLength);
    }

    /// <summary>
    /// Los renglones que toca la selección: desde el comienzo del primero hasta el final del último, sin su salto. Si la
    /// selección termina justo al comienzo de un renglón (se eligieron renglones enteros), ese no cuenta.
    /// </summary>
    private static (int Start, int End) Lines(string text, int selStart, int selLength)
    {
        var start = selStart == 0 ? 0 : text.LastIndexOf('\n', selStart - 1) + 1;
        var last = selStart + selLength;
        if (selLength > 0 && last > start && text[last - 1] == '\n') last--;
        var end = text.IndexOf('\n', last);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, end);
    }

    private static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
