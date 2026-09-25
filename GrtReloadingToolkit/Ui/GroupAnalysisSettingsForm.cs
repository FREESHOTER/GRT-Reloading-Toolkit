using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Lets a user edit <see cref="GroupAnalysis"/>'s thresholds instead of living with a general-purpose
/// default forever — same reasoning, and the same grid-per-curve shape, as
/// <see cref="LoadScoringSettingsForm"/>. Everything here is already unit-independent (MOA, or a
/// plain shot count), so unlike that form there is no GrtUnits conversion to do. One tab per
/// <see cref="DistanceBand"/> rather than a fixed short/long pair, since the bands themselves are a
/// list the user could in principle resize (not exposed here — count is fixed at 6, only each band's
/// own numbers are editable — resizing the list is a bigger UI than this settings dialog needs to be).
/// </summary>
internal sealed class GroupAnalysisSettingsForm : Form
{
    private sealed class BandEditor
    {
        public required DataGridView ScoreGrid;
        public required NumericUpDown MediumAt;
        public required NumericUpDown HighAt;
        public required CheckBox WindCaveat;
        public double MaxDistanceM;
    }

    private readonly List<BandEditor> _bandEditors = new();
    private readonly NumericUpDown _minShotsOutliers = new() { Minimum = 3, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _offCenter = new() { Minimum = 0, Maximum = 20, DecimalPlaces = 2, Increment = 0.1M, Width = 60 };
    private readonly NumericUpDown _spreadRatio = new() { Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = 0.1M, Width = 60 };
    private readonly NumericUpDown _appCenterMismatch = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 2, Increment = 0.05M, Width = 60 };
    private readonly NumericUpDown _velocityCorrelation = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Width = 60 };
    private readonly NumericUpDown _windNotLoad = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 3, Increment = 0.01M, Width = 70 };
    private readonly TabControl _bandTabs = new() { Dock = DockStyle.Fill };
    private GroupAnalysisConfig _working = null!;

    public GroupAnalysisSettingsForm()
    {
        Text = AppVersion.Title(Lang.T("Group Analysis Thresholds"));
        Width = 620; Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(500, 520);

        _working = Clone(GroupAnalysis.Config);
        BuildBandTabs(_working);

        var other = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        void Row(string label, Control c) { other.RowCount++; other.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 6, 0) }); other.Controls.Add(c); }
        Row(Lang.T("Minimum shots for outlier detection"), _minShotsOutliers);
        Row(Lang.T("Off-centre flag threshold (MOA)"), _offCenter);
        Row(Lang.T("Spread-ratio skew flag threshold"), _spreadRatio);
        Row(Lang.T("App-centre mismatch flag threshold (MOA)"), _appCenterMismatch);
        Row(Lang.T("Velocity <-> dispersion correlation flag threshold (|r|)"), _velocityCorrelation);
        Row(Lang.T("\"Load is done, it's wind now\" vertical spread threshold (MOA)"), _windNotLoad);
        PopulateOther(_working);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = Lang.T("Save"), AutoSize = true };
        var reset = new Button { Text = Lang.T("Reset to defaults"), AutoSize = true, Margin = new Padding(20, 3, 0, 3) };
        save.Click += (_, _) =>
        {
            CommitBandsTo(_working);
            CommitOtherTo(_working);
            GroupAnalysis.Config = _working;
            _working.Save();
            DialogResult = DialogResult.OK; Close();
        };
        reset.Click += (_, _) =>
        {
            _working = GroupAnalysisConfig.Default;
            BuildBandTabs(_working);
            PopulateOther(_working);
        };
        bar.Controls.Add(cancel); bar.Controls.Add(save); bar.Controls.Add(reset);

        var note = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 92, Padding = new Padding(8, 6, 8, 0),
            ForeColor = SystemColors.GrayText,
            Text = Lang.T("Each score row means \"at or below this mean-radius MOA, this score\". The last row is the floor for anything worse. A group's own distance picks its band automatically; only the 100 m and 1000 m sample-size minimums are anchored to anything (this tool's own original default, and the practical minimum a competitive shooter said he trusts) — the steps between are a plain interpolation, not independently sourced."),
        };

        Controls.Add(_bandTabs);
        Controls.Add(note);
        Controls.Add(other);
        Controls.Add(bar);
        AcceptButton = save; CancelButton = cancel;
    }

    private void BuildBandTabs(GroupAnalysisConfig cfg)
    {
        _bandTabs.TabPages.Clear();
        _bandEditors.Clear();
        var u = GrtPluginKit.Grt.GrtUnits.Current;
        foreach (var band in cfg.DistanceBands.OrderBy(b => b.MaxDistanceM))
            _bandTabs.TabPages.Add(BuildBandTab(u.Distance(band.MaxDistanceM), band));
    }

    private TabPage BuildBandTab(string title, DistanceBand band)
    {
        var page = new TabPage(title);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grid = new DataGridView { Dock = DockStyle.Fill };
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("value", Lang.T("Up to") + " MOA");
        grid.Columns.Add("score", Lang.T("Score"));
        Populate(grid, band.ScoreTiers);
        layout.Controls.Add(grid, 0, 0);

        var confPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        var mediumAt = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 60, Value = band.MediumAt };
        var highAt = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 60, Value = band.HighAt };
        confPanel.Controls.Add(new Label { Text = Lang.T("Confidence = Medium at"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        confPanel.Controls.Add(mediumAt);
        confPanel.Controls.Add(new Label { Text = Lang.T("shots, High at"), AutoSize = true, Margin = new Padding(8, 6, 4, 0) });
        confPanel.Controls.Add(highAt);
        confPanel.Controls.Add(new Label { Text = Lang.T("shots"), AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        layout.Controls.Add(confPanel, 0, 1);

        var windCaveat = new CheckBox { Text = Lang.T("Show the uncorrected-wind caveat at this distance"), AutoSize = true, Checked = band.WindCaveat, Margin = new Padding(0, 4, 0, 6) };
        layout.Controls.Add(windCaveat, 0, 2);

        page.Controls.Add(layout);
        _bandEditors.Add(new BandEditor { ScoreGrid = grid, MediumAt = mediumAt, HighAt = highAt, WindCaveat = windCaveat, MaxDistanceM = band.MaxDistanceM });
        return page;
    }

    private static void Populate(DataGridView grid, List<ScoreTier> tiers)
    {
        grid.Rows.Clear();
        for (int i = 0; i < tiers.Count; i++)
        {
            bool last = i == tiers.Count - 1;
            string valueText = last ? Lang.T("(worse than all above)") : tiers[i].Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            int row = grid.Rows.Add(valueText, tiers[i].Score.ToString("0.#", CultureInfo.InvariantCulture));
            if (last) grid.Rows[row].Cells[0].ReadOnly = true;
        }
    }

    private void PopulateOther(GroupAnalysisConfig cfg)
    {
        _minShotsOutliers.Value = cfg.MinShotsForOutliers;
        _offCenter.Value = (decimal)cfg.OffCenterThresholdMoa;
        _spreadRatio.Value = (decimal)cfg.SpreadRatioThreshold;
        _appCenterMismatch.Value = (decimal)cfg.AppCenterMismatchThresholdMoa;
        _velocityCorrelation.Value = (decimal)cfg.VelocityCorrelationThreshold;
        _windNotLoad.Value = (decimal)cfg.WindNotLoadVerticalThresholdMoa;
    }

    private void CommitBandsTo(GroupAnalysisConfig cfg)
    {
        var fallbackBands = cfg.DistanceBands.OrderBy(b => b.MaxDistanceM).ToList();
        var updated = new List<DistanceBand>(_bandEditors.Count);
        for (int i = 0; i < _bandEditors.Count; i++)
        {
            var e = _bandEditors[i];
            updated.Add(new DistanceBand(
                e.MaxDistanceM,
                ReadGrid(e.ScoreGrid, fallbackBands[i].ScoreTiers),
                (int)e.MediumAt.Value,
                (int)e.HighAt.Value,
                e.WindCaveat.Checked));
        }
        cfg.DistanceBands = updated;
    }

    private void CommitOtherTo(GroupAnalysisConfig cfg)
    {
        cfg.MinShotsForOutliers = (int)_minShotsOutliers.Value;
        cfg.OffCenterThresholdMoa = (double)_offCenter.Value;
        cfg.SpreadRatioThreshold = (double)_spreadRatio.Value;
        cfg.AppCenterMismatchThresholdMoa = (double)_appCenterMismatch.Value;
        cfg.VelocityCorrelationThreshold = (double)_velocityCorrelation.Value;
        cfg.WindNotLoadVerticalThresholdMoa = (double)_windNotLoad.Value;
    }

    /// <summary>Tolerant of a cell that won't parse (keeps that tier's previous value rather than
    /// crashing on Save), and accepts either decimal separator -- same trick as LoadScoringSettingsForm.</summary>
    private static List<ScoreTier> ReadGrid(DataGridView grid, List<ScoreTier> fallback)
    {
        var result = new List<ScoreTier>(fallback.Count);
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            double threshold = fallback[i].Threshold;
            bool last = i == grid.Rows.Count - 1;
            if (!last && TryParseCell(grid.Rows[i].Cells[0].Value, out double shown)) threshold = shown;
            double score = TryParseCell(grid.Rows[i].Cells[1].Value, out double s) ? Math.Clamp(s, 0.0, 10.0) : fallback[i].Score;
            result.Add(new ScoreTier(threshold, score));
        }
        return result;
    }

    private static bool TryParseCell(object? cell, out double value) =>
        double.TryParse(Convert.ToString(cell)?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static GroupAnalysisConfig Clone(GroupAnalysisConfig src) => new()
    {
        DistanceBands = src.DistanceBands
            .Select(b => new DistanceBand(b.MaxDistanceM, new List<ScoreTier>(b.ScoreTiers), b.MediumAt, b.HighAt, b.WindCaveat))
            .ToList(),
        MinShotsForOutliers = src.MinShotsForOutliers,
        OffCenterThresholdMoa = src.OffCenterThresholdMoa,
        SpreadRatioThreshold = src.SpreadRatioThreshold,
        AppCenterMismatchThresholdMoa = src.AppCenterMismatchThresholdMoa,
        VelocityCorrelationThreshold = src.VelocityCorrelationThreshold,
        WindNotLoadVerticalThresholdMoa = src.WindNotLoadVerticalThresholdMoa,
    };
}
