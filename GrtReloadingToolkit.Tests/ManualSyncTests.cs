using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The manual exists five times: MANUAL.md, an English HTML rendering, and Italian, German and
/// French translations. Nothing generates one from another — the HTML pages are hand-written, and
/// the translations have no Markdown source at all — so the only thing keeping them together is
/// that whoever edits one remembers the others. These tests are what notices when that does not
/// happen: they compare structure, which is language-independent, and say nothing about the prose,
/// which cannot be checked mechanically across a translation.
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

    /// <summary>The repo-root README.md -- the public GitHub landing page, distinct from
    /// <c>GrtReloadingToolkit/README.md</c> (a contributor build guide with no manual links of its
    /// own). Unlike <c>Directory.Build.props</c>'s two-tree-shape split (same content, two possible
    /// locations), this file has NO equivalent in the local working copy: that tree's RepoRoot() is
    /// a shared parent with unrelated sibling projects, which owns no README of its own, public or
    /// otherwise. Null there, not a fallback path -- there is nothing meaningful to fall back to.</summary>
    private static string? RootReadmePath()
    {
        string atRoot = Path.Combine(RepoRoot(), "README.md");
        return File.Exists(atRoot) ? atRoot : null;
    }

    private const string EnHtml = "docs/Reloading-Toolkit-Manual.html";
    private const string ItHtml = "docs/Reloading-Toolkit-Manual-IT.html";
    private const string DeHtml = "docs/Reloading-Toolkit-Manual-DE.html";
    private const string FrHtml = "docs/Reloading-Toolkit-Manual-FR.html";

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
    public void GermanHtmlCoversTheSameSectionsAsTheMarkdown()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));
        int[] html = HtmlSectionNumbers(Doc(DeHtml));

        Assert.Equal(md, html);
    }

    [Fact]
    public void FrenchHtmlCoversTheSameSectionsAsTheMarkdown()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));
        int[] html = HtmlSectionNumbers(Doc(FrHtml));

        Assert.Equal(md, html);
    }

    [Fact]
    public void AllHtmlManualsUseTheSameAnchors()
    {
        // The four pages are meant to be the same document in four languages, so a link into
        // one — #cal, #faq — has to land in the same place in the others.
        string[] en = HtmlSectionIds(Doc(EnHtml));
        Assert.Equal(en, HtmlSectionIds(Doc(ItHtml)));
        Assert.Equal(en, HtmlSectionIds(Doc(DeHtml)));
        Assert.Equal(en, HtmlSectionIds(Doc(FrHtml)));
    }

    [Fact]
    public void MarkdownSectionsAreNumberedContiguouslyFromOne()
    {
        int[] md = MarkdownSectionNumbers(Doc("MANUAL.md"));

        // Guards the four tests above: they compare number sequences, which would still match
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

    [Fact]
    public void DocsHoldsNoPdfs()
    {
        // The PDF renderings are release assets, not tree files -- build-plugin.ps1 copies the
        // whole docs\ folder into the shipped plugin (Assemble, "Copy-Item $docs $outDir
        // -Recurse"), so anything left here is downloaded by every user, in both zips, forever.
        // A 1 MB manual PDF was committed here once and put 41% onto ReloadingToolkit-lite.zip
        // (1,465,419 -> 2,073,659 bytes between v0.2.8 and v0.2.9) before anyone noticed, because
        // the zip is still perfectly valid when it happens -- only bigger.
        string docs = Path.Combine(RepoRoot(), "GrtReloadingToolkit", "docs");
        string[] pdfs = Directory.GetFiles(docs, "*.pdf", SearchOption.AllDirectories);

        Assert.True(pdfs.Length == 0,
            "docs/ ships inside the plugin zip; attach PDFs to the release instead of committing them: "
            + string.Join(", ", pdfs.Select(Path.GetFileName)));
    }

    [Fact]
    public void EveryManualIsLinkedFromTheReadmeAndTheLandingPage()
    {
        // A translation nobody can reach is a translation nobody reads: the German manual shipped
        // in v0.2.9 listed in neither place, so the only way to open it was to know the filename.
        // The two entry points are the repo's root README (relative path) and the landing page that
        // ships beside the manuals inside the plugin folder (bare filename -- GitHub Pages is not
        // enabled, so an absolute URL would not resolve). The README half only runs in the GitHub
        // tree, which is the only one that has a root README to check -- see RootReadmePath().
        string docs = Path.Combine(RepoRoot(), "GrtReloadingToolkit", "docs");
        string? readme = RootReadmePath() is { } path ? File.ReadAllText(path) : null;
        string landing = Doc("docs/GRT-Reloading-Toolkit.html");

        foreach (string manual in Directory.GetFiles(docs, "Reloading-Toolkit-Manual*.html")
                                           .Select(f => Path.GetFileName(f)!)
                                           .OrderBy(n => n))
        {
            if (readme is not null)
                Assert.True(readme.Contains("docs/" + manual),
                    manual + " is not linked from README.md");
            Assert.True(landing.Contains("\"" + manual + "\""),
                manual + " is not linked from the landing page");
        }
    }

    [Fact]
    public void TheLandingPageListsEveryReportTemplate()
    {
        // The landing page names the report templates in chips. It said five from v0.1.1 until
        // v0.2.9 while ReportTemplates.Links grew to eleven, because adding a template touches
        // neither file. Counting is language-independent; the names themselves are not checked.
        string templates = Doc("Reports/ReportTemplates.cs");
        Match links = Regex.Match(templates, @"Links\s*=\s*\{(.*?)\};", RegexOptions.Singleline);
        Assert.True(links.Success, "ReportTemplates.cs has no Links array");
        int installed = Regex.Matches(links.Groups[1].Value, @"\(""toolkit-[a-z]+"",").Count;

        string landing = Doc("docs/GRT-Reloading-Toolkit.html");
        Match chips = Regex.Match(landing, @"<div class=""reports"">(.*?)</div>", RegexOptions.Singleline);
        Assert.True(chips.Success, "the landing page has no <div class=\"reports\"> block");
        int listed = Regex.Matches(chips.Groups[1].Value, "<span>").Count;

        Assert.True(listed == installed,
            $"the landing page lists {listed} report templates, ReportTemplates.Links installs {installed}");

        // The hero's own "N report templates" fact tile is a second, independent place the same
        // count is spelled out in prose -- it said "5" from v0.1.1 until this test was written,
        // even after the chips block above was fixed and guarded, because it is a different string
        // in a different part of the same file.
        Assert.Contains($">{installed} report templates<", landing);
    }
}
