using System.Drawing.Drawing2D;
using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The band on top of a tool window when the program runs as the Banco di Ricarica: the tool's icon and name in the colour
/// of its phase, where it sits on the path ("FASE 2 · SCALA DI CARICHE · PASSO 5"), and the small emblem of the program,
/// so every window reads as part of the home page it was opened from.
/// </summary>
internal sealed class ToolBanner : HomeControl
{
    private readonly string _title, _kicker;
    private readonly Color _accent;
    private readonly Bitmap _icon, _emblem;

    private ToolBanner(string title, string kicker, Color accent, string icon)
    {
        _title = title; _kicker = kicker; _accent = accent;
        float k = DeviceDpi / 96f;
        int px = (int)(28 * k);
        var top = HomeColors.Mix(accent, Color.White, 0.25f);
        var bottom = HomeColors.Mix(accent, HomeColors.HeaderBottom, 0.2f);
        _icon = Decode(ToolIconPainter.RenderPng(icon, px, HomeColors.Hex(top), HomeColors.Hex(bottom)));
        _emblem = Decode(HomeArt.EmblemPng((int)(34 * k)));
        Dock = DockStyle.Top;
        Height = (int)(62 * k);
        BackColor = HomeColors.HeaderBottom;
        TabStop = false;
    }

    private static Bitmap Decode(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <summary>Kicker and colour of a tool: its phase and step on the path, or the block it is listed in.</summary>
    public static (string Title, string Kicker, Color Accent, string Icon)? Describe(Tool t)
    {
        if (ToolCatalog.Find(t) is not var (entry, group)) return null;
        var it = CultureInfo.GetCultureInfo("it-IT");
        string title = LauncherCaption.From(Lang.T(entry.Key));
        string kicker;
        Color accent;
        if (group >= 0)
        {
            accent = HomeColors.Phase[group];
            kicker = string.Format(CultureInfo.InvariantCulture, Lang.T("PHASE {0}"), group + 1) + "  ·  "
                     + Lang.T(ToolCatalog.Phases[group].Title).ToUpper(it)
                     + (entry.Step is { } st ? "  ·  " + string.Format(CultureInfo.InvariantCulture, Lang.T("STEP {0}"), st) : "");
        }
        else
        {
            accent = group == -1 ? HomeColors.Neutral : HomeColors.Accent;
            kicker = group switch { -1 => Lang.T("MORE ANALYSES"), -2 => Lang.T("ARCHIVE AND DOCUMENTS"), _ => Lang.T("GUIDED PATH") };
        }
        return (title, kicker, accent, entry.Icon);
    }

    /// <summary>
    /// Puts the band on top of a tool window, before it is themed and shown. What the window holds moves, as it is, into a
    /// panel under the band, and the window grows by the band's height: controls placed by coordinates or anchored to the
    /// edges keep the room they were laid out in.
    /// </summary>
    public static void Attach(Form f, Tool t)
    {
        if (Describe(t) is not var (title, kicker, accent, icon)) return;
        var banner = new ToolBanner(title, kicker, accent, icon);
        f.SuspendLayout();
        var host = new Panel { Size = f.ClientSize, AutoScroll = f.AutoScroll, AutoScrollMinSize = f.AutoScrollMinSize };
        foreach (var c in f.Controls.Cast<Control>().ToArray()) host.Controls.Add(c);
        f.AutoScroll = false;
        host.Dock = DockStyle.Fill;
        f.Controls.Add(host);
        f.Controls.Add(banner);   // added last, docked first: the band is the top edge, the panel fills what is left
        f.ClientSize = new Size(f.ClientSize.Width, f.ClientSize.Height + banner.Height);
        if (f.MinimumSize.Height > 0) f.MinimumSize = new Size(f.MinimumSize.Width, f.MinimumSize.Height + banner.Height);
        f.ResumeLayout(true);
        f.Disposed += (_, _) => { banner._icon.Dispose(); banner._emblem.Dispose(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = K;
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                   HomeColors.Mix(HomeColors.HeaderTop, _accent, 0.10f), HomeColors.HeaderBottom, 0f))
            g.FillRectangle(fill, 0, 0, Width, Height);
        using (var line = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), 3), _accent, Color.FromArgb(0, _accent), 0f))
            g.FillRectangle(line, 0, Height - 2 * k, Width, 2 * k);
        using (var bar = new SolidBrush(_accent)) g.FillRectangle(bar, 0, 0, 4 * k, Height - 2 * k);

        float x = 18 * k;
        g.DrawImage(_icon, x, (Height - _icon.Height) / 2f, _icon.Width, _icon.Height);
        x += _icon.Width + 14 * k;
        using var kf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var tf = new Font("Segoe UI Semibold", 13.5f);
        int kh = TextRenderer.MeasureText(g, "Ag", kf).Height, th = TextRenderer.MeasureText(g, "Ag", tf).Height;
        int top = (Height - kh - th) / 2;
        int right = Width - _emblem.Width - (int)(150 * k);
        TextRenderer.DrawText(g, _kicker, kf, new Rectangle((int)x, top, Math.Max(10, right - (int)x), kh), _accent,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _title, tf, new Rectangle((int)x, top + kh, Math.Max(10, right - (int)x), th), HomeColors.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        // the program's mark on the right
        float ex = Width - _emblem.Width - 16 * k;
        g.DrawImage(_emblem, ex, (Height - _emblem.Height) / 2f, _emblem.Width, _emblem.Height);
        using var nf = new Font("Segoe UI", 8.5f);
        var size = TextRenderer.MeasureText(g, "Banco di Ricarica", nf);
        if (ex - size.Width - 10 * k > x + 200 * k)
            TextRenderer.DrawText(g, "Banco di Ricarica", nf, new Point((int)(ex - size.Width - 8 * k), (Height - size.Height) / 2), HomeColors.Dim,
                TextFormatFlags.NoPrefix);
    }
}
