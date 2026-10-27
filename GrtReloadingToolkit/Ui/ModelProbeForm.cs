using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Probe;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// UI for <see cref="ModelProbe"/>: pick one input of the load open in GRT, sweep it over a range while everything
/// else stays as the load has it, and see how GRT's simulation answers -- each output point by point, on a chart, and
/// summed up as an elasticity at the load's own value and a power-law exponent over the range. It studies GRT's model
/// from the outside, through the documented plugin API only; what it shows is the model, not a load to shoot.
/// Always in the units GRT's file stores (the input) and its IPC reports (the outputs): elasticities and exponents
/// are ratios and come out the same in any unit.
/// </summary>
internal sealed class ModelProbeForm : Form
{
    private const string NoteTitle = "Model Probe";

    private readonly GrtClient? _grt;

    private readonly ComboBox _param = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    private readonly NumericUpDown _from = Nud();
    private readonly NumericUpDown _to = Nud();
    private readonly NumericUpDown _count = new() { Minimum = 3, Maximum = 21, Value = 9, Width = 50 };
    private readonly Label _unit = new() { AutoSize = true, Margin = new Padding(4, 6, 8, 0) };
    private readonly CheckBox _readCurve = new() { AutoSize = true, Checked = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly Button _readBtn = new() { AutoSize = true, Margin = new Padding(0, 2, 10, 0) };
    private readonly Button _runBtn = new() { AutoSize = true, Margin = new Padding(10, 2, 0, 0) };
    private readonly Button _stopBtn = new() { AutoSize = true, Margin = new Padding(4, 2, 0, 0), Enabled = false };
    private readonly Button _writeBtn = new() { AutoSize = true, Margin = new Padding(10, 2, 0, 0), Enabled = false };
    private readonly Button _csvBtn = new() { AutoSize = true, Margin = new Padding(4, 2, 0, 0), Enabled = false };
    private readonly Label _warning = new() { AutoSize = true, ForeColor = Color.DarkOrange, Margin = new Padding(0, 2, 0, 2), Visible = false };
    private readonly DataGridView _pointsGrid = Grid();
    private readonly DataGridView _sensGrid = Grid();
    private readonly ComboBox _chartOutput = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly ProbeChart _chart = new() { Dock = DockStyle.Fill };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 70 };

    private string? _basePath;
    private bool _unsaved;
    private IReadOnlyList<GrtInput> _inputs = Array.Empty<GrtInput>();
    private GrtInput? _probed;
    private List<ProbePoint> _points = new();
    private CancellationTokenSource? _cts;

    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public ModelProbeForm(GrtClient? grt)
    {
        _grt = grt;
        Text = AppVersion.Title(Lang.T("GRT Model Probe"));
        Width = 1180; Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);

        var row1 = Row();
        _readBtn.Text = Lang.T("Read load from GRT");
        _readBtn.Click += async (_, _) => await ReadLoadAsync();
        row1.Controls.Add(_readBtn);
        row1.Controls.Add(Caption(Lang.T("Input")));
        row1.Controls.Add(_param);
        _param.SelectedIndexChanged += (_, _) => OnParamChanged();

