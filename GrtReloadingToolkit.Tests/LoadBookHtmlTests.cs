using System.Globalization;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class LoadBookHtmlTests
{
    private static LoadBook.Entry Entry(
        string caliber = "6.5 Creedmoor", string loadName = "Test Load", string firearm = "Tikka",
        string powder = "N140", string? bullet = "Berger 140", double? chargeGr = 41.0, double? coalMm = 57.2,
        double? ba = 0.485, double? a0 = null, double? mv = 850, double? sd = 4.2, double? es = 9.0, int shots = 5,
        double? groupMoa = 0.42, double? distanceM = 100, string? date = "2026-09-20", string? notes = null,
        bool fromJournal = true) =>
        new("path", loadName, caliber, firearm, powder, bullet, chargeGr, coalMm, ba, a0,
            mv, sd, es, shots, groupMoa, distanceM, date, notes, fromJournal);

    [Fact]
    public void RendersOneSectionPerCaliberWithItsOwnRows()
    {
        string html = LoadBookHtml.Render(new[]
        {
            Entry(caliber: "6.5 Creedmoor", loadName: "A"),
            Entry(caliber: ".308 Win", loadName: "B"),
        }, "My Book");

        Assert.Contains("<h2>6.5 Creedmoor</h2>", html);
        Assert.Contains("<h2>.308 Win</h2>", html);
        Assert.Contains(">A<", html);
        Assert.Contains(">B<", html);
    }

    [Fact]
    public void UserSuppliedTextIsHtmlEncodedNotInjected()
    {
        string html = LoadBookHtml.Render(new[] { Entry(notes: "<script>alert(1)</script> & \"quoted\"") }, "Book");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void NullOptionalFieldsRenderAsAPlaceholderRatherThanThrowing()
    {
        string html = LoadBookHtml.Render(new[]
        {
            Entry(bullet: null, coalMm: null, ba: null, a0: null, mv: null, sd: null, es: null,
                shots: 0, groupMoa: null, distanceM: null, date: null, notes: null, fromJournal: false),
        }, "Book");
        Assert.Contains("–", html); // the "-" placeholder appears at least once
    }

    /// <summary>
    /// The exact live-caught bug from the same session's Pressure Signs tool, guarded here too: a
    /// number formatted through a culture-dependent path renders with a comma on an Italian-locale
    /// machine. Ba/a0 and the group MOA are the two places this file formats a raw double itself
    /// (velocity/charge/length go through GrtUnits, already proven invariant elsewhere).
    /// </summary>
    [Fact]
    public void NumbersRenderTheSameRegardlessOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            string html = LoadBookHtml.Render(new[] { Entry(ba: 0.485, a0: 1.2, groupMoa: 0.42) }, "Book");
            Assert.Contains("0.485", html);
            Assert.Contains("0.42 MOA", html);
            Assert.DoesNotContain("0,485", html);
            Assert.DoesNotContain("0,42", html);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void FileOnlyRowsAreVisuallyDistinguishedFromJournalRows()
    {
        string html = LoadBookHtml.Render(new[] { Entry(fromJournal: false) }, "Book");
        Assert.Contains("no-journal", html);
    }
}
