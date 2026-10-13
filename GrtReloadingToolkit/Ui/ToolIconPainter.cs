using SkiaSharp;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The launcher's tool icons, drawn here rather than taken from the system emoji font: Windows draws each emoji in
/// its own colours (or as an empty box, where the font has no glyph), none of which look like GRT. These are one set of
/// line drawings in GRT's own icon palette -- <c>color_icon_normal</c> blue fading to dark, and the orange
/// <c>color_icon_hover</c> pair on hover -- on a 24 x 24 grid. Pure SkiaSharp (no System.Drawing), so the same
/// drawings are exercised by the tests on any machine; the window turns the PNG into a bitmap.
/// </summary>
internal static class ToolIconPainter
{
    public const string NormalTop = "#0058b0", NormalBottom = "#0a2d52";
    public const string HoverTop = "#ff8000", HoverBottom = "#800000";

    private enum CircleStyle { Ring, White, Solid }

    /// <summary>One stroke of a drawing: an SVG path, or a circle that is a ring, a ring filled white, or a solid dot.</summary>
    private sealed record Prim(string? Path, float Cx, float Cy, float R, CircleStyle Style);

    private static Prim P(string d) => new(d, 0, 0, 0, CircleStyle.Ring);
    private static Prim C(float cx, float cy, float r, CircleStyle s) => new(null, cx, cy, r, s);

    private static readonly Dictionary<string, Prim[]> Icons = new()
    {
        ["wizard"] = new Prim[] { P("M4 20 16 8"), P("m14 6 4 4"), P("M19 2v4M17 4h4"), P("M6 6v3M4.5 7.5h3") },
        ["brass"] = new Prim[] { P("M15 4a5 5 0 0 0-4.6 6.9L4 17.3 6.7 20l6.4-6.4A5 5 0 0 0 20 9l-3 3-2.4-.6-.6-2.4 3-3A5 5 0 0 0 15 4z") },
        ["force"] = new Prim[] { P("M12 4v16M7 20h10M5 7h14"), P("M5 7 2 14a3 3 0 0 0 6 0zM19 7l-3 7a3 3 0 0 0 6 0z") },
        ["label"] = new Prim[] { P("M3 12V4h8l9 9-8 8z"), C(7.5f, 8.5f, 1.3f, CircleStyle.Ring) },
        ["target"] = new Prim[] { C(12.0f, 12.0f, 9.0f, CircleStyle.Ring), C(12.0f, 12.0f, 5.0f, CircleStyle.Ring), C(12.0f, 12.0f, 1.3f, CircleStyle.Ring) },
        ["journal"] = new Prim[] { P("M6 3h12v18H6z"), P("M6 3v18M10 8h6M10 12h6") },
        ["findba"] = new Prim[] { C(10.0f, 10.0f, 6.0f, CircleStyle.Ring), P("m15 15 6 6") },
        ["stopwatch"] = new Prim[] { C(12.0f, 14.0f, 7.0f, CircleStyle.Ring), P("M12 14l3-3M10 3h4M12 3v4") },
        ["shotmarker"] = new Prim[] { C(11.0f, 13.0f, 8.0f, CircleStyle.Ring), C(11.0f, 13.0f, 3.5f, CircleStyle.Ring), P("M21 3l-8 8M21 3h-5M21 3v5") },
        ["stats"] = new Prim[] { P("M3 19h18"), P("M3 18c3 0 4-12 9-12s6 12 9 12") },
        ["ladder"] = new Prim[] { P("M3 20V4M3 20h18"), P("m6 16 5-5 3 3 6-7"), P("M16 7h4v4") },
        ["ruler"] = new Prim[] { P("M3 8h18v8H3z"), P("M7 8v3M11 8v4M15 8v3M19 8v4") },
        ["sliders"] = new Prim[] { P("M6 4v16M12 4v16M18 4v16"), C(6.0f, 9.0f, 2.0f, CircleStyle.White), C(12.0f, 15.0f, 2.0f, CircleStyle.White), C(18.0f, 8.0f, 2.0f, CircleStyle.White) },
        ["thermo"] = new Prim[] { P("M10 14V5a2 2 0 0 1 4 0v9a4 4 0 1 1-4 0z"), P("M12 9v8") },
        ["trophy"] = new Prim[] { P("M7 4h10v5a5 5 0 0 1-10 0z"), P("M7 6H4a3 3 0 0 0 3 4M17 6h3a3 3 0 0 1-3 4M12 14v4M8 20h8") },
        ["compass"] = new Prim[] { C(12.0f, 12.0f, 9.0f, CircleStyle.Ring), P("m15.5 8.5-2 5-5 2 2-5z") },
        ["flask"] = new Prim[] { P("M9 3h6M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3"), P("M7.5 15h9") },
        ["curve"] = new Prim[] { P("M3 4v16h18"), P("M5 7c4 8 8 10 15 10"), C(7.4f, 11.0f, 1.2f, CircleStyle.White), C(14.5f, 16.0f, 1.2f, CircleStyle.White) },
        ["pulse"] = new Prim[] { P("M3 12h4l2-6 4 12 2-6h6") },
        ["sdcause"] = new Prim[] { C(10.0f, 10.0f, 6.0f, CircleStyle.Ring), P("m15 15 6 6"), P("M7 12c1 0 1-4 3-4s2 4 3 4") },
        ["group"] = new Prim[] { C(12.0f, 12.0f, 9.0f, CircleStyle.Ring), C(12.0f, 12.0f, 5.0f, CircleStyle.Ring), C(9.6f, 10.0f, 0.9f, CircleStyle.Solid), C(14.2f, 11.2f, 0.9f, CircleStyle.Solid), C(12.0f, 14.6f, 0.9f, CircleStyle.Solid) },
        ["box"] = new Prim[] { P("M3 8l9-5 9 5v8l-9 5-9-5z"), P("M3 8l9 5 9-5M12 13v8") },
        ["books"] = new Prim[] { P("M4 5h4v14H4zM10 5h4v14h-4z"), P("m16 6 4-1 3 13-4 1z") },
        ["doc"] = new Prim[] { P("M6 3h8l5 5v13H6z"), P("M14 3v5h5M9 13h7M9 17h7") },
    };

