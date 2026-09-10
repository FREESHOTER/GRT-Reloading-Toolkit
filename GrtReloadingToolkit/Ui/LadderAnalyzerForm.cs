using System.Globalization;
using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>Shared UI for the charge-ladder (OCW) and seating-depth analysers.</summary>
internal abstract class LadderAnalyzerForm : Form
{
    protected abstract LadderMode Mode { get; }
    protected abstract string WindowTitle { get; }
    protected abstract string XHeader { get; }          // grid column header
    protected abstract string XUnit { get; }            // "gr" / "mm" / "in"
    protected abstract string NoteTitle { get; }        // stable, e.g. "OCW Analysis"
    protected abstract string GalleryPictureName { get; } // stable, e.g. "ocw_chart" — for ~~result.picture.<name>~~
    protected abstract string SiblingSuffix { get; }    // e.g. "ocw" / "seating"
    protected abstract string? PresetEnvVar { get; }

    private readonly GrtClient? _grt;
    private readonly TextBox _folder = new() { ReadOnly = true, Width = 420 };
    private readonly DataGridView _grid = new();
    private readonly LadderChart _chart = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly NumericUpDown _wPoi = new() { DecimalPlaces = 1, Increment = 0.1M, Minimum = 0, Maximum = 5, Width = 55 };
    private readonly NumericUpDown _wPrim = new() { DecimalPlaces = 1, Increment = 0.1M, Minimum = 0, Maximum = 5, Width = 55 };
    private readonly NumericUpDown _win = new() { Minimum = 3, Maximum = 5, Value = 3, Width = 45 };
    private readonly Button _writeBtn = new() { Text = "Write note to GRT load", AutoSize = true, Enabled = false };

