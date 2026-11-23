using System.Globalization;
using GrtSensitivityLab.WhatIf;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtSensitivityLab.Ui;

/// <summary>
/// For a chosen GRT input, replaces the .grtload file's own value with one you actually measured
/// (a real case-volume burette reading, a chronograph-verified bullet weight, a caliper seating
/// depth...) and re-simulates, so you see what GRT predicts for the barrel and components you
/// actually have -- not the file's nominal defaults. One substitution at a time; results
/// accumulate in the grid as you try more inputs. This studies GRT's own model reacting to a real
/// measurement, not a hypothetical +/- tolerance -- that's the older sweep-and-rank idea living
/// (unreferenced) in GrtModelLab's SensitivitySweep/*.cs, which this tool intentionally does not
/// reuse the shape of, only the parameter catalogue and the GRT-load-editing plumbing.
/// </summary>
internal sealed class WhatIfForm : Form
{
    private const string NoteTitle = "Sensitivity Lab What If";

    private const double MmPerInch = 25.4;
    // GRT's own convention for case capacity (see e.g. "Volume bossolo ... grain H2O" in its UI):
    // water at 1 g/cm3, 1 g = 15.4323584 grains.
    private const double GrainsH2OPerCm3 = 15.4323584;

    private readonly GrtClient? _grt;
    private readonly List<Row> _rows = new();
    private readonly Dictionary<WhatIfParam, Row> _rowByParam = new();
    private readonly ToolTip _tips = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly Button _refreshBtn = new() { Text = "Read current values from GRT", AutoSize = true };
    private readonly Button _runBtn = new() { Text = "Run what-if (re-simulates in GRT)", AutoSize = true };
    private readonly Button _pickBtn = new() { Text = "Open a different load...", AutoSize = true };
    private readonly Button _writeBtn = new() { Text = "Write what-if note to GRT load", AutoSize = true, Enabled = false };
    private readonly Label _targetLabel = new() { Text = "Targets whatever load is on top in GRT." };

    /// <summary>
    /// One parameter's row. <see cref="UnitCombo"/> is null for a parameter with no metric/imperial
    /// choice (mass is always gr, pressure is always bar -- no imperial equivalent to switch to);
    /// otherwise it's that row's OWN mm/in or cm3/gr-H2O switch. Per-row rather than one global
    /// toggle for the whole form, per explicit user feedback after live-testing (2026-09-16): a
    /// user measuring case volume with a burette in cm3 and bullet-adjacent case length with a
    /// caliper in inches, say, wants both at once, not one form-wide unit.
    /// </summary>
    private sealed class Row
    {
        public required WhatIfParam Param { get; init; }
        public required CheckBox Check { get; init; }
        public required NumericUpDown Measured { get; init; }
        public required Label FileValue { get; init; }
        public ComboBox? UnitCombo { get; init; }
        public bool WasImperial { get; set; }
    }

    private string? _basePath;
    private WhatIfReport? _report;

