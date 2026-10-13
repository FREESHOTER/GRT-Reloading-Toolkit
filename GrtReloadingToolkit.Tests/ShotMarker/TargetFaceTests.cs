using System;
using System.Globalization;
using System.Linq;
using ShotMarker.Core.Faces;
using ShotMarker.Core.Render;
using ShotMarker.Core.Sm;
using SkiaSharp;
using Xunit;

namespace ShotMarker.Core.Tests;

public class TargetFaceTests
{
    /// <summary>Every face the tables publish loads, and the id dictionary is built once
    /// without a duplicate (<c>Load()</c> throws on one). Bump this with the tables.</summary>
    [Fact]
    public void LoadsEveryFaceFromTheTables()
    {
        Assert.Equal(128, TargetFaceLibrary.Count);
    }

    /// <summary>The half-MOA grid is derived from its rule, and — because no Austrian body
    /// publishes it — it is the one face whose display name deliberately differs from the one
    /// the device writes. The device's string has to keep resolving it, or the face silently
    /// becomes <c>Generic</c> for whoever shoots it.</summary>
    [Fact]
    public void TheHalfMoaGridIsHalfAMinuteAt600mAndStillAnswersToTheDeviceName()
    {
        var f = TargetFaceLibrary.Find("AT_600");
        Assert.NotNull(f);

        // 600000 mm x tan(0.5/60 deg) = 87.266, which is 87.3 at 0.1 mm.
        const double halfMoa = 87.3;
        Assert.Equal(5, f!.Rings.Count);
        for (int step = 1; step <= 5; step++)
        {
            Assert.Equal(halfMoa * step, f.Rings[step - 1].DiamMm, 3);
            Assert.Equal((11 - step).ToString(), f.Rings[step - 1].Score);
        }

        Assert.Same(f, TargetFaceLibrary.FindByName("Austria 600"));
    }

    [Fact]
    public void NraLongRangeFClassMatchesThePublishedSpecification()
    {
        var f = TargetFaceLibrary.Find("NRA_LRFC");
        Assert.NotNull(f);

        const double mmPerInch = 25.4;
        Assert.Equal(72 * mmPerInch, f!.BoardWidthMm, 1);
        Assert.Equal(72 * mmPerInch, f.BoardHeightMm, 1);

        // X=5", 10=10", 9=20", 8=30", 7=44", 6=60" — the user confirmed the 5" X ring.
        var byScore = f.Rings
            .Where(r => r.Score is not null)
            .GroupBy(r => r.Score!)
            .ToDictionary(g => g.Key, g => g.Last().DiamMm / mmPerInch);
        Assert.Equal(5, byScore["X"], 1);
        Assert.Equal(10, byScore["10"], 1);
        Assert.Equal(20, byScore["9"], 1);
        Assert.Equal(30, byScore["8"], 1);
        Assert.Equal(44, byScore["7"], 1);
        Assert.Equal(60, byScore["6"], 1);
    }

    [Fact]
    public void FacesWithOffsetRingsAndPolygonsSurvive()
    {
        // IBS 100yd BR is the one face in the tables whose rings are not all concentric: IBS
        // Rule V.A.1 puts "an additional 10 and 9 ring ... in the third and forth quadrants",
        // so five record rings and two offset pairs make nine. It also carries the published
        // border rectangle and the two squares that draw the aiming square, and the renderer
        // reads XMm/YMm and Polys nowhere else, so this face stands guard over both.
        var ibs = TargetFaceLibrary.Find("IBS100BR");
        Assert.NotNull(ibs);
        Assert.Equal(9, ibs!.Rings.Count);
        Assert.Equal(4, ibs.Rings.Count(r => r.XMm != 0 || r.YMm != 0));
        Assert.Equal(3, ibs.Polys.Count);
    }

    [Fact]
    public void UnknownFaceIdReturnsNull()
    {
        Assert.Null(TargetFaceLibrary.Find("NO_SUCH_FACE"));
    }

