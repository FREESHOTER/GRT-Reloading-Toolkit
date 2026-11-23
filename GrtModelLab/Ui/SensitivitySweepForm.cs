using System.Globalization;
using GrtModelLab.SensitivitySweep;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtModelLab.Ui;

/// <summary>
/// For each selected GRT input, briefly re-simulates the active load with that input nudged by a
/// (user-editable) delta, and reports how much GRT's own predicted muzzle velocity / max pressure
/// moves. This studies GRT's own model, not the barrel — it answers "which of these inputs is
/// worth measuring precisely", not "what will my barrel actually do".
/// </summary>
internal sealed class SensitivitySweepForm : Form
{
    private const string NoteTitle = "Model Lab Sensitivity Sweep";

    private const double MmPerInch = 25.4;
    // GRT's own convention for case capacity (see e.g. "Volume bossolo ... grain H2O" in its UI):
    // water at 1 g/cm3, 1 g = 15.4323584 grains.
    private const double GrainsH2OPerCm3 = 15.4323584;

    private readonly GrtClient? _grt;
    private readonly List<(SweepParam Param, CheckBox Check, NumericUpDown? Delta)> _rows = new();
    private readonly List<(SweepParam Param, NumericUpDown Delta, Label UnitLabel)> _lengthRows = new();
    private readonly List<(SweepParam Param, NumericUpDown Delta, Label UnitLabel)> _volumeRows = new();
    private readonly ComboBox _units = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Button _runBtn = new() { Text = "Run sweep (sweeps GRT)", AutoSize = true };
    private readonly Button _pickBtn = new() { Text = "Open a different load…", AutoSize = true };
    private readonly Button _writeBtn = new() { Text = "Write sensitivity note to GRT load", AutoSize = true, Enabled = false };
    private readonly Label _targetLabel = new() { Text = "Targets whatever load is on top in GRT." };

    private string? _basePath;
    private SweepReport? _report;

