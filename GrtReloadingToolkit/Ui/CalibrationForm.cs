using System.Globalization;
using GrtReloadingToolkit.Cal;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Barrel calibration: compare GRT's simulated muzzle velocity to what the barrel actually
/// does, over one or more charges, and suggest a Ba tweak / offset.
/// </summary>
internal sealed class CalibrationForm : Form
{
    private const string NoteTitle = "Barrel Calibration";

    private readonly GrtClient? _grt;
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Button _writeNote = new() { Text = "Write calibration note", AutoSize = true, Enabled = false };
    private readonly Button _writeBa = new() { Text = "Write Ba-corrected .grtload", AutoSize = true, Enabled = false };

    private readonly CalResult _result = new();
    private readonly Action<string> _logHandler;
    private double? _baOld;
    private string _powder = "powder";
    private string? _basePath;
    private bool _suppressGrid;

    public CalibrationForm(GrtClient? grt)
    {
        _grt = grt;
        _logHandler = AppendLog;
        Text = AppVersion.Title("GRT Barrel Calibration");
        Width = 820; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);
        Build();
        NudFix.ApplyTo(this);
        if (_grt != null) _grt.Log += _logHandler;
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT)";
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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0), WrapContents = true };
        var loadBtn = new Button { Text = "Load measured from GRT load", AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadMeasuredAsync();
        top.Controls.Add(loadBtn);
        var capAllBtn = new Button { Text = "Capture ALL sim MV (sweeps GRT)", AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        capAllBtn.Click += async (_, _) => await CaptureAllAsync();
        top.Controls.Add(capAllBtn);
        var capBtn = new Button { Text = "Capture current charge only", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
        capBtn.Click += async (_, _) => await CaptureCurrentAsync();
        top.Controls.Add(capBtn);
        var delBtn = new Button { Text = "Remove row", AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        delBtn.Click += (_, _) => RemoveRow();
        top.Controls.Add(delBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "chg", HeaderText = "Charge gr" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "meas", HeaderText = "Meas MV m/s" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sim", HeaderText = "Sim MV m/s" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dm", HeaderText = "Δ m/s", ReadOnly = true });
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

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 210 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_summary);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _writeNote.Click += async (_, _) => await WriteAsync(false);
        _writeBa.Click += async (_, _) => await WriteAsync(true);
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
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return null; }
        var top = await _grt.GetTabOnTopAsync();
        if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
        {
            MessageBox.Show(this, "No saved load is open in GRT.", "Calibration"); return null;
        }
        if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
        {
            MessageBox.Show(this, "The active tab is a generated file — switch to your real load in GRT.", "Calibration",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        _basePath = top.file;
        // Ba/powder from the ORIGINAL load — never a toolkit sibling that may already carry a
        // corrected Ba, or a second calibration would compound the correction.
        string pristine = GrtLoadDoc.PristineBasePath(top.file);
        var pdoc = GrtLoadDoc.Load(File.Exists(pristine) ? pristine : top.file);
        _baOld = pdoc.PropellantBa is > 0 ? pdoc.PropellantBa : null;
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
            if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }

            double? chg = GrtLoadDoc.Load(top.file).PropellantChargeGr;
            if (chg is not { } c || c <= 0) { MessageBox.Show(this, "Could not read the current charge (mc) from the load."); return; }

            var res = await _grt.GetTabResultsAsync(top.handle);
            if (res.MuzzleVelocityMps is not { } sim || sim <= 0)
            {
                AppendLog("Get_TabResults returned no muzzle velocity — compute a result in GRT first.");
                return;
            }
            SetSim(c, sim);
            AppendLog($"captured sim MV {sim:0.0} m/s for {c:0.00} gr (GRT's current charge)  Pmax {res.MaxPressure:0} {res.MaxPressureUnit}");
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
        if (measured.Count == 0) { MessageBox.Show(this, "Load the measured charges first."); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }

        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    $"This will briefly open {measured.Count} tabs in GRT (one per charge) to read each simulated MV, then reopen your load.\n\nContinue?",
                    "Capture all", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
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
                    AppendLog($"{chg:0.00} gr -> sim {sim:0.0} m/s");
                }
                else AppendLog($"{chg:0.00} gr -> no sim MV (skipped)");
                RefreshGrid();
                Recompute();
            }

            await _grt.LoadFileAsync(basePath);                    // back to the user's load
            try { Directory.Delete(tmpDir, true); } catch { }
            _status.Text = $"swept {measured.Count} charges — close the extra GRT tabs when done.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private void SetSim(double chargeGr, double simMps)
    {
        var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chargeGr) < 0.03);
        if (pt is null) { pt = new CalPoint { ChargeGr = Math.Round(chargeGr, 2) }; _result.Points.Add(pt); }
        pt.SimMps = simMps;
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
        foreach (var p in _result.Points.OrderBy(p => p.ChargeGr))
            _grid.Rows.Add(
                p.ChargeGr.ToString("0.00", CultureInfo.InvariantCulture),
                p.MeasMps > 0 ? p.MeasMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                p.SimMps > 0 ? p.SimMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                p.Valid ? p.DeltaMps.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) : "",
                p.Valid ? p.DeltaPct.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "");
        _suppressGrid = false;
    }

    private void SyncFromGrid()
    {
        var ordered = _result.Points.OrderBy(p => p.ChargeGr).ToList();
        for (int i = 0; i < _grid.Rows.Count && i < ordered.Count; i++)
        {
            var p = ordered[i];
            if (D(_grid.Rows[i].Cells["chg"].Value) is { } c) p.ChargeGr = c;
            if (D(_grid.Rows[i].Cells["meas"].Value) is { } m) p.MeasMps = m;
            if (D(_grid.Rows[i].Cells["sim"].Value) is { } s) p.SimMps = s;
        }
    }

    private void Recompute()
    {
        string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
        _summary.Text = _result.BuildReport(headline, _baOld, _powder).Replace("\n", "\r\n");
        bool ok = _result.N >= 1 && _grt is { Connected: true } && _basePath != null;
        _writeNote.Enabled = ok;
        _writeBa.Enabled = ok && _baOld is > 0;
        _status.Text = _result.N >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0} point(s), mean offset {1:+0.0;-0.0} m/s ({2:+0.0;-0.0} %)", _result.N, _result.MeanDeltaMps, _result.MeanDeltaPct)
            : "capture at least one charge";
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
                { AppendLog($"set Ba {ba:0.######} -> {baNew:0.######}"); suffix = "cal_ba"; }
            }
            string outPath = doc.SaveSibling(suffix);
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = withBa ? "Ba-corrected load written and opened in GRT." : "Calibration note written.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private static double? D(object? v) => GrtPluginKit.Util.Str.ParseNumber(Convert.ToString(v, CultureInfo.InvariantCulture));

    private void Err(Exception ex)
    {
        AppendLog("ERROR: " + ex.Message);
        MessageBox.Show(this, ex.Message, "Calibration", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void AppendLog(string line) => _summary.SafeAppend(line);
}
