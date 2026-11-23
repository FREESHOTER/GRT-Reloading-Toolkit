using System.Diagnostics;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Reports;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// "PDF report" in the analysis tools: the tool's report and charts as a PDF in Documenti\Ricarica\rapporti, opened for the
/// shooter, and — when the archive is there — kept in it with the analysis it belongs to.
/// </summary>
internal static class ReportPdfAction
{
    public static async Task MakeAsync(Form owner, Button? button, ArchiveLink? link, string tool, string title,
        long? loadId, long? sessionId, string inputs, string report, IEnumerable<ReportImage> images)
    {
        var facts = new List<(string, string)>();
        try { if (link != null) facts.AddRange(ArchiveQueries.ReportFacts(link.Db, loadId, sessionId)); }
        catch (Exception ex) { Debug.WriteLine("report facts: " + ex.Message); }
        var doc = new ReportDoc(title, tool, AppVersion.Short, DateTime.Now, facts, report, images.ToList());
        string path = PdfReport.DefaultPath(doc, Path.Combine(ArchivePaths.Folder, "rapporti"));
        var cursor = owner.Cursor;
        if (button != null) button.Enabled = false;
        owner.Cursor = Cursors.WaitCursor;
        try
        {
            byte[] pdf = await Task.Run(() => PdfReport.Write(doc, path));
            string kept = "";
            if (link != null)
            {
                ArchiveQueries.SaveAnalysis(link.Db, tool, AppVersion.Short, loadId, sessionId, inputs, report, (pdf, Path.GetFileName(path)));
                kept = "\n\n" + Lang.T("The report and its analysis are also kept in the archive.");
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine("open pdf: " + ex.Message); }
            MessageBox.Show(owner, Lang.T("PDF report saved:") + "\n" + path + kept, tool, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, tool, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            owner.Cursor = cursor;
            if (button != null) button.Enabled = true;
        }
    }

    public static IEnumerable<ReportImage> Chart(string caption, byte[]? png)
        => png is { Length: > 0 } ? new[] { new ReportImage(caption, png) } : Array.Empty<ReportImage>();
}
