using System.Globalization;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The cartridges of one shooter, from the pieces on the bench to the check after the shooting: measure cases, bullets and primers,
/// put them together into lots of numbered cartridges, let the program learn from the shots of the archive which measures move the
/// velocity, plan a race or a training with the cartridges in the order that gives the lowest SD, save that prediction (it can never
/// be changed afterwards) and, after the shooting, see whether it held. Works without GRT: everything lives in the archive.
/// </summary>
internal sealed partial class CartridgesForm : Form
{
    private readonly ArchiveLink _link;
    private ArchiveDb Db => _link.Db;
    private long Shooter => _link.ShooterId();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly (string Kind, string Label)[] Kinds = { ("case", "Cases"), ("bullet", "Bullets"), ("primer", "Primers") };
    private static readonly string[] CartridgeFields = MeasureFields.All.Where(f => f.Owner == MeasureFields.Cartridge).Select(f => f.Key).ToArray();

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private TabPage _checkPage = null!;

    /// <summary>After a lot changes section or is deleted, both pages show it.</summary>
    private void RefreshLotPanes() { _matchPane.Fill(); _trainPane.Fill(); }

    // ---- pieces
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly DataGridView _pieces = Grid();
    private List<PieceRow> _pieceRows = new();

    // ---- lots: the match lots and the training lots (each page is a LotsPane)
    private LotsPane _matchPane = null!;
    private LotsPane _trainPane = null!;
    private bool _filling;

    // ---- prediction
    private readonly ModelChart _modelChart = new() { Dock = DockStyle.Fill };
    private readonly PlanChart _planChart = new() { Dock = DockStyle.Right, Width = 470 };
    private readonly TextBox _modelText = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _planLot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly RadioButton _race = new() { AutoSize = true, Checked = true };
    private readonly RadioButton _training = new() { AutoSize = true };
    private readonly NumericUpDown _targets = new() { Minimum = 1, Maximum = 20, Value = 3, Width = 50 };
    private readonly NumericUpDown _perTarget = new() { Minimum = 1, Maximum = 100, Value = 10, Width = 55 };
    private readonly NumericUpDown _returns = new() { Minimum = 0, Maximum = 20, Value = 0, Width = 50 };
    private readonly NumericUpDown _perReturn = new() { Minimum = 1, Maximum = 100, Value = 10, Width = 55 };
    private readonly NumericUpDown _proving = new() { Minimum = 0, Maximum = 50, Value = 5, Width = 50 };
    private readonly NumericUpDown _perSeries = new() { Minimum = 1, Maximum = 100, Value = 5, Width = 55 };
    private readonly ListView _plan = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly TextBox _planText = new() { Dock = DockStyle.Bottom, Height = 96, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _note = new() { Width = 300 };
    private readonly Button _save = new() { AutoSize = true, Enabled = false };
    private List<CartridgeLotRow> _planLots = new();
    private VelocityModelResult? _model;
    private LearningSet? _set;
    private CartridgePlan? _computed;
    private long _computedLot;

    // ---- check
    private readonly ListView _preds = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly TextBox _checkText = new() { Dock = DockStyle.Bottom, Height = 170, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private List<PredictionListItem> _predRows = new();

    // ---- tolerances and classes
    private readonly DataGridView _tolGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label _tolHint = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(4, 4, 4, 0), ForeColor = SystemColors.GrayText };
    private readonly ComboBox _classKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _classKey1 = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox _classKey2 = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly TextBox _classWidth1 = new() { Width = 70 };
    private readonly TextBox _classWidth2 = new() { Width = 70 };
    private readonly Label _classHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
    private readonly ListView _classes = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly TextBox _classText = new() { Dock = DockStyle.Bottom, Height = 60, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly List<string> _classLines = new();

    // ---- SD budget
    private readonly BudgetChart _budgetChart = new() { Dock = DockStyle.Fill };
    private readonly TextBox _budgetText = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f) };
    private readonly NumericUpDown _chronoSd = new() { Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.05m, Width = 70 };
    private readonly NumericUpDown _distance = new() { Minimum = 10, Maximum = 2000, Value = 100, Width = 70 };
    private readonly Label _budgetStrings = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
    private List<long> _budgetIds = new();
    private sealed record FieldItem(string Key) { public override string ToString() => Header(Key); }

    public CartridgesForm(ArchiveLink link, int startTab = 0)
    {
        _link = link;
        Text = AppVersion.Title(Lang.T("Cartridges"));
        Width = 1240; Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 600);

