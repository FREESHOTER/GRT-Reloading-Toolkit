using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Lets a user edit <see cref="GroupAnalysis"/>'s thresholds instead of living with a general-purpose
/// default forever — same reasoning, and the same grid-per-curve shape, as
/// <see cref="LoadScoringSettingsForm"/>. Everything here is already unit-independent (MOA, or a
/// plain shot count), so unlike that form there is no GrtUnits conversion to do.
/// </summary>
internal sealed class GroupAnalysisSettingsForm : Form
{
    private readonly DataGridView _shortGrid = new();
    private readonly DataGridView _longGrid = new();
    private readonly NumericUpDown _shortMedium = new() { Minimum = 1, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _shortHigh = new() { Minimum = 1, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _longMedium = new() { Minimum = 1, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _longHigh = new() { Minimum = 1, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _minShotsOutliers = new() { Minimum = 3, Maximum = 100, Width = 60 };
    private readonly NumericUpDown _offCenter = new() { Minimum = 0, Maximum = 20, DecimalPlaces = 2, Increment = 0.1M, Width = 60 };
    private readonly NumericUpDown _spreadRatio = new() { Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = 0.1M, Width = 60 };
    private readonly NumericUpDown _appCenterMismatch = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 2, Increment = 0.05M, Width = 60 };
    private readonly NumericUpDown _velocityCorrelation = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05M, Width = 60 };
    private GroupAnalysisConfig _working = null!;

    public GroupAnalysisSettingsForm()
    {
        Text = AppVersion.Title(Lang.T("Group Analysis Thresholds"));
        Width = 560; Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 500);

        _working = Clone(GroupAnalysis.Config);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTierTab(Lang.T("Short range score"), _shortGrid, _working.ShortRangeScoreTiers));
        tabs.TabPages.Add(BuildTierTab(Lang.T("Long range score"), _longGrid, _working.LongRangeScoreTiers));

        var other = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        void Row(string label, Control c) { other.RowCount++; other.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 6, 0) }); other.Controls.Add(c); }
        Row(Lang.T("Confidence = Medium at (short range) shots"), _shortMedium);
        Row(Lang.T("Confidence = High at (short range) shots"), _shortHigh);
        Row(Lang.T("Confidence = Medium at (long range) shots"), _longMedium);
        Row(Lang.T("Confidence = High at (long range) shots"), _longHigh);
        Row(Lang.T("Minimum shots for outlier detection"), _minShotsOutliers);
        Row(Lang.T("Off-centre flag threshold (MOA)"), _offCenter);
        Row(Lang.T("Spread-ratio skew flag threshold"), _spreadRatio);
        Row(Lang.T("App-centre mismatch flag threshold (MOA)"), _appCenterMismatch);
        Row(Lang.T("Velocity <-> dispersion correlation flag threshold (|r|)"), _velocityCorrelation);
        PopulateOther(_working);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = Lang.T("Save"), AutoSize = true };
        var reset = new Button { Text = Lang.T("Reset to defaults"), AutoSize = true, Margin = new Padding(20, 3, 0, 3) };
        save.Click += (_, _) =>
        {
            CommitGridsTo(_working);
            CommitOtherTo(_working);
            GroupAnalysis.Config = _working;
            _working.Save();
            DialogResult = DialogResult.OK; Close();
        };
        reset.Click += (_, _) =>
        {
            _working = GroupAnalysisConfig.Default;
            Populate(_shortGrid, _working.ShortRangeScoreTiers);
            Populate(_longGrid, _working.LongRangeScoreTiers);
            PopulateOther(_working);
        };
        bar.Controls.Add(cancel); bar.Controls.Add(save); bar.Controls.Add(reset);

        var note = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 36, Padding = new Padding(8, 6, 8, 0),
            ForeColor = SystemColors.GrayText,
            Text = Lang.T("Each score row means \"at or below this mean-radius MOA, this score\". The last row is the floor for anything worse."),
        };

        Controls.Add(tabs);
        Controls.Add(note);
        Controls.Add(other);
        Controls.Add(bar);
        AcceptButton = save; CancelButton = cancel;
    }

    private TabPage BuildTierTab(string title, DataGridView grid, List<ScoreTier> tiers)
    {
        var page = new TabPage(title);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("value", Lang.T("Up to") + " MOA");
        grid.Columns.Add("score", Lang.T("Score"));
        Populate(grid, tiers);
        page.Controls.Add(grid);
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
        _shortMedium.Value = cfg.ShortRangeMediumAt; _shortHigh.Value = cfg.ShortRangeHighAt;
        _longMedium.Value = cfg.LongRangeMediumAt; _longHigh.Value = cfg.LongRangeHighAt;
        _minShotsOutliers.Value = cfg.MinShotsForOutliers;
        _offCenter.Value = (decimal)cfg.OffCenterThresholdMoa;
        _spreadRatio.Value = (decimal)cfg.SpreadRatioThreshold;
        _appCenterMismatch.Value = (decimal)cfg.AppCenterMismatchThresholdMoa;
        _velocityCorrelation.Value = (decimal)cfg.VelocityCorrelationThreshold;
    }

    private void CommitGridsTo(GroupAnalysisConfig cfg)
    {
        cfg.ShortRangeScoreTiers = ReadGrid(_shortGrid, cfg.ShortRangeScoreTiers);
        cfg.LongRangeScoreTiers = ReadGrid(_longGrid, cfg.LongRangeScoreTiers);
    }

    private void CommitOtherTo(GroupAnalysisConfig cfg)
    {
        cfg.ShortRangeMediumAt = (int)_shortMedium.Value; cfg.ShortRangeHighAt = (int)_shortHigh.Value;
        cfg.LongRangeMediumAt = (int)_longMedium.Value; cfg.LongRangeHighAt = (int)_longHigh.Value;
        cfg.MinShotsForOutliers = (int)_minShotsOutliers.Value;
        cfg.OffCenterThresholdMoa = (double)_offCenter.Value;
        cfg.SpreadRatioThreshold = (double)_spreadRatio.Value;
        cfg.AppCenterMismatchThresholdMoa = (double)_appCenterMismatch.Value;
        cfg.VelocityCorrelationThreshold = (double)_velocityCorrelation.Value;
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
        ShortRangeMaxM = src.ShortRangeMaxM,
        ShortRangeScoreTiers = new List<ScoreTier>(src.ShortRangeScoreTiers),
        LongRangeScoreTiers = new List<ScoreTier>(src.LongRangeScoreTiers),
        ShortRangeMediumAt = src.ShortRangeMediumAt, ShortRangeHighAt = src.ShortRangeHighAt,
        LongRangeMediumAt = src.LongRangeMediumAt, LongRangeHighAt = src.LongRangeHighAt,
        MinShotsForOutliers = src.MinShotsForOutliers,
        OffCenterThresholdMoa = src.OffCenterThresholdMoa,
        SpreadRatioThreshold = src.SpreadRatioThreshold,
        AppCenterMismatchThresholdMoa = src.AppCenterMismatchThresholdMoa,
        VelocityCorrelationThreshold = src.VelocityCorrelationThreshold,
    };
}
