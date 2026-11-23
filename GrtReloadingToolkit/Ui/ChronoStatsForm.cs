using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtReloadingToolkit.ChronoStats;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Measuring;
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
    private readonly NextStepBar? _next = NextStepBar.For("ChronoStats");
    private const string NoteTitle = "Chrono Statistics";

    private sealed class Row
    {
        public required string Label { get; init; }
        public required List<double> Values { get; init; }
        public ChronoSummary Summary { get; set; } = null!;
        /// <summary>The archive string it was read from, when it came from the archive.</summary>
        public long? ArchiveId { get; init; }
    }

    private readonly GrtClient? _grt;
    private readonly List<Row> _rows = new();
    private readonly GrtUnits _u = GrtUnits.Current;

    private readonly DataGridView _grid = new();
    private readonly ComboBox _confidence = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
    private readonly NumericUpDown _targetMargin = new() { DecimalPlaces = 1, Increment = 0.5M, Minimum = 0.5M, Maximum = 50M, Value = 3M, Width = 55 };
    private readonly ComboBox _cmpA = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox _cmpB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly TextBox _cmpResult = new();
    private readonly StringsChart _chart = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new();
    private readonly CheckBox _exclude = new() { AutoSize = true, Margin = new Padding(14, 6, 0, 0),
        Text = Lang.T("Exclude shots flagged by Chauvenet from the statistics") };
    private readonly Button _write = new() { Text = Lang.T("Write note to GRT load"), AutoSize = true, Enabled = false };
    private readonly Button _saveArchive = new() { Text = Lang.T("Save in the archive"), AutoSize = true, Enabled = false };
    private readonly Button _pdf = new() { Text = Lang.T("PDF report"), AutoSize = true, Enabled = false };
    private readonly ArchiveLink? _link;
    private long? _archLoad, _archSession;

    private string? _basePath;
    private (string a, string b, TwoSampleResult r)? _lastCompare;
    private readonly Action<string> _logHandler;

    public ChronoStatsForm(GrtClient? grt, ArchiveLink? link = null)
    {
        _grt = grt;
        _link = link;
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
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 6, 0, 0), WrapContents = true };

        var addFiles = new Button { Text = Lang.T("Add chrono files…"), AutoSize = true };
        addFiles.Click += (_, _) => AddFiles();
        top.Controls.Add(addFiles);
        var addFolder = new Button { Text = Lang.T("Add folder…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        addFolder.Click += (_, _) => AddFolder();
        top.Controls.Add(addFolder);
        var fromLoad = new Button { Text = Lang.T("From GRT load's Measurement"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        fromLoad.Click += async (_, _) => await AddFromLoadAsync();
        top.Controls.Add(fromLoad);
        if (_link != null)
        {
            var fromArchive = new Button { Text = Lang.T("From the archive…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            fromArchive.Click += (_, _) => AddFromArchive();
            top.Controls.Add(fromArchive);
        }
        top.Controls.Add(new Label { Text = "  " + Lang.T("confidence"), AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        _confidence.Items.AddRange(new object[] { "90%", "95%", "99%" });
        _confidence.SelectedIndex = 1;
        _confidence.SelectedIndexChanged += (_, _) => Recompute();
        top.Controls.Add(_confidence);
        top.Controls.Add(new Label { Text = Lang.T("target ±") + " " + _u.VelocityUnitName, AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        // The margin is typed in GRT's own velocity unit, so its range travels with it: the field
        // initializer's 0.5/50/3 are m/s, which as bare numbers would mean a twelfth of the range
        // and a third of the default to someone working in ft/s. Set before the handler is wired,
        // and Maximum before Value, or the assignment clamps against the old ceiling.
        if (_u.VelocityInFps)
        {
            _targetMargin.Maximum = 165M;   // ~50 m/s
            _targetMargin.Minimum = 1.5M;   // ~0.5 m/s
            _targetMargin.Increment = 1M;
            NudFix.Set(_targetMargin, _u.VelocityValue(3.0));
        }
        _targetMargin.ValueChanged += (_, _) => Recompute();
        top.Controls.Add(_targetMargin);
        _exclude.CheckedChanged += (_, _) => { Recompute(); if (_lastCompare is not null) Compare(); };
        top.Controls.Add(_exclude);

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
        foreach (var (n, h) in new[] { ("lbl", Lang.T("String")), ("n", "n"), ("mean", Lang.T("Mean") + " " + _u.VelocityUnitName),
                     ("sd", "SD " + _u.VelocityUnitName), ("es", "ES " + _u.VelocityUnitName),
                     ("ci", Lang.T("CI ± (mean)") + " " + _u.VelocityUnitName),
                     ("need", Lang.T("shots for target")), ("out", Lang.T("outliers")) })
            _grid.Columns.Add(n, h);

        _cmpResult.Multiline = true; _cmpResult.ReadOnly = true; _cmpResult.ScrollBars = ScrollBars.Vertical;
        _cmpResult.Font = new Font(FontFamily.GenericMonospace, 9f);
        _cmpResult.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(220);
        split.Panel1.Controls.Add(_grid);
        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical }.WithDistance(420);
        lower.Panel1.Controls.Add(_cmpResult);
        lower.Panel2.Controls.Add(_chart);
        split.Panel2.Controls.Add(lower);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _write.Click += async (_, _) => await WriteAsync();
        bottom.Controls.Add(_write);
        if (_link != null)
        {
            _saveArchive.Click += (_, _) => SaveToArchive();
            bottom.Controls.Add(_saveArchive);
        }
        _pdf.Click += async (_, _) => await PdfAsync();
        bottom.Controls.Add(_pdf);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
        if (_next != null) { Controls.Add(_next); _ = _next.TrackLoadAsync(_grt); }
    }

    /// <summary>The window drawn for a picture (<c>--anteprima cronostat2</c>): a ladder of seven charges, five invented shots each, with a flat spot in the middle.</summary>
    public static ChronoStatsForm Preview()
    {
        var f = new ChronoStatsForm(null);
        double[][] shots =
        {
            new[] { 801.0, 806.5, 798.2, 803.9, 800.1 }, new[] { 812.4, 809.8, 815.0, 811.1, 813.3 }, new[] { 821.9, 823.4, 820.7, 822.5, 821.2 },
            new[] { 822.8, 821.5, 823.9, 822.2, 823.0 }, new[] { 823.5, 822.9, 824.1, 823.2, 823.8 }, new[] { 832.6, 829.1, 835.8, 831.0, 842.5 },
            new[] { 840.2, 845.9, 838.4, 843.1, 841.7 },
        };
        for (int i = 0; i < shots.Length; i++)
            f._rows.Add(new Row { Label = ProgramUnits.Current.Charge(40.0 + i * 0.3) + " (carica " + (i + 1) + ")", Values = shots[i].ToList() });
        f.Recompute();
        return f;
    }

    /// <summary>GRT names every imported Measurement "Misurazione", so two strings routinely carry the
    /// same label; the comparison picks rows by label, so labels must be unique.</summary>
    private string UniqueLabel(string label)
    {
        if (!_rows.Any(r => r.Label == label)) return label;
        for (int i = 2; ; i++)
        {
            string cand = $"{label} #{i}";
            if (!_rows.Any(r => r.Label == cand)) return cand;
        }
    }

    private double Confidence => _confidence.SelectedIndex switch { 0 => 0.90, 2 => 0.99, _ => 0.95 };
    /// <summary>What the user typed, back in stored m/s -- the calc layer stays metric.</summary>
    private double TargetMarginMps => _u.VelocityToMps((double)_targetMargin.Value);

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
        AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("folder: {0} of {1} file(s) added from {2}"), _rows.Count - before, files.Count, Path.GetFileName(d.SelectedPath)));
    }

    private void AddPaths(IEnumerable<string> paths, bool showErrorDialog)
    {
        foreach (string path in paths)
        {
            try
            {
                var a = AthlonParser.Parse(path);
                var vs = a.Velocities.ToList();
                if (vs.Count == 0) { AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("{0}: no shots, skipped"), Path.GetFileName(path))); continue; }
                string label = a.ChargeGrains is { } g
                    ? $"{ProgramUnits.Current.Charge(g)} ({Path.GetFileName(path)})"
                    : Path.GetFileName(path);
                _rows.Add(new Row { Label = UniqueLabel(label), Values = vs });
                AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("{0}: {1} shots"), Path.GetFileName(path), vs.Count));
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
                        ? $"{ProgramUnits.Current.Charge(g)} ({meas.Title})"
                        : $"{ch.Name} ({meas.Title})";
                    _rows.Add(new Row { Label = UniqueLabel(label), Values = vs });
                    added++;
                }
            AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("load Measurement: {0} string(s) added"), added));
            Recompute();
        }
        catch (Exception ex) { Err(ex); }
    }

    private void Recompute()
    {
        double conf = Confidence;
        foreach (var r in _rows) r.Summary = ChronoStatsCalc.Analyze(r.Values, conf, _exclude.Checked);

        _grid.Rows.Clear();
        foreach (var r in _rows)
        {
            var s = r.Summary;
            int need = ChronoStatsCalc.RequiredSampleSize(s.SampleSd, TargetMarginMps, conf);
            string needTxt = need switch { 0 => "–", -1 => ">1000", _ => need.ToString(CultureInfo.InvariantCulture) };
            int flagged = s.Outliers.Count(o => o.Flagged);
            _grid.Rows.Add(r.Label, s.N,
                _u.VelocityValue(s.Mean).ToString("F1", CultureInfo.InvariantCulture),
                _u.VelocitySd(s.Sd),
                _u.VelocitySd(s.Es),
                FormattableString.Invariant($"±{_u.VelocityValue(s.CiMarginMps):F1}"),
                needTxt,
                flagged > 0 ? string.Format(s.OutliersExcluded ? Lang.T("{0} flagged, excluded") : Lang.T("{0} flagged"), flagged) : "–");
        }

        _chart.SetData(_rows.Select(r => new StringPoint(r.Label, r.Values.Select(v => _u.VelocityValue(v)).ToList(),
            r.Summary.Outliers.Where(o => o.Flagged).Select(o => o.Index).ToHashSet())).ToList(), _u.VelocityUnitName);

        string? selA = _cmpA.SelectedItem as string, selB = _cmpB.SelectedItem as string;
        _cmpA.Items.Clear(); _cmpB.Items.Clear();
        foreach (var r in _rows) { _cmpA.Items.Add(r.Label); _cmpB.Items.Add(r.Label); }
        if (selA != null && _cmpA.Items.Contains(selA)) _cmpA.SelectedItem = selA; else if (_cmpA.Items.Count > 0) _cmpA.SelectedIndex = 0;
        if (selB != null && _cmpB.Items.Contains(selB)) _cmpB.SelectedItem = selB; else if (_cmpB.Items.Count > 1) _cmpB.SelectedIndex = 1;

        _write.Enabled = _rows.Count > 0 && _grt is { Connected: true };
        _saveArchive.Enabled = _rows.Count > 0;
        _pdf.Enabled = _rows.Count > 0;
    }

    /// <summary>Strings picked from a session of the archive, labelled with their charge (or name) and the session's date.</summary>
    private void AddFromArchive()
    {
        if (_link == null) return;
        using var pick = new ArchivePickForm(_link, ArchivePick.Strings);
        if (pick.ShowDialog(this) != DialogResult.OK || pick.Session is not { } s) return;
        foreach (var st in pick.PickedStrings)
        {
            var vs = st.Velocities.Where(v => v > 50).ToList();
            if (vs.Count == 0) continue;
            _rows.Add(new Row { Label = UniqueLabel($"{st.Label} ({s.Date})"), Values = vs, ArchiveId = st.Id });
        }
        _archLoad ??= s.LoadId;
        _archSession ??= s.Id;
        AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("archive: {0} string(s) added from the session of {1}"), pick.PickedStrings.Count, s.Date));
        Recompute();
    }

    private string BuildReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine($"{Lang.T(NoteTitle)} {DateTime.Now:yyyy-MM-dd}");
        report.AppendLine(new string('-', 20));
        report.AppendLine();
        report.Append(ChronoStatsCalc.BuildTableReport(_rows.Select(r => (r.Label, r.Summary)).ToList(), TargetMarginMps, Confidence));
        if (_lastCompare is { } c)
        {
            report.AppendLine();
            report.Append(ChronoStatsCalc.BuildCompareReport(c.a, c.b, c.r));
        }
        return report.ToString();
    }

    private string ArchiveInputs() => System.Text.Json.JsonSerializer.Serialize(new
    {
        serie_archivio = _rows.Where(r => r.ArchiveId != null).Select(r => r.ArchiveId).ToList(),
        serie = _rows.Select(r => r.Label).ToList(),
        confidenza = Confidence,
        escludi_chauvenet = _exclude.Checked,
    });

    private void SaveToArchive()
    {
        if (_link == null || _rows.Count == 0) return;
        _link.Save(this, Lang.T("Chronograph Statistics"), _archLoad, _archSession, ArchiveInputs(), BuildReport());
    }

    private async Task PdfAsync()
    {
        if (_rows.Count == 0) return;
        string title = _rows.Count == 1 ? _rows[0].Label : string.Format(Lang.T("{0} strings"), _rows.Count);
        // Only strings that all came from the archive belong to its load and session; anything else added is not theirs.
        bool fromArchive = _rows.All(r => r.ArchiveId != null);
        await ReportPdfAction.MakeAsync(this, _pdf, _link, Lang.T("Chronograph Statistics"), title,
            fromArchive ? _archLoad : null, fromArchive ? _archSession : null, ArchiveInputs(), BuildReport(), ReportPdfAction.Chart(Lang.T("The shots of each string"), _chart.RenderPng(1300, 760, print: true)));
    }

    private void Compare()
    {
        if (_cmpA.SelectedItem is not string la || _cmpB.SelectedItem is not string lb) { MessageBox.Show(this, Lang.T("Pick two strings first.")); return; }
        var ra = _rows.FirstOrDefault(r => r.Label == la);
        var rb = _rows.FirstOrDefault(r => r.Label == lb);
        if (ra is null || rb is null) return;
        if (ReferenceEquals(ra, rb)) { MessageBox.Show(this, Lang.T("Pick two different strings.")); return; }

        // The same shots the table above was computed from, so the two never disagree.
        var result = ChronoStatsCalc.Compare(ra.Summary.Values, rb.Summary.Values);
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

            var doc = GrtLoadDoc.OpenForToolkitEdit(basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, BuildReport());
            string outPath = doc.SaveSibling("chronostats");
            AppendLog(Lang.T("wrote") + " " + outPath);
            await _grt.LoadFileAsync(outPath);
            _next?.MarkDone();
            _status.Text = Lang.T("Chrono Statistics note written and opened in GRT.");
        }
        catch (Exception ex) { Err(ex); }
    }

    private void Err(Exception ex) { AppendLog(Lang.T("ERROR:") + " " + ex.Message); MessageBox.Show(this, ex.Message, Lang.T("Chrono Statistics"), MessageBoxButtons.OK, MessageBoxIcon.Error); }

    private void AppendLog(string line) => _cmpResult.SafeAppend(line);
}
