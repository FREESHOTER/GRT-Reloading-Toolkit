namespace GrtModelLab.Ui;

/// <summary>
/// GRT Model OBT's entry point. Scope is deliberately narrow: barrel physical data and the barrel
/// vibration model it feeds, including its GRT-charge-ladder sweep (a barrel-specific candidate
/// charge indicator, computed from this barrel's own FEM modes and GRT's real P(t)/v(t) curve — not
/// yet checked against real OCW/ladder/chronograph results). The GRT-input
/// sensitivity sweep (studies GRT's own model in general, not the barrel specifically) doesn't belong
/// here; it's meant for a separate "advanced diagnostics" plugin instead (not yet built) — see
/// GrtModelLab/SensitivitySweep/*.cs and Ui/SensitivitySweepForm.cs, kept in the tree unreferenced by
/// Program.cs, ready to move over when that plugin exists. This plugin is a companion to GRT, not a
/// critique of it — it adds barrel-specific physical data (steel, taper) that a vibration model
/// needs, alongside what GRT already models.
/// </summary>
internal sealed class LauncherForm : Form
{
    public LauncherForm(Action<Tool> open)
    {
        Text = "GRT Model OBT";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;

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
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, flow.Controls.Count == 0 ? 0 : 12, 0, 4),
        });
        void B(string text, Tool t)
        {
            var b = new Button { Text = text, UseMnemonic = false, Width = 300, Height = 34, Margin = new Padding(0, 0, 0, 6), TextAlign = ContentAlignment.MiddleLeft };
            b.Click += (_, _) => open(t);
            flow.Controls.Add(b);
        }

        Section("DATI FISICI CANNA");
        B("🔩  Profilo fisico canna (acciaio, rastremazione)", Tool.BarrelProfile);

        Section("MODELLO DI VIBRAZIONE");
        B("〰️  Modello di vibrazione canna", Tool.BarrelVibration);

        Section("RAPPORTO GRT");
        B("📄  Installa modelli di rapporto GRT", Tool.Reports);

        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }
}