    // GRT shot-group source
    private readonly ComboBox _refUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55, Items = { "mm", "cm", "in" } };
    private readonly ComboBox _shootUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 45, Items = { "m", "yd" } };
    private readonly CheckBox _noFlyers = new() { Text = "drop flyers", AutoSize = true };

    private string? _folderPath;
    private List<TargetGroup>? _grtGroups;   // set when the source is GRT shot-group tabs
    private List<LadderVelocity>? _grtVels;  // velocities read from the open load's Measurement
    private LadderResult? _result;
    private Action<string>? _logHandler;

    protected LadderAnalyzerForm(GrtClient? grt)
    {
        _grt = grt;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_grt != null && _logHandler != null) _grt.Log -= _logHandler;
        base.OnFormClosed(e);
    }

    // called by subclass ctor after base fields exist
    protected void Init()
    {
        Text = WindowTitle;
        Width = 1000; Height = 680;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 520);
        var dw = AnalysisWeights.For(Mode);
        _wPoi.Value = (decimal)dw.Poi;
        _wPrim.Value = (decimal)(Mode == LadderMode.Seating ? dw.Group : dw.Velocity);
        Build();
        NudFix.ApplyTo(this);
        if (_grt != null) { _logHandler = AppendLog; _grt.Log += _logHandler; }
        UpdateStatus();

        string? preset = PresetEnvVar is null ? null : Environment.GetEnvironmentVariable(PresetEnvVar);
        if (!string.IsNullOrWhiteSpace(preset) && Directory.Exists(preset))
            Shown += async (_, _) => { _folderPath = preset; _folder.Text = preset; await RefreshLoadVelocitiesAsync(); Reanalyze(); };
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(6, 6, 6, 0), WrapContents = true };
        var pick = new Button { Text = "Pick ladder folder…", AutoSize = true };
        pick.Click += async (_, _) => await PickFolderAsync();
        top.Controls.Add(pick);
        top.Controls.Add(_folder);
        top.Controls.Add(new Label { Text = "  w POI", AutoSize = true, Padding = new Padding(10, 6, 0, 0) });
        top.Controls.Add(_wPoi);
        top.Controls.Add(new Label { Text = Mode == LadderMode.Seating ? "w group" : "w MV", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        top.Controls.Add(_wPrim);
        top.Controls.Add(new Label { Text = "window", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        top.Controls.Add(_win);
        var re = new Button { Text = "Re-analyze", AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        re.Click += (_, _) => Reanalyze();
        top.Controls.Add(re);

        top.SetFlowBreak(pick, false);
        var grpBtn = new Button { Text = "Groups from GRT load", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        top.SetFlowBreak(re, true);   // start a new row
        grpBtn.Click += async (_, _) => await LoadGrtGroupsAsync();
        _refUnit.SelectedIndex = 0; _shootUnit.SelectedIndex = 0;
        foreach (var c in new Control[] { _refUnit, _shootUnit, _noFlyers }) c.Margin = new Padding(6, 8, 0, 0);
        top.Controls.Add(grpBtn);
        top.Controls.Add(new Label { Text = "ref dist", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_refUnit);
        top.Controls.Add(new Label { Text = "shoot dist", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        top.Controls.Add(_shootUnit);
        top.Controls.Add(_noFlyers);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("x", XHeader), ("vn", "n"), ("mv", "MV m/s"), ("sd", "SD"), ("es", "ES"),
                     ("d", "Dist m"), ("poi", "POI-Y MOA"), ("mr", "Grp MR MOA"), ("vs", "Vert MOA") })
            _grid.Columns.Add(n, h);

        _chart.Dock = DockStyle.Fill;

        _summary.Multiline = true;
        _summary.ReadOnly = true;
        _summary.ScrollBars = ScrollBars.Vertical;
        _summary.Font = new Font(FontFamily.GenericMonospace, 8.25f);
        _summary.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 210 };
        split.Panel1.Controls.Add(_grid);
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 560 };
        rightSplit.Panel1.Controls.Add(_chart);
        rightSplit.Panel2.Controls.Add(_summary);
        split.Panel2.Controls.Add(rightSplit);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _writeBtn.Click += async (_, _) => await WriteNoteAsync();
        bottom.Controls.Add(_writeBtn);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.ForeColor = SystemColors.GrayText;
        _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private async Task PickFolderAsync()
    {
        using var d = new FolderBrowserDialog { Description = "Folder with chrono *.xlsx (Athlon/Garmin) and Ballistic-X *.csv for one ladder" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _folderPath = d.SelectedPath;
        _folder.Text = _folderPath;
        _grtGroups = null;   // folder source replaces the GRT shot-group source
        await RefreshLoadVelocitiesAsync();   // best-effort: fill gaps from the open load's Measurement
        Reanalyze();
    }

    /// <summary>Reads per-charge velocities from the open GRT load's Measurement(s), best-effort.</summary>
    private async Task RefreshLoadVelocitiesAsync()
    {
        _grtVels = null;
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) return;
            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            var vlist = new List<LadderVelocity>();
            foreach (var meas in doc.Measurements())
                foreach (var ch in meas.Charges)
                {
                    var vs = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 50).ToList();
                    if (vs.Count == 0) continue;
                    if (LadderLoader.MeasurementStep(Mode, ch.ChargeGrains, ch.Name, ch.Note) is { } step)
                        vlist.Add(new LadderVelocity(step, vs));
                }
            if (vlist.Count > 0) { _grtVels = vlist; AppendLog($"velocities available from load Measurement: {vlist.Count} charge(s)"); }
        }
        catch (Exception ex) { AppendLog("load-velocity read skipped: " + ex.Message); }
    }

    private async Task LoadGrtGroupsAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            AppendLog($"reading shot groups from: {top.caption} [{top.file}]");
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, "No saved load is open in GRT.", "Groups from GRT"); return;
            }
            var doc = GrtLoadDoc.Load(top.file);
            var opt = new GrtShotGroups.Options(
                _refUnit.SelectedIndex switch { 1 => RefUnit.Cm, 2 => RefUnit.Inch, _ => RefUnit.Mm },
                _shootUnit.SelectedIndex == 1 ? ShootUnit.Yards : ShootUnit.Meters,
                _noFlyers.Checked);
            var (groups, log) = GrtShotGroups.FromDoc(doc, opt);
            foreach (var l in log) AppendLog(l);
            if (groups.Count == 0) { MessageBox.Show(this, "No usable shot groups in that load.", "Groups from GRT"); return; }

            // so the MV flat-spot appears without also pointing at a chrono folder
            await RefreshLoadVelocitiesAsync();

            _grtGroups = groups;
            _folder.Text = $"GRT shot groups ({groups.Count}) — {Path.GetFileName(top.file)}";
            Reanalyze();
        }
        catch (Exception ex)
        {
            AppendLog("GROUPS FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Groups from GRT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Reanalyze()
    {
        if (_folderPath is null && _grtGroups is null) return;
        try
        {
            var loaded = _grtGroups is { } gg
                ? LadderLoader.FromGroups(gg, Mode, _folderPath)
                : LadderLoader.FromFolder(_folderPath!, Mode);
            if (_grtVels is { } lv) LadderLoader.FillMissingVelocities(loaded, lv);
            foreach (var l in loaded.Log) AppendLog(l);

            var w = AnalysisWeights.For(Mode);
            w.Poi = (double)_wPoi.Value;
            if (Mode == LadderMode.Seating) w.Group = (double)_wPrim.Value; else w.Velocity = (double)_wPrim.Value;
            w.Window = (int)_win.Value;

            _result = LadderAnalyzer.Analyze(loaded.Rows, Mode, XUnit, w);

            _grid.Rows.Clear();
            foreach (var x in _result.Rows)
                _grid.Rows.Add(
                    x.X.ToString(Mode == LadderMode.Seating ? "0.0##" : "0.0", CultureInfo.InvariantCulture),
                    x.HasVel ? x.VelN : 0,
                    x.HasVel ? x.MeanMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasVel ? x.SdMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasVel ? x.EsMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.DistanceM.ToString("0", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.PoiYMoa.ToString("0.00", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.MeanRadiusMoa.ToString("0.00", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.VertSpreadMoa.ToString("0.00", CultureInfo.InvariantCulture) : "");

            if (_result.BestNode is { } bn)
                foreach (DataGridViewRow row in _grid.Rows)
                    if (double.TryParse((string)row.Cells["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double c)
                        && c >= bn.Low - 1e-6 && c <= bn.High + 1e-6)
                        row.DefaultCellStyle.BackColor = Color.FromArgb(215, 245, 220);

            _chart.SetData(_result);
            _summary.Text = LadderAnalyzer.BuildReport(_result, HeadlineFor(DateTime.Now)).Replace("\n", "\r\n");
            _writeBtn.Enabled = _result.BestNode != null && _grt is { Connected: true };
            _status.Text = _result.BestNode is { } n
                ? string.Format(CultureInfo.InvariantCulture, "Recommended node {0:0.0##}–{1:0.0##} {2}", n.Low, n.High, XUnit)
                : "analysis done";
        }
        catch (Exception ex)
        {
            AppendLog("ANALYZE FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Analyze failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string HeadlineFor(DateTime d) => $"{NoteTitle} {d:yyyy-MM-dd}";

    private async Task WriteNoteAsync()
    {
        if (_result?.BestNode is null || _grt is not { Connected: true }) return;
        try
        {
            _writeBtn.Enabled = false;
            var top = await _grt.GetTabOnTopAsync();
            AppendLog($"active load: {top.caption} [{top.file}]");
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, "No saved load is open in GRT.", "Write note"); return;
            }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file — switch to your real load in GRT first.", "Write note",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            int removed = doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, LadderAnalyzer.BuildReport(_result, HeadlineFor(DateTime.Now)));
            var png = _chart.RenderPng();
            if (png != null)
            {
                doc.AddGalleryPicture($"{NoteTitle} chart", GalleryPictureName, png);
                AppendLog($"embedded chart as gallery picture '{GalleryPictureName}'");
            }
            string outPath = doc.SaveSibling(SiblingSuffix);
            if (removed > 0) AppendLog($"replaced {removed} earlier '{NoteTitle}' note(s)");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = "Note written — opened in GRT as a new tab.";
        }
        catch (Exception ex)
        {
            AppendLog("WRITE FAILED: " + ex);
            MessageBox.Show(this, ex.Message, "Write failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _writeBtn.Enabled = _result?.BestNode != null && _grt is { Connected: true }; }
    }

    private void UpdateStatus()
    {
        string conn = _grt == null ? "stand-alone (no GRT)"
            : _grt.Connected ? $"connected to GRT :{_grt.Port}" : "GRT connection lost";
        _status.Text = "Pick a ladder folder — " + conn;
    }

    private void AppendLog(string line) => _summary.SafeAppend(line);
}
