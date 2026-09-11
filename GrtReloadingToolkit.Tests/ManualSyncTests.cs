using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The manual exists three times: MANUAL.md, an English HTML rendering, and an Italian
/// translation. Nothing generates one from another — the HTML pages are hand-written, and
/// the Italian one has no Markdown source at all — so the only thing keeping them together
/// is that whoever edits one remembers the others. These tests are what notices when that
/// does not happen: they compare structure, which is language-independent, and say nothing
/// about the prose, which cannot be checked mechanically across a translation.
/// </summary>
public class ManualSyncTests
{
    /// <summary>Walks up from the test binary to the repo root, identified by the manual itself.</summary>
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Doc(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "GrtReloadingToolkit", relative));

    private const string EnHtml = "docs/Reloading-Toolkit-Manual.html";
    private const string ItHtml = "docs/Reloading-Toolkit-Manual-IT.html";

    /// <summary>The "## 7. Powder Temp Coefficients" numbers, in document order.</summary>
    private static int[] MarkdownSectionNumbers(string md) =>
        Regex.Matches(md, @"(?m)^##\s+(\d+)\.")
             .Select(m => int.Parse(m.Groups[1].Value))
             .ToArray();

    /// <summary>The "01" in &lt;h2&gt;&lt;span class="no"&gt;01&lt;/span&gt;Installing&lt;/h2&gt;, in document order.</summary>
    private static int[] HtmlSectionNumbers(string html) =>
        Regex.Matches(html, @"<span class=""no"">\s*(\d+)\s*</span>")
             .Select(m => int.Parse(m.Groups[1].Value))
             .ToArray();

    /// <summary>The anchor ids the table of contents links to, in document order.</summary>
    private static string[] HtmlSectionIds(string html) =>
        Regex.Matches(html, @"<section\s+id=""([^""]+)""")
             .Select(m => m.Groups[1].Value)
             .ToArray();

    [Fact]
    public void EnglishHtmlCoversTheSameSectionsAsTheMarkdown()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));
        int[] html = HtmlSectionNumbers(Doc(EnHtml));

        Assert.NotEmpty(md);
        // A section was added to or removed from one file and not the other.
        Assert.Equal(md, html);
    }

    [Fact]
    public void ItalianHtmlCoversTheSameSectionsAsTheMarkdown()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));
        int[] html = HtmlSectionNumbers(Doc(ItHtml));

        Assert.Equal(md, html);
    }

    [Fact]
    public void BothHtmlManualsUseTheSameAnchors()
    {
        // The two pages are meant to be the same document in two languages, so a link into
        // one — #cal, #faq — has to land in the same place in the other.
        Assert.Equal(HtmlSectionIds(Doc(EnHtml)), HtmlSectionIds(Doc(ItHtml)));
    }

    [Fact]
    public void MarkdownSectionsAreNumberedContiguouslyFromOne()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));

        // Guards the two tests above: they compare number sequences, which would still match
        // if the same section were skipped or repeated in every file.
        Assert.Equal(Enumerable.Range(1, md.Length), md);
    }

    [Fact]
    public void TheManualIsNotDuplicatedInsideDocs()
    {
        // MANUAL.md and README.md were each committed twice, byte-identical, and drifted apart
        // the moment one copy was edited alone.
        string docs = Path.Combine(RepoRoot(), "GrtReloadingToolkit", "docs");

        Assert.False(File.Exists(Path.Combine(docs, "MANUAL.md")));
        Assert.False(File.Exists(Path.Combine(docs, "README.md")));
    }
}
