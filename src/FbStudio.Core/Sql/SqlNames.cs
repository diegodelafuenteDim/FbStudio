using System.Text;

namespace FbStudio.Core.Sql;

/// <summary>
/// Listas de nombres que se escriben a mano («agenda_turnos, PACIENTES») y el patrón SIMILAR TO que reconoce
/// exactamente esos nombres: lo que piden gbak (saltear los datos de algunas tablas) y la validación en línea.
/// </summary>
public static class SqlNames
{
    /// <summary>
    /// Los nombres separados por coma, punto y coma, espacios o renglones, como los guarda Firebird: sin comillas, en
    /// mayúsculas; entre comillas dobles, tal cual. Sin repetidos.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        var names = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        void Flush()
        {
            if (current.Length > 0)
            {
                var name = wasQuoted ? current.ToString() : current.ToString().ToUpperInvariant();
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }
            current.Clear();
            wasQuoted = false;
        }
        var s = text ?? "";
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < s.Length && s[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') { quoted = true; wasQuoted = true; }
            else if (c is ',' or ';' || char.IsWhiteSpace(c)) Flush();
            else current.Append(c);
        }
        Flush();
        return names;
    }

    /// <summary>
    /// Un patrón SIMILAR TO que reconoce solo estos nombres. «_» y «%» son comodines en SIMILAR TO (AGENDA_TURNOS
    /// también reconocería AGENDAXTURNOS), así que todo lo que no es letra, número o «$» va entre corchetes.
    /// </summary>
    public static string SimilarTo(IEnumerable<string> names) =>
        string.Join("|", names.Select(n => string.Concat(n.Select(c => char.IsAsciiLetterOrDigit(c) || c == '$' ? c.ToString() : $"[{c}]"))));
}
