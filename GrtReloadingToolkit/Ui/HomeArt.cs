using SkiaSharp;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The emblem of Reload Lab: a cartridge standing on a target, in brass and copper on a dark disc with an amber
/// ring. Drawn here (SkiaSharp, on a 64 x 64 grid) so it is sharp at any size and needs no image file.
/// </summary>
internal static class HomeArt
{
    public static byte[] EmblemPng(int px)
    {
        if (px < 16) throw new ArgumentOutOfRangeException(nameof(px));
        using var surface = SKSurface.Create(new SKImageInfo(px, px, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.Scale(px / 64f);

        // disc and ring
        using (var disc = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 64),
                   new[] { SKColor.Parse("#2c3d57"), SKColor.Parse("#111925") }, null, SKShaderTileMode.Clamp) })
            c.DrawCircle(32, 32, 30, disc);
        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f,
                   Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(64, 64),
                       new[] { SKColor.Parse("#ffd27a"), SKColor.Parse("#f08c1a") }, null, SKShaderTileMode.Clamp) })
            c.DrawCircle(32, 32, 29, ring);

        // the target behind the cartridge
        using (var rings = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f, Color = new SKColor(148, 163, 184, 90) })
        {
            c.DrawCircle(32, 32, 21, rings);
            c.DrawCircle(32, 32, 13, rings);
            c.DrawLine(6, 32, 58, 32, rings);
            c.DrawLine(32, 6, 32, 58, rings);
        }

        // the case: body, shoulder, neck; then the rim below the extractor groove
        using var brass = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(26, 0), new SKPoint(38, 0),
            new[] { SKColor.Parse("#9a6a1e"), SKColor.Parse("#f7d27e"), SKColor.Parse("#c38a2c"), SKColor.Parse("#8a5c18") },
            new[] { 0f, 0.35f, 0.7f, 1f }, SKShaderTileMode.Clamp) };
        using (var body = SKPath.ParseSvgPathData("M26 53 L26 32 L28.8 27 L28.8 21 L35.2 21 L35.2 27 L38 32 L38 53 Z"))
            c.DrawPath(body, brass);
        using (var rim = SKPath.ParseSvgPathData("M25 55 H39 V58.5 H25 Z"))
            c.DrawPath(rim, brass);
        using (var groove = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#5c3d10") })
            c.DrawRect(27, 53, 10, 2, groove);

        // the bullet
        using var copper = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(28.8f, 0), new SKPoint(35.2f, 0),
            new[] { SKColor.Parse("#8c3f22"), SKColor.Parse("#f0a27a"), SKColor.Parse("#b5552e") }, new[] { 0f, 0.4f, 1f }, SKShaderTileMode.Clamp) };
        using (var bullet = SKPath.ParseSvgPathData("M28.8 21.5 C28.8 15 30.4 10.5 32 6.5 C33.6 10.5 35.2 15 35.2 21.5 Z"))
            c.DrawPath(bullet, copper);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The emblem as a Windows icon file (16 to 256 px): the small sizes as 32-bit bitmaps, which every reader of .ico
    /// understands, the 256 one as PNG, as Windows stores it.
    /// </summary>
    public static byte[] EmblemIco()
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var images = sizes.Select(s => s == 256 ? EmblemPng(s) : Dib(s)).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
        return ms.ToArray();
    }

    /// <summary>An icon image as a bottom-up 32-bit DIB with its (empty) AND mask.</summary>
    private static byte[] Dib(int px)
    {
        using var bmp = SKBitmap.Decode(EmblemPng(px));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int maskRow = ((px + 31) / 32) * 4;
        w.Write(40); w.Write(px); w.Write(px * 2); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(0); w.Write(px * px * 4 + maskRow * px); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = px - 1; y >= 0; y--)
            for (int x = 0; x < px; x++)
            {
                var c = bmp.GetPixel(x, y);   // unpremultiplied
                w.Write(c.Blue); w.Write(c.Green); w.Write(c.Red); w.Write(c.Alpha);
            }
        w.Write(new byte[maskRow * px]);
        return ms.ToArray();
    }
}
