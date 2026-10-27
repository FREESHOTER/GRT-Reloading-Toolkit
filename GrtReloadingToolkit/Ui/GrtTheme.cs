using System.Runtime.InteropServices;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The look of GRT itself, for every window of the toolkit: the dark slate panels, the light text and the Segoe UI 9 pt it draws
/// everything with (colours sampled from a screenshot of GRT's main window, the font read off its glyphs). Applied from one place
/// (<see cref="Program"/> for tool windows, <see cref="Install"/> for any window opened later), so a tool form is written once and
/// follows the theme without knowing about it.
///
/// A colour a form sets by hand for a light background (a pale yellow banner, a dark-red row) would be unreadable on slate;
/// <see cref="Back"/> and <see cref="Fore"/> keep its hue and move it to the dark/light end, so a warning is still a warning.
/// The classic look stays available: <see cref="Enabled"/> is read from the UI state and can be turned off.
/// </summary>
internal static class GrtTheme
{
    public const string StateKey = "theme.grt";

    public static readonly Color PanelBack = Color.FromArgb(0x39, 0x44, 0x54);
    public static readonly Color PanelAlt = Color.FromArgb(0x3d, 0x49, 0x5c);
    public static readonly Color Header = Color.FromArgb(0x2d, 0x36, 0x43);
    public static readonly Color Field = Color.FromArgb(0x28, 0x32, 0x3c);
    public static readonly Color Border = Color.FromArgb(0x4e, 0x5e, 0x73);
    public static readonly Color Selection = Color.FromArgb(0x63, 0x76, 0x90);
    public static readonly Color Text = Color.White;
    public static readonly Color Dim = Color.FromArgb(0x8c, 0x9b, 0xb0);
    public static readonly Color Accent = Color.FromArgb(0xff, 0xb3, 0x47);

    /// <summary>The standard size of GRT's text; smaller captions (status lines, notes) are brought up to it.</summary>
    public const float FontSize = 9f;

    public static bool Enabled
    {
        get
        {
            // The variable is for testing the classic look side by side without touching the stored choice.
            if (Environment.GetEnvironmentVariable("GRT_TOOLKIT_THEME") == "0") return false;
            try { return UiState.Get(StateKey, "1") != "0"; }
            catch (Exception) { return true; }
        }
    }

    // Windows already dressed, so a window met twice (Install sweeps all of them) is not restyled.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Done = new();
    private static bool _installed;

    /// <summary>Dresses every window that opens from now on that has not been dressed by <see cref="Apply"/> already.</summary>
    public static void Install()
    {
        if (_installed || !Enabled) return;
        _installed = true;
        Application.Idle += (_, _) =>
        {
            foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                if (!f.IsDisposed) Apply(f);
        };
    }

    /// <summary>Dresses a window and everything it contains, and everything added to it later. Safe to call twice.</summary>
    public static void Apply(Form form)
    {
        if (!Enabled) return;
        if (Done.TryGetValue(form, out _)) return;
        Done.Add(form, new object());
        form.BackColor = PanelBack;
        form.ForeColor = Text;
        Walk(form);
        if (form.IsHandleCreated) Immersive(form); else form.HandleCreated += (_, _) => Immersive(form);
    }

    private static void Walk(Control c)
    {
        Style(c);
        foreach (Control child in c.Controls) Walk(child);
        // Controls built after the window opens (a results grid, a row of buttons) get the same treatment.
        c.ControlAdded += (_, e) => { if (e.Control is { } n) Walk(n); };
    }

