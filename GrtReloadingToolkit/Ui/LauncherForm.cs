namespace GrtReloadingToolkit.Ui;

internal sealed class LauncherForm : Form
{
    public LauncherForm(Action<Tool> open)
    {
        Text = "Reloading Toolkit";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(260, 388);
        TopMost = false;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
        Button B(string text, Tool t)
        {
            var b = new Button { Text = text, Width = 224, Height = 34, Margin = new Padding(0, 0, 0, 6), TextAlign = ContentAlignment.MiddleLeft };
            b.Click += (_, _) => open(t);
            flow.Controls.Add(b);
            return b;
        }
        B("🎯  Chronograph import (Athlon / Garmin)", Tool.Athlon);
        B("📈  Ladder / OCW analyzer", Tool.Ocw);
        B("📏  Seating-depth analyzer", Tool.Seating);
        B("🎚  Barrel calibration", Tool.Cal);
        B("🌡  Powder temp coefficients", Tool.Temp);
        B("🔧  Brass prep (case vol / neck)", Tool.Brass);
        B("🏷  Load card / label", Tool.Label);
        B("📒  Inventory & load journal", Tool.Log);
        B("📄  Install GRT report templates", Tool.Reports);
        Controls.Add(flow);
    }
}
