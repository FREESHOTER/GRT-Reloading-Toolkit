using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Ranks every load ever logged in the Journal -- grouped by caliber+powder+bullet+charge, since
/// the same recipe tested across several range days is still one load, not several -- by a
/// composite 0-10 quality score. Reads the SAME SQLite database as Inventory &amp; Load Journal
/// (shares <see cref="Db"/>, no separate storage), but is its own top-level tool rather than a tab
/// buried in that already five-tab window -- ranking loads is a distinct question ("which of
/// everything I've tested is actually best?") from logging one session, and deserves its own place
/// in the launcher, not to be found by accident.
///
/// See <see cref="LoadScoring"/> for the score itself and where each threshold comes from. Unlike
/// "Find best Ba" (same journal data, different question: what Ba worked before for this exact
/// combo), a load doesn't need a calibrated Ba to be ranked here -- it only needs whichever of
/// SD/ES/group a session actually recorded.
///
/// Like every other analysis tool in the Toolkit, it can write its result back into the GRT load
/// that's open right now (see <see cref="WriteToGrtAsync"/>) -- this was missed in the first cut and
/// caught by the user, since it broke the pattern every other tool in the manual follows.
/// </summary>
internal sealed class LoadLeaderboardForm : Form
{
    private const string NoteTitle = "Load Leaderboard";

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private sealed record LeaderRow(string Caliber, long? PowderId, string Powder, long? BulletId, string Bullet,
        double ChargeGr, int Sessions, int Rounds, double? Sd, double? Es, double? Group, LoadScoring.Breakdown Score);