    private static void Style(Control c)
    {
        if (c is not Form) NormalizeFont(c);
        switch (c)
        {
            case DataGridView g: StyleGrid(g); break;
            case TextBoxBase t:
                t.BackColor = Field; t.ForeColor = Text;
                if (t is TextBox tb && tb.BorderStyle == BorderStyle.Fixed3D) tb.BorderStyle = BorderStyle.FixedSingle;
                Dark(t); break;
            case NumericUpDown n: n.BackColor = Field; n.ForeColor = Text; Dark(n); break;
            case ComboBox cb: cb.BackColor = Field; cb.ForeColor = Text; Dark(cb, "DarkMode_CFD"); break;
            case ListBox lb: lb.BackColor = Field; lb.ForeColor = Text; lb.BorderStyle = BorderStyle.FixedSingle; Dark(lb); break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false;
                b.BackColor = Border; b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Selection; b.FlatAppearance.MouseOverBackColor = Selection;
                b.FlatAppearance.MouseDownBackColor = Header; break;
            case CheckBox cx when cx.Appearance == Appearance.Normal:
                // The system draws the tick box in white; flat, it takes the field colour and the theme's border.
                cx.FlatStyle = FlatStyle.Flat; cx.ForeColor = Fore(cx.ForeColor, Text); cx.BackColor = Back(cx.BackColor, PanelBack);
                cx.FlatAppearance.BorderColor = Dim; cx.FlatAppearance.BorderSize = 1;
                cx.FlatAppearance.CheckedBackColor = Border; cx.FlatAppearance.MouseOverBackColor = Selection;
                break;
            case CheckBox or RadioButton: c.ForeColor = Fore(c.ForeColor, Text); break;
            case LinkLabel l: l.LinkColor = Accent; l.ActiveLinkColor = Color.White; l.VisitedLinkColor = Accent; break;
            case Label l: l.ForeColor = Fore(l.ForeColor, Text); l.BackColor = Back(l.BackColor, PanelBack); break;
            case TabControl tc: StyleTabs(tc); break;
            case SplitContainer sc: sc.BackColor = Header; break;
            case PictureBox: break;                                 // a printed target, a photo or a preview: its own colours
            case Panel or FlowLayoutPanel or TableLayoutPanel or TabPage or UserControl:
                c.BackColor = Back(c.BackColor, PanelBack); c.ForeColor = Fore(c.ForeColor, Text); break;
            default:
                // A custom-painted chart draws its own background; leave its colours alone, only the text it inherits is made legible.
                if (c.GetType().Namespace != "GrtReloadingToolkit.Ui") c.ForeColor = Fore(c.ForeColor, Text);
                break;
        }
    }

