using System.Globalization;
using GrtReloadingToolkit.Cal;
using GrtReloadingToolkit.Log;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Barrel calibration: compare GRT's simulated muzzle velocity to what the barrel actually
/// does, over one or more charges, and suggest a Ba tweak / offset. When the offset varies with
/// charge (a burn-shape mismatch, not just a scale one), a second sweep at a nudged "k" lets
/// <see cref="CalResult.FitJoint"/> fit the GRT-style joint Ba/k correction -- see that method's own doc.
///
/// Also flags when the load's own Ba looks stale against the Journal's own calibration history for
/// the same caliber+powder (see <see cref="BaStaleness"/>) -- a proactive nudge shown the moment a
/// load is opened here, before the user has run anything, since GRT's own factory Ba for a powder
/// can't be read via the plugin IPC to compare against directly.
/// </summary>
internal sealed class CalibrationForm : Form
{
    private const string NoteTitle = StaleNotes.BarrelCalibrationTitle;

    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Label _staleWarning = new()
    {
        AutoSize = false, Dock = DockStyle.Top, Height = 0, Visible = false,
        ForeColor = Color.FromArgb(153, 76, 0), Padding = new Padding(8, 4, 8, 4),
    };
    private readonly Button _writeNote = new() { Text = Lang.T("Write calibration note"), AutoSize = true, Enabled = false };
    private readonly Button _writeBa = new() { Text = Lang.T("Write Ba-corrected .grtload"), AutoSize = true, Enabled = false };
    private readonly Button _writeBaK = new() { Text = Lang.T("Write Ba+k-corrected .grtload"), AutoSize = true, Enabled = false };

