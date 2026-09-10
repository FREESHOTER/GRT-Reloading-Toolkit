using System.Globalization;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

internal sealed class LogForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Db _db;

    private readonly DataGridView _inv = new();
    private readonly DataGridView _jrn = new();
    private readonly DataGridView _fa = new();
    private readonly BarrelChart _faChart = new();
    private readonly Label _status = new();

    public LogForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title("GRT Inventory & Load Journal");
        Width = 1080; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildInventoryTab());
        tabs.TabPages.Add(BuildJournalTab());
        tabs.TabPages.Add(BuildFirearmsTab());

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.ForeColor = SystemColors.GrayText;
        _status.Padding = new Padding(8, 2, 0, 0);
        _status.Text = $"DB: {_db.Path}   " + (_grt == null ? "stand-alone" : _grt.Connected ? $"GRT :{_grt.Port}" : "GRT lost");

        Controls.Add(tabs);
        Controls.Add(_status);
        NudFix.ApplyTo(this);

        RefreshInventory();
        RefreshJournal();
        RefreshFirearms();
    }

    // ---- firearms / barrel life -------------------------------------------

    private TabPage BuildFirearmsTab()
    {
        var page = new TabPage("Firearms");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        void B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); }
        B("Add", () => EditFirearm(new Firearm()));
        B("Edit", () => { if (_fa.CurrentRow?.Tag is Firearm f) EditFirearm(f); });
        B("Retire", () => { if (_fa.CurrentRow?.Tag is Firearm f) { f.Retired = true; _db.UpsertFirearm(f); RefreshFirearms(); } });

        _fa.ReadOnly = true; _fa.AllowUserToAddRows = false; _fa.RowHeadersVisible = false;
        _fa.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _fa.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _fa.Dock = DockStyle.Fill;
        _fa.CellDoubleClick += (_, _) => { if (_fa.CurrentRow?.Tag is Firearm f) EditFirearm(f); };
        _fa.SelectionChanged += (_, _) => ShowBarrelChart();
        foreach (var (n, h) in new[] { ("name", "Firearm"), ("cal", "Caliber"), ("tot", "Total rounds"), ("ent", "MV entries"), ("last", "Last used") })
            _fa.Columns.Add(n, h);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 180 };
        split.Panel1.Controls.Add(_fa);
        _faChart.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_faChart);

        page.Controls.Add(split);
        page.Controls.Add(bar);
        return page;
    }

    private void RefreshFirearms()
    {
        _fa.Rows.Clear();
        foreach (var f in _db.Firearms())
        {
            var hist = _db.FirearmMvHistory(f.Id);
            int i = _fa.Rows.Add(f.Name, f.Caliber, _db.FirearmRoundCount(f.Id), hist.Count,
                hist.Count > 0 ? hist[^1].date : "");
            _fa.Rows[i].Tag = f;
        }
        ShowBarrelChart();
    }

    private void ShowBarrelChart()
    {
        _faChart.Show(_fa.CurrentRow?.Tag is Firearm f ? (f.Name, _db.FirearmMvHistory(f.Id)) : (null, null));
    }

    private void EditFirearm(Firearm f)
    {
        using var d = new Form { Text = f.Id == 0 ? "Add firearm" : "Edit firearm", ClientSize = new Size(320, 210), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Left = 110, Top = 12, Width = 190, Text = f.Name };
        var cal = new TextBox { Left = 110, Top = 42, Width = 190, Text = f.Caliber };
        var rb = new NumericUpDown { Left = 110, Top = 72, Width = 90, Maximum = 1_000_000, Value = f.RoundsBefore };
        var notes = new TextBox { Left = 110, Top = 102, Width = 190, Height = 44, Multiline = true, Text = f.Notes };
        void L(string t, int y) => d.Controls.Add(new Label { Text = t, Left = 12, Top = y + 3, AutoSize = true });
        L("Name", 12); L("Caliber", 42); L("Rounds before", 72); L("Notes", 102);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 145, Top = 160, Width = 75 };
        var ca = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 225, Top = 160, Width = 75 };
        d.Controls.AddRange(new Control[] { name, cal, rb, notes, ok, ca });
        d.AcceptButton = ok; d.CancelButton = ca;
        NudFix.ApplyTo(d);
        if (d.ShowDialog(this) == DialogResult.OK && name.Text.Trim().Length > 0)
        {
            f.Name = name.Text.Trim(); f.Caliber = cal.Text.Trim(); f.RoundsBefore = (int)rb.Value; f.Notes = notes.Text.Trim();
            _db.UpsertFirearm(f);
            RefreshFirearms();
        }
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    // ---- inventory ------------------------------------------------------

    private TabPage BuildInventoryTab()
    {
        var page = new TabPage("Inventory");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); return b; }
        B("Add", AddComponent);
        B("Edit", EditComponent);
        B("Restock", RestockComponent);
        B("Archive", ArchiveComponent);

        _inv.Dock = DockStyle.Fill;
        _inv.ReadOnly = true; _inv.AllowUserToAddRows = false; _inv.RowHeadersVisible = false;
        _inv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _inv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _inv.CellDoubleClick += (_, _) => EditComponent();
        foreach (var (n, h) in new[] { ("k", "Kind"), ("d", "Component"), ("qty", "Left"), ("pct", "%"), ("cu", "Cost/unit"), ("note", "Notes") })
            _inv.Columns.Add(n, h);

        page.Controls.Add(_inv);
        page.Controls.Add(bar);
        return page;
    }

    private void RefreshInventory()
    {
        _inv.Rows.Clear();
        foreach (var c in _db.Components())
        {
            int i = _inv.Rows.Add(c.Kind.ToString(), c.Display,
                $"{c.QtyCurrent:0.#} {c.Unit}",
                $"{c.FractionRemaining * 100:0}",
                c.CostPerUnit > 0 ? $"{c.CostPerUnit:0.0000} {c.Currency}/{(c.Kind == ComponentKind.Powder ? "g" : "pc")}" : "",
                c.Notes);
            _inv.Rows[i].Tag = c;
            if (c.FractionRemaining <= 0.10) _inv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
            else if (c.FractionRemaining <= 0.25) _inv.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
        }
    }

    private Component? SelectedComponent => _inv.CurrentRow?.Tag as Component;

    private void AddComponent()
    {
        using var d = new ComponentDialog(new Component());
        if (d.ShowDialog(this) == DialogResult.OK) { _db.UpsertComponent(d.Result); RefreshInventory(); }
    }

    private void EditComponent()
    {
        if (SelectedComponent is not { } c) return;
        using var d = new ComponentDialog(c);
        if (d.ShowDialog(this) == DialogResult.OK) { _db.UpsertComponent(d.Result); RefreshInventory(); }
    }

    private void RestockComponent()
    {
        if (SelectedComponent is not { } c) return;
        string? s = Prompt($"Add how many {c.Unit} to '{c.Display}'? (negative to correct down)");
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            _db.AdjustStock(c.Id, d, "manual restock");
            RefreshInventory();
        }
    }

    private void ArchiveComponent()
    {
        if (SelectedComponent is not { } c) return;
        c.Archived = true;
        _db.UpsertComponent(c);
        RefreshInventory();
    }

    // ---- journal -------------------------------------------------------

    private TabPage BuildJournalTab()
    {
        var page = new TabPage("Journal");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); return b; }
        B("New", () => NewEntry(null));
        var g = B("Log from GRT", async () => await LogFromGrtAsync());
        g.Enabled = _grt is { Connected: true };
        B("Edit", EditEntry);
        B("Delete", DeleteEntry);

        _jrn.Dock = DockStyle.Fill;
        _jrn.ReadOnly = true; _jrn.AllowUserToAddRows = false; _jrn.RowHeadersVisible = false;
        _jrn.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _jrn.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _jrn.CellDoubleClick += (_, _) => EditEntry();
        foreach (var (n, h) in new[] { ("date", "Date"), ("load", "Load"), ("cal", "Caliber"), ("chg", "Charge gr"),
                     ("rnd", "Rounds"), ("mv", "MV"), ("sd", "SD"), ("grp", "MOA"), ("cpr", "Cost/rd"), ("tot", "Total") })
            _jrn.Columns.Add(n, h);

        page.Controls.Add(_jrn);
        page.Controls.Add(bar);
        return page;
    }

    private void RefreshJournal()
    {
        _jrn.Rows.Clear();
        var comps = _db.Components(includeArchived: true).ToDictionary(c => c.Id);
        foreach (var e in _db.Journal())
        {
            var cb = Costing.PerRound(e, id => comps.GetValueOrDefault(id));
            int i = _jrn.Rows.Add(e.Date, e.LoadName, e.Caliber,
                e.ChargeGr > 0 ? e.ChargeGr.ToString("0.00", CultureInfo.InvariantCulture) : "",
                e.Rounds,
                e.VelocityAvgMs?.ToString("0", CultureInfo.InvariantCulture) ?? "",
                e.SdMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
                e.GroupMoa?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                cb.PerRound > 0 ? $"{cb.PerRound:0.000} {cb.Currency}" : "",
                cb.PerRound > 0 && e.Rounds > 0 ? $"{cb.PerRound * e.Rounds:0.00} {cb.Currency}" : "");
            _jrn.Rows[i].Tag = e;
        }
    }

    private JournalEntry? SelectedEntry => _jrn.CurrentRow?.Tag as JournalEntry;

    private void NewEntry(JournalEntry? seed)
    {
        var e = seed ?? new JournalEntry();
        using var d = new JournalDialog(e, _db.Components());
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            d.Result.FirearmId = _db.ResolveFirearm(d.Result.Firearm, d.Result.Caliber) is var fid && fid > 0 ? fid : null;
            _db.SaveEntry(d.Result, d.ApplyStock);
            RefreshJournal(); RefreshInventory(); RefreshFirearms();
        }
    }

    private void EditEntry()
    {
        if (SelectedEntry is not { } e) return;
        using var d = new JournalDialog(e, _db.Components());
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            d.Result.FirearmId = _db.ResolveFirearm(d.Result.Firearm, d.Result.Caliber) is var fid && fid > 0 ? fid : null;
            _db.SaveEntry(d.Result, d.Result.StockApplied || d.ApplyStock);
            RefreshJournal(); RefreshInventory(); RefreshFirearms();
        }
    }

    private void DeleteEntry()
    {
        if (SelectedEntry is not { } e) return;
        if (MessageBox.Show(this, $"Delete '{e.LoadName}' ({e.Date})? Deducted stock will be restored.", "Delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            _db.DeleteEntry(e.Id);
            RefreshJournal(); RefreshInventory();
        }
    }

    private async Task LogFromGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, "No saved load is open in GRT.", "Log from GRT");
                return;
            }
            var snap = LoadSnapshot.FromGrtload(top.file);
            NewEntry(snap.ToEntry());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Log from GRT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? Prompt(string text)
    {
        using var f = new Form { Text = text, ClientSize = new Size(320, 90), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var tb = new TextBox { Left = 12, Top = 12, Width = 296 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 148, Top = 46, Width = 75 };
        var ca = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 233, Top = 46, Width = 75 };
        f.Controls.AddRange(new Control[] { tb, ok, ca });
        f.AcceptButton = ok; f.CancelButton = ca;
        return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
    }
}
