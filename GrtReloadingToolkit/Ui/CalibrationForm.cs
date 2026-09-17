using System.Globalization;
using GrtReloadingToolkit.Cal;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Barrel calibration: compare GRT's simulated muzzle velocity to what the barrel actually
/// does, over one or more charges, and suggest a Ba tweak / offset. When the offset varies with
/// charge (a burn-shape mismatch, not just a scale one), a second sweep at a nudged "a0" lets
/// <see cref="CalResult.FitBaAndA0"/> fit both Ba and a0 at once -- see that method's own doc.
/// </summary>
internal sealed class CalibrationForm : Form
{
    private const string NoteTitle = "Barrel Calibration";

    private readonly GrtClient? _grt;
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Button _writeNote = new() { Text = Lang.T("Write calibration note"), AutoSize = true, Enabled = false };
    private readonly Button _writeBa = new() { Text = Lang.T("Write Ba-corrected .grtload"), AutoSize = true, Enabled = false };
    private readonly Button _writeBaA0 = new() { Text = Lang.T("Write Ba+a0-corrected .grtload"), AutoSize = true, Enabled = false };

    // a0 (prog/deg burn-shape coefficient) fit -- the two-parameter extension of the Ba-only fit
    // above, for when the offset varies with charge (see CalResult.FitBaAndA0's own doc for why a0,
    // not GRT's "k", is the right second knob).
    private const double A0PerturbFrac = 0.05;
    private readonly CalResult _result = new();
    private readonly Action<string> _logHandler;
    private double? _baOld;
    private double? _a0Old;
    private double _a0Delta;
    private CalResult.ShapeFit? _shapeFit;
    private string _powder = "powder";
    private string? _basePath;
    private bool _suppressGrid;

