using GrtReloadingToolkit.Measuring;
using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtReloadingToolkit.Log;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Ui;

internal sealed class AthlonForm : Form
{
    private readonly NextStepBar? _next = NextStepBar.For("Athlon");
    private readonly GrtClient? _grt;
    private readonly List<AthlonString> _strings = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _log = UiLog.NewLogBox();
    private readonly Label _status = new();
    private readonly CheckBox _perShotTemp = new();
    private readonly CheckBox _replacePrev = new() { Checked = true };
    private readonly Button _importBtn = new();

    private readonly Action<string> _logHandler;

    private readonly Log.Db? _db;

    // the archive first, GRT if asked
    private readonly Archive.ArchiveDb? _archive;
    private readonly ComboBox _loadPick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly CheckBox _toGrt = new() { Checked = true, AutoSize = true };
    private bool _dateTouched;

    /// <summary>One entry of the "load in the archive" list.</summary>
    private sealed record LoadChoice(long? Id, string Label, bool FromGrt = false, bool New = false, bool FromFile = false)
    {
        public override string ToString() => Label;
    }

    public AthlonForm(GrtClient? grt, Log.Db? db = null, Archive.ArchiveDb? archive = null)
    {
        _grt = grt;
        _db = db;
        _archive = archive;
        _logHandler = AppendLog;
        Text = AppVersion.Title(Lang.T("Chronograph import (Athlon / Garmin)"));
        Width = 980;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 440);

