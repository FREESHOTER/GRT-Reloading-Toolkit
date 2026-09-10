using System.IO.Compression;
using System.Xml;

namespace GrtPluginKit.Import;

/// <summary>
/// Tiny read-only SpreadsheetML (.xlsx) reader — just enough to pull the first
/// worksheet out as a jagged string grid. No external dependency: an .xlsx is a
/// ZIP of XML parts. Handles shared strings, inline strings and numeric cells.
/// </summary>
public static class XlsxReader
{
    /// <summary>Reads the first worksheet as rows of cell text (0-based, ragged, gaps filled with "").</summary>
    public static List<string[]> ReadFirstSheet(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        string[] shared = ReadSharedStrings(zip);
        ZipArchiveEntry sheet = FindFirstSheet(zip)
            ?? throw new InvalidDataException("No worksheet found in " + Path.GetFileName(path));

        var rows = new List<string[]>();
        using Stream s = sheet.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true });

        var pending = new List<string>();
        int pendingRowNumber = 0;           // 1-based value of the row's r="" attribute
        int autoRow = 0;

        void Flush()
        {
            if (pendingRowNumber <= 0) { pending.Clear(); return; }
            int idx = pendingRowNumber - 1;
            while (rows.Count <= idx) rows.Add(Array.Empty<string>());
            rows[idx] = pending.ToArray();
            pending.Clear();
            pendingRowNumber = 0;
        }

        while (xr.Read())
        {
            if (xr.NodeType != XmlNodeType.Element) continue;

            if (xr.Name == "row")
            {
                Flush();
                pendingRowNumber = ParseInt(xr.GetAttribute("r"), ++autoRow);
                autoRow = pendingRowNumber;
                continue;
            }

            if (xr.Name == "c" && pendingRowNumber > 0)
            {
                int col = ColumnIndex(xr.GetAttribute("r") ?? "");
                string type = xr.GetAttribute("t") ?? "";
                string value = ReadCellValue(xr, type, shared);

                while (pending.Count < col) pending.Add("");
                if (pending.Count == col) pending.Add(value);
                else pending[col] = value;
            }
        }
        Flush();

        return rows;
    }

    private static string ReadCellValue(XmlReader xr, string type, string[] shared)
    {
        if (xr.IsEmptyElement) return "";
        string result = "";
        using XmlReader sub = xr.ReadSubtree();
        sub.Read(); // position on <c>
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element) continue;
            if (sub.Name == "v")
            {
                string raw = sub.ReadElementContentAsString();
                result = type == "s" && int.TryParse(raw, out int idx) && idx >= 0 && idx < shared.Length
                    ? shared[idx]
                    : raw;
            }
            else if (sub.Name == "t") // inline string <is><t>
            {
                result = sub.ReadElementContentAsString();
            }
        }
        return result.Trim();
    }

    private static string[] ReadSharedStrings(ZipArchive zip)
    {
        ZipArchiveEntry? e = zip.GetEntry("xl/sharedStrings.xml");
        if (e == null) return Array.Empty<string>();

        var list = new List<string>();
        using Stream s = e.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false });
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.Name == "si")
            {
                using XmlReader sub = xr.ReadSubtree();
                var sb = new System.Text.StringBuilder();
                while (sub.Read())
                    if (sub.NodeType == XmlNodeType.Element && sub.Name == "t")
                        sb.Append(sub.ReadElementContentAsString());
                list.Add(sb.ToString());
            }
        }
        return list.ToArray();
    }

    private static ZipArchiveEntry? FindFirstSheet(ZipArchive zip)
    {
        ZipArchiveEntry? direct = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (direct != null) return direct;

        return zip.Entries
            .Where(en => en.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                      && en.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(en => en.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>"B12" -> 1 ; "AA3" -> 26. Returns 0 if no letters.</summary>
    private static int ColumnIndex(string cellRef)
    {
        int col = 0, letters = 0;
        foreach (char c in cellRef)
        {
            if (c is >= 'A' and <= 'Z') { col = col * 26 + (c - 'A' + 1); letters++; }
            else if (c is >= 'a' and <= 'z') { col = col * 26 + (c - 'a' + 1); letters++; }
            else break;
        }
        return letters == 0 ? 0 : col - 1;
    }

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse(s, out int v) ? v : fallback;
}
