namespace GrtSensitivityLab.Ui;

/// <summary>
/// GRT Sensitivity Lab's entry point. The name is deliberately a group name, not "What If Tool" --
/// this is where GRT-model diagnostics land that study GRT's own model in general rather than the
/// barrel specifically (that's GrtModelLab's job). Two tools so far: "What if" (substitute one
/// measured value, see the effect) and "Sensitivity sweep" (nudge every input by +/- a delta,
/// ranked by impact -- tells you which inputs are worth measuring precisely in the first place,
/// the natural companion question to "what if"). Ported from GrtModelLab's dormant
/// SensitivitySweep/*.cs, which stayed unreferenced there for the same reason.
/// </summary>
internal sealed class LauncherForm : Form
{
    public LauncherForm(Action<Tool> open)
    {
        Text = "GRT Sensitivity Lab";
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
            var b = new Button { Text = text, UseMnemonic = false, Width = 320, Height = 34, Margin = new Padding(0, 0, 0, 6), TextAlign = ContentAlignment.MiddleLeft };
            b.Click += (_, _) => open(t);
            flow.Controls.Add(b);
        }
        void Divider() => flow.Controls.Add(new Panel { Width = 320, Height = 1, BackColor = SystemColors.ControlDark, Margin = new Padding(0, 8, 0, 10) });

        Section("MEASURED-VALUE SUBSTITUTION");
        B("What if... (substitute a real measured value into GRT)", Tool.WhatIf);

        Section("SENSITIVITY RANKING");
        B("Sensitivity sweep (rank GRT inputs by impact)", Tool.Sweep);

        Divider();
        B("📄  Install GRT report templates", Tool.Reports);

        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }
}
