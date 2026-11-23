using System.Globalization;
using GrtPluginKit.Ipc;
using GrtReloadingLog.Data;
using GrtReloadingLog.Grt;

namespace GrtReloadingLog.Ui;

internal sealed class MainForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Db _db;

    private readonly DataGridView _inv = new();
    private readonly DataGridView _jrn = new();
    private readonly Label _status = new();

    public MainForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = "GRT Inventory & Load Journal  (v0.1)";
        Width = 1080; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildInventoryTab());
        tabs.TabPages.Add(BuildJournalTab());

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.ForeColor = SystemColors.GrayText;
        _status.Padding = new Padding(8, 2, 0, 0);
        _status.Text = $"DB: {_db.Path}   " + (_grt == null ? "stand-alone" : _grt.Connected ? $"GRT :{_grt.Port}" : "GRT lost");

        Controls.Add(tabs);
        Controls.Add(_status);

        RefreshInventory();
        RefreshJournal();
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
            _db.SaveEntry(d.Result, d.ApplyStock);
            RefreshJournal(); RefreshInventory();
        }
    }

    private void EditEntry()
    {
        if (SelectedEntry is not { } e) return;
        using var d = new JournalDialog(e, _db.Components());
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            _db.SaveEntry(d.Result, d.Result.StockApplied || d.ApplyStock);
            RefreshJournal(); RefreshInventory();
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
