using System.Globalization;
using System.Text;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// "Why are a few shots dragging an otherwise-good SD down?" for one selected GRT Measurement
/// string — a single, automatic, ranked answer instead of requiring the user to guess which of
/// <see cref="AdvancedDiagnostics"/>'s six separate, manually-configured tabs to open first. See
/// <see cref="SdRootCause"/> for the engine; this form is the thin GRT-IPC/DB/GDI+ shell around it,
/// same split as every other tool in this plugin.
///
/// Per-shot barrel temperature (a probe read at the moment of each shot) is always the user's own
/// manual entry — GRT's plugin API carries only velocity per shot — persisted in this plugin's own
/// database keyed by (caliber, string label, shot index) rather than by .grtload path, so it survives
/// the write-back pattern's file renaming. See <see cref="ShotMeasurement"/>'s class doc.
/// </summary>
internal sealed class SdRootCauseForm : Form
{
    private const string NoteTitle = "SD Root-Cause";

    private sealed class StringRow
    {
        public required string Label { get; init; }
        public required List<double> Velocities { get; init; }
    }

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly GrtUnits _u = GrtUnits.Current;
    private readonly List<StringRow> _rows = new();
    private readonly List<TargetGroup> _groups = new();
    private string _caliber = "";

    private readonly Label _loadStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly ComboBox _stringPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly NumericUpDown _coldBoreShots = new() { Minimum = 0, Maximum = 5, Value = 0, Width = 50 };
    private readonly ComboBox _groupPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly Label _alignmentCaveat = new() { AutoSize = false, Width = 280, Height = 44, ForeColor = SystemColors.GrayText };
    private readonly DataGridView _tempGrid = new();
    private readonly DataGridView _rankingGrid = new();
    private readonly TextBox _verdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 70 };
    private readonly SdRootCauseChart _barChart = new() { Dock = DockStyle.Fill };
    private readonly FindBaChart _tempVelChart = new() { Dock = DockStyle.Fill };
    private readonly FindBaChart _tempPoiChart = new() { Dock = DockStyle.Fill };

    private SdRootCause.SdRootCauseReport? _lastReport;
    private string? _lastLabel;

    public SdRootCauseForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("SD Root-Cause"));
        Width = 1120; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0) };
        var loadBtn = new Button { Text = Lang.T("Load strings from GRT load…"), AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadStringsAsync();
        top.Controls.Add(loadBtn);
        top.Controls.Add(_loadStatus);
        _loadStatus.Margin = new Padding(10, 10, 0, 0);
        var writeBtn = new Button { Text = Lang.T("Write root-cause note to GRT load"), AutoSize = true, Margin = new Padding(20, 2, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        top.Controls.Add(writeBtn);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildControlsPanel());
        split.Panel2.Controls.Add(BuildResultsPanel());

        Controls.Add(split);
        Controls.Add(top);
        NudFix.ApplyTo(this);
    }

    // ── Left panel: string/cause inputs ─────────────────────────────────

    private Control BuildControlsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8), AutoScroll = true };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        void Add(Control c) { panel.Controls.Add(c); panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }

        Add(new Label { Text = Lang.T("String"), AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        _stringPicker.SelectedIndexChanged += (_, _) => OnStringSelected();
        Add(_stringPicker);

        Add(new Label { Text = Lang.T("Cold-bore shots (0 = none marked)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        Add(_coldBoreShots);

        Add(new Label { Text = Lang.T("Shot-group tab (optional, for group dispersion)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        _groupPicker.SelectedIndexChanged += (_, _) => UpdateAlignmentCaveat();
        Add(_groupPicker);
        Add(_alignmentCaveat);

        Add(new Label { Text = Lang.T("Per-shot barrel temperature (optional — probe reading at each shot)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        _tempGrid.Width = 280; _tempGrid.Height = 220;
        _tempGrid.AllowUserToAddRows = false; _tempGrid.RowHeadersVisible = false;
        _tempGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _tempGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "shot", HeaderText = Lang.T("Shot"), ReadOnly = true });
        _tempGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "vel", HeaderText = _u.VelocityUnitName, ReadOnly = true });
        _tempGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "temp", HeaderText = _u.TemperatureUnitName });
        Add(_tempGrid);

        var analyzeBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        analyzeBtn.Click += (_, _) => Analyze();
        Add(analyzeBtn);

        return panel;
    }

    // ── Right panel: verdict + ranking + charts ─────────────────────────

    private Control BuildResultsPanel()
    {
        var container = new Panel { Dock = DockStyle.Fill };
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var rankingPage = new TabPage(Lang.T("Ranking"));
        _rankingGrid.Dock = DockStyle.Fill;
        _rankingGrid.ReadOnly = true; _rankingGrid.AllowUserToAddRows = false; _rankingGrid.RowHeadersVisible = false;
        _rankingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("shot", Lang.T("Shot")), ("vel", _u.VelocityUnitName), ("sdw", "SD " + Lang.T("without") + " " + _u.VelocityUnitName),
                     ("red", Lang.T("Reduction") + " " + _u.VelocityUnitName), ("pct", Lang.T("Reduction") + " %"), ("flag", Lang.T("Outlier?")) })
            _rankingGrid.Columns.Add(n, h);
        rankingPage.Controls.Add(_rankingGrid);
        tabs.TabPages.Add(rankingPage);

        var barPage = new TabPage(Lang.T("SD contribution"));
        barPage.Controls.Add(_barChart);
        tabs.TabPages.Add(barPage);

        var tempVelPage = new TabPage(Lang.T("Temp → velocity"));
        tempVelPage.Controls.Add(_tempVelChart);
        tabs.TabPages.Add(tempVelPage);

        var tempPoiPage = new TabPage(Lang.T("Temp → group"));
        tempPoiPage.Controls.Add(_tempPoiChart);
        tabs.TabPages.Add(tempPoiPage);

        container.Controls.Add(tabs);
        container.Controls.Add(_verdict);
        return container;
    }

    // ── Loading strings + shot-group tabs from the open GRT load ────────

    private async Task LoadStringsAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            _caliber = doc.CaliberName;

            _rows.Clear();
            foreach (var meas in doc.Measurements())
                foreach (var ch in meas.Charges)
                {
                    var vs = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 50).ToList();
                    if (vs.Count < 3) continue;
                    string label = ch.ChargeGrains is { } g
                        ? FormattableString.Invariant($"{g:0.0##} gr ({meas.Title})")
                        : $"{ch.Name} ({meas.Title})";
                    _rows.Add(new StringRow { Label = label, Velocities = vs });
                }

            _groups.Clear();
            var (groups, _) = GrtShotGroups.FromDoc(doc);
            _groups.AddRange(groups);

            _stringPicker.Items.Clear();
            foreach (var r in _rows) _stringPicker.Items.Add(r.Label);
            if (_stringPicker.Items.Count > 0) _stringPicker.SelectedIndex = 0;

            _groupPicker.Items.Clear();
            _groupPicker.Items.Add(Lang.T("— none —"));
            foreach (var g in _groups) _groupPicker.Items.Add($"{g.SourceFile} ({g.Impacts.Count} {Lang.T("hits")})");
            _groupPicker.SelectedIndex = 0;

            _loadStatus.Text = string.Format(Lang.T("{0} string(s), {1} shot-group tab(s) loaded."), _rows.Count, _groups.Count);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OnStringSelected()
    {
        if (_stringPicker.SelectedIndex < 0) return;
        var row = _rows[_stringPicker.SelectedIndex];
        var saved = string.IsNullOrEmpty(_caliber) ? new List<ShotMeasurement>() : _db.ShotTemperatures(_caliber, row.Label);
        var savedByIndex = saved.ToDictionary(s => s.ShotIndex, s => s.TemperatureC);

        _tempGrid.Rows.Clear();
        for (int i = 0; i < row.Velocities.Count; i++)
        {
            double? tC = savedByIndex.TryGetValue(i, out var t) ? t : null;
            string tempDisplay = tC.HasValue ? _u.TemperatureValue(tC.Value).ToString("0.0", CultureInfo.InvariantCulture) : "";
            _tempGrid.Rows.Add(i + 1, _u.VelocityValue(row.Velocities[i]).ToString("0.0", CultureInfo.InvariantCulture), tempDisplay);
        }
        UpdateAlignmentCaveat();
    }

    private void UpdateAlignmentCaveat()
    {
        if (_stringPicker.SelectedIndex < 0 || _groupPicker.SelectedIndex <= 0) { _alignmentCaveat.Text = ""; return; }
        var row = _rows[_stringPicker.SelectedIndex];
        var group = _groups[_groupPicker.SelectedIndex - 1];
        var align = SdRootCause.AlignShotsWithGroup(row.Velocities.Count, group);
        // Lang.T does an exact-string dictionary lookup, so it can never match align.Reason directly
        // once that sentence has runtime numbers baked into it (the mismatch case) -- the caveat is
        // rebuilt here from a literal template instead, the same split every other tool in this
        // plugin uses (Log/ returns data, Ui/ composes and localizes the sentence).
        _alignmentCaveat.Text = align.Aligned
            ? Lang.T("Assumes the shot-group tab's hit order matches this Measurement's firing order -- GRT does not link them itself.")
            : string.Format(Lang.T("{0} chronographed shot(s) but {1} hit(s) in the shot-group tab -- cannot align."),
                row.Velocities.Count, group.Impacts.Count);
        _alignmentCaveat.ForeColor = align.Aligned ? SystemColors.GrayText : Color.Firebrick;
    }

    // ── Analyze ──────────────────────────────────────────────────────────

    private void Analyze()
    {
        if (_stringPicker.SelectedIndex < 0) { _verdict.Text = Lang.T("Load strings from GRT load first."); return; }
        var row = _rows[_stringPicker.SelectedIndex];
        _lastLabel = row.Label;

        // Persist every temperature-grid edit (including a cleared cell, as an explicit null) before
        // analyzing, so the entries survive to the next time this exact string is opened.
        var manualTemps = new List<(int ShotIndex, double TempC)>();
        for (int i = 0; i < _tempGrid.Rows.Count; i++)
        {
            string text = Convert.ToString(_tempGrid.Rows[i].Cells["temp"].Value) ?? "";
            double? tempC = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tDisplay)
                ? _u.TemperatureToCelsius(tDisplay) : null;
            if (!string.IsNullOrEmpty(_caliber)) _db.SaveShotTemperature(_caliber, row.Label, i, tempC);
            if (tempC.HasValue) manualTemps.Add((i, tempC.Value));
        }

        int coldBoreN = (int)_coldBoreShots.Value;
        var coldBoreFlags = coldBoreN > 0
            ? Enumerable.Range(0, row.Velocities.Count).Select(i => i < coldBoreN).ToList()
            : null;

        TargetGroup? group = _groupPicker.SelectedIndex > 0 ? _groups[_groupPicker.SelectedIndex - 1] : null;

        try
        {
            var report = SdRootCause.Diagnose(row.Velocities, coldBoreFlags, manualTemps.Count >= 3 ? manualTemps : null, group);
            _lastReport = report;
            RenderReport(report);
        }
        catch (SdRootCause.SdRootCauseError ex) { _verdict.Text = ex.Message; _lastReport = null; }
    }

    private void RenderReport(SdRootCause.SdRootCauseReport report)
    {
        _rankingGrid.Rows.Clear();
        foreach (var r in report.Ranking)
            _rankingGrid.Rows.Add(r.ShotIndex + 1, _u.VelocityValue(r.VelocityMps).ToString("0.0", CultureInfo.InvariantCulture),
                _u.VelocitySd(r.SdWithoutMps), FormattableString.Invariant($"{_u.VelocityValue(r.ReductionMps):+0.00;-0.00}"),
                r.ReductionPct.ToString("+0.0;-0.0", CultureInfo.InvariantCulture), r.ChauvenetFlagged ? Lang.T("yes") : Lang.T("no"));

        _barChart.SetData(report.Ranking);

        if (report.TempVelocityR is { } tv)
        {
            var pairs = _tempGrid.Rows.Cast<DataGridViewRow>()
                .Select((r, i) => (i, tempText: Convert.ToString(r.Cells["temp"].Value)))
                .Where(p => double.TryParse(p.tempText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .Select(p => (X: double.Parse(p.tempText!, CultureInfo.InvariantCulture), Y: double.Parse(Convert.ToString(_tempGrid.Rows[p.i].Cells["vel"].Value)!, CultureInfo.InvariantCulture)));
            _tempVelChart.SetData(pairs, _u.TemperatureUnitName, _u.VelocityUnitName, "");
        }
        else _tempVelChart.SetData(Array.Empty<(double, double)>(), "", "", Lang.T("No per-shot temperature entered for this string."));

        if (report.TempPoiR is { } tp && _stringPicker.SelectedIndex >= 0 && _groupPicker.SelectedIndex > 0)
        {
            var group = _groups[_groupPicker.SelectedIndex - 1];
            var row = _rows[_stringPicker.SelectedIndex];
            var align = SdRootCause.AlignShotsWithGroup(row.Velocities.Count, group);
            var radiusByIndex = align.Pairs.ToDictionary(p => p.ShotIndex, p => p.RadiusMoa);
            var pairs = _tempGrid.Rows.Cast<DataGridViewRow>()
                .Select((r, i) => (i, tempText: Convert.ToString(r.Cells["temp"].Value)))
                .Where(p => double.TryParse(p.tempText, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && radiusByIndex.ContainsKey(p.i))
                .Select(p => (X: double.Parse(p.tempText!, CultureInfo.InvariantCulture), Y: radiusByIndex[p.i]));
            _tempPoiChart.SetData(pairs, _u.TemperatureUnitName, "MOA", "");
        }
        else _tempPoiChart.SetData(Array.Empty<(double, double)>(), "", "", Lang.T("No matching shot-group data for this string."));

        _verdict.Text = BuildVerdict(report);
    }

    private string BuildVerdict(SdRootCause.SdRootCauseReport report)
    {
        var sb = new StringBuilder();
        string patternLabel = report.Pattern switch
        {
            SdRootCause.SdPatternKind.SingleOutlier => Lang.T("one shot statistically stands out (Chauvenet)"),
            SdRootCause.SdPatternKind.PartialOutlier => Lang.T("a few shots statistically stand out (Chauvenet)"),
            _ => Lang.T("no single shot statistically stands out -- dispersion is spread across the string"),
        };
        sb.AppendLine(string.Format(Lang.T("SD = {0} {1}: {2}."), _u.VelocitySd(report.SdMps), _u.VelocityUnitName, patternLabel));

        var top = report.Ranking.FirstOrDefault();
        if (top is { ChauvenetFlagged: true })
            // ReductionPct is signed (removing an ordinary shot can slightly RAISE the sample SD --
            // see SdRootCauseChart's own doc comment), so the sign is spelled out in words rather than
            // a "+"/"-" glyph, which would read as "the SD grew" right next to the word "reduction".
            sb.AppendLine(string.Format(
                top.ReductionPct >= 0
                    ? Lang.T("Shot #{0} contributes the most: removing it alone would take the SD to {1} {2} (a {3:0}% reduction).")
                    : Lang.T("Shot #{0} contributes the most: removing it alone would take the SD to {1} {2} ({3:0}% HIGHER, not lower)."),
                top.ShotIndex + 1, _u.VelocitySd(top.SdWithoutMps), _u.VelocityUnitName, Math.Abs(top.ReductionPct)));

        if (report.ColdBore is { } cb)
            sb.AppendLine(cb.ColdBoreIsOutlier
                ? string.Format(Lang.T("Cold-bore shot(s) print {0} than the rest by {1} {2} (z={3})."),
                    cb.ColdBoreFaster ? Lang.T("faster") : Lang.T("slower"), _u.VelocitySd(Math.Abs(cb.DeltaMps)), _u.VelocityUnitName, cb.ZScore)
                : Lang.T("No notable cold-bore effect."));

        if (report.TempVelocityR is { } tv)
            sb.AppendLine(string.Format(Lang.T("Temperature <-> velocity correlation: r={0}."), tv.ToString("0.00", CultureInfo.InvariantCulture)));
        if (report.TempPoiR is { } tp)
            sb.AppendLine(string.Format(Lang.T("Temperature <-> group-dispersion correlation: r={0}{1}"), tp.ToString("0.00", CultureInfo.InvariantCulture),
                tp > 0.3 ? "  " + Lang.T("-- this barrel may be opening groups up as it warms.") : "."));
        else if (report.ProgressiveDrift is { } fd)
            sb.AppendLine(fd.FoulingSuspected
                ? Lang.T("Probable unmeasured thermal/fouling cause: SD rises as the string progresses (no per-shot temperature was entered for this string).")
                : Lang.T("No progressive drift detected across the string."));

        if (report.Pattern == SdRootCause.SdPatternKind.Systematic && Math.Abs(report.Skewness) > 1.0)
            sb.AppendLine(string.Format(Lang.T("Note (informal convention, not a reloading-specific benchmark): the velocity distribution is notably skewed ({0})."),
                report.Skewness.ToString("0.00", CultureInfo.InvariantCulture)));

        return sb.ToString();
    }

    // ── Write-back ────────────────────────────────────────────────────

    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || _lastReport is not { } report || _lastLabel is not { } label) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write root-cause note to GRT load")); return; }

            var text = new StringBuilder();
            text.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd} — {label}");
            text.AppendLine(new string('-', 20));
            text.AppendLine(BuildVerdict(report));

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, text.ToString());
            byte[]? png = _barChart.RenderPng();
            if (png != null) writeDoc.AddGalleryPicture($"{NoteTitle} chart", "sdrootcause_chart", png);
            string outPath = writeDoc.SaveSibling("sdrootcause");
            await _grt.LoadFileAsync(outPath);
            _loadStatus.Text = Lang.T("Root-cause note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write root-cause note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
