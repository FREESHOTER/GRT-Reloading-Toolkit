namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The one entry point every toolbar/menu click opens (see Program.ToolkitContext). Grouped by
/// when you'd actually reach for each tool — before a range trip (bench prep, nothing needs
/// measured data yet) or after one (everything else, since every analysis in this Toolkit needs
/// either a chronograph string or a logged session to work from — there's no tool here that's
/// useful mid-string, so a third "at the range" group would sit empty) — rather than a flat
/// alphabetic list.
/// </summary>
internal sealed class LauncherForm : Form
{
    /// <summary>Holds the tool list, so OnLoad can read the height it wants.</summary>
    private readonly FlowLayoutPanel _flow;

    /// <summary>Clear space either side of a button's caption, so the text never runs up against
    /// the border.</summary>
    private const int CaptionAir = 32;

    /// <summary>A floor under the measured column, so the window is not a narrow ribbon on an
    /// install where every caption happens to be short.</summary>
    private const int MinColumnWidth = 280;

    /// <summary>How tall the window opens at most: sixteen rows of the 40px button pitch. The whole
    /// list wants about 1065px, which on a tall monitor opens as a ribbon down the screen -- this is
    /// enough for the pre-range group and most of the post-range one, and the rest is a wheel-scroll
    /// away. Only the opening size; the grip and <see cref="MinimumSize"/> still govern by hand.</summary>
    private const int MaxOpeningClientHeight = 640;

