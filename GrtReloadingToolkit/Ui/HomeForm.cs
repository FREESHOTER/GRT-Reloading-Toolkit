using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>The palette of the home page: deep slate, cards a step lighter, one colour per phase of the path.</summary>
internal static class HomeColors
{
    public static readonly Color Back = Color.FromArgb(0x16, 0x1d, 0x29);
    public static readonly Color HeaderTop = Color.FromArgb(0x22, 0x31, 0x47);
    public static readonly Color HeaderBottom = Color.FromArgb(0x14, 0x1b, 0x27);
    public static readonly Color Card = Color.FromArgb(0x21, 0x2b, 0x3a);
    public static readonly Color CardHot = Color.FromArgb(0x2d, 0x3a, 0x4e);
    public static readonly Color Border = Color.FromArgb(0x33, 0x40, 0x54);
    public static readonly Color Text = Color.FromArgb(0xf1, 0xf5, 0xf9);
    public static readonly Color Dim = Color.FromArgb(0x94, 0xa3, 0xb8);
    public static readonly Color Accent = Color.FromArgb(0xff, 0xb3, 0x47);
    public static readonly Color Neutral = Color.FromArgb(0x93, 0xc5, 0xfd);

    /// <summary>Preparation, charge ladder, seating depth, check at distance, final load, training, race.</summary>
    public static readonly Color[] Phase =
    {
        Color.FromArgb(0x38, 0xbd, 0xf8), Color.FromArgb(0xfb, 0xbf, 0x24), Color.FromArgb(0xa7, 0x8b, 0xfa),
        Color.FromArgb(0x34, 0xd3, 0x99), Color.FromArgb(0xf4, 0x72, 0xb6),
        Color.FromArgb(0xfb, 0x92, 0x3c), Color.FromArgb(0xf8, 0x71, 0x71),
    };

    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>
/// A painted part of the home page. Not a Panel, so the GRT theme leaves its colours alone (it restyles panels, not the
/// program's own painted controls); the wheel turned over it scrolls the page.
/// </summary>
internal abstract class HomeControl : Control
{
    protected HomeControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    protected float K => DeviceDpi / 96f;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        for (Control? c = Parent; c != null; c = c.Parent)
            if (c is HomeCanvas canvas) { canvas.Wheel(e.Delta); return; }
        base.OnMouseWheel(e);
    }
}

/// <summary>One tool (or one entry of a list): icon, caption, an optional second line and a small badge with the step.</summary>
internal sealed class HomeTile : HomeControl
{
    private readonly Bitmap? _icon, _iconHot;
    private readonly string? _badge;
    private readonly Color _accent;
    private readonly Action? _click;
    private bool _hot;

    public string Subtitle { get; }

