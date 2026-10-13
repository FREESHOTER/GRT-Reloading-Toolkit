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
    public LauncherForm(Action<Tool> open)
    {
        Text = AppVersion.Title("Reloading Toolkit");
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
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
                Text = LauncherCaption.From(text), UseMnemonic = false, Width = 252, Height = 34, Margin = new Padding(0, 0, 0, 6),
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
        void Divider() => flow.Controls.Add(new Panel { Width = 252, Height = 1, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 8, 0, 10) });

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

        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
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