    public LoadLeaderboardForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Load Leaderboard"));
        Width = 1000; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 380);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Control Group(string label, params Control[] ctls)
        {
            var g = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            foreach (var c in ctls) { c.Margin = new Padding(0, 0, 4, 0); g.Controls.Add(c); }
            return g;
        }
        bar.Controls.Add(Group(Lang.T("Caliber"), _caliber));
        var rankBtn = new Button { Text = Lang.T("Rank"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        rankBtn.Click += (_, _) => Run();
        bar.Controls.Add(rankBtn);
        var settingsBtn = new Button { Text = Lang.T("⚙ Customize thresholds"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        settingsBtn.Click += (_, _) => { using var d = new LoadScoringSettingsForm(); if (d.ShowDialog(this) == DialogResult.OK) Run(); };
        bar.Controls.Add(settingsBtn);
        var writeBtn = new Button { Text = Lang.T("Write leaderboard note to GRT load"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        bar.Controls.Add(writeBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        var u = GrtUnits.Current;
        foreach (var (n, h) in new[] { ("rank", "#"), ("cal", Lang.T("Caliber")), ("pw", Lang.T("Powder")), ("bu", Lang.T("Bullet")),
                     ("chg", Lang.T("Charge") + " " + u.ChargeUnitName), ("sess", Lang.T("Sessions")), ("rnd", Lang.T("Rounds")),
                     ("sd", "SD " + u.VelocityUnitName), ("es", "ES " + u.VelocityUnitName), ("grp", Lang.T("Group MOA")),
                     ("score", Lang.T("Score")), ("label", Lang.T("Rating")) })
            _grid.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom; _status.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(bar);
        NudFix.ApplyTo(this);

        RefreshFilters();
        // Rank on open rather than waiting for the button: a window that comes up empty reads as
        // "nothing logged" when it only means "not run yet", and its own "no entries" message is
        // otherwise unreachable. Same fix as "Find best Ba" needed.
        Run();
    }

    /// <summary>Repopulates the caliber filter from whichever calibers the journal currently has
    /// entries for at all (unlike "Find best Ba", this isn't limited to Ba-bearing entries -- the
    /// composite score doesn't need a calibration, just SD/ES/group).</summary>
    private void RefreshFilters()
    {
        string? keep = _caliber.SelectedItem as string;
        _caliber.Items.Clear();
        _caliber.Items.Add(Lang.T("-- any --"));
        foreach (var c in _db.Journal().Select(e => e.Caliber).Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
            _caliber.Items.Add(c);
        _caliber.SelectedItem = keep != null && _caliber.Items.Contains(keep) ? keep : _caliber.Items[0];
    }

    /// <summary>
    /// Ranks every load matching <paramref name="caliberFilter"/> (or every load at all, if null or
    /// "-- any --") -- grouped by caliber+powder+bullet+charge. Shared by the on-screen grid and by
    /// <see cref="WriteToGrtAsync"/>, which always ranks within ONE caliber (the open load's own)
    /// regardless of whatever the grid happens to be filtered to at the time -- comparing a .223
    /// load's score against a .308 load's isn't a meaningful "rank" the way same-caliber comparison is.
    /// </summary>
    private List<LeaderRow> ComputeRanked(string? caliberFilter)
    {
        bool anyCaliber = caliberFilter is null || caliberFilter == Lang.T("-- any --");
        var groups = _db.Journal()
            .Where(e => anyCaliber || string.Equals(e.Caliber, caliberFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (e.Caliber, e.PowderId, e.BulletId, Charge: Math.Round(e.ChargeGr, 2)));

        var componentNames = _db.Components(includeArchived: true).ToDictionary(c => c.Id, c => c.Display);
        var rows = new List<LeaderRow>();

        foreach (var g in groups)
        {
            var entries = g.ToList();
            double? avgSd = Avg(entries.Select(e => e.SdMs));
            double? avgEs = Avg(entries.Select(e => e.EsMs));
            double? avgGroup = Avg(entries.Select(e => e.GroupMoa));
            int totalRounds = entries.Sum(e => e.Rounds);
            var sdAcrossSessions = entries.Where(e => e.SdMs.HasValue).Select(e => e.SdMs!.Value).ToList();
            var score = LoadScoring.LeaderboardScore(avgSd, avgEs, avgGroup, totalRounds, sdAcrossSessions);

            rows.Add(new LeaderRow(g.Key.Caliber, g.Key.PowderId,
                g.Key.PowderId is { } pid && componentNames.TryGetValue(pid, out var pn) ? pn : Lang.T("-- any --"),
                g.Key.BulletId,
                g.Key.BulletId is { } bid && componentNames.TryGetValue(bid, out var bn) ? bn : Lang.T("-- any --"),
                g.Key.Charge, entries.Count, totalRounds, avgSd, avgEs, avgGroup, score));
        }

        return rows.Where(r => r.Score.Total.HasValue).OrderByDescending(r => r.Score.Total)
            .Concat(rows.Where(r => !r.Score.Total.HasValue))
            .ToList();

        static double? Avg(IEnumerable<double?> values)
        {
            var v = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            return v.Count == 0 ? null : v.Average();
        }
    }

    private void Run()
    {
        var u = GrtUnits.Current;
        var ranked = ComputeRanked(_caliber.SelectedItem as string);

        _grid.Rows.Clear();
        int rank = 1;
        foreach (var r in ranked)
        {
            int i = _grid.Rows.Add(r.Score.Total.HasValue ? rank++.ToString() : "-",
                r.Caliber, r.Powder, r.Bullet,
                u.ChargeValue(r.ChargeGr).ToString(u.ChargeFormat, CultureInfo.InvariantCulture),
                r.Sessions, r.Rounds,
                r.Sd is { } sd ? u.VelocitySd(sd) : "",
                r.Es is { } es ? u.VelocitySd(es) : "",
                r.Group?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                r.Score.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                Lang.T(LoadScoring.ScoreLabel(r.Score.Total)));
            _grid.Rows[i].Tag = r;
        }
        // Three states, not two: a load with only a round count logged is listed but unranked (see
        // LoadScoring.Breakdown.Rankable), so "some rows, none scoreable" needs to say why rather
        // than report "0 load(s) ranked" over a full grid.
        int rankable = ranked.Count(r => r.Score.Total.HasValue);
        _status.Text = ranked.Count == 0 ? Lang.T("No journal entries yet -- log a session first.")
            : rankable == 0 ? Lang.T("Nothing to rank yet -- a load needs SD, ES or a group size logged, not just a round count.")
            : string.Format(Lang.T("{0} load(s) ranked, best first."), rankable);
    }

    /// <summary>
    /// Writes the currently-open GRT load's own leaderboard placement into a note -- same
    /// write-back pattern every other analysis tool in the Toolkit uses (timestamped sibling
    /// snapshot, never touches the original). Ranks within the open load's OWN caliber, independent
    /// of whatever the grid is currently filtered to.
    /// </summary>
    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write leaderboard note to GRT load"));
                return;
            }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            string caliber = doc.CaliberName;
            long? powderId = _db.ResolvePowderId(doc.PropellantName);
            long? bulletId = _db.ResolveBulletId(doc.ProjectileName);
            double chargeGr = Math.Round(doc.PropellantChargeGr ?? 0, 2);

            var ranked = ComputeRanked(caliber);
            int idx = ranked.FindIndex(r => r.PowderId == powderId && r.BulletId == bulletId && Math.Abs(r.ChargeGr - chargeGr) < 0.005);
            if (idx < 0)
            {
                MessageBox.Show(this,
                    Lang.T("This load hasn't been logged in the Journal yet, so it has no leaderboard entry to write. Log it (Journal -> New / Log from GRT) first."),
                    Lang.T("Write leaderboard note to GRT load"));
                return;
            }

            var row = ranked[idx];
            int scoredCount = ranked.Count(r => r.Score.Total.HasValue);
            int rank = row.Score.Total.HasValue ? ranked.Take(idx + 1).Count(r => r.Score.Total.HasValue) : -1;
            var u = GrtUnits.Current;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            report.AppendLine();
            report.AppendLine(row.Score.Total.HasValue
                ? string.Format(Lang.T("Rank #{0} of {1} loads tested for {2}."), rank, scoredCount, caliber)
                : string.Format(Lang.T("Not enough data to score yet ({0} known loads for {1})."), scoredCount, caliber));
            report.AppendLine(string.Format(Lang.T("Score: {0} ({1})"),
                row.Score.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? Lang.T("N/A"),
                Lang.T(LoadScoring.ScoreLabel(row.Score.Total))));
            report.AppendLine();
            void Line(string label, double? sub, string detail) =>
                report.AppendLine($"  {label,-14} {(sub?.ToString("0.0", CultureInfo.InvariantCulture) ?? "--"),5}  {detail}");
            Line("SD", row.Score.Sd, row.Sd is { } sd ? u.Velocity(sd) : "--");
            Line("ES", row.Score.Es, row.Es is { } es ? u.Velocity(es) : "--");
            Line(Lang.T("Group MOA"), row.Score.Group, row.Group?.ToString("0.00", CultureInfo.InvariantCulture) + " MOA");
            Line(Lang.T("Sample size"), row.Score.SampleSize, row.Rounds + " " + Lang.T("Rounds"));
            Line(Lang.T("Consistency"), row.Score.Consistency,
                row.Sessions + " " + (row.Sessions == 1 ? Lang.T("session") : Lang.T("Sessions")));

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, report.ToString());
            string outPath = writeDoc.SaveSibling("leaderboard");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Leaderboard note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write leaderboard note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