    // The system draws tab faces in light colours whatever BackColor says, so they are drawn here: the page in view is the panel
    // colour (it joins the content below), the others sit on the header colour, the way GRT's own tabs do.
    private static void StyleTabs(TabControl tc)
    {
        tc.BackColor = PanelBack; tc.ForeColor = Text;
        tc.DrawMode = TabDrawMode.OwnerDrawFixed;
        tc.Padding = new Point(14, 4);
        void Size()
        {
            int w = 40;
            foreach (TabPage p in tc.TabPages) w = Math.Max(w, TextRenderer.MeasureText(p.Text, tc.Font).Width + 28);
            tc.ItemSize = new Size(w, Math.Max(24, tc.Font.Height + 8));
        }
        Size();
        tc.ControlAdded += (_, _) => Size();
        tc.FontChanged += (_, _) => Size();
        void Attach() { if (tc.Alignment == TabAlignment.Top) new TabFrame(tc); }
        if (tc.IsHandleCreated) Attach(); else tc.HandleCreated += (_, _) => Attach();
        tc.DrawItem += (_, e) =>
        {
            var page = tc.TabPages[e.Index];
            bool sel = e.Index == tc.SelectedIndex;
            using (var b = new SolidBrush(sel ? PanelBack : Header)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, page.Text, tc.Font, e.Bounds, sel ? Text : Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (sel) using (var pen = new Pen(Accent, 2)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Top + 1, e.Bounds.Right, e.Bounds.Top + 1);
        };
    }

    /// <summary>
    /// Paints, after the system has, what it draws in light colours whatever BackColor says: the strip behind and beside the tabs
    /// and the frame around the page. Listens to the control's own messages, so the tool form is not asked to change.
    /// </summary>
    private sealed class TabFrame : NativeWindow
    {
        private const int WmPaint = 0x000F;
        private readonly TabControl _tc;

        public TabFrame(TabControl tc)
        {
            _tc = tc;
            AssignHandle(tc.Handle);
            tc.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WmPaint || _tc.TabCount == 0) return;
            try
            {
                using var g = Graphics.FromHwnd(Handle);
                int bottom = 0;
                for (int i = 0; i < _tc.TabCount; i++) bottom = Math.Max(bottom, _tc.GetTabRect(i).Bottom);
                var all = new Rectangle(0, 0, _tc.Width, _tc.Height);
                var disp = _tc.DisplayRectangle;

                // the strip: everything above the page that is not a tab
                var save = g.Save();
                g.SetClip(new Rectangle(0, 0, _tc.Width, bottom));
                for (int i = 0; i < _tc.TabCount; i++) g.ExcludeClip(_tc.GetTabRect(i));
                using (var b = new SolidBrush(Header)) g.FillRectangle(b, all);
                g.Restore(save);

                // the frame: the ring between the control's edge and the page, in the panel colour with a thin border
                save = g.Save();
                var outer = new Rectangle(0, bottom, _tc.Width, _tc.Height - bottom);
                g.SetClip(outer);
                g.ExcludeClip(disp);
                using (var b = new SolidBrush(PanelBack)) g.FillRectangle(b, outer);
                g.Restore(save);
                using var pen = new Pen(Border);
                g.DrawRectangle(pen, 0, bottom, _tc.Width - 1, _tc.Height - bottom - 1);

                // the light outline the system leaves around each tab face
                using (var ring = new Pen(Header, 2))
                    for (int i = 0; i < _tc.TabCount; i++)
                    {
                        var o = Rectangle.Inflate(_tc.GetTabRect(i), 2, 2);
                        g.DrawLines(ring, new[] { new Point(o.Left, o.Bottom - 2), new Point(o.Left, o.Top), new Point(o.Right - 1, o.Top), new Point(o.Right - 1, o.Bottom - 2) });
                    }
                for (int i = 0; i < _tc.TabCount; i++)
                {
                    var r = _tc.GetTabRect(i);
                    bool sel = i == _tc.SelectedIndex;
                    using var face = new Pen(sel ? PanelBack : Header, 2);
                    g.DrawRectangle(face, r.X, r.Y, r.Width - 1, r.Height - 1);
                    g.DrawLines(pen, new[] { new Point(r.Left - 1, r.Bottom), new Point(r.Left - 1, r.Top - 1), new Point(r.Right, r.Top - 1), new Point(r.Right, r.Bottom) });
                    if (sel) using (var acc = new Pen(Accent, 2)) g.DrawLine(acc, r.Left, r.Top + 1, r.Right - 1, r.Top + 1);
                }
            }
            catch (Exception) { /* a frame left in the system colour is not worth a fault */ }
        }
    }

    private static void StyleGrid(DataGridView g)
    {
        g.EnableHeadersVisualStyles = false;
        g.BackgroundColor = PanelBack;
        g.GridColor = Border;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        g.BorderStyle = BorderStyle.None;
        g.ColumnHeadersDefaultCellStyle.BackColor = Header;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
        g.RowHeadersDefaultCellStyle.BackColor = Header;
        g.RowHeadersDefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.BackColor = PanelBack;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = Selection;
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.AlternatingRowsDefaultCellStyle.BackColor = PanelAlt;
        g.AlternatingRowsDefaultCellStyle.ForeColor = Text;
        // Rows coloured by code after the data arrives (pale yellow for "low", dark red for "retire") are moved to the same scheme
        // as they are drawn, so no tool has to know about the theme.
        g.CellFormatting += (_, e) =>
        {
            if (e.CellStyle is not { } s) return;
            Color back = s.BackColor, fore = s.ForeColor;
            if (!back.IsEmpty && Luminance(back) > 0.55) s.BackColor = Back(back, PanelBack);
            if (!fore.IsEmpty && (fore == SystemColors.GrayText || Luminance(fore) < 0.45)) s.ForeColor = Fore(fore, Text);
        };
        Dark(g);
    }