    public CalibrationForm(GrtClient? grt)
    {
        _grt = grt;
        _logHandler = AppendLog;
        Text = AppVersion.Title(Lang.T("GRT Barrel Calibration"));
        // Wider than the original 820: the new shape-fit button pushed "Remove row" onto a second,
        // clipped row of the toolbar's fixed 40px height -- caught by rendering the actual window,
        // not from reading the FlowLayoutPanel code. The toolbar itself is now tall enough for two
        // rows regardless, so a narrower resize wraps visibly instead of clipping silently.
        Width = 960; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);
        Build();
        NudFix.ApplyTo(this);
        if (_grt != null) _grt.Log += _logHandler;
        _status.Text = _grt is { Connected: true } ? string.Format(Lang.T("connected to GRT :{0}"), _grt.Port) : Lang.T("stand-alone (no GRT)");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_grt != null) _grt.Log -= _logHandler;
        base.OnFormClosed(e);
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(6, 6, 0, 0), WrapContents = true };
        var loadBtn = new Button { Text = Lang.T("Load measured from GRT load"), AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadMeasuredAsync();
        top.Controls.Add(loadBtn);
        var capAllBtn = new Button { Text = Lang.T("Capture ALL sim MV (sweeps GRT)"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        capAllBtn.Click += async (_, _) => await CaptureAllAsync();
        top.Controls.Add(capAllBtn);
        var capBtn = new Button { Text = Lang.T("Capture current charge only"), AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
        capBtn.Click += async (_, _) => await CaptureCurrentAsync();
        top.Controls.Add(capBtn);
        var capShapeBtn = new Button { Text = Lang.T("Capture shape-fit sweep (a0)"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        capShapeBtn.Click += async (_, _) => await CaptureAllPertA0Async();
        top.Controls.Add(capShapeBtn);
        var delBtn = new Button { Text = Lang.T("Remove row"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        delBtn.Click += (_, _) => RemoveRow();
        top.Controls.Add(delBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // Charges are stored in grains and this column, like the velocities below it, shows and
        // accepts GRT's unit. It was the one cell here still taking raw grains under a "gr" label.
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "chg", HeaderText = Lang.T("Charge") + " " + GrtUnits.Current.ChargeUnitName });
        // Velocities are stored in m/s; these three columns show and accept GRT's unit instead, so
        // a shooter reading ft/s off their chronograph types the number they are looking at.
        string vu = GrtUnits.Current.VelocityUnitName;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "meas", HeaderText = Lang.T("Meas MV") + " " + vu });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sim", HeaderText = Lang.T("Sim MV") + " " + vu });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dm", HeaderText = "Δ " + vu, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dp", HeaderText = "Δ %", ReadOnly = true });
        // Rebuild after the edit-control teardown completes — clearing rows inside CellEndEdit
        // disposes the DataGridView's editing TextBox mid-event ("Cannot access a disposed object").
        _grid.CellEndEdit += (_, _) =>
        {
            if (_suppressGrid) return;
            BeginInvoke(new Action(() => { SyncFromGrid(); RefreshGrid(); Recompute(); }));
        };

        _summary.Multiline = true; _summary.ReadOnly = true; _summary.ScrollBars = ScrollBars.Vertical;
        _summary.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        _summary.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(210);
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_summary);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _writeNote.Click += async (_, _) => await WriteAsync(false);
        _writeBa.Click += async (_, _) => await WriteAsync(true);
        _writeBaA0.Click += async (_, _) => await WriteShapeAsync();
        bottom.Controls.Add(_writeBaA0);
        bottom.Controls.Add(_writeBa);
        bottom.Controls.Add(_writeNote);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private async Task<GrtLoadDoc?> OpenBaseAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return null; }
        var top = await _grt.GetTabOnTopAsync();
        if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
        {
            MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Calibration")); return null;
        }
        if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
        {
            MessageBox.Show(this, Lang.T("The active tab is a generated file — switch to your real load in GRT."), Lang.T("Calibration"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        _basePath = top.file;
        // Ba/powder from the ORIGINAL load — never a toolkit sibling that may already carry a
        // corrected Ba, or a second calibration would compound the correction.
        string pristine = GrtLoadDoc.PristineBasePath(top.file);
        var pdoc = GrtLoadDoc.Load(File.Exists(pristine) ? pristine : top.file);
        _baOld = pdoc.PropellantBa is > 0 ? pdoc.PropellantBa : null;
        _a0Old = pdoc.InputNumber("propellant", "a0") is { } a0 && a0 > 0 ? a0 : null;
        _powder = string.IsNullOrWhiteSpace(pdoc.PropellantName) ? "powder" : pdoc.PropellantName;
        // Measurements / everything else from the accumulator sibling if it exists (that's where
        // a chrono import lands).
        return GrtLoadDoc.OpenForToolkitEdit(top.file);
    }

    private async Task LoadMeasuredAsync()
    {
        try
        {
            var doc = await OpenBaseAsync();
            if (doc is null) return;

            var byCharge = new SortedDictionary<double, double>();
            foreach (var m in doc.Measurements())
                foreach (var c in m.Charges)
                {
                    var v = c.Shots.Select(s => s.VelocityMps).Where(x => x > 0).ToList();
                    if (v.Count == 0 || c.ChargeGrains is not { } g) continue;
                    byCharge[Math.Round(g, 2)] = StringStats.From(v).Mean;
                }
            if (byCharge.Count == 0) { AppendLog("no measured charges found in the load's Measurements"); return; }

            _result.Points.Clear();
            foreach (var kv in byCharge)
                _result.Points.Add(new CalPoint { ChargeGr = kv.Key, MeasMps = kv.Value });
            AppendLog($"loaded {byCharge.Count} measured charge(s) from '{doc.CaliberName}'  Ba={_baOld}");
            RefreshGrid();
            Recompute();
        }
        catch (Exception ex) { Err(ex); }
    }

    /// <summary>Reads GRT's current sim MV and files it under GRT's CURRENT charge (from the load's mc).</summary>
    private async Task CaptureCurrentAsync()
    {
        try
        {
            if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }

            double? chg = GrtLoadDoc.Load(top.file).PropellantChargeGr;
            if (chg is not { } c || c <= 0) { MessageBox.Show(this, Lang.T("Could not read the current charge (mc) from the load.")); return; }

            var res = await _grt.GetTabResultsAsync(top.handle);
            if (res.MuzzleVelocityMps is not { } sim || sim <= 0)
            {
                AppendLog("Get_TabResults returned no muzzle velocity — compute a result in GRT first.");
                return;
            }
            SetSim(c, sim);
            AppendLog($"captured sim MV {GrtUnits.Current.Velocity(sim)} for {GrtUnits.Current.Charge(c)} (GRT's current charge)  Pmax {res.MaxPressure:0} {res.MaxPressureUnit}");
            RefreshGrid();
            Recompute();
        }
        catch (Exception ex) { Err(ex); }
    }

    /// <summary>
    /// For every measured charge: write a one-shot load with mc set to that charge (ladder off),
    /// open it in GRT so it computes, read the sim MV. Restores the original tab at the end.
    /// </summary>
    private async Task CaptureAllAsync()
    {
        var measured = _result.Points.Where(p => p.MeasMps > 0).Select(p => p.ChargeGr).OrderBy(x => x).ToList();
        if (measured.Count == 0) { MessageBox.Show(this, Lang.T("Load the measured charges first.")); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }

        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    string.Format(Lang.T("This will briefly open {0} tabs in GRT (one per charge) to read each simulated MV, then reopen your load.\n\nContinue?"), measured.Count),
                    Lang.T("Capture all"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_calsweep");
            Directory.CreateDirectory(tmpDir);
            const double grToG = 0.06479891;

            int i = 0;
            foreach (double chg in measured)
            {
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("propellant", "mc", (chg * grToG).ToString("0.############", CultureInfo.InvariantCulture), "g");
                doc.SetInput("propellant", "laddercnt", "0");     // ladder off — single charge
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "calsweep_{0}_{1:000}.grtload", i++, chg * 100));
                doc.Save(tmp);

                await _grt.LoadFileAsync(tmp);
                await Task.Delay(1400);                            // let GRT compute
                var t2 = await _grt.GetTabOnTopAsync();
                var res = await _grt.GetTabResultsAsync(t2.handle);
                if (res.MuzzleVelocityMps is { } sim && sim > 0)
                {
                    SetSim(chg, sim);
                    AppendLog($"{GrtUnits.Current.Charge(chg)} -> sim {GrtUnits.Current.Velocity(sim)}");
                }
                else AppendLog($"{GrtUnits.Current.Charge(chg)} -> no sim MV (skipped)");
                RefreshGrid();
                Recompute();
            }

            await _grt.LoadFileAsync(basePath);                    // back to the user's load
            try { Directory.Delete(tmpDir, true); } catch { }
            _status.Text = string.Format(Lang.T("swept {0} charges — close the extra GRT tabs when done."), measured.Count);
        }
        catch (Exception ex) { Err(ex); }
    }

    /// <summary>
    /// Second sweep for the Ba+a0 shape fit: same one-charge-per-tab mechanism as
    /// <see cref="CaptureAllAsync"/>, but with the propellant's "a0" nudged by
    /// <see cref="A0PerturbFrac"/> (5%) on top of each charge, at the SAME charges already captured
    /// at baseline -- <see cref="CalResult.FitBaAndA0"/> needs both series for the same points to
    /// estimate d(MV)/d(a0) numerically.
    /// </summary>
    private async Task CaptureAllPertA0Async()
    {
        var measured = _result.Points.Where(p => p.Valid).Select(p => p.ChargeGr).OrderBy(x => x).ToList();
        if (measured.Count == 0) { MessageBox.Show(this, Lang.T("Capture the baseline sim MV first ('Capture ALL sim MV').")); return; }
        if (_a0Old is not { } a0Old || a0Old <= 0) { MessageBox.Show(this, Lang.T("No 'a0' input found in this load.")); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }

        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    string.Format(Lang.T("This will briefly open {0} more tabs in GRT (a0 nudged by {1:0%} at each already-captured charge), then reopen your load.\n\nContinue?"), measured.Count, A0PerturbFrac),
                    Lang.T("Capture shape-fit sweep"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _a0Delta = a0Old * A0PerturbFrac;
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_calsweep_a0");
            Directory.CreateDirectory(tmpDir);
            const double grToG = 0.06479891;

            int i = 0;
            foreach (double chg in measured)
            {
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("propellant", "mc", (chg * grToG).ToString("0.############", CultureInfo.InvariantCulture), "g");
                doc.SetInput("propellant", "laddercnt", "0");     // ladder off — single charge
                doc.SetInput("propellant", "a0", (a0Old + _a0Delta).ToString("0.############", CultureInfo.InvariantCulture));
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "calsweep_a0_{0}_{1:000}.grtload", i++, chg * 100));
                doc.Save(tmp);

                await _grt.LoadFileAsync(tmp);
                await Task.Delay(1400);                            // let GRT compute
                var t2 = await _grt.GetTabOnTopAsync();
                var res = await _grt.GetTabResultsAsync(t2.handle);
                if (res.MuzzleVelocityMps is { } sim && sim > 0)
                {
                    SetSimPertA0(chg, sim);
                    AppendLog($"{chg:0.00} gr @ a0+{A0PerturbFrac:0%} -> sim {sim:0.0} m/s");
                }
                else AppendLog($"{chg:0.00} gr @ a0+{A0PerturbFrac:0%} -> no sim MV (skipped)");
                Recompute();
            }

            await _grt.LoadFileAsync(basePath);                    // back to the user's load
            try { Directory.Delete(tmpDir, true); } catch { }
            _status.Text = string.Format(Lang.T("swept {0} charges at a0+{1:0%} — close the extra GRT tabs when done."), measured.Count, A0PerturbFrac);
        }
        catch (Exception ex) { Err(ex); }
    }

    private void SetSim(double chargeGr, double simMps)
    {
        var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chargeGr) < 0.03);
        if (pt is null) { pt = new CalPoint { ChargeGr = Math.Round(chargeGr, 2) }; _result.Points.Add(pt); }
        pt.SimMps = simMps;
    }

    private void SetSimPertA0(double chargeGr, double simMps)
    {
        var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chargeGr) < 0.03);
        if (pt is null) return;                              // shape-fit sweep only nudges charges already captured at baseline
        pt.SimMpsPertA0 = simMps;
    }

    private void RemoveRow()
    {
        var ordered = _result.Points.OrderBy(p => p.ChargeGr).ToList();
        if (_grid.CurrentRow?.Index is { } i && i >= 0 && i < ordered.Count)
        {
            _result.Points.Remove(ordered[i]);
            RefreshGrid();
            Recompute();
        }
    }

    private void RefreshGrid()
    {
        _suppressGrid = true;
        _grid.Rows.Clear();
        var u = GrtUnits.Current;
        foreach (var p in _result.Points.OrderBy(p => p.ChargeGr))
            _grid.Rows.Add(
                u.ChargeValue(p.ChargeGr).ToString(u.ChargeFormat, CultureInfo.InvariantCulture),
                p.MeasMps > 0 ? u.VelocityValue(p.MeasMps).ToString("0.0", CultureInfo.InvariantCulture) : "",
                p.SimMps > 0 ? u.VelocityValue(p.SimMps).ToString("0.0", CultureInfo.InvariantCulture) : "",
                p.Valid ? u.VelocityValue(p.DeltaMps).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) : "",
                p.Valid ? p.DeltaPct.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "");
        _suppressGrid = false;
    }

    private void SyncFromGrid()
    {
        var ordered = _result.Points.OrderBy(p => p.ChargeGr).ToList();
        for (int i = 0; i < _grid.Rows.Count && i < ordered.Count; i++)
        {
            var p = ordered[i];
            // The inverse of RefreshGrid: the cells are in GRT's units, the point is always
            // grains and m/s.
            var u = GrtUnits.Current;
            if (D(_grid.Rows[i].Cells["chg"].Value) is { } c) p.ChargeGr = u.ChargeToGrains(c);
            if (D(_grid.Rows[i].Cells["meas"].Value) is { } m) p.MeasMps = u.VelocityToMps(m);
            if (D(_grid.Rows[i].Cells["sim"].Value) is { } s) p.SimMps = u.VelocityToMps(s);
        }
    }

    private void Recompute()
    {
        string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
        string report = _result.BuildReport(headline, _baOld, _powder);

        _shapeFit = null;
        if (_result.NPert >= 2 && _baOld is { } ba0 && _a0Old is { } a00 && _a0Delta > 0)
        {
            _shapeFit = _result.FitBaAndA0(ba0, a00, _a0Delta);
            if (_shapeFit is { } fit) report += _result.BuildShapeReport(fit, ba0, a00, _powder);
        }
        _summary.Text = report.Replace("\n", "\r\n");

        bool ok = _result.N >= 1 && _grt is { Connected: true } && _basePath != null;
        _writeNote.Enabled = ok;
        _writeBa.Enabled = ok && _baOld is > 0;
        _writeBaA0.Enabled = ok && _shapeFit is { Ok: true };
        _status.Text = _result.N >= 1
            ? string.Format(CultureInfo.InvariantCulture, Lang.T("{0} point(s), mean offset {1:+0.0;-0.0} {2} ({3:+0.0;-0.0} %)"), _result.N, GrtUnits.Current.VelocityValue(_result.MeanDeltaMps), GrtUnits.Current.VelocityUnitName, _result.MeanDeltaPct)
            : Lang.T("capture at least one charge");
    }

    private async Task WriteAsync(bool withBa)
    {
        try
        {
            if (_basePath is null || _grt is not { Connected: true }) return;
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
            doc.AddNote(NoteTitle, _result.BuildReport(headline, _baOld, _powder));

            string suffix = "cal";
            if (withBa && _baOld is { } ba && ba > 0)
            {
                double baNew = ba * _result.BaMultiplier;
                if (doc.SetInput("propellant", "Ba", baNew.ToString("0.###############", CultureInfo.InvariantCulture)))
                {
                    AppendLog("set Ba " + ba.ToString("0.######", CultureInfo.InvariantCulture)
                              + " -> " + baNew.ToString("0.######", CultureInfo.InvariantCulture));
                    suffix = "cal_ba";
                }
            }
            string outPath = doc.SaveSibling(suffix);
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = withBa ? Lang.T("Ba-corrected load written and opened in GRT.") : Lang.T("Calibration note written.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private async Task WriteShapeAsync()
    {
        try
        {
            if (_basePath is null || _grt is not { Connected: true } || _shapeFit is not { Ok: true } fit || _baOld is not { } ba || _a0Old is not { } a0) return;
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
            doc.AddNote(NoteTitle, _result.BuildReport(headline, _baOld, _powder) + _result.BuildShapeReport(fit, ba, a0, _powder));

            string suffix = "cal_ba_a0";
            bool wroteBa = doc.SetInput("propellant", "Ba", fit.NewBa.ToString("0.###############", CultureInfo.InvariantCulture));
            bool wroteA0 = doc.SetInput("propellant", "a0", fit.NewA0.ToString("0.###############", CultureInfo.InvariantCulture));
            if (wroteBa && wroteA0) AppendLog($"set Ba {ba:0.######} -> {fit.NewBa:0.######}, a0 {a0:0.####} -> {fit.NewA0:0.####}");
            else { AppendLog("note written, but Ba/a0 inputs missing in the load"); suffix = "cal"; }

            string outPath = doc.SaveSibling(suffix);
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Ba+a0-corrected load written and opened in GRT.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private static double? D(object? v) => GrtPluginKit.Util.Str.ParseNumber(Convert.ToString(v, CultureInfo.InvariantCulture));

    private void Err(Exception ex)
    {
        AppendLog("ERROR: " + ex.Message);
        MessageBox.Show(this, ex.Message, Lang.T("Calibration"), MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void AppendLog(string line) => _summary.SafeAppend(line);
}