    /// <summary>The generic face has no scoring rings — it stands in for a target we have no
    /// geometry for — but it must still carry a centre cross and a labelled scale bar. Without
    /// them an unknown face_id renders as shots floating on blank white, with nothing to read
    /// group size or point of aim against, which is the one thing the fallback exists to
    /// preserve.</summary>
    [Fact]
    public void GenericFallbackHasNoRingsButDoesCarryACentreCrossAndALabelledScaleBar()
    {
        var g = TargetFaceLibrary.Generic(1887, 1908);

        Assert.Empty(g.Rings);
        Assert.Equal(1887, g.BoardWidthMm, 1);

        // A cross through dead centre: one poly spanning x either side of 0 at y == 0, and
        // one spanning y either side of 0 at x == 0.
        Assert.Contains(g.Polys, p => p.Points.All(q => q.YMm == 0)
                                      && p.Points.Any(q => q.XMm < 0) && p.Points.Any(q => q.XMm > 0));
        Assert.Contains(g.Polys, p => p.Points.All(q => q.XMm == 0)
                                      && p.Points.Any(q => q.YMm < 0) && p.Points.Any(q => q.YMm > 0));

        // The bar is labelled with its own length, so the number on screen is checkable
        // against the drawing rather than merely decorative.
        TargetText label = Assert.Single(g.Texts);
        Assert.EndsWith(" mm", label.Text);
        double stated = double.Parse(label.Text[..^3], CultureInfo.InvariantCulture);
        Assert.Contains(g.Polys, p => p.Points.Count == 2
                                      && Math.Abs(Math.Abs(p.Points[0].XMm - p.Points[1].XMm) - stated) < 1e-9
                                      && p.Points[0].YMm == p.Points[1].YMm);
        Assert.True(stated <= g.BoardWidthMm * 0.25, "the scale bar must fit well inside the board");
    }

    /// <summary>The model above says the furniture exists; this says it is visible. Both are
    /// needed. The first version of that model-only test passed against a face whose polys
    /// carried colour "b" — which the renderer draws as a zero-area FILL, and whose label it
    /// strokes white on a white board. Every assertion above held and the rendered picture was
    /// blank. Only a pixel can tell those two apart.</summary>
    [Fact]
    public void TheGenericFacesFurnitureActuallyRendersAsVisibleInk()
    {
        TargetFace g = TargetFaceLibrary.Generic(1887, 1908);
        var s = new SmString("s1", "empty", DateTimeOffset.UnixEpoch, "GENERIC", 300, "yd",
                             1887, 1908, 7.82, null, Array.Empty<SmShot>(), null);

        var r = TargetRenderer.Render(s, g, new RenderOptions(DrawFurniture: false));
        using SKBitmap bmp = SKBitmap.Decode(r.Png);

        double arm = Math.Min(1887, 1908) * 0.04;
        Assert.True(DarkPixels(bmp, r.Projection, -arm, 0, arm, 0) > 0, "horizontal cross arm drew nothing");
        Assert.True(DarkPixels(bmp, r.Projection, 0, -arm, 0, arm) > 0, "vertical cross arm drew nothing");

        // The bar, its end ticks and the label, taken as one box across the bottom-left
        // corner where Generic() puts them.
        TargetPoly bar = g.Polys.Last(p => p.Points.Count == 2 && p.Points[0].YMm == p.Points[1].YMm);
        TargetText label = Assert.Single(g.Texts);
        Assert.True(
            DarkPixels(bmp, r.Projection,
                       bar.Points[0].XMm, bar.Points[0].YMm,
                       bar.Points[1].XMm, label.YMm + label.SizeMm) > 0,
            "scale bar and its label drew nothing");
    }

    /// <summary>Counts pixels darker than mid-grey inside an mm-space box. The board is white
    /// and its own edge lies on the canvas border, so anything dark inside is furniture.</summary>
    private static int DarkPixels(SKBitmap bmp, TargetProjection p,
                                  double x1Mm, double y1Mm, double x2Mm, double y2Mm)
    {
        var (fx1, fy1) = p.ToFraction(Math.Min(x1Mm, x2Mm), Math.Max(y1Mm, y2Mm));
        var (fx2, fy2) = p.ToFraction(Math.Max(x1Mm, x2Mm), Math.Min(y1Mm, y2Mm));

        // Two pixels of slack: a 1 px stroke centred on the mathematical line can land just
        // outside a box drawn exactly on that line.
        int left = Math.Max(0, (int)(fx1 * bmp.Width) - 2);
        int top = Math.Max(0, (int)(fy1 * bmp.Height) - 2);
        int right = Math.Min(bmp.Width - 1, (int)(fx2 * bmp.Width) + 2);
        int bottom = Math.Min(bmp.Height - 1, (int)(fy2 * bmp.Height) + 2);

        int dark = 0;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                if ((c.Red + c.Green + c.Blue) / 3 < 128) dark++;
            }
        return dark;
    }
}
