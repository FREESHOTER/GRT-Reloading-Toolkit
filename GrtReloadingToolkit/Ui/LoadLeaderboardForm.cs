using System.Globalization;
using GrtPluginKit.Grt;
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
/// </summary>
internal sealed class LoadLeaderboardForm : Form
{
    private readonly Db _db;
    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public LoadLeaderboardForm(Db db)
    {
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

    private void Run()
    {
        var u = GrtUnits.Current;
        string? caliber = _caliber.SelectedItem as string;
        bool anyCaliber = caliber is null || caliber == Lang.T("-- any --");

        var groups = _db.Journal()
            .Where(e => anyCaliber || string.Equals(e.Caliber, caliber, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (e.Caliber, e.PowderId, e.BulletId, Charge: Math.Round(e.ChargeGr, 2)));

        var componentNames = _db.Components(includeArchived: true).ToDictionary(c => c.Id, c => c.Display);

        var rows = new List<(string Caliber, string Powder, string Bullet, double ChargeGr, int Sessions, int Rounds,
            double? Sd, double? Es, double? Group, LoadScoring.Breakdown Score)>();

        foreach (var g in groups)
        {
            var entries = g.ToList();
            double? avgSd = Avg(entries.Select(e => e.SdMs));
            double? avgEs = Avg(entries.Select(e => e.EsMs));
            double? avgGroup = Avg(entries.Select(e => e.GroupMoa));
            int totalRounds = entries.Sum(e => e.Rounds);
            var sdAcrossSessions = entries.Where(e => e.SdMs.HasValue).Select(e => e.SdMs!.Value).ToList();
            var score = LoadScoring.LeaderboardScore(avgSd, avgEs, avgGroup, totalRounds, sdAcrossSessions);

            rows.Add((g.Key.Caliber,
                g.Key.PowderId is { } pid && componentNames.TryGetValue(pid, out var pn) ? pn : Lang.T("-- any --"),
                g.Key.BulletId is { } bid && componentNames.TryGetValue(bid, out var bn) ? bn : Lang.T("-- any --"),
                g.Key.Charge, entries.Count, totalRounds, avgSd, avgEs, avgGroup, score));
        }

        var ranked = rows.Where(r => r.Score.Total.HasValue).OrderByDescending(r => r.Score.Total)
            .Concat(rows.Where(r => !r.Score.Total.HasValue))
            .ToList();

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
        _status.Text = ranked.Count == 0
            ? Lang.T("No journal entries yet -- log a session first.")
            : string.Format(Lang.T("{0} load(s) ranked, best first."), ranked.Count(r => r.Score.Total.HasValue));

        static double? Avg(IEnumerable<double?> values)
        {
            var v = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            return v.Count == 0 ? null : v.Average();
        }
    }
}