        var piecesPage = new TabPage(Lang.T("Pieces")) { Padding = new Padding(4) };
        var cartPage = new TabPage(Lang.T("Match lots")) { Padding = new Padding(4) };
        var trainPage = new TabPage(Lang.T("Training")) { Padding = new Padding(4) };
        var predPage = new TabPage(Lang.T("Prediction and plan")) { Padding = new Padding(4) };
        var checkPage = new TabPage(Lang.T("Check after the shooting")) { Padding = new Padding(4) };
        piecesPage.Controls.Add(BuildPieces());
        _matchPane = new LotsPane(this, "race");
        _trainPane = new LotsPane(this, "training");
        cartPage.Controls.Add(_matchPane.Build());
        trainPage.Controls.Add(_trainPane.Build());
        _checkPage = checkPage;
        predPage.Controls.Add(BuildPrediction());
        checkPage.Controls.Add(BuildCheck());
        var tolPage = new TabPage(Lang.T("Tolerances and classes")) { Padding = new Padding(4) };
        var budgetPage = new TabPage(Lang.T("SD budget")) { Padding = new Padding(4) };
        tolPage.Controls.Add(BuildTolerances());
        budgetPage.Controls.Add(BuildBudget());
        _tabs.TabPages.AddRange(new[] { piecesPage, cartPage, trainPage, predPage, checkPage, tolPage, budgetPage });
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == cartPage) _matchPane.Fill();
            else if (_tabs.SelectedTab == trainPage) _trainPane.Fill();
            else if (_tabs.SelectedTab == predPage) FillPlanLots();
            else if (_tabs.SelectedTab == checkPage) FillPredictions();
            else if (_tabs.SelectedTab == tolPage) { FillTolerances(); RefreshClassFields(); }
        };
        Controls.Add(_tabs);
        if (startTab > 0 && startTab < _tabs.TabCount) _tabs.SelectedIndex = startTab;

        foreach (var (_, label) in Kinds) _kind.Items.Add(Lang.T(label));
        _kind.SelectedIndex = 0;
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.CellSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
    };

    private static string Header(string key) => AppUnits.MeasureHeader(key);

    private static string Num(double v) => v.ToString("0.###", Inv);

    private void Warn(string text) => MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private bool Guard(Action work)
    {
        try { work(); return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }

    // ═══════════════ Pieces ═══════════════

    private string Kind => Kinds[Math.Max(0, _kind.SelectedIndex)].Kind;

    private Control BuildPieces()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        top.Controls.Add(new Label { Text = Lang.T("Kind:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        top.Controls.Add(_kind);
        var tray = new Button { Text = Lang.T("Add a tray…"), AutoSize = true };
        var add = new Button { Text = Lang.T("Add pieces…"), AutoSize = true };
        var del = new Button { Text = Lang.T("Delete selected"), AutoSize = true };
        var import = new Button { Text = Lang.T("Import weighings…"), AutoSize = true };
        top.Controls.AddRange(new Control[] { tray, add, import, del });
        top.Controls.Add(new Label { Text = Lang.T("Type the measures in the cells: Enter moves down. A measure taken again replaces the old one; an empty cell removes it. Right-click the title of a column to change its unit."), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(14, 8, 0, 0) });
        import.Click += (_, _) => ImportWeighings();
        _kind.SelectedIndexChanged += (_, _) => FillPieces();
        tray.Click += (_, _) => AddTray();
        add.Click += (_, _) => AddPieces();
        del.Click += (_, _) => DeletePieces();
        _pieces.CellEndEdit += (_, e) => PieceEdited(e.RowIndex, e.ColumnIndex);
        AppUnits.AttachColumnMenu(_pieces, FillPieces);               // a right click on the title of a column changes its unit
        root.Controls.Add(_pieces);
        root.Controls.Add(top);
        return root;
    }

    /// <summary>The fields to show for a kind of piece: its own, then any other that some piece of that kind carries (a weighing of the neck, for instance).</summary>
    private List<string> FieldsFor(string kind, IEnumerable<Dictionary<string, double>> measured)
    {
        var keys = MeasureFields.All.Where(f => f.Owner == kind).Select(f => f.Key).ToList();
        var extra = measured.SelectMany(m => m.Keys).Distinct().Where(k => !keys.Contains(k))
            .OrderBy(k => MeasureFields.All.ToList().FindIndex(f => f.Key == k) is var i && i >= 0 ? i : int.MaxValue).ThenBy(k => k, StringComparer.Ordinal);
        keys.AddRange(extra);
        return keys;
    }

    private void FillPieces()
    {
        _filling = true;
        _pieces.Columns.Clear();
        _pieceRows = CartridgeStore.Pieces(Db, Shooter, Kind).OrderBy(p => p.Mark, Comparer<string>.Create(CartridgeText.CompareMarks)).ToList();
        var measures = _pieceRows.ToDictionary(p => p.Id, p => CartridgeStore.Measures(Db, "piece", p.Id));
        var fields = FieldsFor(Kind, measures.Values);
        _pieces.Columns.Add(new DataGridViewTextBoxColumn { Name = "mark", HeaderText = Lang.T("Mark"), ReadOnly = true, Width = 90 });
        foreach (string key in fields)
            _pieces.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = Header(key), Width = 130 });
        _pieces.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = Lang.T("Status"), ReadOnly = true, Width = 90 });
        _pieces.Columns.Add(new DataGridViewTextBoxColumn { Name = "notes", HeaderText = Lang.T("Notes"), ReadOnly = true, Width = 320 });
        _pieces.Rows.Clear();
        foreach (var p in _pieceRows)
        {
            var m = measures[p.Id];
            var cells = new List<object?> { p.Mark };
            foreach (string key in fields) cells.Add(m.TryGetValue(key, out double v) ? AppUnits.ShowMeasure(key, v) : "");
            cells.Add(p.Retired ? Lang.T("set aside") : "");
            cells.Add(p.Notes);
            int i = _pieces.Rows.Add(cells.ToArray());
            _pieces.Rows[i].Tag = p.Id;
        }
        _filling = false;
    }

    private void PieceEdited(int row, int col)
    {
        if (_filling || row < 0 || col < 1) return;
        long id = (long)_pieces.Rows[row].Tag!;
        string field = _pieces.Columns[col].Name;
        string text = _pieces[col, row].Value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text)) { Guard(() => CartridgeStore.DeleteMeasure(Db, "piece", id, field)); return; }
        if (!CartridgeText.TryParseNumber(text, out double v))
        {
            Warn(Lang.T("That is not a number."));
            var old = CartridgeStore.Measures(Db, "piece", id);
            _pieces[col, row].Value = old.TryGetValue(field, out double o) ? AppUnits.ShowMeasure(field, o) : "";
            return;
        }
        double stored = AppUnits.MeasureToStored(field, v);              // what was typed is in the unit of the column; the archive keeps its own
        if (Guard(() => CartridgeStore.SetMeasure(Db, "piece", id, field, stored))) _pieces[col, row].Value = AppUnits.ShowMeasure(field, stored);
    }

    /// <summary>Takes the sessions saved by PesataOgive and PesataBossoli into the archive. Their own files are only read.</summary>
    private void ImportWeighings()
    {
        using var open = new OpenFileDialog
        {
            Title = Lang.T("Weighing sessions"), Multiselect = true, Filter = Lang.T("Weighing sessions") + " (*.json)|*.json|*.*|*.*",
            InitialDirectory = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pesata ogive"))
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pesata ogive")
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        string report = ImportFiles(open.FileNames, batch => MessageBox.Show(this, string.Format(Inv, Lang.T("\"{0}\": are the holes of the tray the codes engraved on the cases (like 7C)? If yes, the measures go to the cases that already carry that code, and two sessions of the same cases meet in the same piece. If not, the pieces of this session get a code of their own."), batch.Name),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
        using var dlg = new LongTextDialog(Lang.T("Weighing sessions"), Lang.T("What was taken into the archive:"), report);
        dlg.ShowDialog(this);
    }

    /// <summary>Reads the session files and takes them into the archive, one step each; says what happened. <paramref name="engravedMarks"/> is asked for each tray of cases.</summary>
    private string ImportFiles(IEnumerable<string> files, Func<WeighingBatch, bool> engravedMarks)
    {
        var report = new List<string>();
        string? lastKind = null;
        foreach (string file in files)
        {
            try
            {
                var batch = WeighingReader.Read(File.ReadAllText(file));
                bool engraved = batch.PieceKind == "case" && engravedMarks(batch);
                var r = CartridgeStore.ImportWeighing(Db, Shooter, batch, engraved, file);
                lastKind = batch.PieceKind;
                report.Add((r.BatchCode.Length > 0
                        ? string.Format(Inv, Lang.T("{0} ({1}): {2} pieces added, {3} updated, code {4}."), batch.Name, Path.GetFileName(file), r.Added, r.Updated, r.BatchCode)
                        : string.Format(Inv, Lang.T("{0} ({1}): {2} pieces added, {3} updated, with the engraved codes."), batch.Name, Path.GetFileName(file), r.Added, r.Updated))
                    + (r.SetAside > 0 ? " " + string.Format(Inv, Lang.T("{0} set aside."), r.SetAside) : ""));
                report.AddRange(r.Warnings.Select(w => "   • " + w));
            }
            catch (Exception ex)
            {
                report.Add(string.Format(Inv, "{0}: {1}", Path.GetFileName(file), ex.Message));
            }
        }
        if (lastKind != null) _kind.SelectedIndex = Kinds.ToList().FindIndex(k => k.Kind == lastKind);
        FillPieces();
        return string.Join(Environment.NewLine, report);
    }

    private void AddTray()
    {
        using var d = new Form
        {
            Text = Lang.T("Add a tray"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 130),
        };
        var cols = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 10, Left = 130, Top = 14, Width = 60 };
        var rows = new NumericUpDown { Minimum = 1, Maximum = 26, Value = 10, Left = 130, Top = 46, Width = 60 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 206, Top = 90, Width = 75 };
        var no = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 290, Top = 90, Width = 75 };
        d.Controls.AddRange(new Control[]
        {
            new Label { Text = Lang.T("Columns (1, 2, 3…):"), Left = 12, Top = 17, AutoSize = true },
            new Label { Text = Lang.T("Rows (A, B, C…):"), Left = 12, Top = 49, AutoSize = true },
            new Label { Text = Lang.T("Marks: 1A, 1B … column by column."), Left = 12, Top = 74, AutoSize = true, ForeColor = SystemColors.GrayText },
            cols, rows, ok, no,
        });
        d.AcceptButton = ok; d.CancelButton = no;
        GrtTheme.Apply(d);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        AddMarks(CartridgeText.TrayMarks((int)cols.Value, (int)rows.Value));
    }

    private void AddPieces()
    {
        using var d = new Form
        {
            Text = Lang.T("Add pieces"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 250),
        };
        var box = new TextBox { Left = 12, Top = 32, Width = 356, Height = 160, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 206, Top = 206, Width = 75 };
        var no = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 290, Top = 206, Width = 75 };
        d.Controls.AddRange(new Control[] { new Label { Text = Lang.T("The marks, separated by spaces, commas or new lines:"), Left = 12, Top = 10, AutoSize = true }, box, ok, no });
        d.AcceptButton = null; d.CancelButton = no;
        GrtTheme.Apply(d);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        AddMarks(CartridgeText.ParseMarks(box.Text));
    }

    private void AddMarks(List<string> marks)
    {
        int added = 0;
        Guard(() => Db.InTransaction(() =>
        {
            var have = CartridgeStore.Pieces(Db, Shooter, Kind).Select(p => MeasureFields.NormalizeMark(p.Mark)).ToHashSet();
            foreach (string m in marks)
                if (have.Add(MeasureFields.NormalizeMark(m))) { CartridgeStore.AddPiece(Db, Shooter, Kind, m); added++; }
        }));
        FillPieces();
        Warn(string.Format(Inv, Lang.T("{0} pieces added, {1} were already there."), added, marks.Count - added));
    }

    private void DeletePieces()
    {
        var rows = _pieces.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().ToList();
        if (rows.Count == 0) return;
        if (MessageBox.Show(this, string.Format(Inv, Lang.T("Delete {0} pieces with their measures? The cartridges that use them stay, without them."), rows.Count),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Guard(() => { foreach (int r in rows) CartridgeStore.DeletePiece(Db, (long)_pieces.Rows[r].Tag!); });
        FillPieces();
    }

    // ═══════════════ Prediction and plan ═══════════════

    private Control BuildPrediction()
    {
        var learn = new Button { Text = Lang.T("Learn from the archive"), AutoSize = true };
        learn.Click += (_, _) => Learn();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        top.Controls.Add(learn);
        top.Controls.Add(new Label
        {
            Text = Lang.T("The program looks at which measures of the cartridges really moved the velocity, inside each string."),
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 8, 0, 0),
        });

        var plan = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(4, 6, 4, 0), WrapContents = true };
        plan.Controls.Add(new Label { Text = Lang.T("Lot:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        plan.Controls.Add(_planLot);
        _planLot.SelectedIndexChanged += (_, _) => PlanLotChanged();
        _race.Text = Lang.T("Race"); _training.Text = Lang.T("Training");
        _race.Margin = new Padding(14, 4, 0, 0); _training.Margin = new Padding(8, 4, 0, 0);
        plan.Controls.Add(_race); plan.Controls.Add(_training);
        void Param(string text, Control c) { plan.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(12, 6, 3, 0) }); plan.Controls.Add(c); }
        Param(Lang.T("targets"), _targets); Param(Lang.T("shots each"), _perTarget); Param(Lang.T("returns"), _returns); Param(Lang.T("shots per return"), _perReturn);
        Param(Lang.T("proving shots"), _proving); Param(Lang.T("per series"), _perSeries);
        LoadPlanSettings();
        var compute = new Button { Text = Lang.T("Compute the plan"), AutoSize = true, Margin = new Padding(16, 2, 0, 0) };
        compute.Click += (_, _) => ComputePlan();
        plan.Controls.Add(compute);
        _race.CheckedChanged += (_, _) => { _targets.Enabled = _perTarget.Enabled = _returns.Enabled = _perReturn.Enabled = _proving.Enabled = _race.Checked; _perSeries.Enabled = !_race.Checked; };
        _perSeries.Enabled = false;

        foreach (var (h, w) in new[] { (Lang.T("Group"), 120), (Lang.T("Cartridges (number · case)"), 520), (Lang.T("Predicted SD (m/s)"), 120), (Lang.T("Estimates (m/s from the median)"), 190) })
            _plan.Columns.Add(h, w);

        var saveBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        _save.Text = Lang.T("Save the prediction (frozen)");
        _save.Click += (_, _) => SavePrediction();
        saveBar.Controls.Add(new Label { Text = Lang.T("Note:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        saveBar.Controls.Add(_note);
        saveBar.Controls.Add(_save);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(260);
        var upper = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(520);          // the words of the model | the picture of what moves the velocity
        upper.Panel1.Controls.Add(_modelText);
        upper.Panel2.Controls.Add(_modelChart);
        split.Panel1.Controls.Add(upper);
        split.Panel1.Controls.Add(top);
        var lower = new Panel { Dock = DockStyle.Fill };
        lower.Controls.Add(_plan);
        lower.Controls.Add(_planChart);
        lower.Controls.Add(_planText);
        lower.Controls.Add(saveBar);
        lower.Controls.Add(plan);
        split.Panel2.Controls.Add(lower);
        return split;
    }

    private void FillPlanLots()
    {
        long? keep = (_planLot.SelectedIndex >= 0 && _planLot.SelectedIndex < _planLots.Count) ? _planLots[_planLot.SelectedIndex].Id : null;
        _planLots = CartridgeStore.Lots(Db, Shooter).Where(l => l.Cartridges > 0).OrderBy(l => l.IsTraining).ThenByDescending(l => l.Id).ToList();
        _planLot.Items.Clear();
        foreach (var l in _planLots) _planLot.Items.Add($"{l.Name} ({l.Cartridges}) · " + Lang.T(l.IsTraining ? "training" : "match"));
        int at = keep is { } k ? _planLots.FindIndex(l => l.Id == k) : -1;
        if (_planLots.Count > 0) _planLot.SelectedIndex = Math.Max(0, at);
    }

    /// <summary>A match lot gets the plan of the match (targets, proving shots), a training lot the order of the training: the choice follows what the lot is for.</summary>
    private void PlanLotChanged()
    {
        if (_planLot.SelectedIndex < 0 || _planLot.SelectedIndex >= _planLots.Count) return;
        bool training = _planLots[_planLot.SelectedIndex].IsTraining;
        _training.Checked = training;
        _race.Checked = !training;
        _race.Enabled = _training.Enabled = false;
        _computed = null; _save.Enabled = false;
    }

    private void Learn()
    {
        _set = CartridgeStore.LearningShots(Db, Shooter);
        _model = _set.Samples.Count == 0 ? null : VelocityModel.Learn(_set.Samples);
        _modelText.Text = _model == null
            ? Lang.T("No shot of the archive is linked to a measured cartridge yet. Link the shots of a session to their cartridges in the \"Check after the shooting\" tab.")
            : CartridgeText.ModelSummary(_model, MeasureShown) + Environment.NewLine + Environment.NewLine
              + Tr.F("The model learned from {0} shots: {1} fired from training lots, {2} from match lots.", _model.NShots, _set.TrainingShots, _set.RaceShots);
        _computed = null; _save.Enabled = false;
        UpdateModelChart();
    }

    /// <summary>The measure with the strongest proof, against the velocity, each series centred on itself: the picture of why the model believes it.</summary>
    private void UpdateModelChart()
    {
        if (_model is null || _set is null || _model.Significant.Count == 0) { _modelChart.SetData(Array.Empty<ModelPoint>(), "", "", 0); return; }
        var t = _model.Significant.OrderBy(x => x.PAdj).First();
        var shown = AppUnits.ForMeasure(t.Key);
        double per = shown is { } sh ? UnitSystem.Find(sh.Code).PerBase : 1;          // shown units in one stored unit
        var points = new List<ModelPoint>();
        int index = 0;
        foreach (var series in _set.Samples.Where(s => s.Measures.ContainsKey(t.Key)).GroupBy(s => s.SeriesId))
        {
            double mx = series.Average(s => s.Measures[t.Key]), mv = series.Average(s => s.VelocityMps);
            points.AddRange(series.Select(s => new ModelPoint((s.Measures[t.Key] - mx) * per, s.VelocityMps - mv, index)));
            index++;
        }
        string unit = shown is { } u ? UnitSystem.Label(u.Code) : MeasureFields.UnitOf(t.Key);
        _modelChart.SetData(points, Lang.T(MeasureFields.LabelOf(t.Key)), unit, t.Slope / per);
    }

    private void UpdatePlanChart(CartridgePlan plan)
    {
        if (plan.Error is not null || plan.Groups.Count == 0) { _planChart.SetData(Array.Empty<PlanLane>(), 0); return; }
        _planChart.SetData(plan.Groups.Select(g => new PlanLane(GroupName(g.Name), g.Cartridges.Select(x => (x.Estimate, x.Number.ToString(Inv))).ToList(), g.PredictedSd)).ToList(), plan.SdAtRandom);
    }

    private void ComputePlan()
    {
        if (_planLot.SelectedIndex < 0) { Warn(Lang.T("Make a lot with cartridges first.")); return; }
        if (_model == null) Learn();
        if (_model == null || _model.Significant.Count == 0)
        {
            Warn(Lang.T("No measure has a proven effect on velocity yet, so there is nothing to base a plan on. Link more shots to measured cartridges."));
            return;
        }
        var lot = _planLots[_planLot.SelectedIndex];
        var (est, _) = CartridgePlanner.Predict(CartridgeStore.CartridgeInputs(Db, lot.Id), _model);
        var plan = CartridgePlanner.Compose(est, _race.Checked ? PlanMode.Race : PlanMode.Training, (int)_targets.Value, (int)_perTarget.Value,
            (int)_proving.Value, (int)_perSeries.Value, _model.SdResidual, (int)_returns.Value, (int)_perReturn.Value);
        SavePlanSettings();
        _plan.Items.Clear();
        _computed = null; _save.Enabled = false;
        UpdatePlanChart(plan);
        if (plan.Error is not null) { _planText.Text = plan.Error; return; }

        foreach (var g in plan.Groups)
            _plan.Items.Add(new ListViewItem(new[]
            {
                GroupName(g.Name),
                string.Join("  ", g.Cartridges.Select(x => x.Number + (x.Cartridge.Mark.Length > 0 ? "·" + x.Cartridge.Mark : ""))),
                g.PredictedSd.ToString("0.00", Inv),
                g.From.ToString("+0.0;-0.0;0.0", Inv) + " … " + g.To.ToString("+0.0;-0.0;0.0", Inv),
            }));
        var lines = new List<string>
        {
            Tr.F("Shot in random order, these cartridges would show an SD of {0} m/s ({1} m/s of it from the cartridges).",
                plan.SdAtRandom.ToString("0.00", Inv), plan.CartridgeSdAtRandom.ToString("0.00", Inv)),
        };
        if (plan.Mode == PlanMode.Race)
            lines.Add(PlanSpec() + " " + Tr.T("The most uniform block of cartridges goes to the first target, the returns get the blocks that are left."));
        if (plan.Excluded.Count > 0)
            lines.Add(Tr.T("Left out:") + " " + string.Join(", ", plan.Excluded.Select(x => x.Number + (x.Cartridge.OutOfSpec ? " (" + Tr.T("out of spec") + ")" : " (" + Tr.T("missing") + " " + string.Join(", ", x.Missing.Select(k => Tr.T(MeasureFields.LabelOf(k)))) + ")"))));
        if (_set is { } s && _model != null)
            lines.Add(Tr.F("The prediction comes from {0} shots; a check on the sessions it learned from is not independent.", _model.NShots));
        _planText.Text = string.Join(Environment.NewLine, lines);
        _computed = plan; _computedLot = lot.Id;
        _save.Enabled = true;
    }

    /// <summary>"3 targets of 10 shots, 2 returns of 10 shots, 5 proving shots." — what the plan was made for.</summary>
    private string PlanSpec() => Tr.F("{0} targets of {1} shots, {2} returns of {3} shots, {4} proving shots.", (int)_targets.Value, (int)_perTarget.Value, (int)_returns.Value, (int)_perReturn.Value, (int)_proving.Value);

    // The shape of a race changes from one race to the next, so the program does not decide it: it remembers what was typed last, in the archive.
    private static readonly (string Key, Func<CartridgesForm, NumericUpDown> Box)[] PlanKeys =
    {
        ("plan_targets", f => f._targets), ("plan_per_target", f => f._perTarget), ("plan_returns", f => f._returns),
        ("plan_per_return", f => f._perReturn), ("plan_proving", f => f._proving), ("plan_per_series", f => f._perSeries),
    };

    private void LoadPlanSettings()
    {
        foreach (var (key, box) in PlanKeys)
            if (int.TryParse(Db.GetMeta(key), NumberStyles.Integer, Inv, out int v) && v >= box(this).Minimum && v <= box(this).Maximum) box(this).Value = v;
    }

    private void SavePlanSettings()
    {
        foreach (var (key, box) in PlanKeys) Db.SetMeta(key, ((int)box(this).Value).ToString(Inv));
    }

    /// <summary>"target 2" / "return 1" / "series 3" / "proving shots", in the language of the window.</summary>
    private static string GroupName(string name)
    {
        foreach (string head in new[] { "target", "return", "series" })
            if (name.StartsWith(head + " ", StringComparison.Ordinal)) return Lang.T(head) + name[head.Length..];
        return Lang.T(name);
    }

    private void SavePrediction()
    {
        if (_computed == null || _model == null || _set == null) return;
        if (MessageBox.Show(this, Lang.T("Once saved, the prediction can never be changed: that is what makes the check after the shooting honest. Save it?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        long id = 0;
        if (!Guard(() => id = CartridgeStore.SavePrediction(Db, Shooter, _computedLot, _computed, _model, _set.SessionIds,
            (_computed.Mode == PlanMode.Race ? PlanSpec() + (_note.Text.Trim().Length > 0 ? " " : "") : "") + _note.Text.Trim()))) return;
        _save.Enabled = false;
        _tabs.SelectedTab = _checkPage;
        Warn(string.Format(Inv, Lang.T("Prediction {0} saved. After the shooting, link the shots to the cartridges and check it here."), id));
    }

    // ═══════════════ Check after the shooting ═══════════════

    private Control BuildCheck()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        var link = new Button { Text = Lang.T("Link shots to cartridges…"), AutoSize = true };
        var check = new Button { Text = Lang.T("Check against a session…"), AutoSize = true };
        var show = new Button { Text = Lang.T("Show the prediction"), AutoSize = true };
        var del = new Button { Text = Lang.T("Delete"), AutoSize = true };
        bar.Controls.AddRange(new Control[] { link, check, show, del });
        link.Click += (_, _) => LinkShots();
        check.Click += (_, _) => CheckPrediction();
        show.Click += (_, _) => ShowPrediction();
        del.Click += (_, _) => DeletePrediction();
        foreach (var (h, w) in new[] { (Lang.T("No."), 50), (Lang.T("Saved"), 140), (Lang.T("Lot"), 220), (Lang.T("Mode"), 90), (Lang.T("Cartridges"), 80), (Lang.T("Checked"), 80), (Lang.T("Note"), 320) })
            _preds.Columns.Add(h, w);
        var root = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(_preds);
        root.Controls.Add(_checkText);
        root.Controls.Add(bar);
        return root;
    }

    private void FillPredictions()
    {
        _predRows = CartridgeStore.Predictions(Db, Shooter);
        _preds.Items.Clear();
        foreach (var p in _predRows)
            _preds.Items.Add(new ListViewItem(new[]
            {
                p.Id.ToString(Inv), p.CreatedAt, p.LotName, Lang.T(p.Mode == "race" ? "Race" : "Training"), p.Cartridges.ToString(Inv),
                p.Verified ? Lang.T("yes") : Lang.T("no"), p.Note,
            }));
        if (_predRows.Count > 0) _preds.Items[0].Selected = true;
    }

    private PredictionListItem? SelectedPrediction => _preds.SelectedIndices.Count == 0 ? null : _predRows[_preds.SelectedIndices[0]];

    private void LinkShots()
    {
        var lots = CartridgeStore.Lots(Db, Shooter).Where(l => l.Cartridges > 0).ToList();
        if (lots.Count == 0) { Warn(Lang.T("Make a lot with cartridges first.")); return; }
        using var pick = new ArchivePickForm(_link, ArchivePick.Strings);
        if (pick.ShowDialog(this) != DialogResult.OK) return;

        // the order of a training plan is the order the cartridges are fired in: offer it
        var sel = SelectedPrediction;
        string preset = "";
        if (sel is { Mode: "training" } && CartridgeStore.GetPrediction(Db, sel.Id) is { } pr)
            preset = string.Join(" ", pr.Rows.Where(r => r.Order > 0).OrderBy(r => r.Order).Select(r => r.Mark));
        int total = 0;
        foreach (var s in pick.PickedStrings)
        {
            using var d = new Form
            {
                Text = Lang.T("Link shots to cartridges"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(520, 330),
            };
            var lotBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 32, Width = 496 };
            foreach (var l in lots) lotBox.Items.Add(l.Name + " · " + Lang.T(l.IsTraining ? "training" : "match"));
            lotBox.SelectedIndex = sel is { } p0 && lots.FindIndex(l => l.Id == p0.LotId) is var at && at >= 0 ? at : 0;
            var marks = new TextBox { Left = 12, Top = 82, Width = 496, Height = 190, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = preset };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 342, Top = 288, Width = 75 };
            var no = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 426, Top = 288, Width = 75 };
            d.Controls.AddRange(new Control[]
            {
                new Label { Text = Lang.T("Lot of the cartridges:"), Left = 12, Top = 10, AutoSize = true },
                new Label { Text = string.Format(Inv, Lang.T("Marks of the cases, in firing order, for the {0} shots of \"{1}\":"), s.Shots.Count, s.Label), Left = 12, Top = 62, AutoSize = true },
                lotBox, marks, ok, no,
            });
            d.CancelButton = no;
            GrtTheme.Apply(d);
            if (d.ShowDialog(this) != DialogResult.OK) continue;
            if (!lots[lotBox.SelectedIndex].IsTraining
                && MessageBox.Show(this, string.Format(Inv, Lang.T("The lot \"{0}\" is the match lot, kept for the match. Link these shots to it only if they were fired in the match or in a check of this lot, not in training. Link them?"), lots[lotBox.SelectedIndex].Name),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) continue;
            var list = CartridgeText.ParseMarks(marks.Text);
            int linked = 0;
            Guard(() => linked = CartridgeStore.LinkStringByMarks(Db, s.Id, lots[lotBox.SelectedIndex].Id, list));
            total += linked;
            if (list.Count != s.Shots.Count)
                Warn(string.Format(Inv, Lang.T("{0} marks for {1} shots: they were matched in order as far as they go."), list.Count, s.Shots.Count));
        }
        _checkText.Text = string.Format(Inv, Lang.T("{0} shots linked to their cartridges. \"Learn from the archive\" will use them."), total);
        _model = null;
    }

    private void CheckPrediction()
    {
        if (SelectedPrediction is not { } p) { Warn(Lang.T("Pick a prediction first.")); return; }
        using var pick = new ArchivePickForm(_link, ArchivePick.Strings);
        if (pick.ShowDialog(this) != DialogResult.OK || pick.Session is not { } session) return;
        (PredictionCheckResult Result, bool Independent)? res = null;
        if (!Guard(() => res = CartridgeStore.Verify(Db, p.Id, session.Id))) return;
        if (res is null)
        {
            _checkText.Text = string.Format(Inv, Lang.T("Fewer than {0} shots of this session are linked to a cartridge of this prediction's lot: link them first."), PredictionCheck.MinShots);
            return;
        }
        var (r, independent) = res.Value;
        _checkText.Text = string.Format(Inv, Lang.T("{0} shots in {1} strings of the session of {2}."), r.N, r.Series, session.Date)
            + Environment.NewLine + Environment.NewLine + CartridgeText.VerdictText(r, independent);
        FillPredictions();
    }

    private void ShowPrediction()
    {
        if (SelectedPrediction is not { } sel || CartridgeStore.GetPrediction(Db, sel.Id) is not { } p) return;
        var lines = new List<string>
        {
            string.Format(Inv, Lang.T("Prediction {0} of {1}, {2}, lot \"{3}\"."), p.Id, p.CreatedAt, Lang.T(p.Mode == "race" ? "Race" : "Training"), sel.LotName),
        };
        if (!string.IsNullOrEmpty(p.Note)) lines.Add(p.Note);
        lines.Add("");
        foreach (var r in p.Rows.OrderBy(r => r.Order > 0 ? r.Order : int.MaxValue).ThenBy(r => r.Assigned).ThenBy(r => r.Number))
            lines.Add(string.Format(Inv, "{0,3} · {1,-5} {2,-3} {3,6:+0.0;-0.0;0.0} ± {4:0.0}", r.Number, r.Mark, r.Order > 0 ? r.Order.ToString(Inv) : r.Assigned, r.Estimate, r.EstimateSe));
        if (p.VerificationJson is { } v)
            lines.Add("");
        _checkText.Text = string.Join(Environment.NewLine, lines);
    }

    private void DeletePrediction()
    {
        if (SelectedPrediction is not { } p) return;
        if (p.Verified) { Warn(Lang.T("A prediction that has been checked is kept as it was: it cannot be deleted.")); return; }
        if (MessageBox.Show(this, Lang.T("Delete this prediction? It was never checked."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Guard(() => Db.Exec("DELETE FROM predictions WHERE id=$i", ("$i", p.Id)));
        FillPredictions();
    }

    // ═══════════════ Tolerances and classes ═══════════════

    private Control BuildTolerances()
    {
        var learn = new Button { Text = Lang.T("Learn from the archive"), AutoSize = true };
        learn.Click += (_, _) => { Learn(); FillTolerances(); };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        top.Controls.Add(learn);
        _tolHint.Text = Lang.T("A band is how far apart the biggest and the smallest piece of a class may be for the measure to put no more than that SD into the velocity. Pieces spread evenly over the band have an SD of band/√12. The slopes have their uncertainty: with more shots the bands become more reliable.");

        foreach (var (n, h) in new[] { ("m", "Measure"), ("s", "m/s per unit"), ("d", "Spread today (SD)"), ("t", "SD it puts in today (m/s)"), ("b1", "Band for 1 m/s"), ("b5", "Band for 0.5 m/s") })
            _tolGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = Lang.T(h), FillWeight = n == "m" ? 150 : 90 });

        // classes
        _classKind.Items.AddRange(Kinds.Select(k => (object)Lang.T(k.Label)).ToArray());
        _classKind.SelectedIndex = 0;
        _classKind.SelectedIndexChanged += (_, _) => FillClassFields();
        _classKey1.SelectedIndexChanged += (_, _) => SuggestWidth();
        var make = new Button { Text = Lang.T("Make the classes"), AutoSize = true };
        var copy = new Button { Text = Lang.T("Copy the list"), AutoSize = true };
        make.Click += (_, _) => MakeClasses();
        copy.Click += (_, _) => { if (_classLines.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, _classLines)); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(4, 6, 4, 0), WrapContents = true };
        bar.Controls.Add(new Label { Text = Lang.T("Classes of:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_classKind);
        bar.Controls.Add(new Label { Text = Lang.T("by"), AutoSize = true, Margin = new Padding(10, 6, 4, 0) });
        bar.Controls.Add(_classKey1);
        bar.Controls.Add(new Label { Text = Lang.T("band"), AutoSize = true, Margin = new Padding(6, 6, 4, 0) });
        bar.Controls.Add(_classWidth1);
        bar.Controls.Add(_classHint);
        bar.SetFlowBreak(_classHint, true);
        bar.Controls.Add(new Label { Text = Lang.T("then by"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_classKey2);
        bar.Controls.Add(new Label { Text = Lang.T("band"), AutoSize = true, Margin = new Padding(6, 6, 4, 0) });
        bar.Controls.Add(_classWidth2);
        bar.Controls.Add(make);
        bar.Controls.Add(copy);

        foreach (var (h, w) in new[] { (Lang.T("Class"), 70), (Lang.T("Pieces"), 60), (Lang.T("Range"), 300), (Lang.T("Marks"), 700) })
            _classes.Columns.Add(h, w);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(190);
        split.Panel1.Controls.Add(_tolGrid);
        split.Panel1.Controls.Add(_tolHint);
        split.Panel1.Controls.Add(top);
        var lower = new Panel { Dock = DockStyle.Fill };
        lower.Controls.Add(_classes);
        lower.Controls.Add(_classText);
        lower.Controls.Add(bar);
        split.Panel2.Controls.Add(lower);
        FillClassFields();
        return split;
    }

    private void FillTolerances()
    {
        _tolGrid.Rows.Clear();
        var cc = Inv;
        foreach (var r in ToleranceBands.From(_model))
        {
            // the model works in the units the archive keeps; the table speaks the units of the column of that measure
            var shown = AppUnits.ForMeasure(r.Key);
            double per = shown is { } s ? UnitSystem.Find(s.Code).PerBase : 1;      // shown units in one stored unit
            string u = shown is { } s2 ? UnitSystem.Label(s2.Code) : MeasureFields.UnitOf(r.Key);
            string Q(double stored, int d) => (stored * per).ToString("0." + new string('0', d), cc);
            _tolGrid.Rows.Add(Header(r.Key), (r.Slope / per).ToString("+0.00;-0.00", cc) + " m/s", Q(r.SdOfMeasure, 3) + " " + u,
                r.SdTodayMps.ToString("0.00", cc),
                Q(r.BandForOne, shown?.Code == "in" || shown?.Code == "g" ? 4 : 2) + " " + u + " (±" + Q(r.BandForOne / 2, shown?.Code == "in" || shown?.Code == "g" ? 4 : 2) + ")",
                Q(r.BandForHalf, shown?.Code == "in" || shown?.Code == "g" ? 4 : 2) + " " + u + " (±" + Q(r.BandForHalf / 2, shown?.Code == "in" || shown?.Code == "g" ? 4 : 2) + ")");
        }
        if (_tolGrid.Rows.Count == 0)
            _tolGrid.Rows.Add(_model == null ? Lang.T("Press \"Learn from the archive\".") : Lang.T("No measure has a proven effect on velocity yet: there is no band to give."), "", "", "", "", "");
        SuggestWidth();
    }

    private void FillClassFields()
    {
        string kind = Kinds[Math.Max(0, _classKind.SelectedIndex)].Kind;
        var fields = FieldsFor(kind, CartridgeStore.Pieces(Db, Shooter, kind, includeRetired: false).Select(p => CartridgeStore.Measures(Db, "piece", p.Id))).Select(k => (object)new FieldItem(k)).ToArray();
        foreach (var c in new[] { _classKey1, _classKey2 })
        {
            c.Items.Clear();
            if (c == _classKey2) c.Items.Add(Lang.T("(none)"));
            c.Items.AddRange(fields);
            c.SelectedIndex = 0;
        }
        _classWidth1.Text = "";
        SuggestWidth();
    }

    /// <summary>Pieces may have been added or weighed since the tab was built: the list of measures follows them, without losing what was typed.</summary>
    private void RefreshClassFields()
    {
        string kind = Kinds[Math.Max(0, _classKind.SelectedIndex)].Kind;
        int now = FieldsFor(kind, CartridgeStore.Pieces(Db, Shooter, kind, includeRetired: false).Select(p => CartridgeStore.Measures(Db, "piece", p.Id))).Count;
        if (_classKey1.Items.Count != now) FillClassFields();
    }

    /// <summary>The band of the first criterion, from the model when it has proven that measure.</summary>
    private void SuggestWidth()
    {
        _classHint.Text = "";
        if (_classKey1.SelectedItem is not FieldItem f) return;
        var row = ToleranceBands.From(_model).FirstOrDefault(r => r.Key == f.Key);
        if (row == null) { _classHint.Text = Lang.T("This measure has no proven effect yet: choose the band as you like."); return; }
        var shown = AppUnits.ForMeasure(f.Key);
        string u = shown is { } su ? UnitSystem.Label(su.Code) : MeasureFields.UnitOf(f.Key);
        string Band(double stored) => shown is { } s ? UnitSystem.Show(stored, s.Code, 4) : stored.ToString("0.00", Inv);
        _classHint.Text = string.Format(Inv, Lang.T("For 1 m/s of SD: {0} {2}; for 0.5 m/s: {1} {2}."), Band(row.BandForOne), Band(row.BandForHalf), u);
        if (_classWidth1.Text.Length == 0) _classWidth1.Text = Band(row.BandForOne);
    }

    private void MakeClasses()
    {
        string kind = Kinds[Math.Max(0, _classKind.SelectedIndex)].Kind;
        var criteria = new List<(string, double)>();
        if (_classKey1.SelectedItem is not FieldItem k1 || !CartridgeText.TryParseNumber(_classWidth1.Text, out double w1) || w1 <= 0)
        { Warn(Lang.T("Choose a measure and a band wider than zero.")); return; }
        criteria.Add((k1.Key, AppUnits.MeasureToStored(k1.Key, w1)));                     // the bands are typed in the unit of the measure
        if (_classKey2.SelectedItem is FieldItem k2)
        {
            if (!CartridgeText.TryParseNumber(_classWidth2.Text, out double w2) || w2 <= 0) { Warn(Lang.T("Choose a measure and a band wider than zero.")); return; }
            criteria.Add((k2.Key, AppUnits.MeasureToStored(k2.Key, w2)));
        }
        var pieces = CartridgeStore.Pieces(Db, Shooter, kind, includeRetired: false).Select(p => new ClassPiece(p.Id, p.Mark, CartridgeStore.Measures(Db, "piece", p.Id))).ToList();
        ClassResult res = null!;
        if (!Guard(() => res = MeasureClasses.Build(pieces, criteria))) return;

        _classes.Items.Clear(); _classLines.Clear();
        _classLines.Add(string.Join("\t", new[] { Lang.T("Class"), Lang.T("Pieces"), Lang.T("Range"), Lang.T("Marks") }));
        var cc = Inv;
        foreach (var c in res.Classes)
        {
            string range = string.Join("   ", criteria.Select(cr => Header(cr.Item1) + ": " + AppUnits.ShowMeasure(cr.Item1, c.Ranges[cr.Item1].Min) + " – " + AppUnits.ShowMeasure(cr.Item1, c.Ranges[cr.Item1].Max)));
            string marks = string.Join(" ", c.Pieces.Select(p => p.Mark));
            _classes.Items.Add(new ListViewItem(new[] { c.Label, c.Pieces.Count.ToString(Inv), range, marks }));
            _classLines.Add(string.Join("\t", new[] { c.Label, c.Pieces.Count.ToString(Inv), range, marks }));
        }
        string text = string.Format(Inv, Lang.T("{0} pieces in {1} classes."), res.Classes.Sum(c => c.Pieces.Count), res.Classes.Count);
        if (res.Unclassified.Count > 0)
            text += " " + string.Format(Inv, Lang.T("{0} pieces lack the measure and are left out: {1}."), res.Unclassified.Count, string.Join(" ", res.Unclassified.Select(p => p.Mark)));
        _classText.Text = text;
    }

    // ═══════════════ SD budget ═══════════════

    private Control BuildBudget()
    {
        var pick = new Button { Text = Lang.T("Pick the strings…"), AutoSize = true };
        var latest = new Button { Text = Lang.T("Latest training session"), AutoSize = true };
        latest.Click += (_, _) => LatestTrainingBudget();
        var go = new Button { Text = Lang.T("Compute the budget"), AutoSize = true };
        var copy = new Button { Text = Lang.T("Copy"), AutoSize = true };
        pick.Click += (_, _) => PickBudgetStrings();
        go.Click += (_, _) => ComputeBudget();
        copy.Click += (_, _) => { if (_budgetText.Text.Length > 0) Clipboard.SetText(_budgetText.Text); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(4, 6, 4, 0), WrapContents = true };
        bar.Controls.Add(pick);
        bar.Controls.Add(latest);
        bar.Controls.Add(_budgetStrings);
        bar.SetFlowBreak(_budgetStrings, true);
        bar.Controls.Add(new Label { Text = Lang.T("Chronograph precision (SD, m/s):"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_chronoSd);
        bar.Controls.Add(new Label { Text = Lang.T("Distance of the shooting") + " (" + Measuring.ProgramUnits.Current.DistanceUnitName + "):", AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        bar.Controls.Add(_distance);
        bar.Controls.Add(go);
        bar.Controls.Add(copy);
        _budgetStrings.Text = Lang.T("Pick the strings of one session (a series each): with shots linked to measured cartridges the budget can also say which measure costs what.");
        var root = new Panel { Dock = DockStyle.Fill };
        var halves = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical }.WithDistance(560);
        halves.Panel1.Controls.Add(_budgetText);
        halves.Panel2.Controls.Add(_budgetChart);
        root.Controls.Add(halves);
        root.Controls.Add(bar);
        return root;
    }

    private void PickBudgetStrings()
    {
        using var pick = new ArchivePickForm(_link, ArchivePick.Strings);
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        _budgetIds = pick.PickedStrings.Select(s => s.Id).ToList();
        _budgetStrings.Text = string.Format(Inv, Lang.T("{0} strings of the session of {1} picked."), _budgetIds.Count, pick.Session?.Date ?? "");
        ComputeBudget();
    }

    /// <summary>The strings of the latest session shot with a training lot: where the SD comes from, with the cartridges that were really fired.</summary>
    private void LatestTrainingBudget()
    {
        var ids = CartridgeStore.LatestTrainingStrings(Db, Shooter);
        if (ids.Count == 0) { Warn(Lang.T("No shot fired with a training lot is linked to its cartridges yet. Link the shots of a training session in the \"Check after the shooting\" tab.")); return; }
        _budgetIds = ids;
        _budgetStrings.Text = string.Format(Inv, Lang.T("{0} strings of the latest training session picked."), ids.Count);
        ComputeBudget();
    }

    private void ComputeBudget()
    {
        if (_budgetIds.Count == 0) { Warn(Lang.T("Pick the strings first.")); return; }
        BudgetFor(_budgetIds);
    }

    /// <summary>A measure of the cartridges in the unit chosen for its field (for the texts that name the measure and its unit).</summary>
    private static (double Value, string Unit) MeasureShown(string key, double stored) =>
        AppUnits.ForMeasure(key) is { } u ? (Measuring.UnitSystem.FromBase(stored, u.Code), Measuring.UnitSystem.Label(u.Code)) : (stored, Cartridges.MeasureFields.UnitOf(key));

    private void BudgetFor(IReadOnlyList<long> strings)
    {
        var units = Measuring.ProgramUnits.Current;
        var b = VarianceBudget.Analyze(CartridgeStore.StringSamples(Db, strings), (double)_chronoSd.Value, units.DistanceToMetres((double)_distance.Value));
        _budgetChart.SetData(b);
        _budgetText.Text = b == null
            ? string.Format(Inv, Lang.T("At least {0} shots in series of two or more are needed to say where the SD comes from."), VarianceBudget.MinShots)
            : CartridgeText.BudgetReport(b, (double)_chronoSd.Value, units, MeasureShown);
    }

    // ═══════════════ helpers ═══════════════

    private string? Ask(string caption, string prompt, string value)
    {
        using var d = new Form
        {
            Text = caption, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 104),
        };
        var box = new TextBox { Left = 12, Top = 32, Width = 336, Text = value };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 186, Top = 66, Width = 75 };
        var no = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 270, Top = 66, Width = 75 };
        d.Controls.AddRange(new Control[] { new Label { Text = prompt, Left = 12, Top = 10, AutoSize = true }, box, ok, no });
        d.AcceptButton = ok; d.CancelButton = no;
        GrtTheme.Apply(d);
        return d.ShowDialog(this) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }

    /// <summary>The window drawn for a picture (<c>--anteprima cartucce1…7</c>): opened on one tab, with the learning and the plan already done on the third.</summary>
    public static CartridgesForm Preview(ArchiveLink link, int tab)
    {
        var f = new CartridgesForm(link);
        f._tabs.SelectedIndex = tab;
        if (tab == 0) f.FillPieces();
        if (tab == 1) f._matchPane.Fill();
        if (tab == 2) { f._trainPane.Fill(); f.Shown += (_, _) => f._trainPane.Compare(quiet: true); }
        if (tab == 3) { f.FillPlanLots(); f.Learn(); f.ComputePlan(); }
        if (tab == 4) { f.FillPredictions(); f.ShowPrediction(); }
        if (tab == 5) { f.Learn(); f.FillTolerances(); f.MakeClasses(); }
        if (tab == 6)
        {
            var ids = new List<long>();
            using (var cmd = link.Db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT st.id FROM strings st JOIN shots sh ON sh.string_id=st.id WHERE sh.cartridge_id IS NOT NULL ORDER BY st.id";
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt64(0));
            }
            f._budgetIds = ids;
            f.BudgetFor(ids);
        }
        return f;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FillPieces();
    }
}
