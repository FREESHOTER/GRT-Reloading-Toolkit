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
        void B(string text, Tool t)
        {
            var b = new Button { Text = text, UseMnemonic = false, Width = 252, Height = 34, Margin = new Padding(0, 0, 0, 6), TextAlign = ContentAlignment.MiddleLeft };
            b.Click += (_, _) => open(t);
            flow.Controls.Add(b);
        }
        void Divider() => flow.Controls.Add(new Panel { Width = 252, Height = 1, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 8, 0, 10) });

        // Above both sections, not inside either: it starts before the range (brass, card) and can
        // only finish after (calibration needs a real measured velocity) -- see WizardForm's own doc
        // comment for why it doesn't get filed under just one phase.
        B(Lang.T("🪄  Guided new load (wizard)"), Tool.Wizard);

        Section(Lang.T("BEFORE THE RANGE"));
        B(Lang.T("🔧  Brass prep (case vol / seating / neck)"), Tool.Brass);
        B(Lang.T("⚖️  Seating force estimate (QC)"), Tool.SeatingForce);
        B(Lang.T("🏷  Load card / label"), Tool.Label);

        Section(Lang.T("AFTER THE RANGE"));
        // Journal and Find best Ba lead this section on purpose: the journal is where every other
        // analysis here gets its data from, and Find best Ba is the one that reads it back most
        // directly -- both used to be tabs buried inside the Inventory window, easy to miss.
        B(Lang.T("📓  Load journal"), Tool.Journal);
        B(Lang.T("🔎  Find best Ba"), Tool.FindBa);
        B(Lang.T("🎯  Chronograph import (Athlon / Garmin)"), Tool.Athlon);
        B(Lang.T("📐  Chronograph statistics"), Tool.ChronoStats);
        B(Lang.T("📈  Ladder / OCW analyzer"), Tool.Ocw);
        B(Lang.T("📏  Seating-depth analyzer"), Tool.Seating);
        B(Lang.T("🎚  Barrel calibration"), Tool.Cal);
        B(Lang.T("🌡  Powder temp coefficients"), Tool.Temp);
        B(Lang.T("🏆  Load leaderboard"), Tool.Leaderboard);
        B(Lang.T("🧭  Distance workflow"), Tool.Workflow);
        B(Lang.T("🧪  Powder compare"), Tool.PowderCompare);
        B(Lang.T("📉  Velocity model (charge + temperature)"), Tool.VelocityModel);
        B(Lang.T("🔬  Advanced diagnostics"), Tool.Diagnostics);
        B(Lang.T("🌡🎯  SD root-cause"), Tool.SdRootCause);

        Divider();
        B(Lang.T("📒  Inventory"), Tool.Log);
        B(Lang.T("📄  Install GRT report templates"), Tool.Reports);

        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }
}
