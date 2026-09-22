using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Compares every powder ever logged in the Journal, one caliber at a time -- "of the powders I've
/// tried here, which tends to work out for me" rather than Load Leaderboard's "which exact recipe is
/// best" (see <see cref="PowderCompare"/> for the grouping difference). Same journal, same SQLite
/// database as every other Journal-backed tool, no separate storage.
///
/// This can't do what a "best powder" picker like QuickLoad's would: GRT's factory propellant
/// database is a single opaque binary file the plugin IPC has no call to enumerate (see
/// reference_grt_plugin_ipc), so a powder the journal has never seen simply isn't comparable here.
/// What IS comparable is powders you've actually calibrated, which is also the more grounded
/// question -- GRT's own predicted numbers for an untried powder are no more trustworthy than the
/// factory data the German-language GRT community has been complaining is stale since 2022.
/// </summary>
internal sealed class PowderCompareForm : Form
{
    private const string NoteTitle = "Powder Compare";

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public PowderCompareForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Powder Compare"));
        Width = 980; Height = 520;
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
        var compareBtn = new Button { Text = Lang.T("Compare"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        compareBtn.Click += (_, _) => Run();
        bar.Controls.Add(compareBtn);
        var writeBtn = new Button { Text = Lang.T("Write comparison note to GRT load"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        bar.Controls.Add(writeBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        var u = GrtUnits.Current;
        foreach (var (n, h) in new[] { ("rank", "#"), ("cal", Lang.T("Caliber")), ("pw", Lang.T("Powder")),
                     ("rec", Lang.T("Recipes")), ("sess", Lang.T("Sessions")), ("rnd", Lang.T("Rounds")),
                     ("ba", "Ba"), ("a0", "a0"),
                     ("sd", "SD " + u.VelocityUnitName), ("es", "ES " + u.VelocityUnitName), ("grp", Lang.T("Group MOA")),
                     ("score", Lang.T("Score")), ("label", Lang.T("Rating")) })
            _grid.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom; _status.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(bar);
        NudFix.ApplyTo(this);

        RefreshFilters();
        // Compare on open rather than waiting for the button -- same fix Load Leaderboard and
        // "Find best Ba" both needed: an empty grid otherwise reads as "nothing logged" when it
        // only means "not run yet".
        Run();
    }

    /// <summary>Repopulates the caliber filter from whichever calibers the journal currently has
    /// entries for at all -- same convention as Load Leaderboard.</summary>
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
    /// Compares every powder matching <paramref name="caliberFilter"/> (or every powder at all, if
    /// null or "-- any --") via <see cref="PowderCompare"/>. Shared by the on-screen grid and by
    /// <see cref="WriteToGrtAsync"/>, which always compares within ONE caliber (the open load's own)
    /// regardless of whatever the grid happens to be filtered to -- same reasoning as Load
    /// Leaderboard: a .223 powder's score isn't comparable to a .308 one's.
    /// </summary>
    private List<PowderCompare.Row> ComputeCompared(string? caliberFilter) =>
        PowderCompare.Compare(
            _db.Journal(),
            _db.Components(includeArchived: true).ToDictionary(c => c.Id, c => c.Display),
            caliberFilter == Lang.T("-- any --") ? null : caliberFilter,
            Lang.T("— none —"));

    private void Run()
    {
        var u = GrtUnits.Current;
        RefreshFilters();
        var compared = ComputeCompared(_caliber.SelectedItem as string);

        _grid.Rows.Clear();
        int rank = 1;
        foreach (var r in compared)
        {
            int i = _grid.Rows.Add(r.Score.Total.HasValue ? rank++.ToString() : "-",
                r.Caliber, r.Powder, r.Recipes, r.Sessions, r.Rounds,
                r.AvgBa?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "",
                r.AvgA0?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "",
                r.Sd is { } sd ? u.VelocitySd(sd) : "",
                r.Es is { } es ? u.VelocitySd(es) : "",
                r.Group?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                r.Score.Total?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                Lang.T(LoadScoring.ScoreLabel(r.Score.Total)));
            _grid.Rows[i].Tag = r;
        }
        int rankable = compared.Count(r => r.Score.Total.HasValue);
        _status.Text = compared.Count == 0 ? Lang.T("No journal entries yet -- log a session first.")
            : rankable == 0 ? Lang.T("Nothing to compare yet -- a powder needs SD, ES or a group size logged, not just a round count.")
            : string.Format(Lang.T("{0} powder(s) compared, best first."), rankable);
    }

    /// <summary>
    /// Writes the currently-open GRT load's own powder placement into a note -- same write-back
    /// pattern every other analysis tool in the Toolkit uses (timestamped sibling snapshot, never
    /// touches the original). Compares within the open load's OWN caliber, independent of whatever
    /// the grid is currently filtered to, and adds one line Load Leaderboard has no equivalent for:
    /// how this file's own Ba compares to the average of every other time this powder was
    /// calibrated -- the closest this plugin can get to flagging a possibly-stale value, since it
    /// has no access to GRT's own factory number for comparison (see the class doc).
    /// </summary>
    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write comparison note to GRT load"));
                return;
            }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            string caliber = doc.CaliberName;
            long? powderId = _db.ResolvePowderId(doc.PropellantName);

            var compared = ComputeCompared(caliber);
            int idx = compared.FindIndex(r => r.PowderId == powderId);
            if (idx < 0)
            {
                MessageBox.Show(this,
                    Lang.T("This load hasn't been logged in the Journal yet, so its powder has no comparison entry to write. Log it (Journal -> New / Log from GRT) first."),
                    Lang.T("Write comparison note to GRT load"));
                return;
            }

            var row = compared[idx];
            int comparedCount = compared.Count(r => r.Score.Total.HasValue);
            int rank = row.Score.Total.HasValue ? compared.Take(idx + 1).Count(r => r.Score.Total.HasValue) : -1;
            var u = GrtUnits.Current;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            report.AppendLine();
            report.AppendLine(row.Score.Total.HasValue
                ? string.Format(Lang.T("Rank #{0} of {1} powders tried for {2}."), rank, comparedCount, caliber)
                : string.Format(Lang.T("Not enough data to score yet ({0} known powders for {1})."), comparedCount, caliber));
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
            report.AppendLine();
            report.AppendLine(string.Format(Lang.T("Recipes tried: {0}   Sessions: {1}"), row.Recipes, row.Sessions));

            if (row.AvgBa is { } avgBa && doc.PropellantBa is { } fileBa)
            {
                double diffPct = avgBa == 0 ? 0 : (fileBa - avgBa) / avgBa * 100.0;
                report.AppendLine(string.Format(Lang.T("This load's Ba: {0}   Average across {1} calibrated session(s): {2} ({3}{4:0.0}%)"),
                    fileBa.ToString("0.000000", CultureInfo.InvariantCulture),
                    compared[idx].Sessions, avgBa.ToString("0.000000", CultureInfo.InvariantCulture),
                    diffPct >= 0 ? "+" : "", diffPct));
            }

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, report.ToString());
            string outPath = writeDoc.SaveSibling("powdercompare");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Comparison note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write comparison note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
