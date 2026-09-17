using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtReloadingToolkit.ChronoStats;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The statistics GRT's own AVG/SD/ES line on an imported Measurement doesn't give you: a
/// confidence interval on the true mean, how many shots you'd need to tighten it, Chauvenet-flagged
/// outliers, and a proper two-sample comparison (Welch t-test on the means, F-test on the spreads)
/// between any two strings — "is this charge actually faster, or is that chrono noise?"
/// </summary>
internal sealed class ChronoStatsForm : Form
{
    private const string NoteTitle = "Chrono Statistics";

    private sealed class Row
    {
        public required string Label { get; init; }
        public required List<double> Values { get; init; }
        public ChronoSummary Summary { get; set; } = null!;
    }

    private readonly GrtClient? _grt;
    private readonly List<Row> _rows = new();

    private readonly DataGridView _grid = new();
    private readonly ComboBox _confidence = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
    private readonly NumericUpDown _targetMargin = new() { DecimalPlaces = 1, Increment = 0.5M, Minimum = 0.5M, Maximum = 50M, Value = 3M, Width = 55 };
    private readonly ComboBox _cmpA = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox _cmpB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _cmpResult = new();
    private readonly Label _status = new();
    private readonly Button _write = new() { Text = Lang.T("Write note to GRT load"), AutoSize = true, Enabled = false };

    private string? _basePath;
    private (string a, string b, TwoSampleResult r)? _lastCompare;
    private readonly Action<string> _logHandler;

    public ChronoStatsForm(GrtClient? grt)
    {
        _grt = grt;
        _logHandler = AppendLog;
        Text = AppVersion.Title(Lang.T("Chronograph Statistics"));
        Width = 920; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(6, 6, 0, 0), WrapContents = true };

