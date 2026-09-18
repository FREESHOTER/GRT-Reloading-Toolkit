using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Lets a user edit the SD/ES/Group-MOA score tiers <see cref="LoadScoring"/> reads, instead of
/// living with a general-purpose default forever -- a benchrest competitor and a hunter reasonably
/// want different thresholds for what counts as "excellent". Sample size and cross-session
/// consistency are NOT editable here: those are statistical-confidence curves with no external
/// benchmark to tune against, unlike SD/ES/group which are all anchored to a specific citation (see
/// <see cref="LoadScoringConfig"/>).
///
/// SD/ES are shown and typed in GRT's own configured velocity unit (m/s or ft/s), converting to/from
/// the metric value the config actually stores -- editing thresholds is exactly the kind of place a
/// unit mismatch would silently produce a nonsense scale, so it follows the same GrtUnits convention
/// as every other number in the Toolkit. Group MOA needs no such conversion; MOA is already
/// unit-independent.
/// </summary>
internal sealed class LoadScoringSettingsForm : Form
{
    private readonly GrtUnits _u = GrtUnits.Current;
    private readonly DataGridView _sdGrid = new();
    private readonly DataGridView _esGrid = new();
    private readonly DataGridView _groupGrid = new();
    private LoadScoringConfig _working = null!;

    public LoadScoringSettingsForm()
    {
        Text = AppVersion.Title(Lang.T("GRT Load Scoring Thresholds"));
        Width = 560; Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(460, 420);

        _working = Clone(LoadScoring.Config);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCurveTab("SD " + _u.VelocityUnitName, _sdGrid, _working.SdTiers, velocityTiers: true));
        tabs.TabPages.Add(BuildCurveTab("ES " + _u.VelocityUnitName, _esGrid, _working.EsTiers, velocityTiers: true));
        tabs.TabPages.Add(BuildCurveTab(Lang.T("Group MOA"), _groupGrid, _working.GroupMoaTiers, velocityTiers: false));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = Lang.T("Save"), AutoSize = true };
        var reset = new Button { Text = Lang.T("Reset to defaults"), AutoSize = true, Margin = new Padding(20, 3, 0, 3) };
        save.Click += (_, _) => { CommitGridsTo(_working); LoadScoring.Config = _working; _working.Save(); DialogResult = DialogResult.OK; Close(); };
        reset.Click += (_, _) =>
        {
            _working = LoadScoringConfig.Default;
            Populate(_sdGrid, _working.SdTiers, velocityTiers: true);
            Populate(_esGrid, _working.EsTiers, velocityTiers: true);
            Populate(_groupGrid, _working.GroupMoaTiers, velocityTiers: false);
        };
        bar.Controls.Add(cancel); bar.Controls.Add(save); bar.Controls.Add(reset);

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 36,
            Padding = new Padding(8, 6, 8, 0),
            ForeColor = SystemColors.GrayText,
            Text = Lang.T("Each row means \"at or below this value, this score\". The last row is the floor for anything worse than every other row."),
        };

        Controls.Add(tabs);
        Controls.Add(note);
        Controls.Add(bar);
        AcceptButton = save; CancelButton = cancel;
    }

    private TabPage BuildCurveTab(string title, DataGridView grid, List<ScoreTier> tiers, bool velocityTiers)
    {
        var page = new TabPage(title);
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add("value", velocityTiers ? Lang.T("Up to") + " " + _u.VelocityUnitName : Lang.T("Up to") + " MOA");
        grid.Columns.Add("score", Lang.T("Score"));
        Populate(grid, tiers, velocityTiers);
        page.Controls.Add(grid);
        return page;
    }

    private void Populate(DataGridView grid, List<ScoreTier> tiers, bool velocityTiers)
    {
        grid.Rows.Clear();
        for (int i = 0; i < tiers.Count; i++)
        {
            bool last = i == tiers.Count - 1;
            string valueText = last ? Lang.T("(worse than all above)")
                : (velocityTiers ? _u.VelocityValue(tiers[i].Threshold) : tiers[i].Threshold).ToString("0.###");
            int row = grid.Rows.Add(valueText, tiers[i].Score.ToString("0.#"));
            if (last) grid.Rows[row].Cells[0].ReadOnly = true;
        }
    }

    /// <summary>Reads whatever the user left in each grid back into a config -- tolerant of a cell
    /// that won't parse (keeps that tier's previous value rather than crashing on Save).</summary>
    private void CommitGridsTo(LoadScoringConfig cfg)
    {
        cfg.SdTiers = ReadGrid(_sdGrid, cfg.SdTiers, velocityTiers: true);
        cfg.EsTiers = ReadGrid(_esGrid, cfg.EsTiers, velocityTiers: true);
        cfg.GroupMoaTiers = ReadGrid(_groupGrid, cfg.GroupMoaTiers, velocityTiers: false);
    }

    private List<ScoreTier> ReadGrid(DataGridView grid, List<ScoreTier> fallback, bool velocityTiers)
    {
        var result = new List<ScoreTier>(fallback.Count);
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            double threshold = fallback[i].Threshold;
            bool last = i == grid.Rows.Count - 1;
            if (!last && double.TryParse(Convert.ToString(grid.Rows[i].Cells[0].Value), out double shown))
                threshold = velocityTiers ? _u.VelocityToMps(shown) : shown;
            double score = double.TryParse(Convert.ToString(grid.Rows[i].Cells[1].Value), out double s)
                ? Math.Clamp(s, 0.0, 10.0) : fallback[i].Score;
            result.Add(new ScoreTier(threshold, score));
        }
        return result;
    }

    private static LoadScoringConfig Clone(LoadScoringConfig src) => new()
    {
        SdTiers = new List<ScoreTier>(src.SdTiers),
        EsTiers = new List<ScoreTier>(src.EsTiers),
        GroupMoaTiers = new List<ScoreTier>(src.GroupMoaTiers),
    };
}
