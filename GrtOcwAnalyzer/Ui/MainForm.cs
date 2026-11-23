using System.Globalization;
using GrtOcwAnalyzer.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtOcwAnalyzer.Ui;

internal sealed class MainForm : Form
{
    private const string NoteTitlePrefix = "OCW ";

    private readonly GrtClient? _grt;
    private readonly TextBox _folder = new() { ReadOnly = true };
    private readonly DataGridView _grid = new();
    private readonly LadderChart _chart = new();
    private readonly TextBox _summary = new();
    private readonly Label _status = new();
    private readonly NumericUpDown _wPoi = new() { DecimalPlaces = 1, Increment = 0.1M, Minimum = 0, Maximum = 5, Value = 1.4M, Width = 55 };
    private readonly NumericUpDown _wVel = new() { DecimalPlaces = 1, Increment = 0.1M, Minimum = 0, Maximum = 5, Value = 1.0M, Width = 55 };
    private readonly NumericUpDown _win = new() { Minimum = 3, Maximum = 5, Value = 3, Width = 45 };
    private readonly Button _writeBtn = new() { Text = "Write OCW note to GRT load", AutoSize = true, Enabled = false };

    private LadderResult? _result;

    public MainForm(GrtClient? grt)
    {
        _grt = grt;
        Text = "GRT Ladder / OCW Analyzer  (v0.1)";
        Width = 1000; Height = 680;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 520);
        if (_grt != null) _grt.Log += s => AppendLog(s);
        Build();
        UpdateStatus();

        string? preset = Environment.GetEnvironmentVariable("OCW_FOLDER");
        if (!string.IsNullOrWhiteSpace(preset) && Directory.Exists(preset))
        {
            Shown += (_, _) => { _folderPath = preset; _folder.Text = preset; Reanalyze(); };
        }
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(6, 6, 6, 0), WrapContents = true };
        var pick = new Button { Text = "Pick ladder folder…", AutoSize = true };
        pick.Click += (_, _) => PickFolder();
        _folder.Width = 420;
        top.Controls.Add(pick);
        top.Controls.Add(_folder);
        top.Controls.Add(new Label { Text = "  weight POI", AutoSize = true, Padding = new Padding(10, 6, 0, 0) });
        top.Controls.Add(_wPoi);
        top.Controls.Add(new Label { Text = "MV", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        top.Controls.Add(_wVel);
        top.Controls.Add(new Label { Text = "window", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        top.Controls.Add(_win);
        var re = new Button { Text = "Re-analyze", AutoSize = true, Margin = new Padding(10, 3, 0, 0) };
        re.Click += (_, _) => Reanalyze();
        top.Controls.Add(re);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("chg", "Charge gr"), ("vn", "n"), ("mv", "MV m/s"), ("sd", "SD"), ("es", "ES"),
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

    private string? _folderPath;

    private void PickFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Folder with Athlon *.xlsx and Ballistic-X *.csv for one ladder" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _folderPath = d.SelectedPath;
        _folder.Text = _folderPath;
        Reanalyze();
    }

    private void Reanalyze()
    {
        if (_folderPath is null) return;
        try
        {
            var loaded = LadderLoader.FromFolder(_folderPath);
            foreach (var l in loaded.Log) AppendLog(l);

            var w = new AnalysisWeights
            {
                Poi = (double)_wPoi.Value,
                Velocity = (double)_wVel.Value,
                Window = (int)_win.Value,
            };
            _result = LadderAnalyzer.Analyze(loaded.Rows, w);

            _grid.Rows.Clear();
            foreach (var x in _result.Rows)
                _grid.Rows.Add(
                    x.ChargeGr.ToString("0.0", CultureInfo.InvariantCulture),
                    x.HasVel ? x.VelN : 0,
                    x.HasVel ? x.MeanMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasVel ? x.SdMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasVel ? x.EsMps.ToString("0.0", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.DistanceM.ToString("0", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.PoiYMoa.ToString("0.00", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.MeanRadiusMoa.ToString("0.00", CultureInfo.InvariantCulture) : "",
                    x.HasTarget ? x.VertSpreadMoa.ToString("0.00", CultureInfo.InvariantCulture) : "");

            HighlightNode();
            _chart.Show(_result);
            _summary.Text = LadderAnalyzer.BuildReport(_result, "OCW / Ladder analysis").Replace("\n", "\r\n");
            _writeBtn.Enabled = _result.BestNode != null && _grt is { Connected: true };
            _status.Text = _result.BestNode is { } n
                ? $"Recommended node {n.LowGr:0.0}–{n.HighGr:0.0} gr"
                : "analysis done";
        }
        catch (Exception ex)
        {
            AppendLog("ANALYZE FAILED: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Analyze failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HighlightNode()
    {
        if (_result?.BestNode is not { } n) return;
        foreach (DataGridViewRow row in _grid.Rows)
            if (double.TryParse((string)row.Cells["chg"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double c)
                && c >= n.LowGr - 0.001 && c <= n.HighGr + 0.001)
                row.DefaultCellStyle.BackColor = Color.FromArgb(215, 245, 220);
    }

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

            var doc = GrtLoadDoc.Load(top.file);
            int removed = doc.RemoveByTitlePrefix(NoteTitlePrefix);
            string headline = $"OCW / Ladder analysis {DateTime.Now:yyyy-MM-dd}";
            doc.AddNote($"{NoteTitlePrefix}Analysis {DateTime.Now:yyyy-MM-dd}",
                        LadderAnalyzer.BuildReport(_result, headline));
            string outPath = doc.SaveSibling("ocw");
            if (removed > 0) AppendLog($"replaced {removed} earlier OCW note(s)");
            AppendLog("wrote " + outPath);
            await _grt.LoadFileAsync(outPath);
            _status.Text = "OCW note written — opened in GRT as a new tab.";
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

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(line)); return; }
        _summary.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }
}