        var row2 = Row();
        row2.Controls.Add(Caption(Lang.T("From")));
        row2.Controls.Add(_from);
        row2.Controls.Add(Caption(Lang.T("to")));
        row2.Controls.Add(_to);
        row2.Controls.Add(_unit);
        row2.Controls.Add(Caption(Lang.T("Points")));
        row2.Controls.Add(_count);
        _readCurve.Text = Lang.T("Read the pressure curve (slower)");
        row2.Controls.Add(_readCurve);
        _runBtn.Text = Lang.T("Run sweep");
        _runBtn.Click += async (_, _) => await RunAsync();
        row2.Controls.Add(_runBtn);
        _stopBtn.Text = Lang.T("Stop");
        _stopBtn.Click += (_, _) => _cts?.Cancel();
        row2.Controls.Add(_stopBtn);
        _writeBtn.Text = Lang.T("Write probe note to GRT load");
        _writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        row2.Controls.Add(_writeBtn);
        _csvBtn.Text = Lang.T("Export CSV…");
        _csvBtn.Click += (_, _) => ExportCsv();
        row2.Controls.Add(_csvBtn);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(6, 6, 6, 2) };
        top.Controls.Add(row1);
        top.Controls.Add(row2);
        top.Controls.Add(_warning);

        foreach (var (n, h) in new[] { ("output", Lang.T("Output")), ("base", Lang.T("At the load's value")), ("elast", Lang.T("Elasticity")),
                     ("exp", Lang.T("Exponent")), ("r2", "R²"), ("range", Lang.T("Range over the sweep")) })
            _sensGrid.Columns.Add(n, h);
        foreach (ProbeOutput o in Enum.GetValues<ProbeOutput>()) _chartOutput.Items.Add(new OutputItem(o));
        _chartOutput.SelectedIndex = 0;
        _chartOutput.SelectedIndexChanged += (_, _) => RefreshChart();

        var chartHost = new Panel { Dock = DockStyle.Fill };
        var chartBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4, 4, 0, 2) };
        chartBar.Controls.Add(Caption(Lang.T("Chart")));
        chartBar.Controls.Add(_chartOutput);
        chartHost.Controls.Add(_chart);
        chartHost.Controls.Add(chartBar);

        var explain = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 48, ForeColor = SystemColors.GrayText, Padding = new Padding(4, 4, 4, 0),
            Text = Lang.T("Elasticity: a 1 % change of the input moves the output by that many percent, at the load's own value. Exponent: the b of output ≈ a · input^b over the whole sweep; R² near 1 means one power law describes it, and an exponent far from the elasticity means the response bends inside the range."),
        };
        var sensHost = new Panel { Dock = DockStyle.Fill };
        sensHost.Controls.Add(_sensGrid);
        sensHost.Controls.Add(explain);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(330);
        right.Panel1.Controls.Add(chartHost);
        right.Panel2.Controls.Add(sensHost);
        var split = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(560);
        split.Panel1.Controls.Add(_pointsGrid);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(_log);
        Controls.Add(top);
        NudFix.ApplyTo(this);
        RefreshChart();

        Shown += async (_, _) => await ReadLoadAsync();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    private static NumericUpDown Nud() => new() { DecimalPlaces = 6, Minimum = -10_000_000, Maximum = 10_000_000, Width = 110 };

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
    };

    private static FlowLayoutPanel Row() => new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 4, 0) };

    /// <summary>An output in the chart's combo, shown by its translated name.</summary>
    private sealed record OutputItem(ProbeOutput Output)
    {
        public override string ToString() => OutputName(Output);
    }

    /// <summary>An input in the parameter combo: section, GRT's field name, the load's value and GRT's own description.</summary>
    private sealed record InputItem(GrtInput Input)
    {
        public override string ToString() =>
            $"{Input.Section} · {Input.Name} = {Num(Input.Value)}{(Input.Unit.Length > 0 ? " " + Input.Unit : "")}" +
            (Input.Description.Length > 0 ? $"  —  {Input.Description}" : "");
    }

    internal static string OutputName(ProbeOutput o) => o switch
    {
        ProbeOutput.MuzzleVelocity => Lang.T("Muzzle velocity"),
        ProbeOutput.MaxPressure => Lang.T("Peak pressure"),
        ProbeOutput.BarrelTime => Lang.T("Barrel time"),
        ProbeOutput.BulletLeadTime10Pmax => Lang.T("Barrel time from 10 % of Pmax"),
        ProbeOutput.TimeOfPmax => Lang.T("Time to peak pressure"),
        ProbeOutput.MuzzlePressurePct => Lang.T("Muzzle pressure, % of peak"),
        ProbeOutput.BurntAtMuzzlePct => Lang.T("Powder burnt at the muzzle"),
        ProbeOutput.BurnoutTime => Lang.T("All-burnt time"),
        ProbeOutput.Efficiency => Lang.T("Efficiency (bullet energy / powder energy)"),
        _ => o.ToString(),
    };

    private string OutputUnit(ProbeOutput o) => o switch
    {
        ProbeOutput.MuzzleVelocity => "m/s",
        ProbeOutput.MaxPressure => _points.FirstOrDefault(p => p.PressureUnit.Length > 0)?.PressureUnit ?? "",
        ProbeOutput.MuzzlePressurePct or ProbeOutput.BurntAtMuzzlePct or ProbeOutput.Efficiency => "%",
        _ => "ms",
    };

    /// <summary>Four significant digits at most, never scientific notation, invariant culture.</summary>
    internal static string Num(double v)
    {
        double a = Math.Abs(v);
        string f = a >= 1000 ? "0" : a >= 100 ? "0.0" : a >= 10 ? "0.00" : a >= 1 ? "0.000" : "0.00000";
        return v.ToString(f, Ci);
    }

    private void Log(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
        _log.AppendText(line + Environment.NewLine);
    }

    private async Task ReadLoadAsync()
    {
        if (_grt is not { Connected: true }) { Log(Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                Log(Lang.T("No saved load is open in GRT."));
                return;
            }
            if (Path.GetFileName(top.file).StartsWith("probe_", StringComparison.OrdinalIgnoreCase))
            {
                Log(Lang.T("The active tab is one of the probe's own points — switch to your real load in GRT."));
                return;
            }

            _unsaved = false;
            try { _unsaved = (await _grt.GetTabAsync(top.handle)).Changed; }
            catch (Exception ex) { Log("Get_Tab: " + ex.Message); }
            _warning.Text = Lang.T("⚠ This tab has changes that are not saved. The probe reads the saved file: save the load in GRT first, then read it again.");
            _warning.Visible = _unsaved;

            var doc = GrtLoadDoc.Load(top.file);
            _basePath = top.file;
            string keep = _param.SelectedItem is InputItem k ? k.Input.Section + "/" + k.Input.Name : "propellant/Ba";
            _inputs = ModelProbe.Offered(doc);
            _param.Items.Clear();
            foreach (var i in _inputs) _param.Items.Add(new InputItem(i));
            int sel = _inputs.ToList().FindIndex(i => i.Section + "/" + i.Name == keep);
            if (_param.Items.Count > 0) _param.SelectedIndex = Math.Max(0, sel);
            Log(string.Format(Lang.T("Read '{0}': {1} inputs can be probed."), Path.GetFileName(top.file), _inputs.Count));
        }
        catch (Exception ex) { Log(ex.Message); }
    }

    private void OnParamChanged()
    {
        if (_param.SelectedItem is not InputItem item) return;
        var (from, to) = ModelProbe.DefaultRange(item.Input.Value);
        NudFix.Set(_from, from);
        NudFix.Set(_to, to);
        double step = Math.Abs(item.Input.Value) > 0 ? Math.Abs(item.Input.Value) / 100 : 0.01;
        _from.Increment = _to.Increment = (decimal)Math.Max(step, 0.000001);
        _unit.Text = item.Input.Unit;
    }

    private async Task RunAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        if (_basePath is null || _param.SelectedItem is not InputItem item) { MessageBox.Show(this, Lang.T("Read a load from GRT and pick an input first.")); return; }

        IReadOnlyList<double> values;
        try { values = ModelProbe.Values((double)_from.Value, (double)_to.Value, (int)_count.Value, item.Input.Value); }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, Lang.T("Model probe")); return; }

        if (_unsaved && MessageBox.Show(this, Lang.T("The tab has changes that are not saved: the probe will use the saved file, not what GRT shows. Continue anyway?"),
                Lang.T("Model probe"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        if (MessageBox.Show(this,
                string.Format(Lang.T("This opens {0} tabs in GRT, one per point, reads each result and then reopens your load. Your own file is not changed. GRT's plugin interface cannot close tabs: close them in GRT when done.\n\nContinue?"), values.Count),
                Lang.T("Model probe"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        Log(string.Format(Lang.T("Sweep of {0}.{1} over {2} points…"), item.Input.Section, item.Input.Name, values.Count));
        // Probed input and points change together, before the sweep: a stop or an error leaves what was read for THIS
        // input, never the previous sweep's points under the new input's name.
        _probed = item.Input;
        _points = new List<ProbePoint>();
        try
        {
            await ProbeRunner.RunAsync(_grt, _basePath, item.Input, values, _readCurve.Checked, _points, Log, Lang.T, _cts.Token);
            Log(string.Format(Lang.T("Done: {0} of {1} points read."), _points.Count, values.Count));
        }
        catch (Exception ex) { Log(ex.Message); }
        finally
        {
            SetBusy(false);
            _cts.Dispose(); _cts = null;
        }
        RefreshResults();
    }

    private void SetBusy(bool busy)
    {
        _runBtn.Enabled = _readBtn.Enabled = !busy;
        _stopBtn.Enabled = busy;
        _param.Enabled = !busy;
        _writeBtn.Enabled = _csvBtn.Enabled = !busy && _points.Count > 0;
    }

    private void RefreshResults()
    {
        _pointsGrid.Columns.Clear();
        _pointsGrid.Rows.Clear();
        _sensGrid.Rows.Clear();
        _writeBtn.Enabled = _csvBtn.Enabled = _points.Count > 0;
        if (_probed is null || _points.Count == 0) { RefreshChart(); return; }

        var outs = Enum.GetValues<ProbeOutput>().Where(o => _points.Any(p => p.Get(o) is not null)).ToList();
        _pointsGrid.Columns.Add("x", $"{_probed.Name}{(_probed.Unit.Length > 0 ? " [" + _probed.Unit + "]" : "")}");
        foreach (var o in outs) _pointsGrid.Columns.Add(o.ToString(), $"{OutputName(o)} [{OutputUnit(o)}]");
        foreach (var p in _points.OrderBy(p => p.X))
        {
            var cells = new List<object> { Num(p.X) };
            cells.AddRange(outs.Select(o => (object)(p.Get(o) is { } v ? Num(v) : "—")));
            int r = _pointsGrid.Rows.Add(cells.ToArray());
            if (p.IsBaseline) _pointsGrid.Rows[r].DefaultCellStyle.Font = new Font(_pointsGrid.Font, FontStyle.Bold);
        }

        foreach (var s in ModelProbe.Analyze(_points))
            _sensGrid.Rows.Add(OutputName(s.Output),
                s.Baseline is { } b ? $"{Num(b)} {OutputUnit(s.Output)}" : "—",
                s.Elasticity is { } e ? e.ToString("+0.000;-0.000", Ci) : "—",
                s.Exponent is { } x ? x.ToString("+0.000;-0.000", Ci) : "—",
                s.R2 is { } r2 ? r2.ToString("0.0000", Ci) : "—",
                s.Min is { } lo && s.Max is { } hi ? $"{Num(lo)} … {Num(hi)}" : "—");
        RefreshChart();
    }

    private void RefreshChart()
    {
        var o = (_chartOutput.SelectedItem as OutputItem)?.Output ?? ProbeOutput.MuzzleVelocity;
        var pts = _points.Where(p => p.Get(o) is not null).Select(p => (p.X, p.Get(o)!.Value));
        string xLabel = _probed is null ? "" : $"{_probed.Name}{(_probed.Unit.Length > 0 ? " [" + _probed.Unit + "]" : "")}";
        _chart.SetData(pts, _points.FirstOrDefault(p => p.IsBaseline)?.X, xLabel, $"{OutputName(o)} [{OutputUnit(o)}]",
            _points.Count == 0 ? Lang.T("Run a sweep to see how GRT's model answers.") : Lang.T("GRT returned no value of this output."));
    }

    /// <summary>The note: what was probed, the sensitivities, every point, and that it is GRT's model, not load data.</summary>
    private string BuildNote()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine(new string('-', 20));
        sb.AppendLine();
        var p0 = _probed!;
        sb.AppendLine(string.Format(Lang.T("Input: {0}.{1} ({2}), load value {3} {4}"), p0.Section, p0.Name, p0.Description, Num(p0.Value), p0.Unit));
        sb.AppendLine(string.Format(Lang.T("Sweep: {0} … {1} {2}, {3} points"), Num(_points.Min(p => p.X)), Num(_points.Max(p => p.X)), p0.Unit, _points.Count));
        sb.AppendLine();
        sb.AppendLine(string.Format(Ci, "{0,-44} {1,14} {2,10} {3,10} {4,8}", Lang.T("Output"), Lang.T("At load value"), Lang.T("Elasticity"), Lang.T("Exponent"), "R²"));
        foreach (var s in ModelProbe.Analyze(_points))
            sb.AppendLine(string.Format(Ci, "{0,-44} {1,14} {2,10} {3,10} {4,8}",
                $"{OutputName(s.Output)} [{OutputUnit(s.Output)}]",
                s.Baseline is { } b ? Num(b) : "-",
                s.Elasticity is { } e ? e.ToString("+0.000;-0.000", Ci) : "-",
                s.Exponent is { } x ? x.ToString("+0.000;-0.000", Ci) : "-",
                s.R2 is { } r2 ? r2.ToString("0.000", Ci) : "-"));
        sb.AppendLine();
        sb.AppendLine(Lang.T("Elasticity: % change of the output for 1 % of the input, at the load's value. Exponent: output ≈ a · input^b over the sweep."));
        sb.AppendLine();
        var outs = Enum.GetValues<ProbeOutput>().Where(o => _points.Any(p => p.Get(o) is not null)).ToList();
        sb.AppendLine(string.Join("  ", new[] { p0.Name }.Concat(outs.Select(o => o.ToString()))));
        foreach (var p in _points.OrderBy(p => p.X))
            sb.AppendLine(string.Join("  ", new[] { Num(p.X) + (p.IsBaseline ? "*" : "") }.Concat(outs.Select(o => p.Get(o) is { } v ? Num(v) : "-"))));
        sb.AppendLine(Lang.T("* = the load's own value"));
        sb.AppendLine();
        sb.AppendLine(Lang.T("What GRT's model predicts, not measurements: it says nothing about whether any of these combinations is safe to load."));
        return sb.ToString();
    }

    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || _basePath is null || _probed is null || _points.Count == 0) return;
        try
        {
            var doc = GrtLoadDoc.OpenForToolkitEdit(_basePath);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, BuildNote());
            if (_chart.RenderPng() is { } png) doc.AddGalleryPicture($"{NoteTitle} chart", "modelprobe_chart", png);
            string outPath = doc.SaveSibling("modelprobe");
            await _grt.LoadFileAsync(outPath);
            Log(Lang.T("Probe note written and opened in GRT."));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write probe note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv()
    {
        if (_probed is null || _points.Count == 0) return;
        using var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"model_probe_{_probed.Name}_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(d.FileName, ModelProbe.Csv(_probed, _points), new System.Text.UTF8Encoding(false));
            Log(string.Format(Lang.T("Saved {0}"), d.FileName));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("Export CSV…"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
