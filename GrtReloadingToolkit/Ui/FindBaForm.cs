using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Searches the same journal rows the Journal window writes, filtered to one caliber+powder+bullet
/// combo (Ba/a0 from a different combo aren't comparable) and ranked by whichever "best" means for
/// the user: tightest group, closest velocity, closest temperature. On its own window now, so it
/// reloads its filter lists on Activated to pick up whatever the Journal window logged since.
/// </summary>
internal sealed class FindBaForm : Form
{
    private readonly Db _db;

    private readonly ComboBox _fbCaliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _fbPowder = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DisplayMember = "Text", ValueMember = "Id" };
    private readonly ComboBox _fbBullet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DisplayMember = "Text", ValueMember = "Id" };
    private readonly ComboBox _fbSortBy = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Items = { Lang.T("Tightest group (MOA)"), Lang.T("Closest velocity to target"), Lang.T("Closest temperature to target") } };
    private readonly NumericUpDown _fbTargetMv = new() { DecimalPlaces = 0, Maximum = 3000, Width = 70 };
    private readonly NumericUpDown _fbTargetTemp = new() { DecimalPlaces = 1, Minimum = -40, Maximum = 60, Width = 70 };
    private readonly ComboBox _fbTargetTempUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50, Items = { "C", "F" } };
    private readonly Panel _fbTargetMvPanel = new() { AutoSize = true };
    private readonly Panel _fbTargetTempPanel = new() { AutoSize = true };
    private readonly ComboBox _fbYAxis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Items = { "Ba", "MV m/s", Lang.T("Group MOA") } };
    private readonly DataGridView _fb = new();
    private readonly FindBaChart _fbChart = new();
    private List<JournalEntry> _fbLastResults = new();
    private readonly Label _fbStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public FindBaForm(Db db)
    {
        _db = db;
        Text = AppVersion.Title(Lang.T("Find best Ba"));
        Width = 1080; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 480);

        // WrapContents=true can split a label from its own control onto different lines if they're
        // added as separate top-level children -- caught by rendering the real tab (the "Rank by"
        // label stranded on the row above its own dropdown), not from reading the code. Grouping
        // each label+control pair into its own little FlowLayoutPanel makes the pair wrap together.
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 0, 0), WrapContents = true };
        Control Group(string label, params Control[] ctls)
        {
            var g = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            foreach (var c in ctls) { c.Margin = new Padding(0, 0, 4, 0); g.Controls.Add(c); }
            return g;
        }

        bar.Controls.Add(Group(Lang.T("Caliber"), _fbCaliber));
        bar.Controls.Add(Group(Lang.T("Powder"), _fbPowder));
        bar.Controls.Add(Group(Lang.T("Bullet"), _fbBullet));
        bar.Controls.Add(Group(Lang.T("Rank by"), _fbSortBy));
        bar.Controls.Add(Group(Lang.T("Plot vs temperature"), _fbYAxis));

        // Both target boxes take a number the user reads off the grid beside them, so they take it
        // in the grid's unit: an ft/s shooter typing 2900 means 2900 ft/s, not 2900 m/s. The ceilings
        // are the same physical limits in whatever unit is showing -- 3000 m/s, and -40 C to 60 C,
        // whose Fahrenheit span is -40 to 140. Capped at 60 no Fahrenheit user could enter a warm day.
        var fbU = GrtUnits.Current;
        _fbTargetMv.Maximum = (decimal)Math.Ceiling(fbU.VelocityValue(3000));
        _fbTargetTemp.Minimum = -40;
        _fbTargetTemp.Maximum = 140;
        var targetMvGroup = (FlowLayoutPanel)Group(Lang.T("Target MV") + " " + fbU.VelocityUnitName, _fbTargetMv);
        _fbTargetMvPanel.Controls.Add(targetMvGroup);
        bar.Controls.Add(_fbTargetMvPanel);

        var targetTempGroup = (FlowLayoutPanel)Group(Lang.T("Target temp"), _fbTargetTemp, _fbTargetTempUnit);
        _fbTargetTempPanel.Controls.Add(targetTempGroup);
        bar.Controls.Add(_fbTargetTempPanel);

        var searchBtn = new Button { Text = Lang.T("Search"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        searchBtn.Click += (_, _) => RunFindBa();
        bar.Controls.Add(searchBtn);

        _fbSortBy.SelectedIndex = 0;
        _fbSortBy.SelectedIndexChanged += (_, _) => UpdateFindBaTargetVisibility();
        _fbTargetTempUnit.SelectedIndex = fbU.TemperatureInF ? 1 : 0;
        _fbYAxis.SelectedIndex = 0;
        _fbYAxis.SelectedIndexChanged += (_, _) => UpdateFindBaChart();
        UpdateFindBaTargetVisibility();

        _fb.Dock = DockStyle.Fill;
        _fb.ReadOnly = true; _fb.AllowUserToAddRows = false; _fb.RowHeadersVisible = false;
        _fb.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _fb.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // Every column that carries a unit names it in the header and prints a bare number in the
        // cell, the way the rest of the toolkit does -- and names the unit GRT is configured for,
        // not the one the numbers happen to be stored in.
        foreach (var (n, h) in new[] { ("date", Lang.T("Date")), ("load", Lang.T("Load")), ("chg", Lang.T("Charge") + " " + fbU.ChargeUnitName), ("ba", "Ba"), ("a0", "a0"),
                     ("mv", "MV " + fbU.VelocityUnitName), ("sd", "SD " + fbU.VelocityUnitName), ("grp", Lang.T("Group MOA")),
                     ("temp", Lang.T("Temp") + " " + fbU.TemperatureUnitName), ("notes", Lang.T("Notes")) })
            _fb.Columns.Add(n, h);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(260);
        split.Panel1.Controls.Add(_fb);
        _fbChart.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_fbChart);

        _fbStatus.Dock = DockStyle.Bottom; _fbStatus.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(split);
        Controls.Add(_fbStatus);
        Controls.Add(bar);
        NudFix.ApplyTo(this);

        RefreshFindBaFilters();

        // A new/edited journal entry can introduce a caliber or component this search hasn't seen
        // yet -- that now happens in a different window, so pick it up whenever this one gets focus.
        Activated += (_, _) => RefreshFindBaFilters();
    }

    private void UpdateFindBaChart()
    {
        var axis = _fbYAxis.SelectedIndex switch
        {
            1 => FindBaChart.YAxis.Velocity,
            2 => FindBaChart.YAxis.Group,
            _ => FindBaChart.YAxis.Ba,
        };
        _fbChart.SetData(_fbLastResults, axis);
    }

    private void UpdateFindBaTargetVisibility()
    {
        _fbTargetMvPanel.Visible = _fbSortBy.SelectedIndex == 1;
        _fbTargetTempPanel.Visible = _fbSortBy.SelectedIndex == 2;
    }

    /// <summary>Repopulates the caliber/powder/bullet dropdowns from whatever the journal currently
    /// has calibrated entries for -- called on load and whenever the window regains focus, since a
    /// new entry can introduce a caliber or component this search hasn't seen yet.</summary>
    private void RefreshFindBaFilters()
    {
        string? keepCal = _fbCaliber.SelectedItem as string;
        _fbCaliber.Items.Clear();
        foreach (var c in _db.CalibratedCalibers()) _fbCaliber.Items.Add(c);
        if (keepCal != null && _fbCaliber.Items.Contains(keepCal)) _fbCaliber.SelectedItem = keepCal;
        else if (_fbCaliber.Items.Count > 0) _fbCaliber.SelectedIndex = 0;

        // The caliber list is a DropDownList fed only by journal entries that carry a Ba, so on a
        // journal written before the Ba column existed it comes up empty -- an unfillable box with
        // no way to type into it and, until this, nothing on screen saying why. Say it here rather
        // than only after a Search the user has no reason to press on an empty form.
        _fbStatus.Text = _fbCaliber.Items.Count == 0 ? NoBaHint : "";

        var powders = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Powder).ToList();
        var bullets = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Bullet).ToList();
        FillFbCombo(_fbPowder, powders);
        FillFbCombo(_fbBullet, bullets);
    }

    private static void FillFbCombo(ComboBox cb, List<Component> items)
    {
        long? keep = (cb.SelectedItem as FbItem)?.Id;
        cb.Items.Clear();
        cb.Items.Add(new FbItem(0, Lang.T("-- any --")));
        foreach (var c in items) cb.Items.Add(new FbItem(c.Id, c.Display));
        cb.SelectedIndex = 0;
        if (keep is { } id)
            for (int i = 0; i < cb.Items.Count; i++)
                if (((FbItem)cb.Items[i]!).Id == id) { cb.SelectedIndex = i; break; }
    }

    private sealed record FbItem(long Id, string Text) { public override string ToString() => Text; }

    private void RunFindBa()
    {
        if (_fbCaliber.SelectedItem is not string caliber)
        {
            _fbStatus.Text = NoBaHint;
            _fb.Rows.Clear();
            _fbLastResults = new List<JournalEntry>();
            UpdateFindBaChart();
            return;
        }
        long? powderId = (_fbPowder.SelectedItem as FbItem)?.Id is > 0 ? (_fbPowder.SelectedItem as FbItem)!.Id : null;
        long? bulletId = (_fbBullet.SelectedItem as FbItem)?.Id is > 0 ? (_fbBullet.SelectedItem as FbItem)!.Id : null;

        var u = GrtUnits.Current;
        var results = _db.FindCalibrations(caliber, powderId, bulletId);
        _fbLastResults = results;

        IEnumerable<JournalEntry> ranked = _fbSortBy.SelectedIndex switch
        {
            1 => results.Where(e => e.VelocityAvgMs.HasValue)
                        .OrderBy(e => Math.Abs(e.VelocityAvgMs!.Value - u.VelocityToMps((double)_fbTargetMv.Value))),
            2 => results.Where(e => e.TemperatureC.HasValue)
                        .OrderBy(e => Math.Abs(e.TemperatureC!.Value - TargetTempC())),
            _ => results.Where(e => e.GroupMoa.HasValue).OrderBy(e => e.GroupMoa),
        };
        var list = ranked.ToList();

        _fb.Rows.Clear();
        foreach (var e in list)
        {
            // Invariant digits under translated headers: this grid is read next to GRT's own
            // numbers, and a comma decimal on an Italian machine would not match them.
            string temp = e.TemperatureC is { } tc ? u.TemperatureValue(tc).ToString("0.0", CultureInfo.InvariantCulture) : "";
            int i = _fb.Rows.Add(e.Date, e.LoadName, u.ChargeValue(e.ChargeGr).ToString(u.ChargeFormat, CultureInfo.InvariantCulture),
                e.Ba?.ToString("0.######", CultureInfo.InvariantCulture) ?? "",
                e.A0?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                e.VelocityAvgMs is { } mv ? u.VelocityValue(mv).ToString("0", CultureInfo.InvariantCulture) : "",
                e.SdMs is { } sd ? u.VelocitySd(sd) : "",
                e.GroupMoa?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                temp, e.Notes);
            _fb.Rows[i].Tag = e;
        }
        _fbStatus.Text = list.Count == 0
            ? Lang.T("No calibrated entries match this caliber/powder/bullet combo yet.")
            : string.Format(Lang.T("{0} matching calibration(s), best first."), list.Count);
        UpdateFindBaChart();
    }

    /// <summary>Why the caliber list can be empty, and the three ways to fill it. Barrel Calibration
    /// writes Ba into the .grtload, never into the journal -- the old text sent the user there and
    /// they came back with the box still empty.</summary>
    private static string NoBaHint => Lang.T(
        "No journal entry carries a Ba yet, so there is no caliber to pick. In the Journal window, use "
        + "'Log from GRT' (it reads Ba from the load open in GRT), or 'Fill Ba from loads' for entries "
        + "logged before Ba was recorded, or open an entry and type it into 'Ba (calibrated)'.");

    private double TargetTempC() => _fbTargetTempUnit.SelectedIndex == 1 ? FToC((double)_fbTargetTemp.Value) : (double)_fbTargetTemp.Value;
    private static double FToC(double f) => (f - 32.0) * 5.0 / 9.0;
}
