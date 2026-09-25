using System.Globalization;
using System.Text;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The 2D sibling of <see cref="SdRootCauseForm"/>: for one shot group — from an OnTarget CSV, GRT's
/// own "Shot group analysis" tabs, or typed in by hand — a Score, a separate Confidence, and a plain
/// list of specific, evidenced problems. See <see cref="GroupAnalysis"/> for the engine; this is the
/// thin GRT-IPC/GDI+ shell, same split as every other tool in this plugin.
///
/// Three group sources, not two: someone using neither OnTarget nor GRT's own digitizer still needs a
/// way in, so a manual per-shot X/Y grid (in the user's own display length unit) is the third.
/// </summary>
internal sealed class GroupAnalysisForm : Form
{
    private const string NoteTitle = "Group Analysis";

    private sealed class GroupEntry
    {
        /// <summary>Where the group came from (file name, GRT tab name, or "manual"/"combined") --
        /// shown in its own grid column, deliberately separate from the charge so the two are never
        /// run together into one hard-to-parse string.</summary>
        public required string Source { get; init; }
        public required TargetGroup Group { get; init; }
        public bool HasAppCenter { get; init; }

        /// <summary>Per-shot velocities in firing order, when a matching Athlon/Garmin file for the
        /// same charge was found -- either alongside this group's OnTarget CSV in a folder import, or
        /// matched afterwards from a separate velocity-only folder via <c>LoadAthlonVelocities</c>
        /// (hence a settable property, not init-only: it can be filled in after the entry already exists).</summary>
        public IReadOnlyList<double>? Velocities { get; set; }

        /// <summary>The charge weight (gr) this group and, when present, these velocities both belong
        /// to -- its own explicit column, so "charge 39.20 gr produced these N chrono shots AND this
        /// OnTarget group" is a fact you can see in one row, not something to infer from a filename.</summary>
        public double? ChargeGrains { get; init; }

        /// <summary>The Ladder/OCW Analyzer's own node (raw grains), when this group was loaded
        /// straight from GRT's currently-open load AND that same file's "OCW Analysis" note carried
        /// one -- null for every other source, since cross-checking against a node found for a
        /// DIFFERENT rifle/load would be a coincidence dressed up as a finding, not a real connection.
        /// See <see cref="GroupAnalysis.CheckAgainstNode"/>.</summary>
        public (double Low, double High)? OcwNodeGrains { get; init; }
    }

    private readonly GrtClient? _grt;
    private readonly GrtUnits _u = GrtUnits.Current;
    private readonly List<GroupEntry> _entries = new();