    public WhatIfForm(GrtClient? grt)
    {
        _grt = grt;
        Text = "GRT Sensitivity Lab -- What If";
        // Tall enough that the left sidebar's 8 parameter rows + buttons fit without its own
        // vertical scrollbar at the default size -- checked by rendering the actual window, not
        // assumed from the layout code (640 fell about 20-40px short and hid the Run button below
        // the fold). AutoScroll on the sidebar panel stays on regardless, as a fallback if the user
        // shrinks the window below MinimumSize's headroom.
        Width = 940; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Build();
        NudFix.ApplyTo(this);
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT) -- connect to GRT to run a what-if";
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 380, AutoScroll = true, Padding = new Padding(8) };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        // MaximumSize (not manual \r\n line breaks) lets WinForms wrap this to the panel's own
        // width -- hard-coded breaks assume a line width that doesn't actually match the panel's,
        // which is exactly what pushed the whole sidebar wider and forced a needless horizontal
        // scrollbar (only visible by rendering the real window, not from reading the layout code).
        var hint = new Label
        {
            Text = "Pick which GRT input to replace, type the value you actually measured, and run -- one input at a time, results accumulate below. Length/volume fields each have their own metric/imperial switch.",
            AutoSize = true,
            MaximumSize = new Size(344, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
        };
        flow.Controls.Add(hint);

        // The what-if always acts on whatever load GRT currently has on top (the only load GRT
        // itself can simulate) -- lets you point GRT at a specific file from here instead of
        // switching tabs by hand in GRT first.
        _pickBtn.Margin = new Padding(0, 0, 0, 4);
        _pickBtn.Click += async (_, _) => await PickLoadAsync();
        flow.Controls.Add(_pickBtn);
        _targetLabel.AutoSize = true;
        _targetLabel.ForeColor = SystemColors.GrayText;
        _targetLabel.Margin = new Padding(0, 0, 0, 4);
        flow.Controls.Add(_targetLabel);

        _refreshBtn.Margin = new Padding(0, 0, 0, 10);
        _refreshBtn.Click += async (_, _) => await RefreshFromGrtAsync();
        flow.Controls.Add(_refreshBtn);

        foreach (var p in WhatIfParams.All)
        {
            // Two lines per parameter, not one long horizontal row: the checkbox label alone (the
            // longest is "Bearing-surface pressure (gpressure)") already used most of a single
            // row's width, and adding a file-value label plus a per-row unit combo on top of that
            // pushed the total past the left panel's width -- it only became visible as a
            // horizontal-scroll mess once live-tested, not from reading the code. AutoSize on the
            // checkbox (instead of a fixed width) also stops the longest labels from being cut off.
            var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 8) };

            var chk = new CheckBox { Text = p.Label, AutoSize = true, Checked = false, Margin = new Padding(0, 0, 0, 2) };
            group.Controls.Add(chk);

            // gfriction is GRT's "bullet resistance / friction": a value RELATIVE to GRT's own
            // computed default, not an absolute physical friction figure -- 0 means "use the
            // default", not "zero resistance", and GRT explicitly allows a negative value to
            // reduce resistance below that default (see doku/en/doku/inputfields-projectile.txt).
            // Easy to misread as "0 bar of friction", so it gets a tooltip and (unlike every other
            // row here) a negative-allowed Minimum.
            bool isRelativeFriction = p.Name == "gfriction";
            if (isRelativeFriction)
                _tips.SetToolTip(chk, "Relative to GRT's own default, not an absolute value: 0 = GRT's default resistance (not zero friction). Positive adds resistance, negative reduces it.");

            var valueRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(18, 0, 0, 0) };
            var fileValue = new Label { Text = "file: --", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 8, 0) };
            valueRow.Controls.Add(fileValue);
            var measured = new NumericUpDown { DecimalPlaces = 3, Increment = 0.01m, Minimum = isRelativeFriction ? -100000m : 0m, Maximum = 100000m, Width = 80, Margin = new Padding(0, 0, 4, 0) };
            valueRow.Controls.Add(measured);
            if (isRelativeFriction) _tips.SetToolTip(measured, "Relative to GRT's own default -- 0 = default, not zero friction.");

            ComboBox? unitCombo = null;
            if (IsLengthUnit(p) || IsVolumeUnit(p))
            {
                // "imperial" here means what a US reloader actually measures with: inches for
                // length, grains of water (not cubic inches) for case capacity -- matches GRT's
                // own "grain H2O" convention and a burette/scale-based workflow. Per-row, not one
                // form-wide toggle -- see the Row class comment.
                unitCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
                unitCombo.Items.AddRange(IsLengthUnit(p) ? new object[] { "mm", "in" } : new object[] { "cm3", "gr H2O" });
                unitCombo.SelectedIndex = 0;
                valueRow.Controls.Add(unitCombo);
            }
            else
            {
                valueRow.Controls.Add(new Label { Text = p.Unit, AutoSize = true, Margin = new Padding(0, 3, 0, 0) });
            }

            group.Controls.Add(valueRow);
            flow.Controls.Add(group);
            var row = new Row { Param = p, Check = chk, Measured = measured, FileValue = fileValue, UnitCombo = unitCombo };
            if (unitCombo != null) unitCombo.SelectedIndexChanged += (_, _) => OnRowUnitChanged(row);
            _rows.Add(row);
            _rowByParam[p] = row;
        }

        _runBtn.Margin = new Padding(0, 10, 0, 0);
        _runBtn.Click += async (_, _) => await RunWhatIfAsync();
        flow.Controls.Add(_runBtn);
        left.Controls.Add(flow);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "param", HeaderText = "Parameter" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File value" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "measured", HeaderText = "Measured" });
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

    private bool IsLengthUnit(WhatIfParam p) => p.Unit == "mm";
    private bool IsVolumeUnit(WhatIfParam p) => p.Unit == "cm3";

    private bool IsImperial(Row r) => r.UnitCombo is { SelectedIndex: 1 };

    private string DisplayUnit(Row r) =>
        IsLengthUnit(r.Param) ? (IsImperial(r) ? "in" : "mm") :
        IsVolumeUnit(r.Param) ? (IsImperial(r) ? "gr H2O" : "cm3") :
        r.Param.Unit;

    private double ToDisplay(Row r, double native) =>
        IsLengthUnit(r.Param) ? (IsImperial(r) ? native / MmPerInch : native) :
        IsVolumeUnit(r.Param) ? (IsImperial(r) ? native * GrainsH2OPerCm3 : native) :
        native;

    private double ToNative(Row r, double display) =>
        IsLengthUnit(r.Param) ? (IsImperial(r) ? display * MmPerInch : display) :
        IsVolumeUnit(r.Param) ? (IsImperial(r) ? display / GrainsH2OPerCm3 : display) :
        display;

    private double ToNativeWith(Row r, double display, bool wasImperial) =>
        IsLengthUnit(r.Param) ? (wasImperial ? display * MmPerInch : display) :
        IsVolumeUnit(r.Param) ? (wasImperial ? display / GrainsH2OPerCm3 : display) :
        display;

    /// <summary>
    /// Converts one row's measured-value NumericUpDown and file-value label between metric and
    /// imperial, when that row's own unit combo changes -- per-row, not a single form-wide toggle
    /// (see the Row class comment). Reads the control's current value converted to its CANONICAL
    /// unit (mm / cm3) BEFORE touching DecimalPlaces/Value -- the same read-before-clamp-or-truncate
    /// hazard as GrtModelLab's BarrelProfileForm/BrassForm unit toggles: changing DecimalPlaces
    /// first can silently round away precision the conversion still needs.
    /// </summary>
    private void OnRowUnitChanged(Row r)
    {
        bool toImperial = IsImperial(r);
        double native = ToNativeWith(r, (double)r.Measured.Value, r.WasImperial);
        double? nativeFile = _nativeFileValues.GetValueOrDefault(r.Param);
        r.WasImperial = toImperial;

        double display = ToDisplay(r, native);
        r.Measured.DecimalPlaces = IsLengthUnit(r.Param) ? (toImperial ? 5 : 3) : (toImperial ? 1 : 3);
        r.Measured.Increment = IsLengthUnit(r.Param) ? (toImperial ? 0.0001m : 0.01m) : (toImperial ? 0.1m : 0.01m);
        r.Measured.Value = (decimal)Math.Clamp(display, (double)r.Measured.Minimum, (double)r.Measured.Maximum);

        if (nativeFile is { } fv)
            r.FileValue.Text = "file: " + ToDisplay(r, fv).ToString("0.###", CultureInfo.InvariantCulture);
    }

    // A stashed copy of each row's file-value in GRT's native unit (mm/cm3/etc), so the display
    // label can be re-rendered in whichever unit that row's combo currently shows without
    // re-reading GRT.
    private readonly Dictionary<WhatIfParam, double?> _nativeFileValues = new();

    /// <summary>
    /// Opens a chosen .grtload in GRT (a new tab, brought to the front) so the what-if -- which can
    /// only ever act on whatever GRT currently has on top -- targets that file instead of whatever
    /// tab happened to be active already.
    /// </summary>
    private async Task PickLoadAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
        using var dlg = new OpenFileDialog { Filter = "GRT load (*.grtload)|*.grtload", Title = "Open a load to test" };
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
            _targetLabel.Text = "Target: " + Path.GetFileName(dlg.FileName);
            _status.Text = $"Opened '{Path.GetFileName(dlg.FileName)}' in GRT.";
            await RefreshFromGrtAsync();
        }
        catch (Exception ex) { Err(ex); }
    }

    private async Task RefreshFromGrtAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;
            _basePath = top.file;
            _targetLabel.Text = "Target: " + Path.GetFileName(top.file);

            var doc = GrtLoadDoc.Load(basePath);
            foreach (var r in _rows)
            {
                double? cur = WhatIfEngine.CurrentValue(doc, r.Param);
                _nativeFileValues[r.Param] = cur;
                r.FileValue.Text = cur is { } c ? "file: " + ToDisplay(r, c).ToString("0.###", CultureInfo.InvariantCulture) : "file: --";
                if (cur is { } c2) r.Measured.Value = (decimal)Math.Clamp(ToDisplay(r, c2), (double)r.Measured.Minimum, (double)r.Measured.Maximum);
            }
            _status.Text = "Read current values from '" + Path.GetFileName(basePath) + "'.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private async Task RunWhatIfAsync()
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
                MessageBox.Show(this, "The active tab is a generated file -- switch to your real load in GRT.", "What if",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _basePath = top.file;
            _targetLabel.Text = "Target: " + Path.GetFileName(top.file);
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            if (MessageBox.Show(this,
                    $"This will briefly open {selected.Count + 1} tabs in GRT (one as-is + one per substitution) to read each simulated result, then reopen your load.\n\nContinue?",
                    "Run what-if", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _runBtn.Enabled = false;
            _grid.Rows.Clear();
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_sensitivity_lab_whatif_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            int seq = 0;

            var pristine = GrtLoadDoc.Load(basePath);
            string caliberName = pristine.CaliberName;
            string powderName = pristine.PropellantName;

            var (bmv, bpm, bpu) = await RunOneAsync(basePath, _ => { }, tmpDir, seq++);
            AppendLog(bmv is { } b0 ? $"file as-is -> MV {b0:0.0} m/s, Pmax {bpm:0} {bpu}" : "file as-is -> no sim result (compute a result in GRT first)");

            // Accumulate into the existing report across repeated "Run" clicks (that's the point --
            // one substitution at a time, results piling up) rather than starting a fresh one each
            // time; only the very first run's baseline is kept.
            var report = _report ?? new WhatIfReport { BaselineMv = bmv, BaselinePmax = bpm, PmaxUnit = bpu };

            foreach (var r in selected)
            {
                double measuredDisplay = (double)r.Measured.Value;
                double measuredNative = ToNative(r, measuredDisplay);
                var doc = GrtLoadDoc.Load(basePath);
                double? fileNative = WhatIfEngine.CurrentValue(doc, r.Param);

                var (mv, pmax, pmu) = await RunOneAsync(basePath, d => WhatIfEngine.Substitute(d, r.Param, measuredNative), tmpDir, seq++);
                report.Points.Add(new WhatIfPoint { Param = r.Param, FileValue = fileNative, MeasuredValue = measuredNative, MvMps = mv, Pmax = pmax, PmaxUnit = pmu });
                AppendLog(mv is { } m ? $"{r.Param.Label} -> measured {measuredDisplay:0.###} {DisplayUnit(r)} -> MV {m:0.0} m/s, Pmax {pmax:0} {pmu}" : $"{r.Param.Label} -> no sim result");
                RefreshGrid(report);
            }

            await _grt.LoadFileAsync(basePath);
            try { Directory.Delete(tmpDir, true); } catch { }

            _report = report;
            _summary.Text = report.Build(caliberName, powderName).Replace("\n", "\r\n") + "\r\n" + _summary.Text;
            _writeBtn.Enabled = report.Points.Any(p => p.MvMps is not null);
            _status.Text = $"tested {selected.Count} substitution(s) -- close the extra GRT tabs when done.";
        }
        catch (Exception ex) { Err(ex); }
        finally { _runBtn.Enabled = true; }
    }

    // A unique path per run ("whatif_NNN.grtload"), not one reused scratch file: tried reusing a
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
        string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "whatif_{0:000}.grtload", seq));
        doc.Save(tmp);

        await _grt!.LoadFileAsync(tmp);
        await Task.Delay(1400);                            // let GRT compute
        var t2 = await _grt.GetTabOnTopAsync();
        var res = await _grt.GetTabResultsAsync(t2.handle);
        return (res.MuzzleVelocityMps, res.MaxPressure, res.MaxPressureUnit);
    }

    private void RefreshGrid(WhatIfReport report)
    {
        _grid.Rows.Clear();
        var ci = CultureInfo.InvariantCulture;
        foreach (var p in report.Points)
        {
            var row = _rowByParam[p.Param];
            double? dmv = p.MvMps is { } mv && report.BaselineMv is { } b ? mv - b : null;
            double? dpm = p.Pmax is { } pm && report.BaselinePmax is { } bp ? pm - bp : null;
            _grid.Rows.Add(
                p.Param.Label,
                p.FileValue is { } fv ? ToDisplay(row, fv).ToString("0.###", ci) : "-",
                ToDisplay(row, p.MeasuredValue).ToString("0.###", ci),
                p.MvMps is { } m ? m.ToString("0.0", ci) : "",
                dmv is { } d ? WhatIfReport.Signed(d, "0.0", ci) : "",
                p.Pmax is { } pp ? pp.ToString("0", ci) : "",
                dpm is { } dp ? WhatIfReport.Signed(dp, "0", ci) : "");
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
            string outPath = doc.SaveSibling("whatif");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = "What-if note written and opened in GRT.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private void AppendLog(string line) => _summary.SafeAppend(line);

    private void Err(Exception ex)
    {
        AppendLog("ERROR: " + ex.Message);
        MessageBox.Show(this, ex.Message, "What if", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
