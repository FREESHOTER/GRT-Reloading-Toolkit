namespace GrtPluginKit.Import;

/// <summary>Reads a CSV/TSV/semicolon file into a jagged string grid, sniffing the delimiter.</summary>
public static class DelimitedReader
{
    public static List<string[]> ReadRows(string path)
    {
        // utf-8-sig: strip a BOM if present
        string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                        .Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return new List<string[]>();

        char delim = SniffDelimiter(lines.Take(10));
        return lines.Select(l => SplitLine(l, delim)).ToList();
    }

    private static char SniffDelimiter(IEnumerable<string> sample)
    {
        char best = ',';
        int bestScore = -1;
        foreach (char d in new[] { ',', ';', '\t' })
        {
            var counts = sample.Select(l => SplitLine(l, d).Length).ToList();
            if (counts.Count == 0) continue;
            int cols = counts.Max();
            if (cols < 2) continue;
            int consistent = counts.Count(c => c == cols);
            int score = consistent * 100 + cols;
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    private static string[] SplitLine(string line, char delim)
    {
        var outp = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (q && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else q = !q;
            }
            else if (ch == delim && !q) { outp.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(ch);
        }
        outp.Add(cur.ToString().Trim());
        return outp.ToArray();
    }
}
