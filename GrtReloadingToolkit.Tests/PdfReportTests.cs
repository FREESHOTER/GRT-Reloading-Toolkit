using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Reports;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The PDF reports of the analysis tools: the page they are printed from, and what the archive keeps of them.</summary>
public sealed class PdfReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rapporto_test_" + Guid.NewGuid().ToString("N"));

    public PdfReportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static ReportDoc Doc(string body = "SD 3,2 m/s\n  41.5 gr   820") => new("Scala N550 <prova>", "Statistiche cronografo", "0.4.0",
        new DateTime(2026, 11, 16, 10, 5, 0), new List<(string, string)> { ("Carico", "Scala N550"), ("Arma", "") }, body,
        new[] { new ReportImage("Grafico", new byte[] { 0x89, 0x50, 0x4E, 0x47 }) });

    [Fact]
    public void PageKeepsTheTextAlignedEscapedAndTheChartsInside()
    {
        string html = PdfReport.Html(Doc());
        Assert.Contains("<html lang=\"it\">", html);
        Assert.Contains("Scala N550 &lt;prova&gt;", html);           // escaped, never markup
        Assert.Contains("16/11/2026 10:05", html);
        Assert.Contains("<pre>SD 3,2 m/s\n  41.5 gr   820</pre>", html);   // spaces kept: the report's tables stay in columns
        Assert.Contains("<td class=\"k\">Carico</td><td>Scala N550</td>", html);
        Assert.DoesNotContain("<td class=\"k\">Arma</td>", html);     // an empty fact is left out
        Assert.Contains("data:image/png;base64,iVBORw==", html);
        Assert.Contains("Reloading Toolkit Desktop v0.4.0", html);
    }

    [Fact]
    public void FileNameCarriesDateToolAndTitleWithoutForbiddenCharacters()
    {
        string p = PdfReport.DefaultPath(Doc() with { Title = "a/b:c" }, _dir);
        Assert.Equal(_dir, Path.GetDirectoryName(p));
        string name = Path.GetFileName(p);
        Assert.StartsWith("2026-11-16 1005 Statistiche cronografo - a", name);
        Assert.EndsWith(".pdf", name);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void ArchiveKeepsThePdfWithItsAnalysisAndDescribesTheLoad()
    {
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(a, null);
        long load = ArchiveImports.NewLoad(a, sh, "Scala N550", "6.5 Creedmoor");
        a.Exec($"UPDATE loads SET powder='Vihtavuori N550', charge_gr=41.5, bullet='Hornady ELD-M', bullet_weight_gr=140 WHERE id={load}");
        long session = a.Insert("sessions", ("shooter_id", sh), ("load_id", load), ("date", "2026-11-15"), ("distance_m", 100.0), ("temp_c", 12.5));
        byte[] pdf = { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 };
        long id = ArchiveQueries.SaveAnalysis(a, "Statistiche cronografo", "0.4.0", load, session, "{}", "testo", (pdf, "r.pdf"));
        Assert.Equal("rapporto PDF", a.ScalarP("SELECT f.kind FROM analyses an JOIN files f ON f.id=an.report_file_id WHERE an.id=$i", ("$i", id)));
        Assert.Equal(pdf, (byte[])a.ScalarP("SELECT f.data FROM analyses an JOIN files f ON f.id=an.report_file_id WHERE an.id=$i", ("$i", id))!);
        Assert.True(a.IsMaster);
        // without a PDF the analysis is kept as before
        long plain = ArchiveQueries.SaveAnalysis(a, "x", "0.4.0", null, null, "{}", "t");
        Assert.Equal(DBNull.Value, a.ScalarP("SELECT report_file_id FROM analyses WHERE id=$i", ("$i", plain)) ?? DBNull.Value);

        var facts = ArchiveQueries.ReportFacts(a, null, session).ToDictionary(f => f.Label, f => f.Value);
        Assert.Equal("Scala N550", facts["Carico"]);          // the load comes from the session
        Assert.Equal("Hornady ELD-M 140 gr", facts["Palla"]);
        Assert.Equal("Vihtavuori N550 41,5 gr", facts["Polvere"]);
        Assert.Equal("2026-11-15", facts["Seduta"]);
        Assert.Equal("100 m", facts["Distanza"]);
        Assert.Equal("12,5 °C", facts["Condizioni"]);
        Assert.Empty(ArchiveQueries.ReportFacts(a, null, null));

        // a GRT bullet name that already says its weight does not get it a second time
        a.Exec($"UPDATE loads SET bullet='Lapua, Scenar GB504, 100.00 grain', bullet_weight_gr=100 WHERE id={load}");
        Assert.Equal("Lapua, Scenar GB504, 100.00 grain", ArchiveQueries.ReportFacts(a, load, null).First(f => f.Label == "Palla").Value);
    }
}