    private static void NormalizeFont(Control c)
    {
        var f = c.Font;
        if (f.Size < FontSize && f.FontFamily.Name == "Segoe UI" || f.Size > FontSize && f.Size < 10f && f.FontFamily.Name == "Segoe UI")
            c.Font = new Font(f.FontFamily, FontSize, f.Style);
        else if (f.Size < FontSize && f.FontFamily.Name != "Segoe UI" && f.Size >= 7f && f.Name != Control.DefaultFont.Name)
            c.Font = new Font(f.FontFamily, FontSize, f.Style);
    }

    // ---- colours ---------------------------------------------------------------------------------------------------------------

    /// <summary>Relative luminance 0..1 (sRGB, no gamma): enough to tell a pale colour from a dark one.</summary>
    internal static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>A text colour for the dark panels: the default and "grey" ones become the theme's, a hue is kept and lightened.</summary>
    internal static Color Fore(Color c, Color fallback)
    {
        if (c.IsEmpty || c == SystemColors.ControlText || c == SystemColors.WindowText || c == Color.Black) return fallback;
        if (c == SystemColors.GrayText || c == Color.Gray) return Dim;
        if (Luminance(c) >= 0.6) return c;
        c.ToHsl(out float h, out float s, out _);
        return FromHsl(h, Math.Min(1f, s * 0.9f + 0.1f), 0.72f);
    }

    /// <summary>A background colour for the dark panels: default/light colours become the theme's, a hue is kept and darkened.</summary>
    internal static Color Back(Color c, Color fallback)
    {
        if (c.IsEmpty || c == SystemColors.Control || c == SystemColors.Window || c == Color.White || c == Color.Transparent) return fallback;
        if (c.A < 255) return c;
        if (Luminance(c) < 0.4) return c;
        c.ToHsl(out float h, out float s, out _);
        return FromHsl(h, Math.Min(0.45f, s * 0.6f), 0.26f);
    }

    private static void ToHsl(this Color c, out float h, out float s, out float l)
    {
        h = c.GetHue(); s = c.GetSaturation(); l = c.GetBrightness();
    }

    private static Color FromHsl(float h, float s, float l)
    {
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static float Chan(float p, float q, float t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            return t < 1f / 6 ? p + (q - p) * 6 * t : t < 0.5f ? q : t < 2f / 3 ? p + (q - p) * (2f / 3 - t) * 6 : p;
        }
        float hh = h / 360f;
        return Color.FromArgb((int)(Chan(p, q, hh + 1f / 3) * 255), (int)(Chan(p, q, hh) * 255), (int)(Chan(p, q, hh - 1f / 3) * 255));
    }

    // ---- the Windows side: dark scroll bars, spin buttons and title bar -------------------------------------------------------

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void Dark(Control c, string theme = "DarkMode_Explorer")
    {
        void Set() { try { SetWindowTheme(c.Handle, theme, null); } catch (Exception) { /* decoration only */ } }
        if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
    }

    private static void Immersive(Form f)
    {
        try
        {
            int on = 1;
            // 20 on Windows 10 2004+ and 11; 19 on the 1809/1903 builds before it. Whichever the system does not know is ignored.
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, sizeof(int));
            // A window already on screen keeps the bar it was drawn with until the frame is redrawn.
            if (f.Visible)
            {
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
                SendMessage(f.Handle, 0x0086, IntPtr.Zero, IntPtr.Zero);          // WM_NCACTIVATE off, then on: repaints the caption
                SendMessage(f.Handle, 0x0086, new IntPtr(1), IntPtr.Zero);
            }
        }
        catch (Exception) { /* a light title bar is not a fault */ }
    }
}
