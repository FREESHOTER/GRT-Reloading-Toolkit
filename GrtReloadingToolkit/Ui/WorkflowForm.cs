using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// "Workflow Distanze" from Ballistic Lab v2, ported: after testing several charges at one distance
/// (say 100 m), ranks them by single-session quality to decide which charge(s) earn a test at the
/// next distance (say 300 m) or a seating-depth follow-up. Same grouping as
/// <see cref="LoadLeaderboardForm"/> (caliber + powder + bullet + charge), but scoped to ONE
/// distance instead of aggregated across every distance ever tested, and scored with
/// <see cref="LoadScoring.CompositeQualityScore"/> rather than <see cref="LoadScoring.LeaderboardScore"/>
/// -- the source's own reasoning (see its <c>core/workflow.py</c> docstring) is that a same-day
/// distance decision shouldn't fold in cross-session consistency, which answers a different question
/// ("has this load been reliable over time?") than "which of today's charges looked best?". Groups
/// tested fewer than <see cref="MinRoundsForRanking"/> rounds are left out of the ranking entirely
/// (not just scored low), matching the source's own <c>min_shots</c> cutoff -- a workflow decision
/// is exactly the place NOT to trust a two-shot group.
/// </summary>
internal sealed class WorkflowForm : Form
{
    private const string NoteTitle = "Distance Workflow";
    private const int MinRoundsForRanking = WorkflowRanking.MinRoundsForRanking;

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _distance = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    /// <summary>A <see cref="WorkflowRanking.Row"/> plus the display names <see cref="Ui"/> needs
    /// and the Log/ class doesn't know about.</summary>
    private sealed record WorkflowRow(WorkflowRanking.Row Row, string Powder, string Bullet)
    {
        public long? PowderId => Row.PowderId;
        public long? BulletId => Row.BulletId;
        public double ChargeGr => Row.ChargeGr;
        public int Sessions => Row.Sessions;
        public int Rounds => Row.Rounds;
        public double? Sd => Row.Sd;
        public double? Es => Row.Es;
        public double? Group => Row.Group;
        public LoadScoring.Breakdown Score => Row.Score;
    }

    /// <summary>A distance shown in <see cref="_distance"/>'s own unit, carrying the metres value
    /// the query actually groups on.</summary>
    private sealed class DistanceItem
    {
        public double Meters;
        public override string ToString() => GrtUnits.Current.Distance(Meters);
    }

