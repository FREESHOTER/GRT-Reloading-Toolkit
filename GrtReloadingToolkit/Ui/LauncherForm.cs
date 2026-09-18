namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The one entry point every toolbar/menu click opens (see Program.ToolkitContext). Grouped by
/// where each tool sits in an actual reload-development sequence — plan the powder and prep brass,
/// go shoot, tune to the barrel, then wrap up the recipe — rather than a flat alphabetic list.
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

        Section(Lang.T("PLAN & PREPARE"));
        B(Lang.T("🔧  Brass prep (case vol / seating / neck)"), Tool.Brass);
        B(Lang.T("⚖️  Seating force estimate (QC)"), Tool.SeatingForce);

        Section(Lang.T("RANGE DAY"));
        B(Lang.T("🎯  Chronograph import (Athlon / Garmin)"), Tool.Athlon);
        B(Lang.T("📐  Chronograph statistics"), Tool.ChronoStats);
        B(Lang.T("📈  Ladder / OCW analyzer"), Tool.Ocw);
        B(Lang.T("📏  Seating-depth analyzer"), Tool.Seating);

        Section(Lang.T("TUNE TO YOUR BARREL"));
        B(Lang.T("🎚  Barrel calibration"), Tool.Cal);
        B(Lang.T("🌡  Powder temp coefficients"), Tool.Temp);

        Section(Lang.T("EVALUATE YOUR LOADS"));
        B(Lang.T("🏆  Load leaderboard"), Tool.Leaderboard);
        B(Lang.T("🧭  Distance workflow"), Tool.Workflow);

        Section(Lang.T("WRAP UP"));
        B(Lang.T("🏷  Load card / label"), Tool.Label);
        B(Lang.T("📒  Inventory & load journal"), Tool.Log);

        Divider();
        B(Lang.T("📄  Install GRT report templates"), Tool.Reports);

        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }
}