        BuildLayout();
        UiState.Bind(this, "athlon", ("perShotTemp", _perShotTemp), ("replacePrev", _replacePrev), ("toGrt", _toGrt));
        FillLoadChoices();
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
        var addBtn = new Button { Text = Lang.T("Add chrono files…"), AutoSize = true };
        var addFolderBtn = new Button { Text = Lang.T("Add folder…"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        var removeBtn = new Button { Text = Lang.T("Remove selected"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        addBtn.Click += (_, _) => AddFiles();
        addFolderBtn.Click += (_, _) => AddFolder();
        removeBtn.Click += (_, _) => RemoveSelected();
        _perShotTemp.Text = Lang.T("TEMP= on every shot");
        _perShotTemp.AutoSize = true;
        _perShotTemp.Margin = new Padding(16, 6, 0, 0);
        _replacePrev.Text = Lang.T("replace previous chrono import in this load");
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

        // where the strings go: the archive (load + date), and GRT too if ticked
        var dest = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 0), WrapContents = false };
        var loadLbl = new Label { Text = Lang.T("Load in the archive:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
        var dateLbl = new Label { Text = Lang.T("Session date:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) };
        _toGrt.Text = Lang.T("also send to GRT");
        _toGrt.Margin = new Padding(14, 5, 0, 0);
        _date.ValueChanged += (_, _) => _dateTouched = true;
        _loadPick.SelectedIndexChanged += (_, _) => PickedLoad();
        _loadPick.DropDownWidth = 560;
        // the two GRT-only options mean nothing when the strings stay in the archive
        void GrtOptions() => _perShotTemp.Enabled = _replacePrev.Enabled = _toGrt.Checked;
        _toGrt.CheckedChanged += (_, _) => GrtOptions();
        Shown += (_, _) => GrtOptions();
        if (_archive == null)
        {
            loadLbl.Text = Lang.T("archive not available: the strings go to GRT only");
            _loadPick.Visible = _date.Visible = dateLbl.Visible = false;
            _toGrt.Checked = true;
            _toGrt.Enabled = false;
        }
        dest.Controls.AddRange(new Control[] { loadLbl, _loadPick, dateLbl, _date, _toGrt });

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "inc", HeaderText = Lang.T("Use"), FillWeight = 6 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = Lang.T("File"), ReadOnly = true, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "charge", HeaderText = ChargeHeader(), FillWeight = 12,
            ToolTipText = Lang.T("Editable — type the charge here when the session note and file name don't carry it.") });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = Lang.T("Shots"), ReadOnly = true, FillWeight = 8 });
        // Velocities and temperatures are stored in m/s and °C whatever the chronograph file said,
        // so the headers name the unit the cells are actually shown in — GRT's.
        var u = ProgramUnits.Current;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "avg", HeaderText = "AVG " + u.VelocityUnitName, ReadOnly = true, FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sd", HeaderText = "SD", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "es", HeaderText = "ES", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "temp", HeaderText = Lang.T("Temp") + " " + u.TemperatureUnitName, FillWeight = 10 });

        AppUnits.AttachFieldMenu(_grid, "charge", "athlon.charge", Measuring.UnitKind.Mass, RefreshChargeColumn);

        // The charge is guessed from the session note / file name and shows "?" when neither
        // carries it. Writing the corrected value back into the parsed string is what the
        // import reads — the grid cell alone would be cosmetic.
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "charge") return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            if (row.Tag is not AthlonString s) return;
            // Typed in GRT's unit, kept in grains: the importer and everything downstream of it
            // price and plot a charge in grains whatever the box is headed with.
            var cu = ChargeUnits;
            double? g = Str.ParseNumber(Convert.ToString(row.Cells["charge"].Value, CultureInfo.InvariantCulture));
            s.ChargeGrains = g is > 0 ? cu.ChargeToGrains(g.Value) : null;
            row.Cells["charge"].Value = s.ChargeGrains is { } v ? ChargeCell(v) : "?";
            RefreshChart();
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        _log.Dock = DockStyle.Fill;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _importBtn.Text = _archive != null ? Lang.T("Import") : Lang.T("Import into GRT");
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

        var middle = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical }.WithDistance(640);
        middle.Panel1.Controls.Add(_grid);
        middle.Panel2.Controls.Add(_chart);
        Controls.Add(middle);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(dest);
        Controls.Add(top);
        if (_next != null) { Controls.Add(_next); _ = _next.TrackLoadAsync(_grt); }
    }

    private void AddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Chronograph export (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*",
            Title = Lang.T("Select one chrono file (Athlon or Garmin) per charge"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(dlg.FileNames, showErrorDialog: true);
    }

    private void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = Lang.T("Folder with one chrono file (Athlon / Garmin, .xlsx / .csv) per charge") };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        var exts = new[] { ".xlsx", ".csv", ".tsv" };
        var files = Directory.EnumerateFiles(d.SelectedPath)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0) { MessageBox.Show(this, Lang.T("No .xlsx / .csv files in that folder."), Lang.T("Add folder")); return; }

        int before = _strings.Count;
        AddPaths(files, showErrorDialog: false);      // a folder may hold target CSVs etc. — just skip those
        AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("folder: {0} of {1} file(s) imported from {2}"), _strings.Count - before, files.Count, Path.GetFileName(d.SelectedPath)));
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
                SuggestDate();
            }
            catch (Exception ex)
            {
                AppendLog(showErrorDialog
                    ? "ERROR " + Path.GetFileName(path) + ": " + ex.Message
                    : "skipped " + Path.GetFileName(path) + " (not a chrono export)");
                if (showErrorDialog)
                    MessageBox.Show(this, ex.Message, Lang.T("Could not read file"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        UpdateStatus();
    }

    // Stored in grains, shown in GRT's unit. ChargeFormat keeps a grams reading to four places:
    // 0.02 gr is a good scale's last digit and also 0.0013 g, which two places would round away.
    private static string ChargeCell(double grains)
    {
        var u = ChargeUnits;
        return u.ChargeValue(grains).ToString(u.ChargeFormat, CultureInfo.InvariantCulture);
    }

    // The charge column has a unit of its own that a right click on its title changes (weights follow the single choice of the program until then).
    private static ProgramUnitView ChargeUnits => ProgramUnits.Current.ForField("athlon.charge");
    private static string ChargeHeader() => Lang.T("Charge") + " (" + ChargeUnits.ChargeUnitName + ")";

    private void RefreshChargeColumn()
    {
        _grid.Columns["charge"]!.HeaderText = ChargeHeader();
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is AthlonString s) row.Cells["charge"].Value = s.ChargeGrains is { } g ? ChargeCell(g) : "?";
        RefreshChart();
    }

    private void AddGridRow(AthlonString s)
    {
        var stats = StringStats.From(s.Velocities.ToList());
        int i = _grid.Rows.Add();
        DataGridViewRow row = _grid.Rows[i];
        row.Cells["inc"].Value = true;
        row.Cells["file"].Value = s.FileName;
        row.Cells["charge"].Value = s.ChargeGrains is { } g ? ChargeCell(g) : "?";
        var u = ProgramUnits.Current;
        row.Cells["n"].Value = stats.N;
        row.Cells["avg"].Value = Str.Num(u.VelocityValue(stats.Mean), 1);
        row.Cells["sd"].Value = Str.Num(u.VelocityValue(stats.Sd), 1);
        row.Cells["es"].Value = Str.Num(u.VelocityValue(stats.Es), 1);
        row.Cells["temp"].Value = s.SessionTempC is { } t ? Str.Num(u.TemperatureValue(t), 1) : "";
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

    // ---- the archive ------------------------------------------------------------------------------------------------

    private long ArchiveShooter() => Archive.ArchiveImports.ShooterFor(_archive!, _db?.CurrentShooterId);

    private void FillLoadChoices(long? select = null)
    {
        if (_archive == null) return;
        _loadPick.BeginUpdate();
        _loadPick.Items.Clear();
        // never a load picked by default: a session filed on the wrong load is worse than one that asks
        if (_grt != null) _loadPick.Items.Add(new LoadChoice(null, Lang.T("(the load open in GRT)"), FromGrt: true));
        else _loadPick.Items.Add(new LoadChoice(null, Lang.T("(choose the load…)")));
        foreach (var (id, label) in Archive.ArchiveImports.LoadsOf(_archive, ArchiveShooter()))
            _loadPick.Items.Add(new LoadChoice(id, label));
        _loadPick.Items.Add(new LoadChoice(null, Lang.T("From a GRT file (.grtload)…"), FromFile: true));
        _loadPick.Items.Add(new LoadChoice(null, Lang.T("New load…"), New: true));
        _loadPick.EndUpdate();
        int idx = select is { } s ? _loadPick.Items.Cast<LoadChoice>().ToList().FindIndex(c => c.Id == s) : -1;
        _loadPick.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private bool _choosing;

    /// <summary>"New load…" asks for a name and a caliber, files the load in the archive and selects it.</summary>
    private void PickedLoad()
    {
        if (_choosing || _archive == null || _loadPick.SelectedItem is not LoadChoice c || !(c.New || c.FromFile)) return;
        _choosing = true;
        try
        {
            if (c.FromFile)
            {
                var log = new List<string>();
                long? fromFile = ArchiveLoadPicker.PickGrtFile(this, _archive, ArchiveShooter(), log);
                if (fromFile == null) { _loadPick.SelectedIndex = 0; return; }
                FillLoadChoices(fromFile);
                foreach (var l in log) AppendLog(l);
                return;
            }
            var (name, caliber) = AskNewLoad();
            if (name.Length == 0) { _loadPick.SelectedIndex = 0; return; }
            long id = Archive.ArchiveImports.NewLoad(_archive, ArchiveShooter(), name, caliber);
            FillLoadChoices(id);
            AppendLog(string.Format(Lang.T("new load in the archive: {0}"), name));
        }
        finally { _choosing = false; }
    }

    private (string Name, string Caliber) AskNewLoad()
    {
        using var f = new Form
        {
            Text = Lang.T("New load"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 130),
        };
        var name = new TextBox { Left = 100, Top = 14, Width = 300 };
        var cal = new TextBox { Left = 100, Top = 46, Width = 300 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 244, Top = 88, Width = 75 };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 325, Top = 88, Width = 75 };
        f.Controls.AddRange(new Control[]
        {
            new Label { Text = Lang.T("Name"), Left = 12, Top = 17, AutoSize = true }, name,
            new Label { Text = Lang.T("Caliber"), Left = 12, Top = 49, AutoSize = true }, cal, ok, cancel,
        });
        f.AcceptButton = ok; f.CancelButton = cancel;
        GrtTheme.Apply(f);
        return f.ShowDialog(this) == DialogResult.OK ? (name.Text.Trim(), cal.Text.Trim()) : ("", "");
    }

    /// <summary>The exports carry clock times only: the session date starts at the oldest file's date, until the shooter changes it.</summary>
    private void SuggestDate()
    {
        if (_dateTouched || _strings.Count == 0) return;
        _date.Value = Archive.ArchiveImports.LikelyDate(_strings.Select(s => s.SourceFile));
        _dateTouched = false;
    }

    /// <summary>
    /// Writes the ticked strings into the archive as one session. Returns false when the shooter stops at the "already in
    /// the archive" question; true otherwise, also when there is no archive (then the strings go to GRT only).
    /// </summary>
    private bool SaveToArchive(List<ImportBuilder.Row> rows, string? grtPath)
    {
        if (_archive == null) { AppendLog(Lang.T("archive not available: the strings go to GRT only")); return true; }
        long shooter = ArchiveShooter();
        var log = new List<string>();
        long? loadId = null;
        if (_loadPick.SelectedItem is LoadChoice c)
        {
            if (c.FromGrt)
            {
                if (grtPath != null) loadId = Archive.ArchiveImports.LoadForGrtFile(_archive, grtPath, shooter, log);
                else AppendLog(Lang.T("no load open in GRT: the session is saved without a load"));
            }
            else loadId = c.Id;
        }

        string firearm = "";
        var strings = new List<Archive.ChronoString>();
        foreach (var r in rows.Where(r => r.Include))
        {
            var s = r.String_;
            var stats = StringStats.From(s.Velocities.ToList());
            strings.Add(new Archive.ChronoString(
                s.ChargeGrains is { } g ? Archive.ArchiveImports.Grains(g) : Path.GetFileNameWithoutExtension(s.SourceFile),
                s.ChargeGrains, r.TempC, ImportBuilder.BuildChargeNote(s, stats, r.TempC),
                s.Shots.Select(m => new Archive.ChronoShot(m.VelocityMps, m.Time, m.Note)).ToList(),
                s.SourceFile, File.ReadAllBytes(s.SourceFile)));
        }

        var already = Archive.ArchiveImports.AlreadyImported(_archive, strings);
        if (already.Count > 0 && MessageBox.Show(this,
                string.Format(Lang.T("These files are already in the archive:\n{0}\n\nImport them again as a new session?"), string.Join("\n", already)),
                Lang.T("Import"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            AppendLog(Lang.T("import cancelled"));
            return false;
        }

        var res = Archive.ArchiveImports.AddChronoSession(_archive, shooter, loadId, _date.Value.Date, firearm, strings, "Athlon / Garmin");
        foreach (string l in log) AppendLog(l);
        AppendLog(string.Format(Lang.T("archive: session saved ({0} string(s), {1} shot(s))"), res.Strings, res.Shots));
        FillLoadChoices(loadId);
        return true;
    }

    private async Task DoImportAsync()
    {
        try
        {
            _importBtn.Enabled = false;
            bool toGrt = _archive == null || _toGrt.Checked;
            bool loadFromGrt = _loadPick.SelectedItem is LoadChoice { FromGrt: true };
            if (_archive != null && _loadPick.SelectedItem is LoadChoice { Id: null, FromGrt: false, New: false })
            {
                MessageBox.Show(this, Lang.T("Choose the load in the archive these strings belong to (or \"New load…\")."), Lang.T("Import"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var rows = new List<ImportBuilder.Row>();
            foreach (DataGridViewRow gr in _grid.Rows)
            {
                if (gr.Tag is not AthlonString s) continue;
                bool inc = gr.Cells["inc"].Value is true;
                // The cell is in GRT's unit (the header says so); everything downstream is °C.
                double? shown = Str.ParseNumber(Convert.ToString(gr.Cells["temp"].Value, CultureInfo.InvariantCulture));
                double? temp = shown is { } sv ? ProgramUnits.Current.TemperatureToCelsius(sv) : null;
                rows.Add(new ImportBuilder.Row(s, temp, inc));
            }
            if (rows.All(r => !r.Include)) { MessageBox.Show(this, Lang.T("Nothing selected."), Lang.T("Import")); return; }

            string? basePath = null;
            if (_grt is { Connected: true } && (toGrt || loadFromGrt))
            {
                try
                {
                    var top = await _grt.GetTabOnTopAsync();
                    basePath = string.IsNullOrWhiteSpace(top.file) ? null : top.file;
                    AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("active load in GRT: {0}  [{1}]"), top.caption, basePath ?? Lang.T("unsaved tab")));
                }
                catch (Exception ex) { AppendLog(Lang.T("Get_TabOnTop failed:") + " " + ex.Message); }
            }

            // Anti-chaining: never merge into a file we generated, or a placeholder tab.
            if (basePath != null && GrtLoadDoc.LooksLikeGeneratedSibling(basePath))
            {
                MessageBox.Show(this,
                    Lang.T("The tab active in GRT is a generated file, not your load.\n\n" +
                    "Switch to your real load tab in GRT, then import again."),
                    Lang.T("Import"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The load on top in GRT is where the strings go: say whose it is before writing, since the shooter keeps other people's loads too.
            if (basePath != null && !ImportTarget.Confirm(this, basePath, _db))
            {
                AppendLog(Lang.T("import cancelled"));
                return;
            }

            // the archive first: it is the shooter's own copy, whatever happens with GRT next
            if (!SaveToArchive(rows, basePath)) return;
            if (!toGrt)
            {
                _next?.MarkDone();
                _status.Text = Lang.T("Saved in the archive (not sent to GRT).");
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
                if (cal is "" or "Generic") AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("note: base load has no real caliber name ('{0}') — importing anyway"), cal));
            }
            else
            {
                using var save = new SaveFileDialog
                {
                    Title = Lang.T("No load open in GRT — choose where to save the imported strings"),
                    Filter = "GRT load (*.grtload)|*.grtload",
                    FileName = $"Athlon_{DateTime.Now:yyyyMMdd_HHmm}.grtload",
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                explicitPath = save.FileName;
                doc = GrtLoadDoc.CreateMinimal(title, explicitPath);
                AppendLog(Lang.T("no load open — writing a minimal file with a Generic caliber/gun/projectile"));
            }

            if (_replacePrev.Checked)
            {
                int rm = doc.RemoveByTitlePrefix(ImportBuilder.MeasurementTitlePrefix);
                if (rm > 0) AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("replaced {0} earlier \"Athlon …\" Measurement(s)"), rm));
            }
            int flagged = StaleNotes.FlagBarrelCalibration(doc, DateTime.Now);
            if (flagged > 0) AppendLog(string.Format(CultureInfo.InvariantCulture, Lang.T("an earlier Barrel Calibration note described the PREVIOUS measurements: renamed it \"{0}\" and marked it as outdated (nothing deleted)"), StaleNotes.OldBarrelCalibrationTitle));
            doc.AddMeasurement(title, charges);

            string outPath;
            if (explicitPath != null) { doc.Save(explicitPath); outPath = explicitPath; }
            else outPath = doc.SaveSibling("athlon");

            AppendLog(Lang.T("wrote") + " " + outPath);
            if (_grt is { Connected: true })
            {
                await _grt.LoadFileAsync(outPath);
                AppendLog(Lang.T("asked GRT to open it"));
                _next?.MarkDone();
                _status.Text = string.Format(Lang.T("Imported {0} charge(s) into GRT."), charges.Count)
                    + (flagged > 0 ? " " + Lang.T("The old Barrel Calibration note is now marked OLD - redo the calibration.") : "");
            }
            else
            {
                _next?.MarkDone();
                _status.Text = Lang.T("Wrote") + " " + Path.GetFileName(outPath) + " " + Lang.T("(not connected to GRT).");
                MessageBox.Show(this, Lang.T("Saved:") + "\n" + outPath, Lang.T("Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog(Lang.T("IMPORT FAILED:") + " " + ex);
            MessageBox.Show(this, ex.Message, Lang.T("Import failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _importBtn.Enabled = _strings.Count > 0;
        }
    }

    private readonly StringsChart _chart = new() { Dock = DockStyle.Fill };

    private void RefreshChart()
    {
        var u = ProgramUnits.Current;
        _chart.SetData(_strings.Where(x => x.Velocities.Any()).Select(x => new StringPoint(
            x.ChargeGrains is { } g ? ChargeUnits.Charge(g) + " (" + x.FileName + ")" : x.FileName,
            x.Velocities.Select(v => u.VelocityValue(v)).ToList())).ToList(), u.VelocityUnitName);
    }

    private void UpdateStatus()
    {
        RefreshChart();
        _importBtn.Enabled = _strings.Count > 0;
        string conn = _grt == null ? Lang.T("stand-alone (no GRT)")
            : _grt.Connected ? string.Format(Lang.T("connected to GRT :{0}"), _grt.Port)
            : Lang.T("GRT connection lost");
        _status.Text = string.Format(Lang.T("{0} file(s) loaded"), _strings.Count) + " — " + conn;
    }

    private void AppendLog(string line) => _log.SafeAppend(line);
}
