using System.Globalization;
using System.Text.RegularExpressions;
using GrtReloadingToolkit.Brass;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Guards the plugin's one formatting convention: every number it renders is invariant, so a
/// decimal point stays a decimal point on an Italian machine. GrtUnits formats all of its string
/// helpers that way and GrtLoadDoc reads and writes every attribute that way; there is no global
/// culture override anywhere, so a bare $"{x:0.0}" genuinely picks up the OS locale at runtime.
/// The visible symptom was a report reading "823,4" beside a GrtUnits value showing "823.4" for
/// the same quantity, in the same window.
/// </summary>
public class CultureFormattingTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    /// <summary>A format specifier that emits a decimal separator, so it renders per the culture:
    /// ":0.0", ":0.##", ":F2", ":N1", "+0.0;-0.0". Specifiers like ":0" or ":0%" emit neither a
    /// decimal nor a group separator and read the same in every locale, so they are not matched.</summary>
    private static readonly Regex Separator = new(@"\{[^{}""]*:[^{}""]*(?:0\.0|0\.#|#\.#|[FfNn][0-9])[^{}""]*\}");

    /// <summary>A number sent through .ToString(format) with no culture argument. The pattern ends
    /// at the closing quote-paren -- a call that passes a culture has a comma there instead -- so a
    /// match proves the call itself is culture-sensitive, and no surrounding-line check applies.
    /// That asymmetry is the point: an InvariantCulture elsewhere on the line does NOT make such a
    /// call safe. TempCoeffForm's status line hid one inside string.Format(InvariantCulture, ...),
    /// which looks right and is not -- the argument is already a string by the time string.Format
    /// sees it, and string is not IFormattable, so the outer culture never reaches the number.</summary>
    private static readonly Regex UnculturedToString =
        new(@"\.ToString\(""[^""]*(?:0\.0|0\.#|#\.#|[FfNn][0-9])[^""]*""\)");

    /// <summary>Console diagnostics for whoever is running the CLI by hand -- this text never
    /// reaches a GRT note or the UI, so it is the one place the convention does not apply.</summary>
    private static bool IsCliDiagnostic(string path) =>
        path.EndsWith("Cli.cs", StringComparison.Ordinal) || path.EndsWith("DbSelfTest.cs", StringComparison.Ordinal);

    private static List<string> CultureSensitiveFormats(bool cliOnly)
    {
        var found = new List<string>();
        foreach (string path in Directory.EnumerateFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (IsCliDiagnostic(path) != cliOnly) continue;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                // Comments talk ABOUT these formats -- including this file's own explanation of them.
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*')) continue;
                // Checked before the interpolation scan and without any context check, for the
                // reason given on UnculturedToString: this call carries its own proof.
                if (UnculturedToString.IsMatch(lines[i]))
                    found.Add($"{Path.GetRelativePath(RepoRoot(), path)}:{i + 1}: {lines[i].Trim()}");

                if (!lines[i].Contains("$\"") || !Separator.IsMatch(lines[i])) continue;
                // The wrapper usually sits on the line above, because these interpolations are long.
                string ctx = string.Join(' ', lines[Math.Max(0, i - 2)..(i + 1)]);
                if (ctx.Contains("FormattableString.Invariant") || ctx.Contains("InvariantCulture")) continue;
                found.Add($"{Path.GetRelativePath(RepoRoot(), path)}:{i + 1}: {lines[i].Trim()}");
            }
        }
        return found;
    }

    [Fact]
    public void EveryRenderedNumberIsFormattedInvariantly()
    {
        var bare = CultureSensitiveFormats(cliOnly: false);
        Assert.True(bare.Count == 0,
            "number rendered per the OS locale:\n  " + string.Join("\n  ", bare));
    }

    /// <summary>Guard the guard: if the pattern above ever stops matching real code, the test
    /// would pass vacuously. The CLI diagnostics are the known population it must still find.</summary>
    [Fact]
    public void TheScannerStillMatchesCultureSensitiveFormats()
    {
        Assert.NotEmpty(CultureSensitiveFormats(cliOnly: true));
    }

    [Fact]
    public void SeatingForceNotesReadTheSameInEveryLocale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            var none = new SeatingForceCalc.Factor(1.0, 1.0);
            // Neck wider than the bullet: the "dangerous, no neck tension" warning quotes both
            // diameters to 3 decimals, which is where the locale used to show through.
            var est = SeatingForceCalc.Compute(
                bulletDiameterMm: 6.172, neckIdMm: 6.198,
                baselineForceKg: 25, baselineStdKg: 3,
                typicalInterferenceMm: 0.0508, typicalSeatingDepthMm: 3.0, actualSeatingDepthMm: 3.0,
                none, none, none, none, boatTail: false);

            string warning = Assert.Single(est.Notes, n => n.Contains("WARNING"));
            Assert.Contains("6.172", warning);
            Assert.Contains("6.198", warning);
            Assert.DoesNotContain("6,172", warning);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