    public SensitivitySweepForm(GrtClient? grt)
    {
        _grt = grt;
        Text = "GRT Model Lab — Input Sensitivity Sweep";
        Width = 900; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 460);
        Build();
        NudFix.ApplyTo(this);
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT) — connect to GRT to run a sweep";
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 340, AutoScroll = true, Padding = new Padding(8) };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        var hint = new Label
        {
            Text = "Pick which GRT inputs to nudge, and by how much. Each checked\r\nparameter is tested at +/- its delta (or, for friction, at 0/50/100 bar).",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
        };
        flow.Controls.Add(hint);

        // The sweep always acts on whatever load GRT currently has on top (that's the only load GRT
        // itself can simulate) — this lets you point GRT at a specific file from here instead of
        // having to switch tabs by hand in GRT first.
        _pickBtn.Margin = new Padding(0, 0, 0, 4);
        _pickBtn.Click += async (_, _) => await PickLoadAsync();
        flow.Controls.Add(_pickBtn);
        _targetLabel.AutoSize = true;
        _targetLabel.ForeColor = SystemColors.GrayText;
        _targetLabel.Margin = new Padding(0, 0, 0, 8);
        flow.Controls.Add(_targetLabel);

        var unitsRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        unitsRow.Controls.Add(new Label { Text = "Deltas in", AutoSize = true, Margin = new Padding(0, 3, 4, 0) });
        // "imperial" here means what a US reloader actually uses: inches for length, grains of
        // water (not cubic inches) for case capacity — matches GRT's own "grain H2O" convention.
        _units.Items.AddRange(new object[] { "metric (mm, cm³)", "imperial (in, gr H2O)" });
        _units.SelectedIndex = 0;
        _units.SelectedIndexChanged += (_, _) => OnUnitsChanged();
        unitsRow.Controls.Add(_units);
        flow.Controls.Add(unitsRow);

        foreach (var p in SweepParams.All)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            var chk = new CheckBox { Text = p.Label, AutoSize = true, Checked = true, Width = 220 };
            row.Controls.Add(chk);
            NumericUpDown? nud = null;
            if (p.IsAbsolute)
            {
                row.Controls.Add(new Label { Text = $"tests {string.Join("/", p.AbsoluteValues!.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)))} {p.Unit}", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 3, 0, 0) });
            }
            else
            {
                // Minimum stays tiny rather than a tidy "0.001" — that reads fine for an mm delta but
                // silently floors an inch-converted one (e.g. Dz's 0.02 mm is 0.00079 in, below 0.001)
                // and OnUnitsChanged()'s Math.Clamp would otherwise round it up to the floor instead
                // of converting it — a real bug caught by live-testing the mm/in toggle, not review.
                nud = new NumericUpDown { DecimalPlaces = 3, Increment = (decimal)(p.Delta / 10), Minimum = 0.00001m, Maximum = 1000m, Value = (decimal)p.Delta, Width = 70 };
                row.Controls.Add(nud);
                var unitLabel = new Label { Text = $"± {p.Unit}", AutoSize = true, Margin = new Padding(4, 3, 0, 0) };
                row.Controls.Add(unitLabel);
                // The metric/imperial toggle only applies to GRT inputs with a real imperial
                // counterpart: a length (mm/in) or a case capacity (cm3/grain H2O). A mass (mp, already
                // in gr) or a pressure (gpressure, bar) has no imperial equivalent to switch to.
                if (p.Unit == "mm") _lengthRows.Add((p, nud, unitLabel));
                else if (p.Unit == "cm3") _volumeRows.Add((p, nud, unitLabel));
            }
            flow.Controls.Add(row);
            _rows.Add((p, chk, nud));
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dmv", HeaderText = "Δ MV m/s" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "pmax", HeaderText = "Pmax" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dpmax", HeaderText = "Δ Pmax" });

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

    /// <summary>
    /// Converts every length- and volume-based delta NumericUpDown between metric and imperial.
    /// Reads each control's current value converted to its CANONICAL unit (mm / cm3) BEFORE touching
    /// DecimalPlaces/Value on any of them — the same read-before-clamp-or-truncate hazard as the
    /// mm/inch toggle in BarrelProfileForm and BrassForm's unit switch (PR #1's fix): changing
    /// DecimalPlaces first can silently round away precision the conversion still needs.
    /// </summary>
    private void OnUnitsChanged()
    {
        bool toImperial = _units.SelectedIndex == 1;
        var mmValues = _lengthRows.Select(r => (double)r.Delta.Value * (_wasImperial ? MmPerInch : 1.0)).ToList();
        var cm3Values = _volumeRows.Select(r => (double)r.Delta.Value * (_wasImperial ? 1.0 / GrainsH2OPerCm3 : 1.0)).ToList();
        _wasImperial = toImperial;

        for (int i = 0; i < _lengthRows.Count; i++)
        {
            var (_, nud, unitLabel) = _lengthRows[i];
            double display = toImperial ? mmValues[i] / MmPerInch : mmValues[i];
            nud.DecimalPlaces = toImperial ? 5 : 3;
            nud.Increment = toImperial ? 0.0001m : 0.01m;
            nud.Value = (decimal)Math.Clamp(display, (double)nud.Minimum, (double)nud.Maximum);
            unitLabel.Text = $"± {(toImperial ? "in" : "mm")}";
        }
        for (int i = 0; i < _volumeRows.Count; i++)
        {
            var (_, nud, unitLabel) = _volumeRows[i];
            double display = toImperial ? cm3Values[i] * GrainsH2OPerCm3 : cm3Values[i];
            nud.Increment = toImperial ? 0.1m : 0.01m;
            nud.Value = (decimal)Math.Clamp(display, (double)nud.Minimum, (double)nud.Maximum);
            unitLabel.Text = toImperial ? "± gr H2O" : "± cm3";
        }
    }

    private bool _wasImperial;

    /// <summary>The delta a length-parameter's NumericUpDown holds right now, converted to mm —
    /// every write to a GRT load happens in GRT's own native mm regardless of what unit the user
    /// typed the delta in.</summary>
    private double DeltaMm(NumericUpDown nud) => (double)nud.Value * (_units.SelectedIndex == 1 ? MmPerInch : 1.0);

    /// <summary>The delta a volume-parameter's (casevol) NumericUpDown holds right now, converted to
    /// cm3 — the unit GRT's own file format uses regardless of what the user is looking at.</summary>
    private double DeltaCm3(NumericUpDown nud) => (double)nud.Value * (_units.SelectedIndex == 1 ? 1.0 / GrainsH2OPerCm3 : 1.0);

    /// <summary>
    /// Opens a chosen .grtload in GRT (a new tab, brought to the front) so the sweep — which can only
    /// ever act on whatever GRT currently has on top — targets that file instead of whatever tab
    /// happened to be active already.
    /// </summary>
    private async Task PickLoadAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
        using var dlg = new OpenFileDialog { Filter = "GRT load (*.grtload)|*.grtload", Title = "Open a load to sweep" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (GrtLoadDoc.LooksLikeGeneratedSibling(dlg.FileName))
        {
            MessageBox.Show(this, "That looks like a generated toolkit/import file, not a real load — pick your original .grtload instead.",
                "Open a different load", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            await _grt.LoadFileAsync(dlg.FileName);
            _targetLabel.Text = "Sweeping: " + Path.GetFileName(dlg.FileName);
            _status.Text = $"Opened '{Path.GetFileName(dlg.FileName)}' in GRT — ready to sweep.";
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
                MessageBox.Show(this, "The active tab is a generated file — switch to your real load in GRT.", "Sensitivity sweep",
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
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_sensitivity_sweep_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            int seq = 0;

            var pristine = GrtLoadDoc.Load(basePath);
            string caliberName = pristine.CaliberName;
            string powderName = pristine.PropellantName;

            var (bmv, bpm, bpu) = await RunOneAsync(basePath, _ => { }, tmpDir, seq++);
            AppendLog(bmv is { } b0 ? $"baseline -> MV {b0:0.0} m/s, Pmax {bpm:0} {bpu}" : "baseline -> no sim result (compute a result in GRT first)");

            var report = new SweepReport { BaselineMv = bmv, BaselinePmax = bpm, PmaxUnit = bpu };

            foreach (var (param, _, delta) in selected)
            {
                double deltaNative = delta is null ? param.Delta
                    : param.Unit == "mm" ? DeltaMm(delta)
                    : param.Unit == "cm3" ? DeltaCm3(delta)
                    : (double)delta.Value;
                var variants = SweepEngine.BuildVariants(param, deltaNative);
                foreach (var v in variants)
                {
                    var (mv, pmax, pmu) = await RunOneAsync(basePath, doc => SweepEngine.ApplyVariant(doc, param, v.Value, v.Absolute), tmpDir, seq++);
                    report.Points.Add(new SweepPoint { Param = param, VariantLabel = v.Label, MvMps = mv, Pmax = pmax, PmaxUnit = pmu });
                    AppendLog(mv is { } m ? $"{param.Label} {v.Label} -> MV {m:0.0} m/s, Pmax {pmax:0} {pmu}" : $"{param.Label} {v.Label} -> no sim result");
                    RefreshGrid(report);
                }
            }

            await _grt.LoadFileAsync(basePath);
            try { Directory.Delete(tmpDir, true); } catch { }

            _report = report;
            _summary.Text = report.Build(caliberName, powderName).Replace("\n", "\r\n") + "\r\n" + _summary.Text;
            _writeBtn.Enabled = report.Points.Any(p => p.MvMps is not null);
            _status.Text = $"swept {report.Points.Count} variant(s) across {selected.Count} parameter(s) — close the extra GRT tabs when done.";
        }
        catch (Exception ex) { Err(ex); }
        finally { _runBtn.Enabled = true; }
    }

    private async Task<(double? mv, double? pmax, string pmaxUnit)> RunOneAsync(string basePath, Action<GrtLoadDoc> mutate, string tmpDir, int seq)
    {
        var doc = GrtLoadDoc.Load(basePath);
        doc.SetInput("propellant", "laddercnt", "0");     // ladder off — one deterministic charge per run
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
            string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
            doc.AddNote(headline, _report.Build(doc.CaliberName, doc.PropellantName));
            string outPath = doc.SaveSibling("sensitivity");
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