    // k (isentropic exponent) fit -- the two-parameter extension of the Ba-only fit
    // above, for when the offset varies with charge (see CalResult.FitJoint's own doc: Ba and k are the
    // two coefficients GRT's own OBT tool moves).
    private const double KPerturbFrac = 0.002;   // Ba and k nudged together by +0.2 %
    private readonly CalResult _result = new();
    private readonly Action<string> _logHandler;
    private double? _baOld;
    private double? _kOld;
    private double _kDelta;
    private CalResult.JointFit? _jointFit;
    private CalResult.JointVerification? _verification;
    private (double Ba, double K)? _verifiedFor;     // the exact proposal the verification sweep was run for
    private readonly Button _checkSebert = new() { Text = Lang.T("Check Sebert sensitivity (GRT)"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
    private SebertSensitivity? _sebert;
    private readonly Button _verifyJoint = new() { Text = Lang.T("Verify correction in GRT"), AutoSize = true, Margin = new Padding(10, 2, 0, 0), Enabled = false };
    private string _powder = "powder";
    private string? _basePath;
    private bool _suppressGrid;

    public CalibrationForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 6, 0, 4), WrapContents = true };
        var loadBtn = new Button { Text = Lang.T("Load measured from GRT load"), AutoSize = true };
        loadBtn.Click += async (_, _) => await LoadMeasuredAsync();
        top.Controls.Add(loadBtn);
        var capAllBtn = new Button { Text = Lang.T("Capture ALL sim MV (sweeps GRT)"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        capAllBtn.Click += async (_, _) => await CaptureAllAsync();
        top.Controls.Add(capAllBtn);
        var capBtn = new Button { Text = Lang.T("Capture current charge only"), AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
        capBtn.Click += async (_, _) => await CaptureCurrentAsync();
        top.Controls.Add(capBtn);
        var capShapeBtn = new Button { Text = Lang.T("Capture GRT-style sweep (Ba + k)"), AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        capShapeBtn.Click += async (_, _) => await CaptureAllPertKAsync();
        top.Controls.Add(capShapeBtn);
        _verifyJoint.Click += async (_, _) => await VerifyJointAsync();
        top.Controls.Add(_verifyJoint);
        _checkSebert.Click += async (_, _) => await CheckSebertAsync();
        top.Controls.Add(_checkSebert);
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
        _writeBaK.Click += async (_, _) => await WriteShapeAsync();
        bottom.Controls.Add(_writeBaK);
        bottom.Controls.Add(_writeBa);
        bottom.Controls.Add(_writeNote);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(_staleWarning);
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
        _kOld = pdoc.InputNumber("propellant", "k") is { } kv && kv > 0 ? kv : null;
        _powder = string.IsNullOrWhiteSpace(pdoc.PropellantName) ? "powder" : pdoc.PropellantName;
        UpdateStaleWarning(pdoc.CaliberName, pdoc.PropellantName);
        // Measurements / everything else from the accumulator sibling if it exists (that's where
        // a chrono import lands).
        return GrtLoadDoc.OpenForToolkitEdit(top.file);
    }

    /// <summary>
    /// Compares this load's own Ba against the most recent CALIBRATED Ba the Journal has for the
    /// same caliber+powder (see <see cref="BaStaleness"/>'s own doc for why that's the comparison,
    /// not GRT's factory value, which this plugin has no way to read). A powder that has never been
    /// calibrated has nothing to compare against and shows no warning — silence here means "unknown",
    /// not "fine".
    /// </summary>
    private void UpdateStaleWarning(string caliber, string powderName)
    {
        long? powderId = _db.ResolvePowderId(powderName);
        var lastCalibrated = _db.Journal()
            .Where(e => e.Ba is > 0 && powderId.HasValue && e.PowderId == powderId
                        && string.Equals(e.Caliber, caliber, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Id)
            .FirstOrDefault();

        var check = BaStaleness.Check(_baOld, lastCalibrated?.Ba);
        if (check is { Stale: true } s)
        {
            _staleWarning.Text = string.Format(Lang.T("⚠ Ba differs from the last calibration: this file uses Ba={0}, the last calibration logged in the Journal for {1} in {2} was Ba={3} ({4}% difference)."),
                s.FileBa.ToString("0.000000", CultureInfo.InvariantCulture), powderName, caliber,
                s.LastCalibratedBa.ToString("0.000000", CultureInfo.InvariantCulture), s.DiffPct.ToString("0.0", CultureInfo.InvariantCulture));
            _staleWarning.Height = 36;
            _staleWarning.Visible = true;
        }
        else
        {
            _staleWarning.Visible = false;
            _staleWarning.Height = 0;
        }
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

            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_calsweep_" + Guid.NewGuid().ToString("N")[..8]);
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
    /// Second sweep for the Ba+k shape fit: same one-charge-per-tab mechanism as
    /// <see cref="CaptureAllAsync"/>, but with the propellant's "k" nudged by
    /// <see cref="KPerturbFrac"/> (0.5%) on top of each charge, at the SAME charges already captured
    /// at baseline -- <see cref="CalResult.FitJoint"/> needs both series for the same points to
    /// estimate d(MV)/d(k) numerically.
    /// </summary>
    private async Task CaptureAllPertKAsync()
    {
        var measured = _result.Points.Where(p => p.Valid).Select(p => p.ChargeGr).OrderBy(x => x).ToList();
        if (measured.Count == 0) { MessageBox.Show(this, Lang.T("Capture the baseline sim MV first ('Capture ALL sim MV').")); return; }
        if (_kOld is not { } kOld || kOld <= 0 || _baOld is not { } baOldSweep || baOldSweep <= 0) { MessageBox.Show(this, Lang.T("No 'k' input found in this load.")); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }

        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    string.Format(Lang.T("This will briefly open {0} more tabs in GRT (Ba and k nudged together by {1:0.0%} at each already-captured charge), then reopen your load.\n\nContinue?"), measured.Count, KPerturbFrac),
                    Lang.T("Capture GRT-style sweep"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _kDelta = KPerturbFrac;
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_calsweep_k_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            const double grToG = 0.06479891;

            int i = 0;
            foreach (double chg in measured)
            {
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("propellant", "mc", (chg * grToG).ToString("0.############", CultureInfo.InvariantCulture), "g");
                doc.SetInput("propellant", "laddercnt", "0");     // ladder off — single charge
                doc.SetInput("propellant", "k", (kOld * (1 + _kDelta)).ToString("0.############", CultureInfo.InvariantCulture));
                doc.SetInput("propellant", "Ba", (baOldSweep * (1 + _kDelta)).ToString("0.############", CultureInfo.InvariantCulture));
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "calsweep_k_{0}_{1:000}.grtload", i++, chg * 100));
                doc.Save(tmp);

                await _grt.LoadFileAsync(tmp);
                await Task.Delay(1400);                            // let GRT compute
                var t2 = await _grt.GetTabOnTopAsync();
                var res = await _grt.GetTabResultsAsync(t2.handle);
                if (res.MuzzleVelocityMps is { } sim && sim > 0)
                {
                    SetSimPertK(chg, sim);
                    // Invariant, like the Ba/k values written a few lines down and like every
                    // other number this plugin shows. The charge and MV carry decimal separators,
                    // so bare interpolation renders them per the OS locale; k+{:0.0%} has neither a
                    // decimal nor a group separator and reads the same either way.
                    AppendLog(FormattableString.Invariant($"{chg:0.00} gr @ Ba,k +{KPerturbFrac:0.0%} -> sim {sim:0.0} m/s"));
                }
                else AppendLog(FormattableString.Invariant($"{chg:0.00} gr @ Ba,k +{KPerturbFrac:0.0%} -> no sim MV (skipped)"));
                Recompute();
            }

            await _grt.LoadFileAsync(basePath);                    // back to the user's load
            try { Directory.Delete(tmpDir, true); } catch { }
            _status.Text = string.Format(Lang.T("swept {0} charges at Ba and k +{1:0.0%} — close the extra GRT tabs when done."), measured.Count, KPerturbFrac);
        }
        catch (Exception ex) { Err(ex); }
    }

    /// <summary>
    /// The check that decides whether a Ba + k proposal may be written: simulate every captured charge in GRT
    /// with the PROPOSED Ba and k and compare with the measured velocities (see
    /// <see cref="CalResult.VerifyJoint"/>). The straight-line fit can propose numbers that look fine to its own
    /// arithmetic and are wrong for the real model; GRT is the only thing that can say what the model does.
    /// </summary>
    private async Task VerifyJointAsync()
    {
        if (_jointFit is not { Ok: true } fit) { MessageBox.Show(this, Lang.T("There is no Ba + k correction to verify.")); return; }
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        var charges = _result.Points.Where(p => p.Valid).Select(p => p.ChargeGr).OrderBy(x => x).ToList();
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    string.Format(Lang.T("This will briefly open {0} more tabs in GRT, simulating each charge with the proposed Ba and k, then reopen your load.\n\nContinue?"), charges.Count),
                    Lang.T("Verify correction"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            foreach (var pt in _result.Points) pt.SimMpsVerify = 0;
            _verifiedFor = (fit.NewBa, fit.NewK);

            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_calsweep_verify_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            const double grToG = 0.06479891;

            int i = 0;
            foreach (double chg in charges)
            {
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("propellant", "mc", (chg * grToG).ToString("0.############", CultureInfo.InvariantCulture), "g");
                doc.SetInput("propellant", "laddercnt", "0");
                doc.SetInput("propellant", "Ba", fit.NewBa.ToString("0.###############", CultureInfo.InvariantCulture));
                doc.SetInput("propellant", "k", fit.NewK.ToString("0.###############", CultureInfo.InvariantCulture));
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "calverify_{0}_{1:000}.grtload", i++, chg * 100));
                doc.Save(tmp);

                await _grt.LoadFileAsync(tmp);
                await Task.Delay(1400);
                var t2 = await _grt.GetTabOnTopAsync();
                var res = await _grt.GetTabResultsAsync(t2.handle);
                var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chg) < 0.03);
                if (pt is not null && res.MuzzleVelocityMps is { } sim && sim > 0)
                {
                    pt.SimMpsVerify = sim;
                    AppendLog(FormattableString.Invariant($"{chg:0.00} gr @ proposed Ba/k -> sim {sim:0.0} m/s"));
                }
                else AppendLog(FormattableString.Invariant($"{chg:0.00} gr @ proposed Ba/k -> no sim MV (verification incomplete)"));
                Recompute();
            }

            await _grt.LoadFileAsync(basePath);
            try { Directory.Delete(tmpDir, true); } catch { }
            Recompute();
            _status.Text = _verification is { Accepted: true }
                ? Lang.T("Correction verified by GRT -- the Ba+k file can be written.")
                : Lang.T("Correction NOT confirmed by GRT -- use the Ba-only correction. Close the extra GRT tabs when done.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private string SebertText() => _sebert?.BuildReport() ?? "";

    /// <summary>
    /// Measures how far the OBT node would move if the load's Sebert factor were off by 0.1 either way with the velocity
    /// still matching (see <see cref="SebertSensitivity"/>). Reads only: no file of the user's is changed, and the
    /// Sebert factor is never written. The sweep files get a name of their own on every run because GRT answers a
    /// file it already has open with the old tab's results instead of simulating again.
    /// </summary>
    private async Task CheckSebertAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load open in GRT.")); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;
            var pdoc = GrtLoadDoc.Load(basePath);
            double? ba = pdoc.PropellantBa is > 0 ? pdoc.PropellantBa : null;
            double? k = pdoc.InputNumber("propellant", "k") is { } kv && kv > 0 ? kv : null;
            double? s0 = pdoc.PropellantSebert is > 0 ? pdoc.PropellantSebert : null;
            if (ba is not { } baOld || k is not { } kOld || s0 is not { } sebert)
            { MessageBox.Show(this, Lang.T("This load has no Sebert factor, Ba or k to work from.")); return; }

            // The charge to simulate at: the middle of the measured ladder if there is one, else the file's own charge.
            var measured = _result.Points.Where(p => p.MeasMps > 0).Select(p => p.ChargeGr).OrderBy(x => x).ToList();
            double? charge = measured.Count > 0 ? measured[measured.Count / 2] : pdoc.PropellantChargeGr;
            if (charge is not { } chargeGr || chargeGr <= 0) { MessageBox.Show(this, Lang.T("Could not read the current charge (mc) from the load.")); return; }

            if (MessageBox.Show(this,
                    string.Format(Lang.T("This will briefly open {0} tabs in GRT, simulating the load at the Sebert factor +/-{1:0.0} (Ba and k re-matched to the same velocity), then reopen your load.\n\nContinue?"), 12, SebertSensitivity.Step),
                    Lang.T("Sebert sensitivity"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_sebert_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            const double grToG = 0.06479891;
            int n = 0;
            async Task<SebertSim?> Simulate(double s, double f, double chg)
            {
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("caliber", "sebert", s.ToString("0.########", CultureInfo.InvariantCulture));
                doc.SetInput("propellant", "Ba", (baOld * f).ToString("0.###############", CultureInfo.InvariantCulture));
                doc.SetInput("propellant", "k", (kOld * f).ToString("0.###############", CultureInfo.InvariantCulture));
                doc.SetInput("propellant", "mc", (chg * grToG).ToString("0.############", CultureInfo.InvariantCulture), "g");
                doc.SetInput("propellant", "laddercnt", "0");     // ladder off -- single charge
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "sebert_{0:000}.grtload", n++));
                doc.Save(tmp);
                await _grt.LoadFileAsync(tmp);
                await Task.Delay(2000);                            // let GRT compute
                var t2 = await _grt.GetTabOnTopAsync();
                var res = await _grt.GetTabResultsAsync(t2.handle);
                if (res.MuzzleVelocityMps is not { } mv || mv <= 0 || res.MaxPressure is not { } pm
                    || res.BulletLeadTime10PmaxMs is not { } blt || blt <= 0) return null;
                AppendLog(FormattableString.Invariant($"Sebert {s:0.00}, Ba,k x{f:0.0000}, {chg:0.00} gr -> {mv:0.0} m/s, BLT {blt:0.0000} ms"));
                return new SebertSim(mv, pm, blt, res.MaxPressureUnit);
            }

            _sebert = await SebertSensitivity.RunAsync(Simulate, sebert, chargeGr);

            await _grt.LoadFileAsync(basePath);                    // back to the user's load
            try { Directory.Delete(tmpDir, true); } catch { }
            Recompute();
            _status.Text = _sebert is null
                ? Lang.T("Sebert sensitivity could not be measured: GRT did not return a usable simulation (a stale tab, or a charge it cannot compute).")
                : Lang.T("Sebert sensitivity measured -- see the report below.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private void SetSim(double chargeGr, double simMps)
    {
        var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chargeGr) < 0.03);
        if (pt is null) { pt = new CalPoint { ChargeGr = Math.Round(chargeGr, 2) }; _result.Points.Add(pt); }
        pt.SimMps = simMps;
    }

    private void SetSimPertK(double chargeGr, double simMps)
    {
        var pt = _result.Points.FirstOrDefault(p => Math.Abs(p.ChargeGr - chargeGr) < 0.03);
        if (pt is null) return;                              // shape-fit sweep only nudges charges already captured at baseline
        pt.SimMpsPertK = simMps;
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

        _jointFit = null;
        if (_result.NPert >= 1 && _baOld is { } ba0 && _kOld is { } k00 && _kDelta > 0)
        {
            _jointFit = _result.FitJoint(ba0, k00, _kDelta);
            // A verification belongs to one exact proposal: any change to the data that moves the proposal
            // (or a different proposal altogether) throws it away, so a stale "ACCEPTED" can never unlock
            // the write button for numbers GRT never simulated.
            _verification = null;
            if (_jointFit is { Ok: true } vf)
            {
                if (_verifiedFor is { } v && Math.Abs(v.Ba - vf.NewBa) < 1e-12 && Math.Abs(v.K - vf.NewK) < 1e-12)
                    _verification = _result.VerifyJoint();
                else foreach (var pt in _result.Points) pt.SimMpsVerify = 0;
            }
            if (_jointFit is { } fit) report += _result.BuildJointReport(fit, ba0, k00, _powder, _verification);
        }
        else if (_result.N >= 2)
        {
            // Say why the Ba + k button is dead instead of leaving it greyed with no explanation.
            string why = _kOld is null || _baOld is null ? "the load has no Ba / k to correct"
                : _result.NPert < 1 ? "capture the GRT-style sweep first: it needs a second simulated MV per charge, with Ba and k nudged together"
                : "the sweep has no usable nudge";
            report += "\r\nGRT-style correction (Ba and k together): not available yet -- " + why + ".\r\n";
        }
        report += SebertText();
        _summary.Text = report.Replace("\n", "\r\n");

        bool ok = _result.N >= 1 && _grt is { Connected: true } && _basePath != null;
        _writeNote.Enabled = ok;
        _writeBa.Enabled = ok && _baOld is > 0;
        _writeBaK.Enabled = ok && _jointFit is { Ok: true } && _verification is { Accepted: true };
        _verifyJoint.Enabled = _jointFit is { Ok: true } && _grt is { Connected: true };
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
            doc.AddNote(NoteTitle, _result.BuildReport(headline, _baOld, _powder) + SebertText());

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
            if (_basePath is null || _grt is not { Connected: true } || _jointFit is not { Ok: true } fit || _baOld is not { } ba || _kOld is not { } k) return;
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            string headline = $"{NoteTitle} {DateTime.Now:yyyy-MM-dd}";
            doc.AddNote(NoteTitle, _result.BuildReport(headline, _baOld, _powder) + _result.BuildJointReport(fit, ba, k, _powder) + SebertText());

            string suffix = "cal_ba_k";
            bool wroteBa = doc.SetInput("propellant", "Ba", fit.NewBa.ToString("0.###############", CultureInfo.InvariantCulture));
            bool wroteK = doc.SetInput("propellant", "k", fit.NewK.ToString("0.###############", CultureInfo.InvariantCulture));
            if (wroteBa && wroteK) AppendLog(FormattableString.Invariant($"set Ba {ba:0.######} -> {fit.NewBa:0.######}, k {k:0.#######} -> {fit.NewK:0.#######}"));
            else { AppendLog("note written, but Ba/k inputs missing in the load"); suffix = "cal"; }

            string outPath = doc.SaveSibling(suffix);
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Ba+k-corrected load written and opened in GRT.");
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
