using System.Globalization;
using GrtSensitivityLab.Sweep;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtSensitivityLab.Ui;

/// <summary>
/// For each selected GRT input, briefly re-simulates the active load with that input nudged by a
/// (user-editable) delta, and reports how much GRT's own predicted muzzle velocity / max pressure
/// moves -- ranked by impact, largest first. This answers "which of these inputs is worth
/// measuring precisely", complementing <see cref="WhatIfForm"/> ("here's the value I actually
/// measured, what does GRT predict now"). Ported from GrtModelLab's dormant
/// Ui/SensitivitySweepForm.cs, which stayed unreferenced there since 2026-09-11 for the same
/// reason this plugin exists: it studies GRT's own model in general, not the barrel specifically.
/// </summary>
internal sealed class SensitivitySweepForm : Form
{
    private const string NoteTitle = "Sensitivity Lab Sweep";

    private const double MmPerInch = 25.4;
    // GRT's own convention for case capacity (see e.g. "Volume bossolo ... grain H2O" in its UI):
    // water at 1 g/cm3, 1 g = 15.4323584 grains.
    private const double GrainsH2OPerCm3 = 15.4323584;

    private readonly GrtClient? _grt;
    private readonly List<Row> _rows = new();
    private readonly ToolTip _tips = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Button _runBtn = new() { Text = "Run sweep (sweeps GRT)", AutoSize = true };
    private readonly Button _pickBtn = new() { Text = "Open a different load...", AutoSize = true };
    private readonly Button _writeBtn = new() { Text = "Write sensitivity note to GRT load", AutoSize = true, Enabled = false };
    private readonly Label _targetLabel = new() { Text = "Targets whatever load is on top in GRT." };

    /// <summary>One parameter's row. <see cref="Delta"/> and <see cref="UnitCombo"/> are both null
    /// for an absolute-test-value parameter (gfriction, tested at fixed 0/50/100 bar rather than a
    /// +/- delta around the file's own value) -- <see cref="StaticNote"/> carries its description
    /// instead. <see cref="UnitCombo"/> alone is null for a parameter with no metric/imperial
    /// choice (mass is always gr, pressure is always bar). Per-row unit toggle, not one form-wide
    /// one, matching the convention <see cref="WhatIfForm"/> settled on after live-testing.</summary>
    private sealed class Row
    {
        public required SweepParam Param { get; init; }
        public required CheckBox Check { get; init; }
        public NumericUpDown? Delta { get; init; }
        public ComboBox? UnitCombo { get; init; }
        public Label? StaticNote { get; init; }
        public bool WasImperial { get; set; }
    }

    private string? _basePath;
    private SweepReport? _report;

