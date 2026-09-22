using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.ChronoStats;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Bundles the six diagnostics ported from Ballistic Lab v2 (<see cref="AdvancedDiagnostics"/>,
/// <see cref="SessionTrend"/>, <see cref="PressureTrend"/>) into one tool, "Diagnostica Avanzata" —
/// analyses no other tool in the Toolkit does, none of them requiring a calibrated Ba to run.
///
/// Five of the six read strings from the currently-open GRT load's own Measurement — same source
/// and same (label, velocities) shape <see cref="ChronoStatsForm"/> already reads via "From GRT
/// load's Measurement" — loaded ONCE per "Carica dal load GRT" click and shared across every tab
/// rather than re-read per analysis. Only Session Trend reads the Journal instead (cross-session,
/// not tied to one open load).
///
/// Naming conventions a string's GRT Measurement charge name has to follow to be usable here:
/// Primer Sensitivity treats each string's own name as its primer-lot label directly (name it "CCI
/// 200", "Federal 210", etc. in GRT); Neck Tension Correlation parses the first number found in the
/// name as the tension in mm (name it e.g. "0.05 tension" or just "0.05"). Fouling Tracker/Cold Bore/
/// Pressure Trend need no special naming — charge weight parses the same way Ladder/OCW already does.
/// </summary>
internal sealed class AdvancedDiagnosticsForm : Form
{
    private const string NoteTitle = "Advanced Diagnostics";

    private sealed class StringRow
    {
        public required string Label { get; init; }   // display label, e.g. "39.2 gr (Measurement Title)"
        public required string Name { get; init; }     // raw GRT charge name, for the neck-tension number parse
        public double? ChargeGrains { get; init; }
        public required List<double> Velocities { get; init; }
    }

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly List<StringRow> _rows = new();
    private readonly GrtUnits _u = GrtUnits.Current;
    private readonly Label _loadStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    // Session Trend
    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DataGridView _sessionGrid = new();
    private readonly Label _sessionStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private List<SessionTrend.ChargeTrend> _lastSessionTrends = new();

    // Pressure Trend
    private readonly DataGridView _pressureGrid = new();
    private readonly TextBox _pressureVerdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 60 };
    private PressureTrend.PressureTrendResult? _lastPressure;

    // Fouling Tracker
    private readonly ComboBox _foulingString = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly NumericUpDown _foulingWindow = new() { Minimum = 2, Maximum = 20, Value = 5, Width = 60 };
    private readonly DataGridView _foulingGrid = new();
    private readonly TextBox _foulingVerdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 60 };
    private (string label, AdvancedDiagnostics.FoulingResult r)? _lastFouling;

    // Cold Bore
    private readonly ComboBox _coldBoreString = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly NumericUpDown _coldBoreShots = new() { Minimum = 1, Maximum = 5, Value = 1, Width = 60 };
    private readonly TextBox _coldBoreVerdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill };
    private (string label, AdvancedDiagnostics.ColdBoreResult r)? _lastColdBore;

    // Primer Sensitivity
    private readonly DataGridView _primerGrid = new();
    private readonly TextBox _primerVerdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 60 };
    private AdvancedDiagnostics.PrimerSensitivityResult? _lastPrimer;

    // Neck Tension Correlation
    private readonly DataGridView _neckGrid = new();
    private readonly TextBox _neckVerdict = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 60 };
    private AdvancedDiagnostics.NeckTensionResult? _lastNeck;

    public AdvancedDiagnosticsForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Advanced Diagnostics"));
        Width = 1020; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 480);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0) };
        var loadBtn = new Button { Text = Lang.T("Load strings from GRT load…"), AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadStringsAsync();
        top.Controls.Add(loadBtn);
        top.Controls.Add(_loadStatus);
        _loadStatus.Margin = new Padding(10, 10, 0, 0);
        var writeBtn = new Button { Text = Lang.T("Write diagnostics note to GRT load"), AutoSize = true, Margin = new Padding(20, 2, 0, 0) };
        writeBtn.Enabled = _grt is { Connected: true };
        writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        top.Controls.Add(writeBtn);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSessionTrendTab());
        tabs.TabPages.Add(BuildPressureTrendTab());
        tabs.TabPages.Add(BuildFoulingTab());
        tabs.TabPages.Add(BuildColdBoreTab());
        tabs.TabPages.Add(BuildPrimerTab());
        tabs.TabPages.Add(BuildNeckTensionTab());

        Controls.Add(tabs);
        Controls.Add(top);
        NudFix.ApplyTo(this);

        RefreshCaliberFilter();
        RunSessionTrend();
    }

    // ── Shared: load strings from the open GRT load ─────────────────────

    private async Task LoadStringsAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }

            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            _rows.Clear();
            foreach (var meas in doc.Measurements())
                foreach (var ch in meas.Charges)
                {
                    var vs = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 50).ToList();
                    if (vs.Count == 0) continue;
                    string label = ch.ChargeGrains is { } g
                        ? FormattableString.Invariant($"{g:0.0##} gr ({meas.Title})")
                        : $"{ch.Name} ({meas.Title})";
                    _rows.Add(new StringRow { Label = label, Name = ch.Name, ChargeGrains = ch.ChargeGrains, Velocities = vs });
                }

            _loadStatus.Text = string.Format(Lang.T("{0} string(s) loaded."), _rows.Count);
            RefreshStringPickers();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshStringPickers()
    {
        foreach (var combo in new[] { _foulingString, _coldBoreString })
        {
            string? keep = combo.SelectedItem as string;
            combo.Items.Clear();
            foreach (var r in _rows) combo.Items.Add(r.Label);
            if (combo.Items.Count > 0) combo.SelectedItem = keep != null && combo.Items.Contains(keep) ? keep : combo.Items[0];
        }
    }

    // ── Session Trend ─────────────────────────────────────────────────

    private TabPage BuildSessionTrendTab()
    {
        var page = new TabPage(Lang.T("Session Trend"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        bar.Controls.Add(new Label { Text = Lang.T("Caliber"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_caliber);
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        runBtn.Click += (_, _) => RunSessionTrend();
        bar.Controls.Add(runBtn);

        _sessionGrid.Dock = DockStyle.Fill;
        _sessionGrid.ReadOnly = true; _sessionGrid.AllowUserToAddRows = false; _sessionGrid.RowHeadersVisible = false;
        _sessionGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("cal", Lang.T("Caliber")), ("pw", Lang.T("Powder")), ("bu", Lang.T("Bullet")),
                     ("chg", Lang.T("Charge") + " " + _u.ChargeUnitName), ("sess", Lang.T("Sessions")),
                     ("sdt", "SD trend"), ("sdsev", Lang.T("Severity")), ("est", "ES trend"), ("essev", Lang.T("Severity")),
                     ("mvt", "MV trend"), ("decl", Lang.T("Declining")), ("mis", Lang.T("Mismatch")) })
            _sessionGrid.Columns.Add(n, h);

        _sessionStatus.Dock = DockStyle.Bottom; _sessionStatus.Padding = new Padding(8, 4, 0, 4);
        page.Controls.Add(_sessionGrid); page.Controls.Add(_sessionStatus); page.Controls.Add(bar);
        return page;
    }

    private void RefreshCaliberFilter()
    {
        string? keep = _caliber.SelectedItem as string;
        _caliber.Items.Clear();
        _caliber.Items.Add(Lang.T("-- any --"));
        foreach (var c in _db.Journal().Select(e => e.Caliber).Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
            _caliber.Items.Add(c);
        _caliber.SelectedItem = keep != null && _caliber.Items.Contains(keep) ? keep : _caliber.Items[0];
    }

    private void RunSessionTrend()
    {
        RefreshCaliberFilter();
        string? filter = _caliber.SelectedItem as string;
        var journal = _db.Journal();
        if (filter is { } f && f != Lang.T("-- any --"))
            journal = journal.Where(e => string.Equals(e.Caliber, f, StringComparison.OrdinalIgnoreCase)).ToList();

        var names = _db.Components(includeArchived: true).ToDictionary(c => c.Id, c => c.Display);
        _lastSessionTrends = SessionTrend.AnalyzeChargeTrends(journal);

        _sessionGrid.Rows.Clear();
        foreach (var t in _lastSessionTrends)
        {
            string Pw(long? id) => id is { } i && names.TryGetValue(i, out var n) ? n : Lang.T("— none —");
            _sessionGrid.Rows.Add(t.Caliber, Pw(t.PowderId), Pw(t.BulletId),
                _u.ChargeValue(t.ChargeGr).ToString(_u.ChargeFormat, CultureInfo.InvariantCulture), t.NSessions,
                t.SdTrendSlopeMps?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "–", Lang.T(t.SdSeverity),
                t.EsTrendSlopeMps?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "–", Lang.T(t.EsSeverity),
                t.VelocityTrendSlopeMps?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "–",
                t.VelocityDeclining ? Lang.T("yes") : Lang.T("no"), t.VelocityMismatch ? Lang.T("yes") : Lang.T("no"));
        }
        _sessionStatus.Text = _lastSessionTrends.Count == 0
            ? Lang.T("No charge has been logged more than once yet -- a trend needs at least 2 sessions of the same charge.")
            : string.Format(Lang.T("{0} charge(s) with a trend across sessions."), _lastSessionTrends.Count);
    }

    // ── Pressure Trend ────────────────────────────────────────────────

    private TabPage BuildPressureTrendTab()
    {
        var page = new TabPage(Lang.T("Pressure Trend"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true };
        runBtn.Click += (_, _) => RunPressureTrend();
        bar.Controls.Add(runBtn);

        _pressureGrid.Dock = DockStyle.Fill;
        _pressureGrid.ReadOnly = true; _pressureGrid.AllowUserToAddRows = false; _pressureGrid.RowHeadersVisible = false;
        _pressureGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("chg", Lang.T("Charge") + " " + _u.ChargeUnitName), ("mv", "V " + _u.VelocityUnitName),
                     ("slope", "dV/dCharge"), ("jump", Lang.T("Jump")) })
            _pressureGrid.Columns.Add(n, h);

        page.Controls.Add(_pressureGrid); page.Controls.Add(_pressureVerdict); page.Controls.Add(bar);
        return page;
    }

    private void RunPressureTrend()
    {
        var candidates = _rows.Where(r => r.ChargeGrains.HasValue).ToList();
        if (candidates.Count < PressureTrend.MinCharges)
        {
            _pressureVerdict.Text = string.Format(Lang.T("Need at least {0} charges with a chrono string loaded. Click \"Load strings from GRT load…\" first."), PressureTrend.MinCharges);
            _lastPressure = null; _pressureGrid.Rows.Clear();
            return;
        }

        try
        {
            var charges = candidates.Select(r => r.ChargeGrains!.Value).ToList();
            var means = candidates.Select(r => r.Velocities.Average()).ToList();
            _lastPressure = PressureTrend.AnalyzePressureTrend(charges, means);

            _pressureGrid.Rows.Clear();
            for (int i = 0; i < _lastPressure.ChargesGr.Count; i++)
            {
                var jump = _lastPressure.VelocityJumps.FirstOrDefault(j => Math.Abs(j.ChargeGr - _lastPressure.ChargesGr[i]) < 0.001);
                _pressureGrid.Rows.Add(
                    _u.ChargeValue(_lastPressure.ChargesGr[i]).ToString(_u.ChargeFormat, CultureInfo.InvariantCulture),
                    _u.VelocityValue(_lastPressure.VelocitiesMps[i]).ToString("0.0", CultureInfo.InvariantCulture),
                    i < _lastPressure.SlopesMpsPerGr.Count ? _lastPressure.SlopesMpsPerGr[i].ToString("0.00", CultureInfo.InvariantCulture) : "–",
                    jump != null ? Lang.T(jump.Severity) : "");
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format(Lang.T("Pressure estimate: {0}"), Lang.T(_lastPressure.PressureEstimate)));
            if (_lastPressure.FlatteningTrend) sb.AppendLine(Lang.T("The last step(s) show a flattening velocity/charge curve -- a classic sign of approaching a pressure plateau."));
            if (_lastPressure.VelocityJumps.Count > 0) sb.AppendLine(string.Format(Lang.T("{0} abnormal velocity jump(s) -- check for a data entry error or real ignition instability."), _lastPressure.VelocityJumps.Count));
            _pressureVerdict.Text = sb.ToString();
        }
        catch (PressureTrend.PressureTrendError ex) { _pressureVerdict.Text = ex.Message; _lastPressure = null; }
    }

    // ── Fouling Tracker ───────────────────────────────────────────────

    private TabPage BuildFoulingTab()
    {
        var page = new TabPage(Lang.T("Fouling Tracker"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        bar.Controls.Add(new Label { Text = Lang.T("String"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_foulingString);
        bar.Controls.Add(new Label { Text = Lang.T("Window"), AutoSize = true, Margin = new Padding(10, 6, 4, 0) });
        bar.Controls.Add(_foulingWindow);
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        runBtn.Click += (_, _) => RunFouling();
        bar.Controls.Add(runBtn);

        _foulingGrid.Dock = DockStyle.Fill;
        _foulingGrid.ReadOnly = true; _foulingGrid.AllowUserToAddRows = false; _foulingGrid.RowHeadersVisible = false;
        _foulingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("shot", Lang.T("Shot")), ("rsd", "Rolling SD " + _u.VelocityUnitName), ("rmean", "Rolling mean " + _u.VelocityUnitName) })
            _foulingGrid.Columns.Add(n, h);

        page.Controls.Add(_foulingGrid); page.Controls.Add(_foulingVerdict); page.Controls.Add(bar);
        return page;
    }

    private void RunFouling()
    {
        if (_foulingString.SelectedItem is not string label) { _foulingVerdict.Text = Lang.T("Load strings from GRT load first."); return; }
        var row = _rows.First(r => r.Label == label);
        try
        {
            var r = AdvancedDiagnostics.FoulingTrend(row.Velocities, (int)_foulingWindow.Value);
            _lastFouling = (label, r);

            _foulingGrid.Rows.Clear();
            foreach (var p in r.Points)
                _foulingGrid.Rows.Add(p.ShotNumber, _u.VelocitySd(p.RollingSdMps), _u.VelocityValue(p.RollingMeanMps).ToString("0.0", CultureInfo.InvariantCulture));

            _foulingVerdict.Text = (r.FoulingSuspected ? Lang.T("Fouling suspected: SD is rising as the string progresses.") : Lang.T("No fouling trend detected."))
                + (r.VelocityDeclining ? "  " + Lang.T("Velocity is also declining across the string.") : "");
        }
        catch (AdvancedDiagnostics.AdvancedDiagnosticsError ex) { _foulingVerdict.Text = ex.Message; _lastFouling = null; }
    }

    // ── Cold Bore ─────────────────────────────────────────────────────

    private TabPage BuildColdBoreTab()
    {
        var page = new TabPage(Lang.T("Cold Bore"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        bar.Controls.Add(new Label { Text = Lang.T("String"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_coldBoreString);
        bar.Controls.Add(new Label { Text = Lang.T("Cold-bore shots"), AutoSize = true, Margin = new Padding(10, 6, 4, 0) });
        bar.Controls.Add(_coldBoreShots);
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        runBtn.Click += (_, _) => RunColdBore();
        bar.Controls.Add(runBtn);

        page.Controls.Add(_coldBoreVerdict); page.Controls.Add(bar);
        return page;
    }

    private void RunColdBore()
    {
        if (_coldBoreString.SelectedItem is not string label) { _coldBoreVerdict.Text = Lang.T("Load strings from GRT load first."); return; }
        var row = _rows.First(r => r.Label == label);
        int n = (int)_coldBoreShots.Value;
        var flags = Enumerable.Range(0, row.Velocities.Count).Select(i => i < n).ToList();
        try
        {
            var r = AdvancedDiagnostics.ColdBoreAnalysis(row.Velocities, flags);
            _lastColdBore = (label, r);
            _coldBoreVerdict.Text = string.Format(
                Lang.T("Cold bore: {0} {1}, warm: {2} {1} avg (SD {3} {1}).\nDelta: {4} {1} (z={5}) -> {6}"),
                _u.VelocityValue(r.MeanColdMps).ToString("0.0", CultureInfo.InvariantCulture), _u.VelocityUnitName,
                _u.VelocityValue(r.MeanWarmMps).ToString("0.0", CultureInfo.InvariantCulture),
                _u.VelocitySd(r.SdWarmMps),
                _u.VelocityValue(r.DeltaMps).ToString("+0.0;-0.0", CultureInfo.InvariantCulture), r.ZScore,
                r.ColdBoreIsOutlier
                    ? (r.ColdBoreFaster ? Lang.T("the cold-bore shot(s) print notably FASTER than the rest.") : Lang.T("the cold-bore shot(s) print notably SLOWER than the rest."))
                    : Lang.T("no notable cold-bore effect."));
        }
        catch (AdvancedDiagnostics.AdvancedDiagnosticsError ex) { _coldBoreVerdict.Text = ex.Message; _lastColdBore = null; }
    }

    // ── Primer Sensitivity ────────────────────────────────────────────

    private TabPage BuildPrimerTab()
    {
        var page = new TabPage(Lang.T("Primer Sensitivity"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true };
        runBtn.Click += (_, _) => RunPrimer();
        bar.Controls.Add(runBtn);
        bar.Controls.Add(new Label { Text = "  " + Lang.T("Each string's own name is used as its primer lot."), AutoSize = true, Margin = new Padding(6, 8, 0, 0), ForeColor = SystemColors.GrayText });

        _primerGrid.Dock = DockStyle.Fill;
        _primerGrid.ReadOnly = true; _primerGrid.AllowUserToAddRows = false; _primerGrid.RowHeadersVisible = false;
        _primerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("lot", Lang.T("Primer lot")), ("n", Lang.T("Shots")), ("mean", "Mean " + _u.VelocityUnitName), ("sd", "SD " + _u.VelocityUnitName), ("es", "ES " + _u.VelocityUnitName) })
            _primerGrid.Columns.Add(n, h);

        page.Controls.Add(_primerGrid); page.Controls.Add(_primerVerdict); page.Controls.Add(bar);
        return page;
    }

    private void RunPrimer()
    {
        var groups = _rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => (IReadOnlyList<double>)g.SelectMany(r => r.Velocities).ToList());
        try
        {
            _lastPrimer = AdvancedDiagnostics.PrimerSensitivity(groups);
            _primerGrid.Rows.Clear();
            foreach (var lot in _lastPrimer.Lots)
                _primerGrid.Rows.Add(lot.PrimerLot, lot.NShots,
                    _u.VelocityValue(lot.MeanMps).ToString("0.0", CultureInfo.InvariantCulture), _u.VelocitySd(lot.SdMps), _u.VelocitySd(lot.EsMps));
            _primerVerdict.Text = string.Format(Lang.T("Best lot: {0}."), _lastPrimer.BestPrimerLot)
                + "  " + (_lastPrimer.PrimerSensitivityDetected ? Lang.T("A real spread between lots was detected.") : Lang.T("No meaningful spread between lots."));
        }
        catch (AdvancedDiagnostics.AdvancedDiagnosticsError ex) { _primerVerdict.Text = ex.Message; _lastPrimer = null; _primerGrid.Rows.Clear(); }
    }

    // ── Neck Tension Correlation ──────────────────────────────────────

    private TabPage BuildNeckTensionTab()
    {
        var page = new TabPage(Lang.T("Neck Tension"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 0, 0) };
        var runBtn = new Button { Text = Lang.T("Analyze"), AutoSize = true };
        runBtn.Click += (_, _) => RunNeckTension();
        bar.Controls.Add(runBtn);
        bar.Controls.Add(new Label { Text = "  " + Lang.T("Name each string with its tested tension, e.g. \"0.05\"."), AutoSize = true, Margin = new Padding(6, 8, 0, 0), ForeColor = SystemColors.GrayText });

        _neckGrid.Dock = DockStyle.Fill;
        _neckGrid.ReadOnly = true; _neckGrid.AllowUserToAddRows = false; _neckGrid.RowHeadersVisible = false;
        _neckGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("str", Lang.T("String")), ("ten", Lang.T("Tension (mm)")), ("sd", "SD " + _u.VelocityUnitName), ("es", "ES " + _u.VelocityUnitName) })
            _neckGrid.Columns.Add(n, h);

        page.Controls.Add(_neckGrid); page.Controls.Add(_neckVerdict); page.Controls.Add(bar);
        return page;
    }

    private static readonly Regex NumberInName = new(@"(-?\d+(?:[.,]\d+)?)", RegexOptions.Compiled);

    private void RunNeckTension()
    {
        var points = new List<AdvancedDiagnostics.NeckTensionPoint>();
        _neckGrid.Rows.Clear();
        foreach (var row in _rows)
        {
            var m = NumberInName.Match(row.Name);
            if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double tension))
                continue;
            var stats = ChronoStatsCalc.Analyze(row.Velocities);
            points.Add(new AdvancedDiagnostics.NeckTensionPoint(tension, EsMps: stats.Es, SdMps: stats.SampleSd));
            _neckGrid.Rows.Add(row.Label, tension.ToString("0.000", CultureInfo.InvariantCulture), _u.VelocitySd(stats.SampleSd), _u.VelocitySd(stats.Es));
        }

        try
        {
            _lastNeck = AdvancedDiagnostics.NeckTensionCorrelation(points);
            var sb = new StringBuilder();
            if (_lastNeck.CorrelationEs is { } cEs) sb.AppendLine(string.Format(Lang.T("Correlation tension<->ES: {0}, optimal tension: {1} mm"), cEs.ToString("0.00", CultureInfo.InvariantCulture), _lastNeck.OptimalTensionEs?.ToString("0.000", CultureInfo.InvariantCulture)));
            if (_lastNeck.CorrelationSd is { } cSd) sb.AppendLine(string.Format(Lang.T("Correlation tension<->SD: {0}, optimal tension: {1} mm"), cSd.ToString("0.00", CultureInfo.InvariantCulture), _lastNeck.OptimalTensionSd?.ToString("0.000", CultureInfo.InvariantCulture)));
            _neckVerdict.Text = sb.Length > 0 ? sb.ToString() : Lang.T("No ES/SD data on the parsed strings.");
        }
        catch (AdvancedDiagnostics.AdvancedDiagnosticsError ex) { _neckVerdict.Text = ex.Message; _lastNeck = null; }
    }

    // ── Write-back ────────────────────────────────────────────────────

    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write diagnostics note to GRT load")); return; }

            var report = new StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            int sections = 0;

            if (_lastFouling is { } f)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Fouling Tracker")}] {f.label}");
                report.AppendLine(f.r.FoulingSuspected ? Lang.T("Fouling suspected.") : Lang.T("No fouling trend detected."));
            }
            if (_lastColdBore is { } cb)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Cold Bore")}] {cb.label}");
                report.AppendLine(FormattableString.Invariant($"delta={cb.r.DeltaMps:0.0} m/s, z={cb.r.ZScore:0.00} -> ") + (cb.r.ColdBoreIsOutlier ? Lang.T("outlier") : Lang.T("normal")));
            }
            if (_lastPrimer is { } pr)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Primer Sensitivity")}]");
                report.AppendLine(string.Format(Lang.T("Best lot: {0}."), pr.BestPrimerLot));
            }
            if (_lastNeck is { } nt)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Neck Tension")}]");
                if (nt.CorrelationEs is { } ce) report.AppendLine(FormattableString.Invariant($"corr(tension,ES)={ce:0.00}, optimal={nt.OptimalTensionEs:0.000}mm"));
                if (nt.CorrelationSd is { } cs) report.AppendLine(FormattableString.Invariant($"corr(tension,SD)={cs:0.00}, optimal={nt.OptimalTensionSd:0.000}mm"));
            }
            if (_lastPressure is { } pt)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Pressure Trend")}]");
                report.AppendLine(Lang.T(pt.PressureEstimate));
            }
            if (_lastSessionTrends.Count > 0)
            {
                sections++;
                report.AppendLine().AppendLine($"[{Lang.T("Session Trend")}]");
                var flagged = _lastSessionTrends.Where(t => t.SdSeverity != "ok" || t.EsSeverity != "ok").ToList();
                if (flagged.Count == 0) report.AppendLine(Lang.T("No concerning trend across any charge."));
                foreach (var t in flagged)
                    report.AppendLine(FormattableString.Invariant($"{t.Caliber} {t.ChargeGr:0.0}gr: SD {t.SdSeverity}, ES {t.EsSeverity}"));
            }

            if (sections == 0)
            {
                MessageBox.Show(this, Lang.T("Run at least one analysis first."), Lang.T("Write diagnostics note to GRT load"));
                return;
            }

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, report.ToString());
            string outPath = writeDoc.SaveSibling("diagnostics");
            await _grt.LoadFileAsync(outPath);
            _loadStatus.Text = Lang.T("Diagnostics note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write diagnostics note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