    private readonly Label _loadStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly DataGridView _groupGrid = new();
    private readonly ComboBox _rangeOverride = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NumericUpDown _manualDistance = new() { Minimum = 1, Maximum = 2000, DecimalPlaces = 1, Value = 100, Width = 90 };
    private readonly DataGridView _manualGrid = new();
    private readonly NumericUpDown _manualAimX = new() { Minimum = -1000, Maximum = 1000, DecimalPlaces = 2, Value = 0, Width = 70 };
    private readonly NumericUpDown _manualAimY = new() { Minimum = -1000, Maximum = 1000, DecimalPlaces = 2, Value = 0, Width = 70 };
    private readonly TextBox _verdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 240, ScrollBars = ScrollBars.Vertical };
    private readonly GroupTargetChart _chart = new() { Dock = DockStyle.Fill };

    private GroupAnalysisReport? _lastReport;
    private TargetGroup? _lastGroup;

    public GroupAnalysisForm(GrtClient? grt)
    {
        _grt = grt;
        Text = AppVersion.Title(Lang.T("Group Analysis"));
        Width = 1080; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(880, 560);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0) };
        var loadBtn = new Button { Text = Lang.T("Load GRT's shot-group tabs…"), AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadFromGrtAsync();
        top.Controls.Add(loadBtn);
        var csvBtn = new Button { Text = Lang.T("Load OnTarget CSV…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        csvBtn.Click += (_, _) => LoadOnTargetCsv();
        top.Controls.Add(csvBtn);
        var folderBtn = new Button { Text = Lang.T("Load OnTarget folder…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        folderBtn.Click += (_, _) => LoadOnTargetFolder();
        top.Controls.Add(folderBtn);
        top.Controls.Add(_loadStatus);
        _loadStatus.Margin = new Padding(10, 10, 0, 0);
        var settingsBtn = new Button { Text = Lang.T("⚙ Thresholds…"), AutoSize = true, Margin = new Padding(20, 2, 0, 0) };
        settingsBtn.Click += (_, _) => { using var f = new GroupAnalysisSettingsForm(); f.ShowDialog(this); PopulateRangeOverride(); };
        top.Controls.Add(settingsBtn);
        var writeBtn = new Button { Text = Lang.T("Write group analysis note to GRT load"), AutoSize = true, Margin = new Padding(20, 2, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        top.Controls.Add(writeBtn);

        // SplitterDistance is set AFTER the control is docked and parented, not in the object
        // initializer: at initializer time the SplitContainer has no real size yet, so an early
        // assignment gets silently clamped against that placeholder size instead of the final one --
        // the visible symptom was a ~320px-wide left panel rendering at ~120px, cutting off every label.
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 300 };
        split.Panel1.Controls.Add(BuildControlsPanel());
        split.Panel2.Controls.Add(BuildResultsPanel());

        Controls.Add(split);
        Controls.Add(top);
        split.SplitterDistance = 320;
        NudFix.ApplyTo(this);
    }

    // ── Left panel: source picker, range override, manual entry ────────

    private Control BuildControlsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8), AutoScroll = true };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        void Add(Control c) { panel.Controls.Add(c); panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }

        Add(new Label { Text = Lang.T("Group"), AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        _groupGrid.Width = 280; _groupGrid.Height = 120;
        _groupGrid.ReadOnly = true; _groupGrid.AllowUserToAddRows = false; _groupGrid.RowHeadersVisible = false;
        _groupGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _groupGrid.MultiSelect = false;
        _groupGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // Four columns in ~280px means a header like "Chrono shots" doesn't fit on one line -- it was
        // silently clipped to "Colpi" (identical to the Hits column next to it, reading as a
        // duplicate) instead of wrapping, so headers wrap to two lines here rather than truncate.
        _groupGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _groupGrid.ColumnHeadersHeight = 34;
        _groupGrid.Columns.Add("charge", Lang.T("Charge (gr)"));
        _groupGrid.Columns.Add("hits", Lang.T("Hits"));
        _groupGrid.Columns.Add("chrono", Lang.T("Chrono shots"));
        _groupGrid.Columns.Add("source", Lang.T("Source"));
        Add(_groupGrid);

        var velBtn = new Button { Text = Lang.T("Load Athlon velocities…"), AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        velBtn.Click += (_, _) => LoadAthlonVelocities();
        Add(velBtn);

        var combineBtn = new Button { Text = Lang.T("Combine all loaded groups"), AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        combineBtn.Click += (_, _) => CombineLoadedGroups();
        Add(combineBtn);

        Add(new Label { Text = Lang.T("Distance band"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        PopulateRangeOverride();
        Add(_rangeOverride);

        Add(new Label { Text = "— " + Lang.T("or enter a group manually") + " —", AutoSize = true, Margin = new Padding(0, 14, 0, 2), ForeColor = SystemColors.GrayText });

        Add(new Label { Text = Lang.T("Shooting distance") + " (" + _u.DistanceUnitName + ")", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        Add(_manualDistance);

        Add(new Label { Text = Lang.T("Per-shot offsets from centre") + " (" + _u.LengthUnitName + ")", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        Add(new Label { Text = "(" + Lang.T("paste supported") + ")", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 2) });
        _manualGrid.Width = 280; _manualGrid.Height = 160;
        _manualGrid.RowHeadersVisible = false;
        _manualGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _manualGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "x", HeaderText = "X (" + _u.LengthUnitName + ")" });
        _manualGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "y", HeaderText = "Y (" + _u.LengthUnitName + ")" });
        _manualGrid.KeyDown += ManualGridPaste;
        Add(_manualGrid);

        var aimPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        aimPanel.Controls.Add(new Label { Text = Lang.T("Aim X/Y") + ":", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        aimPanel.Controls.Add(_manualAimX);
        aimPanel.Controls.Add(_manualAimY);
        Add(aimPanel);

        var buildBtn = new Button { Text = Lang.T("Build group from manual entry"), AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buildBtn.Click += (_, _) => BuildManualGroup();
        Add(buildBtn);

        var analyzeBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        analyzeBtn.Click += (_, _) => Analyze();
        Add(analyzeBtn);

        return panel;
    }

    private Control BuildResultsPanel()
    {
        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(_chart);
        container.Controls.Add(_verdict);
        return container;
    }

    // ── Sources ──────────────────────────────────────────────────────────

    private async Task LoadFromGrtAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            var (groups, log) = GrtShotGroups.FromDoc(doc);
            // Same file, so a node found here genuinely belongs to these groups -- see OcwNodeGrains'
            // own doc comment for why this is the one case this cross-check is trusted in.
            var node = GroupAnalysis.TryParseOcwNodeGrains(doc.FindNoteText("OCW Analysis"));
            foreach (var g in groups)
                _entries.Add(new GroupEntry { Source = g.SourceFile, Group = g, HasAppCenter = false, ChargeGrains = g.ChargeGrains, OcwNodeGrains = node });

            RefreshGroupGrid();
            _loadStatus.Text = string.Format(Lang.T("{0} shot-group tab(s) loaded."), groups.Count)
                + (log.Count > 0 ? " " + string.Join(" ", log) : "")
                + (node is { } n ? " " + string.Format(Lang.T("OCW node found: {0}-{1} gr."), n.Low.ToString("0.##", CultureInfo.InvariantCulture), n.High.ToString("0.##", CultureInfo.InvariantCulture)) : "");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadOnTargetCsv()
    {
        using var d = new OpenFileDialog { Filter = "OnTarget CSV (*.csv)|*.csv|All files|*.*", Title = Lang.T("Load OnTarget CSV…") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var g = OnTargetCsv.Parse(d.FileName);
            _entries.Add(new GroupEntry { Source = Path.GetFileName(d.FileName), Group = g, HasAppCenter = true, ChargeGrains = g.ChargeGrains });
            RefreshGroupGrid(_entries.Count - 1);
            _loadStatus.Text = string.Format(Lang.T("Loaded {0} impacts from {1}."), g.Impacts.Count, Path.GetFileName(d.FileName));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadOnTargetFolder()
    {
        using var d = new FolderBrowserDialog { Description = Lang.T("Folder with one OnTarget *.csv per charge (Athlon *.xlsx alongside is used to label it, not required)") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (targets, velocities, log) = LadderLoader.ScanFolder(d.SelectedPath, LadderMode.Charge);
            foreach (var (chargeKey, g) in targets.OrderBy(kv => kv.Key))
            {
                velocities.TryGetValue(chargeKey, out var vs);
                _entries.Add(new GroupEntry
                {
                    Source = Path.GetFileName(g.SourceFile),
                    Group = g, HasAppCenter = true, Velocities = vs,
                    ChargeGrains = g.ChargeGrains ?? chargeKey,
                });
            }
            RefreshGroupGrid(targets.Count > 0 ? _entries.Count - 1 : null);
            _loadStatus.Text = string.Format(Lang.T("{0} charge group(s) loaded from folder."), targets.Count)
                + (log.Count > 0 ? " " + string.Join(" ", log) : "");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>
    /// Matches Athlon velocities to already-loaded groups from a SEPARATE folder -- for anyone who
    /// keeps chronograph exports and OnTarget exports in different places rather than one folder with
    /// both, which <see cref="LoadOnTargetFolder"/> alone can't help with. Reuses
    /// <see cref="LadderLoader.ScanFolder"/> purely for its velocity side (a folder of only *.xlsx
    /// files has nothing for the *.csv scan to find), then matches by charge weight to whichever
    /// groups are already in <see cref="_entries"/>, from any source.
    /// </summary>
    private void LoadAthlonVelocities()
    {
        using var d = new FolderBrowserDialog { Description = Lang.T("Folder with Athlon *.xlsx velocity files (one per charge) — can be a different folder than the OnTarget one") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (_, velocities, _) = LadderLoader.ScanFolder(d.SelectedPath, LadderMode.Charge);
            int matched = 0;
            foreach (var entry in _entries)
            {
                if (entry.ChargeGrains is not { } charge) continue;
                if (velocities.TryGetValue(Math.Round(charge, 3), out var vs)) { entry.Velocities = vs; matched++; }
            }
            RefreshGroupGrid();
            _loadStatus.Text = string.Format(Lang.T("Matched Athlon velocities to {0} of {1} loaded group(s)."), matched, _entries.Count);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void CombineLoadedGroups()
    {
        if (_entries.Count < 2) { MessageBox.Show(this, Lang.T("Load at least two groups first.")); return; }
        var combined = GroupAnalysis.CombineGroups(_entries.Select(e => e.Group).ToList());
        _entries.Add(new GroupEntry
        {
            Source = string.Format(Lang.T("combined ({0} groups)"), _entries.Count),
            Group = combined,
            HasAppCenter = false,
        });
        RefreshGroupGrid(_entries.Count - 1);
    }

    /// <summary>Pastes tab/semicolon-separated rows starting at the current cell -- typed values
    /// often use a decimal comma, so unlike a real CSV file this deliberately does NOT split on
    /// comma, or a pasted "5,3" would land in two cells instead of one.</summary>
    private void ManualGridPaste(object? sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.V)) return;
        e.Handled = true;
        string text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        int startRow = _manualGrid.CurrentCell?.RowIndex ?? 0;
        int startCol = _manualGrid.CurrentCell?.ColumnIndex ?? 0;
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            int rowIndex = startRow + i;
            while (rowIndex >= _manualGrid.Rows.Count - 1) _manualGrid.Rows.Add();
            var cells = lines[i].Split('\t', ';');
            for (int c = 0; c < cells.Length && startCol + c < _manualGrid.Columns.Count; c++)
                _manualGrid.Rows[rowIndex].Cells[startCol + c].Value = cells[c].Trim();
        }
    }

    private void BuildManualGroup()
    {
        double distanceM = _u.DistanceToMetres((double)_manualDistance.Value);
        var points = new List<(double, double)>();
        foreach (DataGridViewRow row in _manualGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string? xs = Convert.ToString(row.Cells["x"].Value), ys = Convert.ToString(row.Cells["y"].Value);
            if (!double.TryParse(xs?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double xDisp)) continue;
            if (!double.TryParse(ys?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double yDisp)) continue;
            points.Add((_u.LengthToMm(xDisp), _u.LengthToMm(yDisp)));
        }
        if (points.Count == 0) { MessageBox.Show(this, Lang.T("Enter at least one shot's X/Y first.")); return; }

        double aimXMm = _u.LengthToMm((double)_manualAimX.Value), aimYMm = _u.LengthToMm((double)_manualAimY.Value);
        var g = GroupAnalysis.BuildManualGroup(distanceM, points, aimXMm, aimYMm);
        _entries.Add(new GroupEntry { Source = Lang.T("manual entry"), Group = g, HasAppCenter = false });
        RefreshGroupGrid(_entries.Count - 1);
    }

    private int SelectedEntryIndex => _groupGrid.CurrentRow?.Index ?? -1;

    /// <summary>Rebuilds the grid from <see cref="_entries"/>. <paramref name="selectIndex"/> forces
    /// which row ends up selected (e.g. the one just added); left null, the previous selection is
    /// kept if it still exists, otherwise nothing is selected -- loading several GRT shot-group tabs
    /// at once shouldn't silently pick one of them for you.</summary>
    private void RefreshGroupGrid(int? selectIndex = null)
    {
        int keep = selectIndex ?? SelectedEntryIndex;
        _groupGrid.Rows.Clear();
        foreach (var e in _entries)
        {
            string charge = e.ChargeGrains is { } g ? FormattableString.Invariant($"{g:0.0##}") : "—";
            string chrono = e.Velocities is { Count: > 0 } vs ? vs.Count.ToString(CultureInfo.InvariantCulture) : "—";
            _groupGrid.Rows.Add(charge, e.Group.Impacts.Count, chrono, e.Source);
        }
        if (keep >= 0 && keep < _groupGrid.Rows.Count)
        {
            _groupGrid.ClearSelection();
            _groupGrid.Rows[keep].Selected = true;
            _groupGrid.CurrentCell = _groupGrid.Rows[keep].Cells[0];
        }
    }

    // ── Analyze ──────────────────────────────────────────────────────────

    /// <summary>Rebuilds the override list from the live config's own bands (not a hardcoded
    /// short/long pair) — called at form build and again after the Settings dialog closes, since the
    /// band count/distances themselves are user-editable there.</summary>
    private void PopulateRangeOverride()
    {
        var bands = GroupAnalysis.Config.DistanceBands.OrderBy(b => b.MaxDistanceM).ToList();
        int keep = _rangeOverride.SelectedIndex;
        _rangeOverride.Items.Clear();
        _rangeOverride.Items.Add(Lang.T("Auto (from the group's own distance)"));
        foreach (var band in bands) _rangeOverride.Items.Add(_u.Distance(band.MaxDistanceM));
        _rangeOverride.SelectedIndex = keep >= 0 && keep < _rangeOverride.Items.Count ? keep : 0;
    }

    private void Analyze()
    {
        if (SelectedEntryIndex < 0) { _verdict.Text = Lang.T("Load or build a group first."); return; }
        var entry = _entries[SelectedEntryIndex];
        var group = entry.Group;
        var cfg = GroupAnalysis.Config;
        var bands = cfg.DistanceBands.OrderBy(b => b.MaxDistanceM).ToList();

        // "Auto" reads the band from the group's own distance, same as GroupAnalysis.Diagnose does
        // internally; an explicit override (e.g. comparing a 300 m group against the 1000 m bar on
        // purpose) picks straight from the same ordered list the dropdown itself was built from.
        var band = _rangeOverride.SelectedIndex > 0 && _rangeOverride.SelectedIndex - 1 < bands.Count
            ? bands[_rangeOverride.SelectedIndex - 1]
            : GroupAnalysis.ClassifyBand(group.DistanceM, cfg);

        var outliers = GroupAnalysis.MahalanobisOutliers(group, cfg);
        double? velR = entry.Velocities is { Count: >= 3 } vs ? GroupAnalysis.VelocityPoiCorrelation(vs, group) : null;
        var problems = GroupAnalysis.DiagnoseProblems(group, band, cfg, outliers, entry.HasAppCenter, velR);
        var report = new GroupAnalysisReport(
            band,
            GroupAnalysis.Score(group, band),
            GroupAnalysis.Confidence(group.Impacts.Count, band),
            group.MeanRadiusMoa,
            group.GroupEsMoa,
            GroupAnalysis.Cep50Moa(group),
            group.HorizontalSpreadMoa,
            group.VerticalSpreadMoa,
            outliers,
            problems,
            VelocityPoiR: velR,
            NodeCrossCheck: GroupAnalysis.CheckAgainstNode(group.ChargeGrains, entry.OcwNodeGrains),
            WindNotLoadNote: GroupAnalysis.WindNotLoadNote(group, cfg, band));

        _lastReport = report;
        _lastGroup = group;
        _chart.SetData(group, outliers);
        _verdict.Text = BuildVerdict(report, group);
    }

    private string BuildVerdict(GroupAnalysisReport report, TargetGroup group)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string bandLabel = string.Format(Lang.T("{0} band"), _u.Distance(report.Band.MaxDistanceM));
        string confLabel = report.Confidence switch
        {
            ConfidenceLevel.High => Lang.T("High"),
            ConfidenceLevel.Medium => Lang.T("Medium"),
            _ => Lang.T("Low"),
        };
        sb.AppendLine(string.Format(Lang.T("Score {0}/10, Confidence: {1} ({2}, n={3}, actual distance {4})."),
            report.Score.ToString("0.#", ci), confLabel, bandLabel, group.Impacts.Count, _u.Distance(group.DistanceM)));
        sb.AppendLine();
        sb.AppendLine(string.Format(Lang.T("Mean radius {0} MOA · Extreme spread {1} MOA · CEP50 {2} MOA"),
            report.MeanRadiusMoa.ToString("0.00", ci), report.GroupEsMoa.ToString("0.00", ci), report.Cep50Moa.ToString("0.00", ci)));
        sb.AppendLine(string.Format(Lang.T("Horizontal spread {0} MOA · Vertical spread {1} MOA"),
            report.HorizontalSpreadMoa.ToString("0.00", ci), report.VerticalSpreadMoa.ToString("0.00", ci)));
        if (report.VelocityPoiR is { } vr)
            sb.AppendLine(string.Format(Lang.T("Per-shot velocity <-> distance-from-centre correlation: r={0}."), vr.ToString("0.00", ci)));
        sb.AppendLine();

        if (report.NodeCrossCheck is { } nc)
        {
            string chargeStr = _u.Charge(nc.ChargeGrains), lowStr = _u.Charge(nc.NodeLowGrains), highStr = _u.Charge(nc.NodeHighGrains);
            sb.AppendLine(nc.Relation == NodeRelation.Inside
                ? string.Format(Lang.T("◆ This charge ({0}) is inside the Ladder/OCW node already found for this load ({1}–{2}) — worth noting alongside the dispersion above, not a claimed cause."), chargeStr, lowStr, highStr)
                : string.Format(Lang.T("◆ This charge ({0}) is outside the Ladder/OCW node already found for this load ({1}–{2})."), chargeStr, lowStr, highStr));
            sb.AppendLine();
        }

        if (report.WindNotLoadNote is { } wn)
        {
            sb.AppendLine("✓ " + wn);
            sb.AppendLine();
        }

        if (report.Problems.Count == 0)
            sb.AppendLine(Lang.T("No problems found."));
        else
        {
            sb.AppendLine(Lang.T("Problems found:"));
            foreach (var p in report.Problems) sb.AppendLine("  • " + p.Message);
        }
        return sb.ToString();
    }

    // ── Write-back ────────────────────────────────────────────────────

    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || _lastReport is not { } report || _lastGroup is not { } group) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write group analysis note to GRT load")); return; }

            var text = new StringBuilder();
            text.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            text.AppendLine(new string('-', 20));
            text.AppendLine(BuildVerdict(report, group));

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, text.ToString());
            byte[]? png = _chart.RenderPng();
            if (png != null) writeDoc.AddGalleryPicture($"{NoteTitle} chart", "groupanalysis_chart", png);
            string outPath = writeDoc.SaveSibling("groupanalysis");
            await _grt.LoadFileAsync(outPath);
            _loadStatus.Text = Lang.T("Group analysis note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write group analysis note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