    public SensitivitySweepForm(GrtClient? grt)
    {
        _grt = grt;
        Text = "GRT Sensitivity Lab -- Sensitivity Sweep";
        // Same headroom as WhatIfForm, for the same reason: fitting 8 parameter rows plus buttons
        // without the sidebar needing its own vertical scrollbar (measured against a real rendered
        // window, not assumed).
        Width = 940; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Build();
        NudFix.ApplyTo(this);
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT) -- connect to GRT to run a sweep";
    }

    private void Build()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 380, AutoScroll = true, Padding = new Padding(8) };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        // MaximumSize (not manual \r\n line breaks) lets WinForms wrap this to the panel's own
        // width -- a hard-coded break assuming a line width that doesn't match the panel's is
        // exactly what forced a needless horizontal scrollbar in WhatIfForm's first draft.
        var hint = new Label
        {
            Text = "Pick which GRT inputs to nudge, and by how much. Each checked parameter is tested at +/- its delta (or, for friction, at 0/50/100 bar), ranked by impact.",
            AutoSize = true,
            MaximumSize = new Size(344, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
        };
        flow.Controls.Add(hint);

        // The sweep always acts on whatever load GRT currently has on top (the only load GRT
        // itself can simulate) -- lets you point GRT at a specific file from here instead of
        // switching tabs by hand in GRT first.
        _pickBtn.Margin = new Padding(0, 0, 0, 4);
        _pickBtn.Click += async (_, _) => await PickLoadAsync();
        flow.Controls.Add(_pickBtn);
        _targetLabel.AutoSize = true;
        _targetLabel.ForeColor = SystemColors.GrayText;
        _targetLabel.Margin = new Padding(0, 0, 0, 10);
        flow.Controls.Add(_targetLabel);

        foreach (var p in SweepParams.All)
        {
            // Two lines per parameter (label above, controls below), not one long horizontal row
            // -- see WhatIfForm's own layout comment for why that matters here too.
            var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 8) };

            var chk = new CheckBox { Text = p.Label, AutoSize = true, Checked = true, Margin = new Padding(0, 0, 0, 2) };
            group.Controls.Add(chk);

            var valueRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(18, 0, 0, 0) };

            NumericUpDown? delta = null;
            ComboBox? unitCombo = null;
            Label? staticNote = null;

            if (p.IsAbsolute)
            {
                // Currently only gfriction ("bullet resistance / friction" in GRT's own Projectile
                // window): a value RELATIVE to GRT's computed default, not an absolute physical
                // friction figure -- 0 means "use the default", not "zero resistance" (see
                // doku/en/doku/inputfields-projectile.txt). Worded as "over GRT's default" rather
                // than a bare "0/50/100 bar" so that isn't misread as literal zero friction.
                staticNote = new Label { Text = $"tests {string.Join("/", p.AbsoluteValues!.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)))} {p.Unit} over GRT's default", AutoSize = true, ForeColor = SystemColors.GrayText };
                valueRow.Controls.Add(staticNote);
                _tips.SetToolTip(chk, "Relative to GRT's own default, not an absolute value: 0 = GRT's default resistance (not zero friction).");
                _tips.SetToolTip(staticNote, "Relative to GRT's own default, not an absolute value: 0 = GRT's default resistance (not zero friction).");
            }
            else
            {
                valueRow.Controls.Add(new Label { Text = "+/-", AutoSize = true, Margin = new Padding(0, 3, 4, 0) });
                // Minimum stays tiny rather than a tidy "0.001" -- that reads fine for an mm delta
                // but silently floors an inch-converted one (e.g. Dz's 0.02 mm is 0.00079 in, below
                // 0.001), a real bug caught by live-testing the mm/in toggle in GrtModelLab's
                // original SensitivitySweepForm, not review.
                delta = new NumericUpDown { DecimalPlaces = 3, Increment = (decimal)(p.Delta / 10), Minimum = 0.00001m, Maximum = 1000m, Value = (decimal)p.Delta, Width = 80, Margin = new Padding(0, 0, 4, 0) };
                valueRow.Controls.Add(delta);

                if (IsLengthUnit(p) || IsVolumeUnit(p))
                {
                    unitCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
                    unitCombo.Items.AddRange(IsLengthUnit(p) ? new object[] { "mm", "in" } : new object[] { "cm3", "gr H2O" });
                    unitCombo.SelectedIndex = 0;
                    valueRow.Controls.Add(unitCombo);
                }
                else
                {
                    valueRow.Controls.Add(new Label { Text = p.Unit, AutoSize = true, Margin = new Padding(0, 3, 0, 0) });
                }
            }

            group.Controls.Add(valueRow);
            flow.Controls.Add(group);
            var row = new Row { Param = p, Check = chk, Delta = delta, UnitCombo = unitCombo, StaticNote = staticNote };
            if (unitCombo != null) unitCombo.SelectedIndexChanged += (_, _) => OnRowUnitChanged(row);
            _rows.Add(row);
        }

        _runBtn.Margin = new Padding(0, 10, 0, 0);
        _runBtn.Click += async (_, _) => await RunSweepAsync();
        flow.Controls.Add(_runBtn);
        left.Controls.Add(flow);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "param", HeaderText = "Parameter" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "variant", HeaderText = "Variant" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "mv", HeaderText = "Sim MV m/s" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dmv", HeaderText = "d MV m/s" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "pmax", HeaderText = "Pmax" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dpmax", HeaderText = "d Pmax" });

        _summary.Multiline = true; _summary.ReadOnly = true; _summary.ScrollBars = ScrollBars.Vertical;
        _summary.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        _summary.Dock = DockStyle.Bottom; _summary.Height = 160;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _writeBtn.Click += async (_, _) => await WriteNoteAsync();
        bottom.Controls.Add(_writeBtn);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(left);
    }

    private bool IsLengthUnit(SweepParam p) => p.Unit == "mm";
    private bool IsVolumeUnit(SweepParam p) => p.Unit == "cm3";
    private bool IsImperial(Row r) => r.UnitCombo is { SelectedIndex: 1 };

    private double ToNative(Row r, double display) =>
        IsLengthUnit(r.Param) ? (IsImperial(r) ? display * MmPerInch : display) :
        IsVolumeUnit(r.Param) ? (IsImperial(r) ? display / GrainsH2OPerCm3 : display) :
        display;

    private double ToDisplay(Row r, double native) =>
        IsLengthUnit(r.Param) ? (IsImperial(r) ? native / MmPerInch : native) :
        IsVolumeUnit(r.Param) ? (IsImperial(r) ? native * GrainsH2OPerCm3 : native) :
        native;

    private double ToNativeWith(Row r, double display, bool wasImperial) =>
        IsLengthUnit(r.Param) ? (wasImperial ? display * MmPerInch : display) :
        IsVolumeUnit(r.Param) ? (wasImperial ? display / GrainsH2OPerCm3 : display) :
        display;

    /// <summary>
    /// Converts one row's delta NumericUpDown between metric and imperial when that row's own unit
    /// combo changes -- per-row, not a single form-wide toggle (see the Row class comment). Reads
    /// the control's current value converted to its CANONICAL unit (mm / cm3) BEFORE touching
    /// DecimalPlaces/Value -- the same read-before-clamp-or-truncate hazard noted where the delta
    /// NumericUpDown is created above.
    /// </summary>
    private void OnRowUnitChanged(Row r)
    {
        if (r.Delta is null) return;
        bool toImperial = IsImperial(r);
        double native = ToNativeWith(r, (double)r.Delta.Value, r.WasImperial);
        r.WasImperial = toImperial;

        double display = ToDisplay(r, native);
        r.Delta.DecimalPlaces = IsLengthUnit(r.Param) ? (toImperial ? 5 : 3) : (toImperial ? 1 : 3);
        r.Delta.Increment = IsLengthUnit(r.Param) ? (toImperial ? 0.0001m : 0.01m) : (toImperial ? 0.1m : 0.01m);
        r.Delta.Value = (decimal)Math.Clamp(display, (double)r.Delta.Minimum, (double)r.Delta.Maximum);
    }

    /// <summary>
    /// Opens a chosen .grtload in GRT (a new tab, brought to the front) so the sweep -- which can
    /// only ever act on whatever GRT currently has on top -- targets that file instead of whatever
    /// tab happened to be active already.
    /// </summary>
    private async Task PickLoadAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
        using var dlg = new OpenFileDialog { Filter = "GRT load (*.grtload)|*.grtload", Title = "Open a load to sweep" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (GrtLoadDoc.LooksLikeGeneratedSibling(dlg.FileName))
        {
            MessageBox.Show(this, "That looks like a generated toolkit/import file, not a real load -- pick your original .grtload instead.",
                "Open a different load", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            await _grt.LoadFileAsync(dlg.FileName);
            _targetLabel.Text = "Sweeping: " + Path.GetFileName(dlg.FileName);
            _status.Text = $"Opened '{Path.GetFileName(dlg.FileName)}' in GRT -- ready to sweep.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private async Task RunSweepAsync()
    {
        var selected = _rows.Where(r => r.Check.Checked).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Select at least one parameter."); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }

        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file -- switch to your real load in GRT.", "Sensitivity sweep",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _basePath = top.file;
            _targetLabel.Text = "Sweeping: " + Path.GetFileName(top.file);
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            int totalRuns = 1 + selected.Sum(r => r.Param.IsAbsolute ? r.Param.AbsoluteValues!.Length : 2);
            if (MessageBox.Show(this,
                    $"This will briefly open {totalRuns} tabs in GRT (one baseline + one per test value) to read each simulated result, then reopen your load.\n\nContinue?",
                    "Run sweep", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _runBtn.Enabled = false;
            _grid.Rows.Clear();
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_sensitivity_lab_sweep_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            int seq = 0;

            var pristine = GrtLoadDoc.Load(basePath);
            string caliberName = pristine.CaliberName;
            string powderName = pristine.PropellantName;

            var (bmv, bpm, bpu) = await RunOneAsync(basePath, _ => { }, tmpDir, seq++);
            AppendLog(bmv is { } b0 ? $"baseline -> MV {b0:0.0} m/s, Pmax {bpm:0} {bpu}" : "baseline -> no sim result (compute a result in GRT first)");

            var report = new SweepReport { BaselineMv = bmv, BaselinePmax = bpm, PmaxUnit = bpu };

            foreach (var r in selected)
            {
                double deltaNative = r.Param.IsAbsolute ? r.Param.Delta : ToNative(r, (double)r.Delta!.Value);
                var variants = SweepEngine.BuildVariants(r.Param, deltaNative);
                foreach (var v in variants)
                {
                    var (mv, pmax, pmu) = await RunOneAsync(basePath, doc => SweepEngine.ApplyVariant(doc, r.Param, v.Value, v.Absolute), tmpDir, seq++);
                    report.Points.Add(new SweepPoint { Param = r.Param, VariantLabel = v.Label, MvMps = mv, Pmax = pmax, PmaxUnit = pmu });
                    AppendLog(mv is { } m ? $"{r.Param.Label} {v.Label} -> MV {m:0.0} m/s, Pmax {pmax:0} {pmu}" : $"{r.Param.Label} {v.Label} -> no sim result");
                    RefreshGrid(report);
                }
            }

            await _grt.LoadFileAsync(basePath);
            try { Directory.Delete(tmpDir, true); } catch { }

            _report = report;
            _summary.Text = report.Build(caliberName, powderName).Replace("\n", "\r\n") + "\r\n" + _summary.Text;
            _writeBtn.Enabled = report.Points.Any(p => p.MvMps is not null);
            _status.Text = $"swept {report.Points.Count} variant(s) across {selected.Count} parameter(s) -- close the extra GRT tabs when done.";
        }
        catch (Exception ex) { Err(ex); }
        finally { _runBtn.Enabled = true; }
    }

    // A unique path per run ("sweep_NNN.grtload"), not one reused scratch file: tried reusing a
    // single path 2026-09-16 on the theory that Load_File "activates it if already open" would
    // make GRT re-read the changed content -- live-tested and confirmed WRONG: GRT just reactivates
    // the existing tab showing its ORIGINAL (stale) result without re-parsing the file, so every
    // variant after the first reported the same value. GRT's plugin API has no Close-a-tab command
    // either (checked the full list in doku/en/doku/plugin-api.txt: Get_Tab*/Get_ColorPreset*/
    // Get_Chunk/Close_ChunkStream, nothing else) -- so one tab per variant, left for the user to
    // close by hand, is the only correct option the protocol allows.
    private async Task<(double? mv, double? pmax, string pmaxUnit)> RunOneAsync(string basePath, Action<GrtLoadDoc> mutate, string tmpDir, int seq)
    {
        var doc = GrtLoadDoc.Load(basePath);
        doc.SetInput("propellant", "laddercnt", "0");     // ladder off -- one deterministic charge per run
        mutate(doc);
        string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "sweep_{0:000}.grtload", seq));
        doc.Save(tmp);

        await _grt!.LoadFileAsync(tmp);
        await Task.Delay(1400);                            // let GRT compute
        var t2 = await _grt.GetTabOnTopAsync();
        var res = await _grt.GetTabResultsAsync(t2.handle);
        return (res.MuzzleVelocityMps, res.MaxPressure, res.MaxPressureUnit);
    }

    private void RefreshGrid(SweepReport report)
    {
        _grid.Rows.Clear();
        var ci = CultureInfo.InvariantCulture;
        foreach (var p in report.BySensitivity())
        {
            double? dmv = p.MvMps is { } mv && report.BaselineMv is { } b ? mv - b : null;
            double? dpm = p.Pmax is { } pm && report.BaselinePmax is { } bp ? pm - bp : null;
            _grid.Rows.Add(
                p.Param.Label,
                p.VariantLabel,
                p.MvMps is { } m ? m.ToString("0.0", ci) : "",
                dmv is { } d ? SweepReport.Signed(d, "0.0", ci) : "",
                p.Pmax is { } pp ? pp.ToString("0", ci) : "",
                dpm is { } dp ? SweepReport.Signed(dp, "0", ci) : "");
        }
    }

    private async Task WriteNoteAsync()
    {
        try
        {
            if (_basePath is null || _report is null || _grt is not { Connected: true }) return;
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, _report.Build(doc.CaliberName, doc.PropellantName));
            string outPath = doc.SaveSibling("sweep");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = "Sensitivity note written and opened in GRT.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private void AppendLog(string line) => _summary.SafeAppend(line);

    private void Err(Exception ex)
    {
        AppendLog("ERROR: " + ex.Message);
        MessageBox.Show(this, ex.Message, "Sensitivity sweep", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