    /// <summary>Every icon name, in drawing order.</summary>
    public static IReadOnlyCollection<string> Names => Icons.Keys;

    public static bool Has(string name) => Icons.ContainsKey(name);

    /// <summary>The icon as a square PNG of <paramref name="px"/> pixels with a transparent background.</summary>
    public static byte[] RenderPng(string name, bool hover, int px)
    {
        if (!Icons.TryGetValue(name, out var prims)) throw new ArgumentException("No icon named '" + name + "'.", nameof(name));
        if (px < 8) throw new ArgumentOutOfRangeException(nameof(px));

        var top = SKColor.Parse(hover ? HoverTop : NormalTop);
        var bottom = SKColor.Parse(hover ? HoverBottom : NormalBottom);

        using var surface = SKSurface.Create(new SKImageInfo(px, px, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(px / 24f);

        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 24), new[] { top, bottom }, null, SKShaderTileMode.Clamp);
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.9f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Shader = shader };
        using var white = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        using var solid = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = top };

        foreach (var p in prims)
        {
            if (p.Path is { } d)
            {
                using var path = SKPath.ParseSvgPathData(d);
                canvas.DrawPath(path, stroke);
            }
            else
            {
                if (p.Style == CircleStyle.White) canvas.DrawCircle(p.Cx, p.Cy, p.R, white);
                if (p.Style == CircleStyle.Solid) canvas.DrawCircle(p.Cx, p.Cy, p.R, solid);
                if (p.Style != CircleStyle.Solid) canvas.DrawCircle(p.Cx, p.Cy, p.R, stroke);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
