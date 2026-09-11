using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtReloadingToolkit.TempCoeff;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Fit a powder's GRT temperature coefficients (tcc/tch) from Athlon strings of the
/// same charge shot at different temperatures.
/// </summary>
internal sealed class TempCoeffForm : Form
{
    private const string NoteTitle = "Powder Temp Coefficients";

    private readonly GrtClient? _grt;
    private readonly DataGridView _grid = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly NumericUpDown _ba = new() { DecimalPlaces = 6, Increment = 0.001M, Maximum = 100, Width = 90 };
    private readonly Button _write = new() { Text = "Write tcc/tch to GRT load", AutoSize = true, Enabled = false };

    private readonly List<TempPoint> _points = new();
    private double _baManual;
    private string _powder = "powder";
    private string? _basePath;
    private bool _suppress;
    private TempCoeffResult? _result;
    private readonly Action<string> _logHandler;

    public TempCoeffForm(GrtClient? grt)
    {
        _grt = grt;
        _logHandler = AppendLog;
        Text = AppVersion.Title("GRT Powder Temp-Coefficient Fitter");
        Width = 860; Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(660, 420);
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
        var pick = new Button { Text = "Add Athlon strings…", AutoSize = true };
        pick.Click += (_, _) => AddFiles();
        top.Controls.Add(pick);
        var loadBa = new Button { Text = "Load Ba from GRT load", AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
        loadBa.Click += async (_, _) => await LoadBaAsync();
        top.Controls.Add(loadBa);
        top.Controls.Add(new Label { Text = "  Ba", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        top.Controls.Add(_ba);
        _ba.ValueChanged += (_, _) => { _baManual = (double)_ba.Value; Recompute(); };
        var re = new Button { Text = "Fit", AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
        re.Click += (_, _) => Recompute();
        top.Controls.Add(re);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "use", HeaderText = "Use", FillWeight = 7 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File", ReadOnly = true, FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "chg", HeaderText = "Charge gr", ReadOnly = true, FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "temp", HeaderText = "Temp °C", FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "n", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "mv", HeaderText = "MV m/s", ReadOnly = true, FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sd", HeaderText = "SD", ReadOnly = true, FillWeight = 8 });
        _grid.CellEndEdit += (_, _) => { if (_suppress) return; SyncFromGrid(); Recompute(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, e) => { if (!_suppress && e.ColumnIndex == 0) { SyncFromGrid(); Recompute(); } };

        _summary.Multiline = true; _summary.ReadOnly = true; _summary.ScrollBars = ScrollBars.Vertical;
        _summary.Font = new Font(FontFamily.GenericMonospace, 8.5f);
        _summary.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_summary);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _write.Click += async (_, _) => await WriteAsync();
        bottom.Controls.Add(_write);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private void AddFiles()
    {
        using var d = new OpenFileDialog { Multiselect = true, Filter = "Athlon export (*.xlsx)|*.xlsx|All files|*.*", Title = "Athlon strings — same charge, different temperatures" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        foreach (string f in d.FileNames)
        {
            if (_points.Any(p => string.Equals(p.File, f, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var a = AthlonParser.Parse(f);
                var st = StringStats.From(a.Velocities.ToList());
                _points.Add(new TempPoint
                {
                    File = f,
                    ChargeGr = a.ChargeGrains ?? 0,
                    TempC = a.SessionTempC ?? 21,
                    N = st.N, MeanMps = st.Mean, SdMps = st.Sd,
                });
                AppendLog($"{Path.GetFileName(f)}: {st.N} shots, {a.ChargeGrains} gr, temp {(a.SessionTempC is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) : "?")}°C");
            }
            catch (Exception ex) { AppendLog($"{Path.GetFileName(f)}: {ex.Message}"); }
        }
        RefreshGrid();
        Recompute();
    }

    private async Task LoadBaAsync()
    {
        try
        {
            if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file — switch to your real load.", "Temp coefficients", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _basePath = top.file;
            string pristine = GrtLoadDoc.PristineBasePath(top.file);   // read Ba from the original, not a corrected sibling
            var doc = GrtLoadDoc.Load(File.Exists(pristine) ? pristine : top.file);
            _powder = string.IsNullOrWhiteSpace(doc.PropellantName) ? "powder" : doc.PropellantName;
            if (doc.PropellantBa is { } ba && ba > 0) { _suppress = true; _ba.Value = (decimal)Math.Min(100, ba); _suppress = false; _baManual = ba; }
            AppendLog($"load '{doc.CaliberName}'  powder {_powder}  Ba {doc.PropellantBa}");
            Recompute();
        }
        catch (Exception ex) { Err(ex); }
    }

    private void RefreshGrid()
    {
        _suppress = true;
        _grid.Rows.Clear();
        foreach (var p in _points.OrderBy(p => p.TempC))
        {
            int i = _grid.Rows.Add(p.Use, Path.GetFileName(p.File),
                p.ChargeGr > 0 ? p.ChargeGr.ToString("0.0##", CultureInfo.InvariantCulture) : "?",
                p.TempC.ToString("0.0", CultureInfo.InvariantCulture),
                p.N, p.MeanMps.ToString("0.0", CultureInfo.InvariantCulture), p.SdMps.ToString("0.0", CultureInfo.InvariantCulture));
            _grid.Rows[i].Tag = p;
        }
        _suppress = false;
    }

    private void SyncFromGrid()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not TempPoint p) continue;
            p.Use = row.Cells["use"].Value is true;
            if (GrtPluginKit.Util.Str.ParseNumber(Convert.ToString(row.Cells["temp"].Value, CultureInfo.InvariantCulture)) is { } t)
                p.TempC = t;
        }
    }

    private void Recompute()
    {
        double ba = _baManual > 0 ? _baManual : (double)_ba.Value;
        _result = TempCoeffResult.Fit(_points, ba, _powder);
        _summary.Text = _result.BuildReport($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}").Replace("\n", "\r\n");
        bool haveCoeff = _result.Tcc is not null || _result.Tch is not null;
        _write.Enabled = haveCoeff && _grt is { Connected: true } && _basePath != null;

        var used = _points.Where(p => p.Use && p.MeanMps > 0).ToList();
        var charges = used.Select(p => Math.Round(p.ChargeGr, 2)).Distinct().ToList();
        _status.Text = charges.Count > 1
            ? $"WARNING: {charges.Count} different charges selected — temp fit needs ONE charge"
            : haveCoeff
                ? string.Format(CultureInfo.InvariantCulture, "tcc={0}  tch={1}", _result.Tcc?.ToString("0.######") ?? "–", _result.Tch?.ToString("0.######") ?? "–")
                : "add strings at 2+ temperatures (ideally cold / 21°C / hot)";
    }

    private async Task WriteAsync()
    {
        try
        {
            if (_basePath is null || _result is null || _grt is not { Connected: true }) return;
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, _result.BuildReport($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}"));
            if (_result.Tcc is { } tcc) { if (doc.SetInput("propellant", "tcc", tcc.ToString("0.###############", CultureInfo.InvariantCulture))) AppendLog($"set tcc = {tcc:0.######}"); }
            if (_result.Tch is { } tch) { if (doc.SetInput("propellant", "tch", tch.ToString("0.###############", CultureInfo.InvariantCulture))) AppendLog($"set tch = {tch:0.######}"); }
            string outPath = doc.SaveSibling("tcoeff");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = "tcc/tch written and opened in GRT.";
        }
        catch (Exception ex) { Err(ex); }
    }

    private void Err(Exception ex) { AppendLog("ERROR: " + ex.Message); MessageBox.Show(this, ex.Message, "Temp coefficients", MessageBoxButtons.OK, MessageBoxIcon.Error); }

    private void AppendLog(string line) => _summary.SafeAppend(line);
}