    public WorkflowForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Distance Workflow"));
        Width = 940; Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 360);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Control Group(string label, params Control[] ctls)
        {
            var g = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            foreach (var c in ctls) { c.Margin = new Padding(0, 0, 4, 0); g.Controls.Add(c); }
            return g;
        }
        bar.Controls.Add(Group(Lang.T("Caliber"), _caliber));
        bar.Controls.Add(Group(Lang.T("Distance"), _distance));
        var rankBtn = new Button { Text = Lang.T("Rank"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        rankBtn.Click += (_, _) => Run();
        bar.Controls.Add(rankBtn);
        var settingsBtn = new Button { Text = Lang.T("⚙ Customize thresholds"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        settingsBtn.Click += (_, _) => { using var d = new LoadScoringSettingsForm(); if (d.ShowDialog(this) == DialogResult.OK) Run(); };
        bar.Controls.Add(settingsBtn);
        var writeBtn = new Button { Text = Lang.T("Write workflow note to GRT load"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        bar.Controls.Add(writeBtn);

        _caliber.SelectedIndexChanged += (_, _) => RefreshDistances();

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        var u = GrtUnits.Current;
        foreach (var (n, h) in new[] { ("rank", "#"), ("pw", Lang.T("Powder")), ("bu", Lang.T("Bullet")),
                     ("chg", Lang.T("Charge") + " " + u.ChargeUnitName), ("sess", Lang.T("Sessions")), ("rnd", Lang.T("Rounds")),
                     ("sd", "SD " + u.VelocityUnitName), ("es", "ES " + u.VelocityUnitName), ("grp", Lang.T("Group MOA")),
                     ("score", Lang.T("Score")), ("label", Lang.T("Rating")) })
            _grid.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom; _status.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(bar);
        NudFix.ApplyTo(this);

        RefreshCalibers();
    }

    private void RefreshCalibers()
    {
        string? keep = _caliber.SelectedItem as string;
        _caliber.Items.Clear();
        foreach (var c in _db.Journal().Select(e => e.Caliber).Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
            _caliber.Items.Add(c);
        if (_caliber.Items.Count == 0) return;
        _caliber.SelectedItem = keep != null && _caliber.Items.Contains(keep) ? keep : _caliber.Items[0];
    }

    /// <summary>Every distance actually logged for the selected caliber, nearest metre (matching
    /// how the OCW analyzer already buckets shot-group distances) so 99.6 m and 100.4 m count as
    /// the same range-day distance rather than splitting a load's data in two.</summary>
    private void RefreshDistances()
    {
        _distance.Items.Clear();
        if (_caliber.SelectedItem is not string caliber) return;
        var distances = _db.Journal()
            .Where(e => string.Equals(e.Caliber, caliber, StringComparison.OrdinalIgnoreCase) && e.DistanceM.HasValue)
            .Select(e => Math.Round(e.DistanceM!.Value))
            .Distinct()
            .OrderBy(d => d);
        foreach (var d in distances) _distance.Items.Add(new DistanceItem { Meters = d });
        if (_distance.Items.Count > 0) _distance.SelectedIndex = 0;
    }

    /// <summary>Wraps <see cref="WorkflowRanking.ComputeGroups"/> with the display names the grid
    /// and the write-back note need, which the Log/ class deliberately doesn't know about.</summary>
    private List<WorkflowRow> ComputeGroups(string caliber, double distanceM)
    {
        var componentNames = _db.Components(includeArchived: true).ToDictionary(c => c.Id, c => c.Display);
        // "— none —" rather than the filter dropdown's "-- any --" -- in a Powder/Bullet cell the
        // sentinel means "this entry names no component", which is the opposite of "any". Same fix
        // as LoadLeaderboardForm, which shares this grid shape.
        string Name(long? id) => id is { } i && componentNames.TryGetValue(i, out var n) ? n : Lang.T("— none —");

        return WorkflowRanking.ComputeGroups(_db.Journal(), caliber, distanceM)
            .Select(r => new WorkflowRow(r, Name(r.PowderId), Name(r.BulletId)))
            .ToList();
    }

    private List<WorkflowRow> Rank(IEnumerable<WorkflowRow> groups)
    {
        var byRow = groups.ToDictionary(r => r.Row);
        return WorkflowRanking.Rank(byRow.Keys).Select(r => byRow[r]).ToList();
    }

    private void Run()
    {
        if (_caliber.SelectedItem is not string caliber || _distance.SelectedItem is not DistanceItem dist)
        {
            _grid.Rows.Clear();
            _status.Text = Lang.T("No journal entries yet -- log a session first.");
            return;
        }

        var u = GrtUnits.Current;
        var groups = ComputeGroups(caliber, dist.Meters);
        var ranked = Rank(groups);

        _grid.Rows.Clear();
        int rank = 1;
        foreach (var r in ranked)
        {
            int i = _grid.Rows.Add(r.Score.Total.HasValue ? rank++.ToString() : "-",
                r.Powder, r.Bullet,
                u.ChargeValue(r.ChargeGr).ToString(u.ChargeFormat, CultureInfo.InvariantCulture),
                r.Sessions, r.Rounds,
                r.Sd is { } sd ? u.VelocitySd(sd) : "",
                r.Es is { } es ? u.VelocitySd(es) : "",
                r.Group?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                r.Score.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                Lang.T(LoadScoring.ScoreLabel(r.Score.Total)));
            _grid.Rows[i].Tag = r;
        }

        int excluded = groups.Count - ranked.Count;
        _status.Text = ranked.Count == 0
            ? string.Format(Lang.T("No charges with at least {0} rounds logged at this distance yet."), MinRoundsForRanking)
            : excluded == 0
                ? string.Format(Lang.T("{0} charge(s) ranked at this distance, best first."), ranked.Count)
                : string.Format(Lang.T("{0} charge(s) ranked, best first ({1} excluded, fewer than {2} rounds)."),
                    ranked.Count, excluded, MinRoundsForRanking);
    }

    /// <summary>
    /// Writes the currently-open GRT load's own placement among charges tested at whichever
    /// distance is selected above into a note. Distance can't be read off a <c>.grtload</c> at all
    /// (GRT has no such field -- only the Journal does, as a manual entry), so unlike
    /// <see cref="LoadLeaderboardForm"/>'s caliber (read straight from the file), the distance this
    /// writes against is always whatever the combo box is currently set to.
    /// </summary>
    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            if (_distance.SelectedItem is not DistanceItem dist)
            {
                MessageBox.Show(this, Lang.T("Pick a caliber and distance above first."), Lang.T("Write workflow note to GRT load"));
                return;
            }

            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write workflow note to GRT load"));
                return;
            }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            string caliber = doc.CaliberName;
            long? powderId = _db.ResolvePowderId(doc.PropellantName);
            long? bulletId = _db.ResolveBulletId(doc.ProjectileName);
            double chargeGr = Math.Round(doc.PropellantChargeGr ?? 0, 2);

            var groups = ComputeGroups(caliber, dist.Meters);
            var match = groups.FirstOrDefault(r => r.PowderId == powderId && r.BulletId == bulletId && Math.Abs(r.ChargeGr - chargeGr) < 0.005);
            if (match is null)
            {
                MessageBox.Show(this,
                    string.Format(Lang.T("This load hasn't been logged at {0} yet, so it has no workflow entry to write. Log it (Journal -> New / Log from GRT) first."), dist.ToString()),
                    Lang.T("Write workflow note to GRT load"));
                return;
            }
            if (match.Rounds < MinRoundsForRanking)
            {
                MessageBox.Show(this,
                    string.Format(Lang.T("Only {0} round(s) logged at {1} so far -- {2} are needed before this load is ranked."), match.Rounds, dist.ToString(), MinRoundsForRanking),
                    Lang.T("Write workflow note to GRT load"));
                return;
            }

            var ranked = Rank(groups);
            int idx = ranked.FindIndex(r => r.PowderId == match.PowderId && r.BulletId == match.BulletId && Math.Abs(r.ChargeGr - match.ChargeGr) < 0.005);
            int scoredCount = ranked.Count(r => r.Score.Total.HasValue);
            int rank = match.Score.Total.HasValue ? ranked.Take(idx + 1).Count(r => r.Score.Total.HasValue) : -1;
            var u = GrtUnits.Current;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            report.AppendLine();
            report.AppendLine(string.Format(Lang.T("Distance: {0}"), dist.ToString()));
            report.AppendLine(match.Score.Total.HasValue
                ? string.Format(Lang.T("Rank #{0} of {1} charges tested for {2} at this distance."), rank, scoredCount, caliber)
                : string.Format(Lang.T("Not enough data to score yet ({0} known charges for {1} at this distance)."), scoredCount, caliber));
            if (rank == 1)
                report.AppendLine(Lang.T("Top candidate to carry forward to the next distance."));
            report.AppendLine(string.Format(Lang.T("Score: {0} ({1})"),
                match.Score.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? Lang.T("N/A"),
                Lang.T(LoadScoring.ScoreLabel(match.Score.Total))));
            report.AppendLine();
            void Line(string label, double? sub, string detail) =>
                report.AppendLine($"  {label,-14} {(sub?.ToString("0.0", CultureInfo.InvariantCulture) ?? "--"),5}  {detail}");
            Line("SD", match.Score.Sd, match.Sd is { } sd ? u.Velocity(sd) : "--");
            Line("ES", match.Score.Es, match.Es is { } es ? u.Velocity(es) : "--");
            Line(Lang.T("Group MOA"), match.Score.Group, match.Group?.ToString("0.00", CultureInfo.InvariantCulture) + " MOA");
            Line(Lang.T("Sample size"), match.Score.SampleSize, match.Rounds + " " + Lang.T("Rounds"));

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, report.ToString());
            string outPath = writeDoc.SaveSibling("workflow");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Workflow note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write workflow note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
