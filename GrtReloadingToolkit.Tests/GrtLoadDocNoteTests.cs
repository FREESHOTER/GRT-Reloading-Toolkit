using GrtPluginKit.Grt;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="GrtLoadDoc.FindNoteText"/> is the read-only counterpart to the already-used
/// <see cref="GrtLoadDoc.RemoveByTitlePrefix"/> -- built for Group Analysis to read the Ladder/OCW
/// Analyzer's own "OCW Analysis" note back out of the same file, without touching or removing it.
/// </summary>
public sealed class GrtLoadDocNoteTests
{
    [Fact]
    public void FindNoteTextReturnsTheDecodedTextOfAMatchingNote()
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        doc.AddNote("OCW Analysis 2026-01-01", "Recommended node (weighted): 40.0-40.4 gr\nNODE_GR=40.0-40.4\n");

        string? text = doc.FindNoteText("OCW Analysis");

        Assert.NotNull(text);
        Assert.Contains("NODE_GR=40.0-40.4", text);
    }

    [Fact]
    public void FindNoteTextReturnsNullWhenNoNoteMatches()
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        doc.AddNote("Some Other Note", "irrelevant");

        Assert.Null(doc.FindNoteText("OCW Analysis"));
    }

    [Fact]
    public void FindNoteTextReturnsTheNewestWhenSeveralNotesShareThePrefix()
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        doc.AddNote("OCW Analysis 2026-01-01", "NODE_GR=39.0-39.4");
        doc.AddNote("OCW Analysis 2026-01-02", "NODE_GR=40.0-40.4"); // added after -- the current one

        string? text = doc.FindNoteText("OCW Analysis");

        Assert.Contains("NODE_GR=40.0-40.4", text);
    }

    [Fact]
    public void FindNoteTextIsUnaffectedByRoundTripEncodingOfSpecialCharacters()
    {
        // AddNote URI-encodes both title and text; a real report contains '(', ')', '%', newlines --
        // this must decode back to exactly what was written, not something mangled.
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        string original = "Recommended node (weighted): 40.0-40.4 gr  (center 40.2)  -- weighted 50% chrono, 50% target\nNODE_GR=40.0-40.4\n";
        doc.AddNote("OCW Analysis", original);

        Assert.Equal(original, doc.FindNoteText("OCW Analysis"));
    }
}