    public HomeTile(string caption, string? subtitle, string? badge, Color accent, Bitmap? icon, Bitmap? iconHot, Action? click)
    {
        SetStyle(ControlStyles.Selectable, click != null);
        Text = caption;
        Subtitle = subtitle ?? "";
        _badge = string.IsNullOrWhiteSpace(badge) ? null : badge;
        _accent = accent;
        _icon = icon;
        _iconHot = iconHot ?? icon;
        _click = click;
        TabStop = click != null;
        Cursor = click != null ? Cursors.Hand : Cursors.Default;
        AccessibleRole = click != null ? AccessibleRole.PushButton : AccessibleRole.StaticText;
        AccessibleName = caption;
        BackColor = HomeColors.Card;
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); _click?.Invoke(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_click != null && e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; _click(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        float k = K;
        bool hot = _click != null && (_hot || Focused && ShowFocusCues);
        if (hot)
        {
            using var path = HomeColors.Round(new RectangleF(0, 0, Width - 1, Height - 1), 6 * k);
            using var fill = new SolidBrush(HomeColors.CardHot);
            g.FillPath(fill, path);
            using var bar = new SolidBrush(_accent);
            g.FillRectangle(bar, 0, 6 * k, 3 * k, Height - 12 * k);
        }

        int x = (int)(10 * k);
        var icon = hot ? _iconHot : _icon;
        if (icon != null)
        {
            g.DrawImage(icon, x, (Height - icon.Height) / 2, icon.Width, icon.Height);
            x += icon.Width + (int)(10 * k);
        }

        int right = Width - (int)(8 * k);
        if (_badge != null)
        {
            using var bf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            var size = TextRenderer.MeasureText(g, _badge, bf, Size.Empty, TextFormatFlags.NoPadding);
            float bw = size.Width + 12 * k, bh = size.Height + 4 * k;
            var r = new RectangleF(right - bw, (Height - bh) / 2f, bw, bh);
            using var path = HomeColors.Round(r, bh / 2);
            using var fill = new SolidBrush(Color.FromArgb(hot ? 90 : 55, _accent));
            g.FillPath(fill, path);
            TextRenderer.DrawText(g, _badge, bf, Rectangle.Round(r), HomeColors.Mix(_accent, Color.White, 0.35f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var (tx, tw) = TextSpan(g, Width);
        using var font = new Font("Segoe UI", 9.5f);
        using var small = new Font("Segoe UI", 8.5f);
        int h1 = TextRenderer.MeasureText(g, Text, font, new Size(tw, int.MaxValue), Flags).Height;
        int h2 = Subtitle.Length == 0 ? 0 : TextRenderer.MeasureText(g, Subtitle, small, new Size(tw, int.MaxValue), Flags).Height;
        int top = Math.Max(2, (Height - h1 - h2) / 2);
        TextRenderer.DrawText(g, Text, font, new Rectangle(tx, top, tw, h1), hot ? Color.White : HomeColors.Text, Flags);
        if (h2 > 0)
            TextRenderer.DrawText(g, Subtitle, small, new Rectangle(tx, top + h1, tw, Height - top - h1), HomeColors.Dim, Flags);
    }

    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    /// <summary>Where the text goes at a given width: after the icon, before the badge.</summary>
    private (int X, int Width) TextSpan(IDeviceContext dc, int width)
    {
        float k = K;
        int x = (int)(10 * k) + (_icon != null ? _icon.Width + (int)(10 * k) : 0);
        int right = width - (int)(8 * k);
        if (_badge != null)
        {
            using var bf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            right -= TextRenderer.MeasureText(dc, _badge, bf, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(18 * k);
        }
        return (x, Math.Max(10, right - x));
    }

    /// <summary>The height the caption and second line need at this width, so a long name wraps instead of being cut.</summary>
    public int PreferredHeight(int width, int minimum)
    {
        using var g = CreateGraphics();
        var (_, tw) = TextSpan(g, width);
        using var font = new Font("Segoe UI", 9.5f);
        using var small = new Font("Segoe UI", 8.5f);
        int h = TextRenderer.MeasureText(g, Text, font, new Size(tw, int.MaxValue), Flags).Height;
        if (Subtitle.Length > 0) h += TextRenderer.MeasureText(g, Subtitle, small, new Size(tw, int.MaxValue), Flags).Height;
        return Math.Max(minimum, h + (int)(14 * K));
    }
}

/// <summary>A card: a coloured strip on top, an optional number disc and small heading, a title, a note, and its tiles.</summary>
internal sealed class HomeCard : HomeControl
{
    public string Title { get; set; } = "";
    public string? Kicker { get; set; }
    public string? Note { get; set; }
    public int? Number { get; set; }
    public Color Accent { get; set; } = HomeColors.Neutral;
    public int Columns { get; set; } = 1;
    public int TileHeight { get; set; } = 44;
    public List<HomeTile> Tiles { get; } = new();

    public HomeCard() => BackColor = HomeColors.Back;

    public void SetTiles(IEnumerable<HomeTile> tiles)
    {
        foreach (var t in Tiles) { Controls.Remove(t); t.Dispose(); }
        Tiles.Clear();
        Tiles.AddRange(tiles);
        Controls.AddRange(Tiles.ToArray<Control>());
        PerformLayout();
        Invalidate();
    }

    private float Pad => 14 * K;

    private float TitleX => Pad + (Number != null ? 32 * K : 0);

    /// <summary>The title wraps (a long phase name on a narrow card), never cut.</summary>
    private int TitleHeight(int width)
    {
        using var tf = new Font("Segoe UI Semibold", 11.5f);
        int h = TextRenderer.MeasureText(Title, tf, new Size(Math.Max(10, width - (int)(TitleX + Pad)), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        return Math.Max((int)(26 * K), h + (int)(2 * K));
    }

    private int HeaderHeight(int width)
    {
        float k = K, h = Pad + 4 * k;
        if (Kicker != null) h += 16 * k;
        h += TitleHeight(width);
        if (!string.IsNullOrEmpty(Note))
        {
            using var f = new Font("Segoe UI", 8.5f);
            h += TextRenderer.MeasureText(Note, f, new Size(Math.Max(10, width - (int)(2 * Pad)), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 6 * k;
        }
        return (int)(h + 4 * k);
    }

    /// <summary>Tile width and the height of each row of tiles at a card width.</summary>
    private (int TileWidth, List<int> Rows) Grid(int width)
    {
        int cols = Math.Max(1, Columns);
        float pad = 8 * K, colGap = 8 * K;
        int w = (int)((width - 2 * pad - (cols - 1) * colGap) / cols);
        var rows = new List<int>();
        for (int i = 0; i < Tiles.Count; i += cols)
            rows.Add(Tiles.Skip(i).Take(cols).Max(t => t.PreferredHeight(w, (int)(TileHeight * K))));
        return (w, rows);
    }

    public int PreferredHeight(int width)
    {
        var (_, rows) = Grid(width);
        float gap = 4 * K;
        return (int)(HeaderHeight(width) + rows.Sum() + Math.Max(0, rows.Count - 1) * gap + Pad * 0.8f);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int cols = Math.Max(1, Columns);
        float pad = 8 * K, gap = 4 * K, colGap = 8 * K;
        var (w, rows) = Grid(Width);
        float y = HeaderHeight(Width);
        for (int i = 0; i < Tiles.Count; i++)
        {
            int r = i / cols, c = i % cols;
            if (c == 0 && r > 0) y += rows[r - 1] + gap;
            Tiles[i].SetBounds((int)(pad + c * (w + colGap)), (int)y, w, rows[r]);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        float k = K;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = HomeColors.Round(rect, 10 * k);
        using (var fill = new SolidBrush(HomeColors.Card)) g.FillPath(fill, path);
        g.SetClip(path);
        using (var strip = new LinearGradientBrush(new RectangleF(0, 0, Width, 4 * k), Accent, Color.FromArgb(0, Accent), 0f))
            g.FillRectangle(strip, 0, 0, Width, 4 * k);
        g.ResetClip();
        using (var pen = new Pen(HomeColors.Border)) g.DrawPath(pen, path);

        float x = Pad, y = Pad + 4 * k;
        if (Kicker != null)
        {
            using var kf = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, Kicker, kf, new Point((int)x, (int)y), Accent, TextFormatFlags.NoPrefix);
            y += 16 * k;
        }
        float titleX = TitleX;
        if (Number is { } n)
        {
            float d = 24 * k;
            using var disc = new SolidBrush(Accent);
            g.FillEllipse(disc, x, y, d, d);
            using var nf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, n.ToString(CultureInfo.InvariantCulture), nf, Rectangle.Round(new RectangleF(x, y, d, d)),
                HomeColors.Back, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        int th = TitleHeight(Width);
        using (var tf = new Font("Segoe UI Semibold", 11.5f))
            TextRenderer.DrawText(g, Title, tf, new Rectangle((int)titleX, (int)y, Math.Max(10, Width - (int)(titleX + Pad)), th),
                HomeColors.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        y += th;
        if (!string.IsNullOrEmpty(Note))
        {
            using var nf = new Font("Segoe UI", 8.5f);
            TextRenderer.DrawText(g, Note, nf, new Rectangle((int)x, (int)(y + 2 * k), Math.Max(10, Width - (int)(2 * Pad)), Height),
                HomeColors.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>A number of the archive (loads, sessions, shots, groups) with its label.</summary>
internal sealed class HomeStat : HomeControl
{
    public string Value { get; set; } = "—";
    public string Label { get; set; } = "";
    public Color Accent { get; set; } = HomeColors.Accent;

    public HomeStat() => BackColor = HomeColors.Back;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        float k = K;
        using var path = HomeColors.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10 * k);
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), HomeColors.Mix(HomeColors.Card, Accent, 0.10f), HomeColors.Card, 0f))
            g.FillPath(fill, path);
        using (var pen = new Pen(HomeColors.Border)) g.DrawPath(pen, path);
        using (var bar = new SolidBrush(Accent)) g.FillRectangle(bar, 0, 14 * k, 4 * k, Height - 28 * k);
        using var vf = new Font("Segoe UI Semibold", 19f);
        using var lf = new Font("Segoe UI", 8.5f);
        int lh = TextRenderer.MeasureText(g, "Ag", lf).Height;
        int vh = TextRenderer.MeasureText(g, "0", vf).Height;
        int top = (Height - vh - lh) / 2;
        TextRenderer.DrawText(g, Value, vf, new Rectangle((int)(18 * k), top, Width - (int)(24 * k), vh), HomeColors.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Label, lf, new Rectangle((int)(19 * k), top + vh, Width - (int)(24 * k), lh), HomeColors.Dim,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>The amber call to action of the header.</summary>
internal sealed class HomeButton : HomeControl
{
    private readonly Action _click;
    private bool _hot;

    public HomeButton(string text, Action click)
    {
        SetStyle(ControlStyles.Selectable, true);
        Text = text; _click = click; TabStop = true; Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton; AccessibleName = text;
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); _click(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; _click(); }
    }

    public int PreferredWidth()
    {
        using var f = new Font("Segoe UI Semibold", 10f);
        return TextRenderer.MeasureText(Text, f).Width + (int)(36 * K);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = HomeColors.Round(r, Height / 2f);
        bool hot = _hot || Focused && ShowFocusCues;
        var top = hot ? Color.FromArgb(0xff, 0xd2, 0x7a) : Color.FromArgb(0xff, 0xc2, 0x5c);
        var bottom = hot ? Color.FromArgb(0xff, 0x9f, 0x2a) : Color.FromArgb(0xf0, 0x8c, 0x1a);
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), top, bottom, 90f)) g.FillPath(fill, path);
        using var f = new Font("Segoe UI Semibold", 10f);
        TextRenderer.DrawText(g, Text, f, Rectangle.Round(r), Color.FromArgb(0x1a, 0x12, 0x05),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>The band on top: emblem, name, what it is, the call to action and the shooter.</summary>
internal sealed class HomeHeader : HomeControl
{
    private readonly Bitmap _emblem;

    public HomeHeader(Bitmap emblem)
    {
        _emblem = emblem;
        BackColor = HomeColors.HeaderBottom;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = K;
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), HomeColors.HeaderTop, HomeColors.HeaderBottom, 20f))
            g.FillRectangle(fill, 0, 0, Width, Height);
        using (var line = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), 3), HomeColors.Accent, Color.FromArgb(0, HomeColors.Accent), 0f))
            g.FillRectangle(line, 0, Height - 2 * k, Width, 2 * k);
        float x = 22 * k;
        g.DrawImage(_emblem, x, (Height - _emblem.Height) / 2f, _emblem.Width, _emblem.Height);
        x += _emblem.Width + 16 * k;
        using var tf = new Font("Segoe UI Semibold", 20f);
        using var sf = new Font("Segoe UI", 9.5f);
        int th = TextRenderer.MeasureText(g, "Ag", tf).Height, sh = TextRenderer.MeasureText(g, "Ag", sf).Height;
        int top = (Height - th - sh) / 2;
        TextRenderer.DrawText(g, Product.LabName, tf, new Point((int)x, top), HomeColors.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Lang.T("Your own reloading archive, analyses and reports") + "  ·  v" + AppVersion.Short, sf,
            new Point((int)x + (int)(2 * k), top + th), HomeColors.Dim, TextFormatFlags.NoPrefix);
    }
}

/// <summary>The scrolling page that holds everything and lays it out to the window's width.</summary>
internal sealed class HomeCanvas : ScrollableControl
{
    private readonly List<(string Text, Point At)> _captions = new();
    private bool _layingOut;
    public Action<HomeCanvas>? Arrange { get; set; }

    public HomeCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        BackColor = HomeColors.Back;
    }

    public void ClearCaptions() => _captions.Clear();

    /// <summary>A section heading painted on the page (in page coordinates, not scrolled ones).</summary>
    public void Caption(string text, int x, int y) => _captions.Add((text, new Point(x, y)));

    public void Wheel(int delta)
    {
        var p = AutoScrollPosition;
        AutoScrollPosition = new Point(-p.X, Math.Max(0, -p.Y - delta * SystemInformation.MouseWheelScrollLines * 12 / 120));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_layingOut || Arrange == null) return;
        _layingOut = true;
        try { Arrange(this); }
        finally { _layingOut = false; }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
        foreach (var (text, at) in _captions)
            TextRenderer.DrawText(g, text, f, new Point(at.X + AutoScrollPosition.X, at.Y + AutoScrollPosition.Y), HomeColors.Dim, TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// The home page of Reload Lab when it is started on its own: the archive at a glance (how many loads,
/// sessions, shots and groups; the latest sessions and PDF reports), the five phases of the recommended path as cards
/// in their own colours with their tools, the other tools, and where the archive and its copies are. Opened by GRT the
/// toolkit keeps the compact <see cref="LauncherForm"/> beside GRT's window instead.
/// </summary>
internal sealed class HomeForm : Form
{
    private readonly Action<Tool> _open;
    private readonly ArchiveDb? _archive;
    private readonly Db _db;
    private readonly HomeCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly HomeHeader _header;
    private readonly HomeButton _wizard;
    private readonly ShooterBar? _shooter;
    private readonly HomeStat[] _stats;
    private readonly List<HomeCard> _phaseCards = new();
    private readonly HomeCard _more, _always, _recent, _reports, _store;
    private readonly CheckBox _look;
    private readonly Label _lookNote;
    private readonly List<Bitmap> _bitmaps = new();
    private string _signature = "";

    public HomeForm(Action<Tool> open, Db db, Action<long?>? shooterChanged, ArchiveDb? archive)
    {
        _open = open;
        _db = db;
        _archive = archive;
        Text = AppVersion.Title(Product.LabName);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = HomeColors.Back;
        float k = DeviceDpi / 96f;
        Size = new Size((int)(1560 * k), (int)(900 * k));
        MinimumSize = new Size((int)(860 * k), (int)(560 * k));

        _header = new HomeHeader(Keep(HomeArt.EmblemPng((int)(60 * k))));
        _wizard = new HomeButton("＋  " + LauncherCaption.From(Lang.T(ToolCatalog.Wizard.Key)), () => _open(Tool.Wizard));
        _header.Controls.Add(_wizard);
        if (shooterChanged != null)
        {
            _shooter = new ShooterBar(db, id => { shooterChanged(id); Reload(force: true); }) { BackColor = HomeColors.HeaderBottom, ForeColor = HomeColors.Text };
            foreach (Control c in _shooter.Controls)
                if (c is Label l) { l.BackColor = HomeColors.HeaderBottom; l.ForeColor = l.ForeColor == SystemColors.GrayText ? HomeColors.Dim : HomeColors.Text; }
            _header.Controls.Add(_shooter);
        }
        _canvas.Controls.Add(_header);

        _stats = new[]
        {
            new HomeStat { Label = Lang.T("Loads"), Accent = HomeColors.Phase[0] },
            new HomeStat { Label = Lang.T("Range sessions"), Accent = HomeColors.Phase[1] },
            new HomeStat { Label = Lang.T("Shots recorded"), Accent = HomeColors.Phase[3] },
            new HomeStat { Label = Lang.T("Groups measured"), Accent = HomeColors.Phase[2] },
        };
        _canvas.Controls.AddRange(_stats);

        for (int i = 0; i < ToolCatalog.Phases.Length; i++)
        {
            var (title, tools, note) = ToolCatalog.Phases[i];
            var color = HomeColors.Phase[i];
            var card = new HomeCard
            {
                Title = Lang.T(title), Kicker = string.Format(CultureInfo.InvariantCulture, Lang.T("PHASE {0}"), i + 1),
                Number = i + 1, Accent = color, Note = note == null ? null : Lang.T(note), TileHeight = 46,
            };
            card.SetTiles(tools.Select(t => ToolTile(t, color)));
            _phaseCards.Add(card);
        }
        _canvas.Controls.AddRange(_phaseCards.ToArray<Control>());

        _more = new HomeCard { Title = Lang.T("Optional tools"), Kicker = Lang.T("MORE ANALYSES"), Accent = HomeColors.Neutral, TileHeight = 42 };
        _more.SetTiles(ToolCatalog.More.Select(t => ToolTile(t, HomeColors.Neutral, t.Tool == Tool.Diagnosis ? Lang.T("experimental") : null)));
        _always = new HomeCard { Title = Lang.T("Always at hand"), Kicker = Lang.T("ARCHIVE AND DOCUMENTS"), Accent = HomeColors.Accent, TileHeight = 42 };
        _always.SetTiles(ToolCatalog.Always.Select(t => ToolTile(t, HomeColors.Accent)));
        _recent = new HomeCard { Title = Lang.T("Latest range sessions"), Kicker = Lang.T("FROM THE ARCHIVE"), Accent = HomeColors.Phase[1], TileHeight = 46 };
        _reports = new HomeCard { Title = Lang.T("Latest PDF reports"), Kicker = Lang.T("REPORTS"), Accent = HomeColors.Phase[4], TileHeight = 46 };
        _store = new HomeCard { Title = Lang.T("Where your data is"), Kicker = Lang.T("ARCHIVE"), Accent = HomeColors.Phase[3], TileHeight = 40 };
        _store.SetTiles(new[]
        {
            Tile(Lang.T("Open the Ricarica folder"), null, null, HomeColors.Phase[3], "folder", () => OpenFolder(ArchivePaths.Folder)),
            Tile(Lang.T("Open the reports folder"), null, null, HomeColors.Phase[3], "folder", () => OpenFolder(ReportsFolder)),
        });
        _canvas.Controls.AddRange(new Control[] { _more, _always, _recent, _reports, _store });

        // The tool windows can keep GRT's dark look or the classic one; the home page has its own colours either way.
        _look = new CheckBox
        {
            Text = Lang.T("Dark windows in the style of this page"), AutoSize = true, UseMnemonic = false, Checked = GrtTheme.Enabled,
            ForeColor = HomeColors.Dim, BackColor = HomeColors.Back,
        };
        _lookNote = new Label
        {
            Text = Lang.T("Takes effect the next time the toolkit starts."), AutoSize = true, UseMnemonic = false,
            ForeColor = HomeColors.Dim, BackColor = HomeColors.Back, Visible = false,
        };
        _look.CheckedChanged += (_, _) => { UiState.Set(GrtTheme.StateKey, _look.Checked ? "1" : "0"); _lookNote.Visible = true; };
        _canvas.Controls.Add(_look);
        _canvas.Controls.Add(_lookNote);

        _canvas.Arrange = ArrangePage;
        Controls.Add(_canvas);
        Reload(force: true);
    }

    private static string ReportsFolder => System.IO.Path.Combine(ArchivePaths.Folder, "rapporti");

    private Bitmap Keep(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var b = new Bitmap(ms);
        _bitmaps.Add(b);
        return b;
    }

    private Bitmap TileIcon(string name, Color color, bool hot)
    {
        int px = LogicalToDeviceUnits(22);
        var top = hot ? Color.White : HomeColors.Mix(color, Color.White, 0.25f);
        var bottom = hot ? HomeColors.Mix(color, Color.White, 0.2f) : HomeColors.Mix(color, HomeColors.Card, 0.25f);
        return Keep(ToolIconPainter.RenderPng(name, px, HomeColors.Hex(top), HomeColors.Hex(bottom)));
    }

    private HomeTile Tile(string caption, string? subtitle, string? badge, Color color, string? icon, Action? click)
        => new(caption, subtitle, badge, color, icon == null ? null : TileIcon(icon, color, false), icon == null ? null : TileIcon(icon, color, true), click);

    private HomeTile ToolTile(ToolEntry e, Color color, string? badge = null)
        => Tile(LauncherCaption.From(Lang.T(e.Key)), null, badge ?? e.Step, color, e.Icon, () => _open(e.Tool));

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Product.LabName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Product.LabName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // wide enough for the five phases side by side and the column of the archive, but never past the screen
        var work = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Width, work.Width * 94 / 100), h = Math.Min(Height, work.Height * 94 / 100);
        SetBounds(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h);
    }

    /// <summary>Back in front: a tool may have added a session or a report since the page was last drawn.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Reload(force: false);
    }

    /// <summary>Reads the numbers, the latest sessions and the latest reports; the page is rebuilt only when they changed.</summary>
    private void Reload(bool force)
    {
        var stats = new[] { "—", "—", "—", "—" };
        var sessions = new List<ArchiveSessionInfo>();
        string? problem = null;
        if (_archive != null)
        {
            try
            {
                long sh = ArchiveImports.ShooterFor(_archive, _db.CurrentShooterId);
                var all = ArchiveQueries.Sessions(_archive, sh);
                sessions = all.Take(6).ToList();
                var it = CultureInfo.GetCultureInfo("it-IT");
                stats[0] = ArchiveImports.LoadsOf(_archive, sh).Count.ToString("N0", it);
                stats[1] = all.Count.ToString("N0", it);
                stats[2] = all.Sum(s => s.Shots).ToString("N0", it);
                stats[3] = all.Sum(s => s.Groups).ToString("N0", it);
            }
            catch (Exception ex) { problem = ex.Message; }
        }
        else problem = Lang.T("The archive could not be opened: the tools still work, but nothing is shown here.");

        var reports = Directory.Exists(ReportsFolder)
            ? new DirectoryInfo(ReportsFolder).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTimeUtc).Take(5).ToList()
            : new List<FileInfo>();
        string backup = ArchiveBackup.List(ArchivePaths.Backups).FirstOrDefault() is { } last
            ? File.GetLastWriteTime(last).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";

        string signature = string.Join("|", stats) + "|" + string.Join(",", sessions.Select(s => s.Id + ":" + s.Shots + ":" + s.Groups))
                           + "|" + string.Join(",", reports.Select(r => r.Name + r.Length)) + "|" + backup + "|" + problem;
        if (!force && signature == _signature) return;
        _signature = signature;

        for (int i = 0; i < 4; i++) { _stats[i].Value = stats[i]; _stats[i].Invalidate(); }

        _recent.Note = problem ?? (sessions.Count == 0 ? Lang.T("No range session in the archive yet: import a chronograph or ShotMarker file.") : null);
        _recent.SetTiles(sessions.Select(s =>
        {
            var parts = new List<string>();
            if (s.Strings > 0) parts.Add($"{s.Strings} {Lang.T("strings")}");
            if (s.Shots > 0) parts.Add($"{s.Shots} {Lang.T("shots")}");
            if (s.Groups > 0) parts.Add($"{s.Groups} {Lang.T("groups")}");
            string when = ItalianDate(s.Date) + (s.DateEstimated ? " ≈" : "");
            string load = string.IsNullOrWhiteSpace(s.LoadName) ? Lang.T("(no load)") : ShortLoadName(s.LoadName);
            return Tile(load, when + "  ·  " + string.Join(", ", parts), null, HomeColors.Phase[1], "stopwatch", null);
        }));

        _reports.Note = reports.Count == 0 ? Lang.T("No report yet: the analyses have a \"PDF report\" button.") : null;
        _reports.SetTiles(reports.Select(r =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(r.Name);
            // "2026-11-16 1043 Tool - Title": the date goes to the second line
            string title = name.Length > 16 && char.IsDigit(name[0]) ? name[16..] : name;
            return Tile(title, r.LastWriteTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), null, HomeColors.Phase[4], "doc",
                () => OpenFile(r.FullName));
        }));

        _store.Note = ArchivePaths.Database + Environment.NewLine
                      + (backup.Length > 0 ? string.Format(Lang.T("Last copy: {0}"), backup) : Lang.T("No copy yet: one is made when you close the program after a change."));
        _store.Invalidate();
        _canvas.PerformLayout();
    }

    /// <summary>The name of a load without the stamp of an automatic copy ("…_toolkit_20261012_152946").</summary>
    internal static string ShortLoadName(string name)
    {
        string s = System.Text.RegularExpressions.Regex.Replace(name, @"_(toolkit|athlon|shotmarker)(_\d{8}_\d{6})?$", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s.Length > 0 ? s : name;
    }

    private static string ItalianDate(string isoDate)
        => DateTime.TryParseExact(isoDate.Length >= 10 ? isoDate[..10] : isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("it-IT")) : isoDate;

    /// <summary>
    /// The page to the window's width: the header across the top; under it the numbers, the five phases side by side
    /// (three and two when narrow), the other tools; on the right, when there is room, the latest sessions, the reports
    /// and the archive.
    /// </summary>
    private void ArrangePage(HomeCanvas c)
    {
        float k = DeviceDpi / 96f;
        int m = (int)(22 * k), gap = (int)(14 * k);
        int width = c.ClientSize.Width;
        var o = c.AutoScrollPosition;
        void Place(Control ctl, int x, int y, int w, int h) => ctl.SetBounds(x + o.X, y + o.Y, w, h);
        c.ClearCaptions();

        int headerH = (int)(96 * k);
        Place(_header, 0, 0, width, headerH);
        int shooterW = (int)(300 * k);
        int bw = _wizard.PreferredWidth(), bh = (int)(40 * k);
        if (_shooter != null)
        {
            _shooter.SetBounds(width - shooterW - m, (headerH - _shooter.Height) / 2 + (int)(8 * k), shooterW, _shooter.Height);
            _wizard.SetBounds(_shooter.Left - bw - gap, (headerH - bh) / 2, bw, bh);
        }
        else _wizard.SetBounds(width - bw - m, (headerH - bh) / 2, bw, bh);
        // narrow: the title needs the room, the button goes
        _wizard.Visible = _wizard.Left > (int)(470 * k);

        bool side = width >= (int)(1400 * k);
        int sideW = side ? (int)(320 * k) : 0;
        int mainW = width - 2 * m - (side ? sideW + gap : 0);
        int y = headerH + gap + (int)(4 * k);

        // the numbers
        int statW = (mainW - 3 * gap) / 4, statH = (int)(78 * k);
        for (int i = 0; i < 4; i++) Place(_stats[i], m + i * (statW + gap), y, statW, statH);
        int sideTop = y;
        y += statH + gap + (int)(6 * k);

        // the path
        c.Caption(Lang.T("THE RECOMMENDED PATH"), m + 2, y);
        y += (int)(20 * k);
        int perRow = mainW >= (int)(900 * k) ? (_phaseCards.Count > 5 ? 4 : 5) : 3;
        int cw = (mainW - (perRow - 1) * gap) / perRow;
        for (int start = 0; start < _phaseCards.Count; start += perRow)
        {
            var row = _phaseCards.Skip(start).Take(perRow).ToList();
            int h = row.Max(card => card.PreferredHeight(cw));
            for (int i = 0; i < row.Count; i++) Place(row[i], m + i * (cw + gap), y, cw, h);
            y += h + gap;
        }
        y += (int)(6 * k);

        // the other tools
        c.Caption(Lang.T("MORE TOOLS"), m + 2, y);
        y += (int)(20 * k);
        int moreW = mainW >= (int)(900 * k) ? (mainW - gap) * 62 / 100 : mainW;
        int alwaysW = mainW >= (int)(900 * k) ? mainW - moreW - gap : mainW;
        _more.Columns = Math.Max(1, moreW / (int)(250 * k));
        _always.Columns = Math.Max(1, alwaysW / (int)(250 * k));
        int moreH = _more.PreferredHeight(moreW), alwaysH = _always.PreferredHeight(alwaysW);
        if (mainW >= (int)(900 * k))
        {
            int h = Math.Max(moreH, alwaysH);
            Place(_more, m, y, moreW, h);
            Place(_always, m + moreW + gap, y, alwaysW, h);
            y += h + gap;
        }
        else
        {
            Place(_more, m, y, moreW, moreH); y += moreH + gap;
            Place(_always, m, y, alwaysW, alwaysH); y += alwaysH + gap;
        }

        // the side column (or under everything, when the window is narrow)
        int sx = side ? m + mainW + gap : m, sy = side ? sideTop : y, sw = side ? sideW : mainW;
        foreach (var card in new[] { _recent, _reports, _store })
        {
            int h = card.PreferredHeight(sw);
            Place(card, sx, sy, sw, h);
            sy += h + gap;
        }
        if (!side) y = sy;

        int bottom = Math.Max(y, side ? sy : y);
        Place(_look, m, bottom, _look.PreferredSize.Width, _look.PreferredSize.Height);
        Place(_lookNote, m + _look.PreferredSize.Width + gap, bottom + (int)(2 * k), _lookNote.PreferredSize.Width, _lookNote.PreferredSize.Height);
        bottom += _look.PreferredSize.Height + m;
        c.AutoScrollMinSize = new Size(0, bottom);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var b in _bitmaps) b.Dispose();
        base.Dispose(disposing);
    }
}
