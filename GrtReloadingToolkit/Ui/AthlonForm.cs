using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Ui;

internal sealed class AthlonForm : Form
{
    private readonly GrtClient? _grt;
    private readonly List<AthlonString> _strings = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new();
    private readonly Label _status = new();
    private readonly CheckBox _perShotTemp = new();
    private readonly CheckBox _replacePrev = new() { Checked = true };
    private readonly Button _importBtn = new();

    private readonly Action<string> _logHandler;

    public AthlonForm(GrtClient? grt)
    {
        _grt = grt;
        _logHandler = AppendLog;
        Text = "Chronograph import (Athlon / Garmin)  (v0.1)";
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 420);

        BuildLayout();
        if (_grt != null) _grt.Log += _logHandler;
        UpdateStatus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_grt != null) _grt.Log -= _logHandler;
        base.OnFormClosed(e);
    }

    public void BringToFrontForced()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0) };
        var addBtn = new Button { Text = "Add chrono files…", AutoSize = true };
        var addFolderBtn = new Button { Text = "Add folder…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        var removeBtn = new Button { Text = "Remove selected", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        addBtn.Click += (_, _) => AddFiles();
        addFolderBtn.Click += (_, _) => AddFolder();
        removeBtn.Click += (_, _) => RemoveSelected();
        _perShotTemp.Text = "TEMP= on every shot";
        _perShotTemp.AutoSize = true;
        _perShotTemp.Margin = new Padding(16, 6, 0, 0);
        _replacePrev.Text = "replace previous chrono import in this load";
        _replacePrev.AutoSize = true;
        _replacePrev.Margin = new Padding(12, 6, 0, 0);
        top.Controls.AddRange(new Control[] { addBtn, addFolderBtn, removeBtn, _perShotTemp, _replacePrev });

        if (Environment.GetEnvironmentVariable("ATHLON_DEV") == "1" && _grt != null)
        {
            var dev = new Button { Text = "DEV: Load_File…", AutoSize = true, Margin = new Padding(16, 3, 0, 0) };
            dev.Click += async (_, _) =>
            {
                using var d = new OpenFileDialog { Filter = "grtload|*.grtload|all|*.*" };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { await _grt.LoadFileAsync(d.FileName); AppendLog("DEV Load_File OK: " + d.FileName); }
                catch (Exception ex) { AppendLog("DEV Load_File FAIL: " + ex.Message); }
            };
            var dev2 = new Button { Text = "DEV: TabOnTop", AutoSize = true, Margin = new Padding(6, 3, 0, 0) };
            dev2.Click += async (_, _) =>
            {
                try { var t = await _grt.GetTabOnTopAsync(); AppendLog($"DEV TabOnTop: h={t.handle} '{t.caption}' [{t.file}]"); }
                catch (Exception ex) { AppendLog("DEV TabOnTop FAIL: " + ex.Message); }
            };
            top.Controls.AddRange(new Control[] { dev, dev2 });
        }

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "inc", HeaderText = "Use", FillWeight = 6 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File", ReadOnly = true, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "charge", HeaderText = "Charge (gr)", ReadOnly = true, FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "Shots", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "avg", HeaderText = "AVG m/s", ReadOnly = true, FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sd", HeaderText = "SD", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "es", HeaderText = "ES", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "temp", HeaderText = "Temp °C", FillWeight = 10 });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = SystemColors.Window;
        _log.Font = new Font(FontFamily.GenericMonospace, 8f);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _importBtn.Text = "Import into GRT";
        _importBtn.AutoSize = true;
        _importBtn.Enabled = false;
        _importBtn.Click += async (_, _) => await DoImportAsync();
        actions.Controls.Add(_importBtn);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.Padding = new Padding(8, 2, 0, 0);
        _status.ForeColor = SystemColors.GrayText;

        bottom.Controls.Add(_log);
        bottom.Controls.Add(actions);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private void AddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Chronograph export (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*",
            Title = "Select one chrono file (Athlon or Garmin) per charge",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(dlg.FileNames, showErrorDialog: true);
    }

    private void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Folder with one chrono file (Athlon / Garmin, .xlsx / .csv) per charge" };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        var exts = new[] { ".xlsx", ".csv", ".tsv" };
        var files = Directory.EnumerateFiles(d.SelectedPath)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) { MessageBox.Show(this, "No .xlsx / .csv files in that folder.", "Add folder"); return; }

        int before = _strings.Count;
        AddPaths(files, showErrorDialog: false);      // a folder may hold target CSVs etc. — just skip those
        AppendLog($"folder: {_strings.Count - before} of {files.Count} file(s) imported from {Path.GetFileName(d.SelectedPath)}");
    }

    private void AddPaths(IEnumerable<string> paths, bool showErrorDialog)
    {
        foreach (string path in paths)
        {
            if (_strings.Any(s => string.Equals(s.SourceFile, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                AthlonString parsed = AthlonParser.Parse(path);
                _strings.Add(parsed);
                AddGridRow(parsed);
                foreach (string w in parsed.Warnings) AppendLog($"{parsed.FileName}: {w}");
            }
            catch (Exception ex)
            {
                AppendLog(showErrorDialog
                    ? "ERROR " + Path.GetFileName(path) + ": " + ex.Message
                    : "skipped " + Path.GetFileName(path) + " (not a chrono export)");
                if (showErrorDialog)
                    MessageBox.Show(this, ex.Message, "Could not read file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        UpdateStatus();
    }

    private void AddGridRow(AthlonString s)
    {
        var stats = StringStats.From(s.Velocities.ToList());
        int i = _grid.Rows.Add();
        DataGridViewRow row = _grid.Rows[i];
        row.Cells["inc"].Value = true;
        row.Cells["file"].Value = s.FileName;
        row.Cells["charge"].Value = s.ChargeGrains is { } g ? Str.Num(g, 2) : "?";
        row.Cells["n"].Value = stats.N;
        row.Cells["avg"].Value = Str.Num(stats.Mean, 1);
        row.Cells["sd"].Value = Str.Num(stats.Sd, 1);
        row.Cells["es"].Value = Str.Num(stats.Es, 1);
        row.Cells["temp"].Value = s.SessionTempC is { } t ? Str.Num(t, 1) : "";
        row.Tag = s;
    }

    private void RemoveSelected()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r => r.Index))
        {
            if (row.Tag is AthlonString s) _strings.Remove(s);
            _grid.Rows.RemoveAt(row.Index);
        }
        UpdateStatus();
    }

    private async Task DoImportAsync()
    {
        try
        {
            _importBtn.Enabled = false;

            var rows = new List<ImportBuilder.Row>();
            foreach (DataGridViewRow gr in _grid.Rows)
            {
                if (gr.Tag is not AthlonString s) continue;
                bool inc = gr.Cells["inc"].Value is true;
                double? temp = GrtPluginKit.Util.Str.ParseNumber(Convert.ToString(gr.Cells["temp"].Value, CultureInfo.InvariantCulture));
                rows.Add(new ImportBuilder.Row(s, temp, inc));
            }
            if (rows.All(r => !r.Include)) { MessageBox.Show(this, "Nothing selected.", "Import"); return; }

            string? basePath = null;
            if (_grt is { Connected: true })
            {
                try
                {
                    var top = await _grt.GetTabOnTopAsync();
                    basePath = string.IsNullOrWhiteSpace(top.file) ? null : top.file;
                    AppendLog($"active load in GRT: {top.caption}  [{basePath ?? "unsaved tab"}]");
                }
                catch (Exception ex) { AppendLog("Get_TabOnTop failed: " + ex.Message); }
            }

            // Anti-chaining: never merge into a file we generated, or a placeholder tab.
            if (basePath != null && GrtLoadDoc.LooksLikeGeneratedSibling(basePath))
            {
                MessageBox.Show(this,
                    "The tab active in GRT is a generated file, not your load.\n\n" +
                    "Switch to your real load tab in GRT, then import again.",
                    "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<GrtCharge> charges = ImportBuilder.Build(rows, null, _perShotTemp.Checked);
            string kind = charges.Count > 1 ? "Ladder" : "Measurement";
            string title = $"{ImportBuilder.MeasurementTitlePrefix}{kind} {DateTime.Now:yyyy-MM-dd}";

            GrtLoadDoc doc;
            string? explicitPath = null;
            if (basePath != null)
            {
                doc = GrtLoadDoc.OpenForToolkitEdit(basePath);
                string cal = doc.CaliberName;
                if (cal is "" or "Generic") AppendLog($"note: base load has no real caliber name ('{cal}') — importing anyway");
            }
            else
            {
                using var save = new SaveFileDialog
                {
                    Title = "No load open in GRT — choose where to save the imported strings",
                    Filter = "GRT load (*.grtload)|*.grtload",
                    FileName = $"Athlon_{DateTime.Now:yyyyMMdd_HHmm}.grtload",
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                explicitPath = save.FileName;
                doc = GrtLoadDoc.CreateMinimal(title, explicitPath);
                AppendLog("no load open — writing a minimal file with a Generic caliber/gun/projectile");
            }

            if (_replacePrev.Checked)
            {
                int rm = doc.RemoveByTitlePrefix(ImportBuilder.MeasurementTitlePrefix);
                if (rm > 0) AppendLog($"replaced {rm} earlier \"Athlon …\" Measurement(s)");
            }
            doc.AddMeasurement(title, charges);

            string outPath;
            if (explicitPath != null) { doc.Save(explicitPath); outPath = explicitPath; }
            else outPath = doc.SaveSibling("athlon");

            AppendLog("wrote " + outPath);
            if (_grt is { Connected: true })
            {
                await _grt.LoadFileAsync(outPath);
                AppendLog("asked GRT to open it");
                _status.Text = $"Imported {charges.Count} charge(s) into GRT.";
            }
            else
            {
                _status.Text = "Wrote " + Path.GetFileName(outPath) + " (not connected to GRT).";
                MessageBox.Show(this, "Saved:\n" + outPath, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog("IMPORT FAILED: " + ex);
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _importBtn.Enabled = _strings.Count > 0;
        }
    }

    private void UpdateStatus()
    {
        _importBtn.Enabled = _strings.Count > 0;
        string conn = _grt == null ? "stand-alone (no GRT)"
            : _grt.Connected ? $"connected to GRT :{_grt.Port}"
            : "GRT connection lost";
        _status.Text = $"{_strings.Count} file(s) loaded — {conn}";
    }

    private void AppendLog(string line) => _log.SafeAppend(line);
}
