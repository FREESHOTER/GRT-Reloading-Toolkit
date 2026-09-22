namespace GrtReloadingToolkit.Ui;

/// <summary>
/// A checklist for building a load from scratch, front to back: opens the real tool for each step
/// (never reimplements one) and ticks the step once you've launched it from here. It cannot tell
/// whether a step's own work is actually finished -- this window watches nothing in GRT or the
/// database -- so a checked box means "opened from here", a plain progress marker, not a verified
/// completion.
///
/// Two paths, chosen up front, because they diverge on when a card/calibration even makes sense:
/// a single known charge gets its card printed BEFORE the range (you already know what you're
/// loading) and calibrates after; a ladder brings several charges to the range with no card yet --
/// card and calibration only make sense AFTER Ladder/OCW has picked the one charge (the flat-spot
/// node) the rest of the load gets built around. Both paths still sit above the launcher's own two
/// phases rather than inside either (see <see cref="LauncherForm"/>'s own doc comment): the single
/// path starts before the range and finishes after, and the ladder path is entirely an after-the-
/// range sequence except for its shared first step.
/// </summary>
internal sealed class WizardForm : Form
{
    private readonly Action<Tool> _open;
    private readonly FlowLayoutPanel _steps = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
    };

    public WizardForm(Action<Tool> open)
    {
        _open = open;
        Text = AppVersion.Title(Lang.T("Guided New Load"));
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;

        var outer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 12, 14, 12),
        };

        outer.Controls.Add(new Label
        {
            Text = Lang.T("Pick what you're taking to the range: one known charge, or a ladder of several to find the right one."),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 8),
        });

        var single = new RadioButton { Text = Lang.T("Single load"), Checked = true, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
        var ladder = new RadioButton { Text = Lang.T("Ladder test"), AutoSize = true };
        var modeRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        modeRow.Controls.Add(single);
        modeRow.Controls.Add(ladder);
        outer.Controls.Add(modeRow);
        single.CheckedChanged += (_, _) => { if (single.Checked) BuildSteps(ladder: false); };
        ladder.CheckedChanged += (_, _) => { if (ladder.Checked) BuildSteps(ladder: true); };

        outer.Controls.Add(_steps);
        Controls.Add(outer);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        BuildSteps(ladder: false);
    }

    private void BuildSteps(bool ladder)
    {
        _steps.Controls.Clear();

        void Step(string title, string detail, Tool tool)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 10) };
            var check = new CheckBox { AutoCheck = false, Enabled = false, Margin = new Padding(0, 6, 8, 0) };
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            text.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            text.Controls.Add(new Label { Text = detail, AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = SystemColors.GrayText });
            var btn = new Button { Text = Lang.T("Open"), AutoSize = true, Margin = new Padding(8, 4, 0, 0) };
            btn.Click += (_, _) => { _open(tool); check.Checked = true; };
            row.Controls.Add(check); row.Controls.Add(text); row.Controls.Add(btn);
            _steps.Controls.Add(row);
        }

        Step(Lang.T("1. Brass prep"), Lang.T("Case volume, seating depth, neck sizing -- done at the bench before you leave."), Tool.Brass);

        if (!ladder)
        {
            Step(Lang.T("2. Load card / label"), Lang.T("Print the recipe card or box label to take with you."), Tool.Label);
            Step(Lang.T("3. Barrel calibration"), Lang.T("Back from the range with a real measured velocity: match GRT's model to your barrel."), Tool.Cal);
        }
        else
        {
            Step(Lang.T("2. Chronograph import"), Lang.T("Back from the range: bring in the ladder's velocities from your chronograph."), Tool.Athlon);
            Step(Lang.T("3. Ladder / OCW analyzer"), Lang.T("Find the flat spot / node -- the one charge the rest of this load gets built around."), Tool.Ocw);
            Step(Lang.T("4. Barrel calibration"), Lang.T("Calibrate on that one charge's own measured velocity."), Tool.Cal);
            Step(Lang.T("5. Load card / label"), Lang.T("Print the recipe card for the charge the ladder pointed at."), Tool.Label);
        }

        // AutoSize does not reliably shrink the form back down on its own when the ladder path's
        // 5 rows are replaced by the single path's 3 -- toggling it forces a full recompute.
        AutoSize = false;
        AutoSize = true;
    }
}
