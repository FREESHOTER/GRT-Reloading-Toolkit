using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The launcher is a list that only grows, and it outgrew the screen without anyone noticing: with
/// a fixed border and the Form auto-sizing, Windows capped the window at the desktop working area
/// and the surplus buttons were laid out past the bottom edge and never painted — no grip to drag,
/// no wheel to scroll. Measured on a 1024x768 desktop, the pre-fix launcher opened 788px tall with
/// 8 of its 24 buttons unreachable; it was reported from a 3840x2160 screen showing 17 of 24. The
/// bug is invisible on a tall monitor, which is exactly why it shipped, so these assertions stand
/// in for a render the test suite cannot do (<see cref="LandingPageSyncTests"/> reads the launcher
/// source for the same reason — there is no WinForms to instantiate on the build machine).
/// </summary>
public class LauncherLayoutTests
{
    private static string Source()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, "GrtReloadingToolkit", "Ui", "LauncherForm.cs"));
    }

    [Fact]
    public void TheLauncherCanBeResized()
    {
        // A Fixed* border gives no resize grip, so a clipped list cannot be opened up by hand.
        string src = Source();
        Assert.Contains("FormBorderStyle = FormBorderStyle.SizableToolWindow;", src);
        Assert.DoesNotMatch(new Regex(@"FormBorderStyle\s*=\s*FormBorderStyle\.Fixed"), src);
    }

    [Fact]
    public void TheToolListSitsInAScrollHost()
    {
        // Without this, the buttons past the bottom edge are simply gone.
        Assert.Matches(new Regex(@"new Panel\s*{[^}]*AutoScroll\s*=\s*true"), Source());
    }

    [Fact]
    public void TheFormItselfDoesNotAutoSize()
    {
        // Form AutoSize is what made the window chase its content and get capped by the desktop,
        // and it would silently undo the clamp below on the next layout pass. The flow panel still
        // auto-sizes -- that is what reports the height the whole list wants -- so this looks only
        // at assignments to the Form's own AutoSize, i.e. ones with no object-initializer indent.
        Assert.DoesNotMatch(new Regex(@"^        AutoSize(Mode)? =", RegexOptions.Multiline), Source());
    }

    [Fact]
    public void TheButtonColumnIsMeasuredRatherThanHardcoded()
    {
        // A fixed button width clips whichever caption is longest, and which one that is depends on
        // the UI language and the display scaling: measured at Segoe UI 9pt the longest English
        // caption needs 235px, its Italian translation 277, against the 252 this used to carry.
        // TextRenderer is the measurement; Button.GetPreferredSize is not, because it floors at the
        // explicit Width already set and would just hand back the number under replacement.
        string src = Source();
        Assert.Contains("TextRenderer.MeasureText", src);
        Assert.DoesNotMatch(new Regex(@"new Button\s*{[^}]*\bWidth\s*=\s*\d"), src);
        Assert.DoesNotMatch(new Regex(@"new Panel\s*{[^}]*\bWidth\s*=\s*\d"), src);
    }

    [Fact]
    public void TheWindowOpensShorterThanTheWholeList()
    {
        // The whole list wants about 1065px of client height. Opening at that height is what the
        // user sees as a ribbon down a tall screen, and it is only bearable at all because of the
        // clamp above -- which on a 4K monitor never bites. So the opening height is capped well
        // under the list, and the scroll host carries the rest.
        string src = Source();
        var cap = Regex.Match(src, @"MaxOpeningClientHeight\s*=\s*(\d+)");
        Assert.True(cap.Success, "LauncherForm no longer caps the height it opens at");
        int px = int.Parse(cap.Groups[1].Value);
        Assert.InRange(px, 240, 900);
        Assert.Matches(new Regex(@"int height = Math\.Min\(.*MaxOpeningClientHeight"), src);
    }

    [Fact]
    public void TheWindowIsSizedInOnLoadWhereTheChromeIsReal()
    {
        // In the constructor there is no window handle yet, so Height - ClientSize.Height is an
        // AdjustWindowRectEx estimate: measured 40 against a true 60, which put the window 20px
        // taller than the desktop and clipped the list all over again. OnLoad runs after the handle
        // exists. Keep the ClientSize assignment on that side of the line.
        string src = Source();
        int onLoad = src.IndexOf("protected override void OnLoad", StringComparison.Ordinal);
        Assert.True(onLoad > 0, "LauncherForm no longer overrides OnLoad");

        var sized = Regex.Matches(src, @"^        ClientSize = ", RegexOptions.Multiline);
        Assert.NotEmpty(sized);
        Assert.All(sized, m => Assert.True(m.Index > onLoad,
            "ClientSize is assigned in the constructor, where the window chrome is still an estimate"));
    }
}
