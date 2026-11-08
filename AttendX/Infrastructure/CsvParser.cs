using System.Text;

namespace AttendX.Infrastructure;

/// <summary>Small RFC-4180 style CSV reader (handles quotes, commas inside quotes, CRLF). Used by dataset seeding and CSV imports.</summary>
public static class CsvParser
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                row.Add(sb.ToString()); sb.Clear();
                if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add([.. row]);
                row = new List<string>();
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || row.Count > 0)
        {
            row.Add(sb.ToString());
            if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add([.. row]);
        }
        return rows;
    }

    /// <summary>Parses CSV text where the first row is the header; returns dictionaries keyed by lower-cased header name.</summary>
    public static List<Dictionary<string, string>> ParseWithHeader(string text)
    {
        var rows = Parse(text);
        var result = new List<Dictionary<string, string>>();
        if (rows.Count == 0) return result;
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        foreach (var r in rows.Skip(1))
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < header.Length; i++) d[header[i]] = i < r.Length ? r[i].Trim() : "";
            result.Add(d);
        }
        return result;
    }

    public static string Escape(string? v)
    {
        v ??= "";
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