        var addFiles = new Button { Text = Lang.T("Add chrono files…"), AutoSize = true };
        addFiles.Click += (_, _) => AddFiles();
        top.Controls.Add(addFiles);
        var addFolder = new Button { Text = Lang.T("Add folder…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        addFolder.Click += (_, _) => AddFolder();
        top.Controls.Add(addFolder);
        var fromLoad = new Button { Text = Lang.T("From GRT load's Measurement"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        fromLoad.Click += async (_, _) => await AddFromLoadAsync();
        top.Controls.Add(fromLoad);
        top.Controls.Add(new Label { Text = "  " + Lang.T("confidence"), AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _confidence.Items.AddRange(new object[] { "90%", "95%", "99%" });
        _confidence.SelectedIndex = 1;
        _confidence.SelectedIndexChanged += (_, _) => Recompute();
        top.Controls.Add(_confidence);
        top.Controls.Add(new Label { Text = Lang.T("target ± m/s"), AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        _targetMargin.ValueChanged += (_, _) => Recompute();
        top.Controls.Add(_targetMargin);

        top.SetFlowBreak(_targetMargin, true);   // start a second row
        top.Controls.Add(new Label { Text = Lang.T("Compare"), AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        top.Controls.Add(_cmpA);
        top.Controls.Add(new Label { Text = " " + Lang.T("vs") + " ", AutoSize = true, Padding = new Padding(4, 8, 4, 0) });
        top.Controls.Add(_cmpB);
        var cmpBtn = new Button { Text = Lang.T("Compare"), AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
        cmpBtn.Click += (_, _) => Compare();
        top.Controls.Add(cmpBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("lbl", Lang.T("String")), ("n", "n"), ("mean", Lang.T("Mean m/s")), ("sd", "SD"), ("es", "ES"),
                     ("ci", Lang.T("CI ± (mean)")), ("need", Lang.T("shots for target")), ("out", Lang.T("outliers")) })
            _grid.Columns.Add(n, h);

        _cmpResult.Multiline = true; _cmpResult.ReadOnly = true; _cmpResult.ScrollBars = ScrollBars.Vertical;
        _cmpResult.Font = new Font(FontFamily.GenericMonospace, 9f);
        _cmpResult.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(220);
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_cmpResult);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _write.Click += async (_, _) => await WriteAsync();
        bottom.Controls.Add(_write);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private double Confidence => _confidence.SelectedIndex switch { 0 => 0.90, 2 => 0.99, _ => 0.95 };
    private double TargetMarginMps => (double)_targetMargin.Value;

    private void AddFiles()
    {
        using var d = new OpenFileDialog { Multiselect = true, Filter = "Chronograph export (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*", Title = Lang.T("One chrono file per string") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(d.FileNames, showErrorDialog: true);
    }

    private void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = Lang.T("Folder with chrono files (Athlon / Garmin, .xlsx / .csv), one per string") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var exts = new[] { ".xlsx", ".csv", ".tsv" };
        var files = Directory.EnumerateFiles(d.SelectedPath)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) { MessageBox.Show(this, Lang.T("No .xlsx / .csv files in that folder."), Lang.T("Add folder")); return; }
        int before = _rows.Count;
        AddPaths(files, showErrorDialog: false);   // the folder may hold target CSVs etc. — just skip those
        AppendLog($"folder: {_rows.Count - before} of {files.Count} file(s) added from {Path.GetFileName(d.SelectedPath)}");
    }

    private void AddPaths(IEnumerable<string> paths, bool showErrorDialog)
    {
        foreach (string path in paths)
        {
            try
            {
                var a = AthlonParser.Parse(path);
                var vs = a.Velocities.ToList();
                if (vs.Count == 0) { AppendLog($"{Path.GetFileName(path)}: no shots, skipped"); continue; }
                string label = a.ChargeGrains is { } g
                    ? FormattableString.Invariant($"{g:0.0##} gr ({Path.GetFileName(path)})")
                    : Path.GetFileName(path);
                _rows.Add(new Row { Label = label, Values = vs });
                AppendLog($"{Path.GetFileName(path)}: {vs.Count} shots");
            }
            catch (Exception ex)
            {
                AppendLog($"{Path.GetFileName(path)}: {ex.Message}");
                if (showErrorDialog) MessageBox.Show(this, ex.Message, Path.GetFileName(path), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        Recompute();
    }

    private async Task AddFromLoadAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }
            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            _basePath ??= top.file;
            int added = 0;
            foreach (var meas in doc.Measurements())
                foreach (var ch in meas.Charges)
                {
                    var vs = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 50).ToList();
                    if (vs.Count == 0) continue;
                    string label = ch.ChargeGrains is { } g
                        ? FormattableString.Invariant($"{g:0.0##} gr ({meas.Title})")
                        : $"{ch.Name} ({meas.Title})";
                    _rows.Add(new Row { Label = label, Values = vs });
                    added++;
                }
            AppendLog($"load Measurement: {added} string(s) added");
            Recompute();
        }
        catch (Exception ex) { Err(ex); }
    }

    private void Recompute()
    {
        double conf = Confidence;
        foreach (var r in _rows) r.Summary = ChronoStatsCalc.Analyze(r.Values, conf);

        _grid.Rows.Clear();
        foreach (var r in _rows)
        {
            var s = r.Summary;
            int need = ChronoStatsCalc.RequiredSampleSize(s.SampleSd, TargetMarginMps, conf);
            string needTxt = need switch { 0 => "–", -1 => ">1000", _ => need.ToString(CultureInfo.InvariantCulture) };
            int flagged = s.Outliers.Count(o => o.Flagged);
            _grid.Rows.Add(r.Label, s.N,
                s.Mean.ToString("0.0", CultureInfo.InvariantCulture),
                s.Sd.ToString("0.0", CultureInfo.InvariantCulture),
                s.Es.ToString("0.0", CultureInfo.InvariantCulture),
                FormattableString.Invariant($"±{s.CiMarginMps:0.0}"),
                needTxt,
                flagged > 0 ? string.Format(Lang.T("{0} flagged"), flagged) : "–");
        }

        string? selA = _cmpA.SelectedItem as string, selB = _cmpB.SelectedItem as string;
        _cmpA.Items.Clear(); _cmpB.Items.Clear();
        foreach (var r in _rows) { _cmpA.Items.Add(r.Label); _cmpB.Items.Add(r.Label); }
        if (selA != null && _cmpA.Items.Contains(selA)) _cmpA.SelectedItem = selA; else if (_cmpA.Items.Count > 0) _cmpA.SelectedIndex = 0;
        if (selB != null && _cmpB.Items.Contains(selB)) _cmpB.SelectedItem = selB; else if (_cmpB.Items.Count > 1) _cmpB.SelectedIndex = 1;

        _write.Enabled = _rows.Count > 0 && _grt is { Connected: true };
    }

    private void Compare()
    {
        if (_cmpA.SelectedItem is not string la || _cmpB.SelectedItem is not string lb) { MessageBox.Show(this, Lang.T("Pick two strings first.")); return; }
        var ra = _rows.FirstOrDefault(r => r.Label == la);
        var rb = _rows.FirstOrDefault(r => r.Label == lb);
        if (ra is null || rb is null) return;
        if (ReferenceEquals(ra, rb)) { MessageBox.Show(this, Lang.T("Pick two different strings.")); return; }

        var result = ChronoStatsCalc.Compare(ra.Values, rb.Values);
        _lastCompare = (la, lb, result);
        _cmpResult.Text = ChronoStatsCalc.BuildCompareReport(la, lb, result).Replace("\n", "\r\n");
    }

    private async Task WriteAsync()
    {
        try
        {
            if (_grt is not { Connected: true }) return;
            var top = await _grt.GetTabOnTopAsync();
            string basePath = _basePath ?? top.file;
            if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath)) { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            report.AppendLine();
            report.Append(ChronoStatsCalc.BuildTableReport(_rows.Select(r => (r.Label, r.Summary)).ToList(), TargetMarginMps, Confidence));
            if (_lastCompare is { } c)
            {
                report.AppendLine();
                report.Append(ChronoStatsCalc.BuildCompareReport(c.a, c.b, c.r));
            }

            var doc = GrtLoadDoc.OpenForToolkitEdit(basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, report.ToString());
            string outPath = doc.SaveSibling("chronostats");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Chrono Statistics note written and opened in GRT.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private void Err(Exception ex) { AppendLog("ERROR: " + ex.Message); MessageBox.Show(this, ex.Message, Lang.T("Chrono Statistics"), MessageBoxButtons.OK, MessageBoxIcon.Error); }

    private void AppendLog(string line) => _cmpResult.SafeAppend(line);
}
