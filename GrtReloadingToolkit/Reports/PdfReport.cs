using System.Diagnostics;
using System.Net;
using System.Text;

namespace GrtReloadingToolkit.Reports;

/// <summary>A picture in a report (a tool's chart), with its caption.</summary>
public sealed record ReportImage(string Caption, byte[] Png);

/// <summary>One report: the tool, what it was about (shooter, load, session…), its text and its charts.</summary>
public sealed record ReportDoc(string Title, string Tool, string Version, DateTime When,
    IReadOnlyList<(string Label, string Value)> Facts, string Body, IReadOnlyList<ReportImage> Images);

/// <summary>
/// A tool's report as an A4 PDF in Italian: a page of HTML (header, the facts, the report text with its tables kept aligned,
/// the charts) printed by the browser Windows already has (Edge), so nothing else is installed.
/// </summary>
public static class PdfReport
{
    public static string Html(ReportDoc d)
    {
        static string E(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>").Append(E(d.Title)).Append("</title><style>")
          .Append("@page{size:A4;margin:14mm 14mm 16mm 14mm}")
          .Append("body{font-family:'Segoe UI',Arial,sans-serif;font-size:10pt;color:#1b1b1b}")
          .Append("h1{font-size:17pt;margin:0 0 2mm 0;color:#1f3d5c}")
          .Append(".sub{color:#555;margin-bottom:5mm}")
          .Append("table.f{border-collapse:collapse;margin-bottom:5mm;width:100%}")
          .Append("table.f td{border:1px solid #c8d2dc;padding:1.2mm 2.5mm;vertical-align:top}")
          .Append("table.f td.k{background:#e8eef4;width:32%;font-weight:600}")
          .Append("pre{font-family:Consolas,'Courier New',monospace;font-size:8.6pt;white-space:pre-wrap;background:#f6f8fa;")
          .Append("border:1px solid #dde3e8;padding:3mm;line-height:1.35}")
          .Append("figure{margin:5mm 0;break-inside:avoid;text-align:center}img{max-width:100%;max-height:120mm}")
          .Append("figcaption{color:#555;font-size:9pt;margin-top:1.5mm}")
          .Append(".foot{margin-top:6mm;color:#777;font-size:8pt;border-top:1px solid #ccc;padding-top:2mm}")
          .Append("</style></head><body>");
        sb.Append("<h1>").Append(E(d.Title)).Append("</h1>");
        sb.Append("<div class=\"sub\">").Append(E(d.Tool)).Append(" · ").Append(E(d.When.ToString("dd/MM/yyyy HH:mm"))).Append("</div>");
        if (d.Facts.Count > 0)
        {
            sb.Append("<table class=\"f\">");
            foreach (var (k, v) in d.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
                sb.Append("<tr><td class=\"k\">").Append(E(k)).Append("</td><td>").Append(E(v)).Append("</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("<pre>").Append(E(d.Body.Replace("\r\n", "\n").TrimEnd())).Append("</pre>");
        foreach (var img in d.Images.Where(i => i.Png is { Length: > 0 }))
            sb.Append("<figure><img src=\"data:image/png;base64,").Append(Convert.ToBase64String(img.Png)).Append("\"><figcaption>")
              .Append(E(img.Caption)).Append("</figcaption></figure>");
        sb.Append("<div class=\"foot\">Reloading Toolkit Desktop v").Append(E(d.Version))
          .Append(" — rapporto creato dal programma; i numeri vengono dai dati indicati sopra.</div></body></html>");
        return sb.ToString();
    }

    /// <summary>Edge (always on Windows 10/11), else Chrome; null when neither is there.</summary>
    public static string? FindBrowser()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RICARICA_BROWSER"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    /// <summary>Documenti\Ricarica\rapporti\&lt;date time&gt; &lt;tool&gt; - &lt;title&gt;.pdf</summary>
    public static string DefaultPath(ReportDoc d, string folder)
        => Path.Combine(folder, Recovery.HistoryRecovery.SafeName($"{d.When:yyyy-MM-dd HHmm} {d.Tool} - {d.Title}") + ".pdf");

    /// <summary>
    /// Prints <paramref name="d"/> to <paramref name="pdfPath"/> and returns its bytes. Without a browser the page is kept as
    /// .html beside it and the error says where, so the report is never lost.
    /// </summary>
    public static byte[] Write(ReportDoc d, string pdfPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        string html = Html(d);
        string? browser = FindBrowser();
        if (browser == null)
        {
            string htmlPath = Path.ChangeExtension(pdfPath, ".html");
            File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
            throw new InvalidOperationException("Nessun browser (Edge o Chrome) per fare il PDF: la pagina è stata salvata come " + htmlPath);
        }
        // folders an earlier report could not remove while the browser was still closing
        try
        {
            foreach (var old in new DirectoryInfo(Path.GetTempPath()).GetDirectories("rapporto_*"))
                if (old.CreationTimeUtc < DateTime.UtcNow.AddMinutes(-10)) try { old.Delete(true); } catch { }
        }
        catch { }
        string tmp = Path.Combine(Path.GetTempPath(), "rapporto_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string src = Path.Combine(tmp, "rapporto.html");
            File.WriteAllText(src, html, new UTF8Encoding(false));
            string outPdf = Path.Combine(tmp, "rapporto.pdf");
            var psi = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string a in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-pdf-header-footer",
                         "--user-data-dir=" + Path.Combine(tmp, "profilo"), "--print-to-pdf=" + outPdf, new Uri(src).AbsoluteUri })
                psi.ArgumentList.Add(a);
            // msedge.exe may hand the job to a child process and return at once, so the PDF is waited for, not the process:
            // done when it ends with %%EOF and has stopped growing.
            using (Process.Start(psi)!) { }
            var limit = DateTime.UtcNow.AddSeconds(90);
            long last = -1;
            byte[]? bytes = null;
            while (DateTime.UtcNow < limit)
            {
                Thread.Sleep(250);
                if (!File.Exists(outPdf)) continue;
                long len = new FileInfo(outPdf).Length;
                if (len > 500 && len == last)
                {
                    try { bytes = File.ReadAllBytes(outPdf); } catch (IOException) { continue; }
                    if (Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 32), Math.Min(32, bytes.Length)).Contains("%%EOF")) break;
                    bytes = null;
                }
                last = len;
            }
            if (bytes == null)
                throw new InvalidOperationException("Il browser non ha scritto il PDF entro 90 secondi.");
            File.WriteAllBytes(pdfPath, bytes);
            return bytes;
        }
        finally
        {
            // the headless browser may still be closing and holding its profile folder for a moment
            for (int i = 0; i < 20 && Directory.Exists(tmp); i++)
            {
                try { Directory.Delete(tmp, true); } catch { Thread.Sleep(250); }
            }
        }
    }
}