    /// <summary>What the caption actually needs, by the same measurement the renderer will make.
    /// <c>Button.GetPreferredSize</c> cannot answer this -- it floors at whatever explicit
    /// <c>Width</c> the button already carries, so it reports back the number being replaced.</summary>
    private static int CaptionWidth(Button b) =>
        TextRenderer.MeasureText(b.Text, b.Font, new Size(int.MaxValue, int.MaxValue),
                                 TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

    public LauncherForm(Action<Tool> open)
    {
        Text = AppVersion.Title("Reloading Toolkit");
        // Sizable, not Fixed: the list outgrew the screen. Windows caps a window at the desktop
        // working area, so the last seven buttons -- Velocity model down to the report templates --
        // were laid out past the bottom edge and simply never painted, with no grip to drag and no
        // wheel to scroll. Reported from a 3840x2160 screen showing 17 of the 24.
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        TopMost = false;

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
        };

        void Section(string title) => flow.Controls.Add(new Label
        {
            Text = title,
            UseMnemonic = false,   // section titles are plain text, not accelerator captions
            AutoSize = true,
            Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, flow.Controls.Count == 0 ? 0 : 12, 0, 4),
        });
        void B(string text, Tool t, string icon)
        {
            // GRT-coloured line icon in place of the emoji that used to open the caption: the label keeps its
            // translation key (emoji included) and the emoji is dropped here, so no translation had to change.
            int px = LogicalToDeviceUnits(22);
            var normal = IconBitmap(icon, false, px);
            var hot = IconBitmap(icon, true, px);
            var b = new Button
            {
                Text = LauncherCaption.From(text), UseMnemonic = false, Height = 34, Margin = new Padding(0, 0, 0, 6),
                TextAlign = ContentAlignment.MiddleLeft, Image = normal, ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(8, 0, 0, 0),
            };
            b.MouseEnter += (_, _) => b.Image = hot;
            b.MouseLeave += (_, _) => b.Image = normal;
            b.GotFocus += (_, _) => b.Image = hot;
            b.LostFocus += (_, _) => b.Image = normal;
            b.Click += (_, _) => open(t);
            flow.Controls.Add(b);
        }
        void Divider() => flow.Controls.Add(new Panel { Height = 1, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 8, 0, 10) });

        // Above both sections, not inside either: it starts before the range (brass, card) and can
        // only finish after (calibration needs a real measured velocity) -- see WizardForm's own doc
        // comment for why it doesn't get filed under just one phase.
        B(Lang.T("🪄  Guided new load (wizard)"), Tool.Wizard, "wizard");

        Section(Lang.T("BEFORE THE RANGE"));
        B(Lang.T("🔧  Brass prep (case vol / seating / neck)"), Tool.Brass, "brass");
        B(Lang.T("⚖️  Seating force estimate (QC)"), Tool.SeatingForce, "force");
        B(Lang.T("🏷  Load card / label"), Tool.Label, "label");
        B(Lang.T("🎯🖨  Print ladder/OCW target"), Tool.TargetPrint, "target");

        Section(Lang.T("AFTER THE RANGE"));
        // Journal and Find best Ba lead this section on purpose: the journal is where every other
        // analysis here gets its data from, and Find best Ba is the one that reads it back most
        // directly -- both used to be tabs buried inside the Inventory window, easy to miss.
        B(Lang.T("📓  Load journal"), Tool.Journal, "journal");
        B(Lang.T("🔎  Find best Ba"), Tool.FindBa, "findba");
        B(Lang.T("🎯  Chronograph import (Athlon / Garmin)"), Tool.Athlon, "stopwatch");
        B(Lang.T("🎯📄  ShotMarker import (target + velocities)"), Tool.ShotMarker, "shotmarker");
        B(Lang.T("📐  Chronograph statistics"), Tool.ChronoStats, "stats");
        B(Lang.T("📈  Ladder / OCW analyzer"), Tool.Ocw, "ladder");
        B(Lang.T("📏  Seating-depth analyzer"), Tool.Seating, "ruler");
        B(Lang.T("🎚  Barrel calibration"), Tool.Cal, "sliders");
        B(Lang.T("🌡  Powder temp coefficients"), Tool.Temp, "thermo");
        B(Lang.T("🏆  Load leaderboard"), Tool.Leaderboard, "trophy");
        B(Lang.T("🧭  Distance workflow"), Tool.Workflow, "compass");
        B(Lang.T("🧪  Powder compare"), Tool.PowderCompare, "flask");
        B(Lang.T("📉  Velocity model (charge + temperature)"), Tool.VelocityModel, "curve");
        B(Lang.T("🔬  Advanced diagnostics"), Tool.Diagnostics, "pulse");
        B(Lang.T("🌡🎯  SD root-cause"), Tool.SdRootCause, "sdcause");
        B(Lang.T("🎯📐  Group analysis"), Tool.GroupAnalysis, "group");

        Divider();
        B(Lang.T("📒  Inventory"), Tool.Log, "box");
        B(Lang.T("📚  Load book export"), Tool.LoadBook, "books");
        B(Lang.T("📄  Install GRT report templates"), Tool.Reports, "doc");

        // One column width for all of them, measured rather than hardcoded. These are left-aligned
        // captions, and the Italian overlay runs to 277px where the English reads 235, so the fixed
        // 252 this used to carry clipped "Preparazione bossoli (volume / seating / colletto)" on an
        // Italian machine. Measuring is also what keeps it right under display scaling, where the
        // font grows and a hardcoded number does not.
        // The icon sits to the left of the caption, so its width and the gap around it come on top of the text.
        int iconRoom = LogicalToDeviceUnits(22 + 8 + 8);
        int column = Math.Max(MinColumnWidth, flow.Controls.OfType<Button>().Max(CaptionWidth) + CaptionAir + iconRoom);
        foreach (Control c in flow.Controls)
            if (c is Button or Panel) c.Width = column;

        // The flow keeps auto-sizing, so its PreferredSize is the height the whole list wants; this
        // host is what makes the overflow reachable once OnLoad clamps the window to the desktop.
        _flow = flow;
        Controls.Add(new Panel { Dock = DockStyle.Fill, AutoScroll = true, Controls = { flow } });
    }

    /// <summary>
    /// Sizes the window to the tool list, clamped to the desktop. Deliberately not in the
    /// constructor: there, <c>Height - ClientSize.Height</c> is an AdjustWindowRectEx estimate made
    /// without a window handle, and it came out 20px short of the real chrome -- enough to leave
    /// the window taller than the desktop all over again. By OnLoad the handle exists and the
    /// figure is the true one.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Clamp BEFORE assigning, never after. Windows caps the window at the working area but
        // ClientSize still reports back whatever it was handed, so asking for more than fits leaves
        // the Dock=Fill panel sized to the full content: it sees no overflow, shows no scrollbar,
        // and the surplus is clipped at the window edge where layout cannot see it. That is the
        // original bug, and a scroll host alone does not fix it.
        int chrome = Height - ClientSize.Height;
        int roomFor = Math.Max(240, Screen.FromControl(this).WorkingArea.Height - chrome);
        var wanted = _flow.PreferredSize;
        int height = Math.Min(Math.Min(wanted.Height, roomFor), MaxOpeningClientHeight);
        // Only widen for the scrollbar when there will actually be one, or it eats into the 252px
        // buttons and raises a horizontal bar underneath them.
        int width = wanted.Width + (height < wanted.Height ? SystemInformation.VerticalScrollBarWidth : 0);
        ClientSize = new Size(width, height);

        // Width floor only: narrower clips the buttons, shorter just scrolls. Reading the preferred
        // size rather than hardcoding one is also what keeps this right under display scaling,
        // where the fonts (and so the section labels) grow but the button heights do not. Note the
        // window now opens shorter than the list on purpose, so the scrollbar is there from the
        // start and the width already accounts for it.
        MinimumSize = new Size(Width, 240);

        // The window was centred at its small default size, so after growing its bottom edge can sit below the working
        // area (under the taskbar). Lift it back inside; see LauncherPlacement.
        var work = Screen.FromControl(this).WorkingArea;
        Top = LauncherPlacement.Top(work.Top, work.Bottom, Top, Height);
    }

    private readonly List<Bitmap> _icons = new();

    private Bitmap IconBitmap(string name, bool hover, int px)
    {
        using var ms = new MemoryStream(ToolIconPainter.RenderPng(name, hover, px));
        var bmp = new Bitmap(ms);
        _icons.Add(bmp);
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var b in _icons) b.Dispose();
        base.Dispose(disposing);
    }
}
