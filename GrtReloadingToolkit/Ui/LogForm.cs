using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

internal sealed class LogForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Db _db;

    private readonly DataGridView _inv = new();
    private readonly DataGridView _fa = new();
    private readonly BarrelChart _faChart = new();
    private readonly DataGridView _bl = new();
    private readonly Label _status = new();

    public LogForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Inventory"));
        Width = 1080; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildInventoryTab());
        tabs.TabPages.Add(BuildFirearmsTab());
        tabs.TabPages.Add(BuildBrassLifeTab());

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.ForeColor = SystemColors.GrayText;
        _status.Padding = new Padding(8, 2, 0, 0);
        _status.Text = $"DB: {_db.Path}   " + (_grt == null ? Lang.T("stand-alone") : _grt.Connected ? $"GRT :{_grt.Port}" : Lang.T("GRT lost"));

        Controls.Add(tabs);
        Controls.Add(_status);
        NudFix.ApplyTo(this);

        RefreshInventory();
        RefreshFirearms();
        RefreshBrassLife();

        // Stock, firearm round counts and brass-life all move when a journal entry is logged in the
        // separate Journal window -- pick that up whenever this window gets focus back.
        Activated += (_, _) => { RefreshInventory(); RefreshFirearms(); RefreshBrassLife(); };
    }

    // ---- brass life -------------------------------------------------------

    /// <summary>
    /// Firing count + anneal reminder per brass lot, built from rounds already logged against
    /// <see cref="JournalEntry.BrassId"/> — see <see cref="BrassLife"/> for why this needed no new
    /// per-shot log. Same tab shape as <see cref="BuildFirearmsTab"/> (a lifecycle question about one
    /// kind of owned thing), deliberately without that tab's chart half: a firing COUNT is the whole
    /// question here, there is no "value over time" to plot the way MV-drift is for a barrel.
    /// </summary>
    private TabPage BuildBrassLifeTab()
    {
        var page = new TabPage(Lang.T("Brass Life"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        var annealBtn = new Button { Text = Lang.T("Mark annealed now"), AutoSize = true };
        annealBtn.Click += (_, _) =>
        {
            if (_bl.CurrentRow?.Tag is Component c && _db.MarkBrassAnnealed(c.Id)) RefreshBrassLife();
        };
        bar.Controls.Add(annealBtn);

        _bl.ReadOnly = true; _bl.AllowUserToAddRows = false; _bl.RowHeadersVisible = false;
        _bl.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _bl.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _bl.Dock = DockStyle.Fill;
        foreach (var (n, h) in new[] { ("lot", Lang.T("Lot")), ("pieces", Lang.T("Pieces")), ("rounds", Lang.T("Rounds fired")),
                     ("avg", Lang.T("Avg uses/case")), ("since", Lang.T("Since last anneal")), ("every", Lang.T("Anneal every")),
                     ("status", Lang.T("Status")) })
            _bl.Columns.Add(n, h);

        page.Controls.Add(_bl);
        page.Controls.Add(bar);
        return page;
    }

    private void RefreshBrassLife()
    {
        _bl.Rows.Clear();
        foreach (var c in _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Brass))
        {
            var s = BrassLife.Compute(c, _db.BrassRoundsFired(c.Id));
            string status = s.RetirementDue ? Lang.T("Retire") : s.AnnealDue ? Lang.T("Anneal due") : Lang.T("OK");
            int i = _bl.Rows.Add(c.Display, s.Pieces, s.TotalRoundsFired, s.AvgUsesPerCase, s.UsesSinceAnneal,
                c.AnnealEveryUses?.ToString() ?? "–", status);
            _bl.Rows[i].Tag = c;
            if (s.RetirementDue) _bl.Rows[i].DefaultCellStyle.ForeColor = Color.DarkRed;
            else if (s.AnnealDue) _bl.Rows[i].DefaultCellStyle.ForeColor = Color.DarkOrange;
        }
    }

    // ---- firearms / barrel life -------------------------------------------

    private TabPage BuildFirearmsTab()
    {
        var page = new TabPage(Lang.T("Firearms"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        void B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); }
        B(Lang.T("Add"), () => EditFirearm(new Firearm()));
        B(Lang.T("Edit"), () => { if (_fa.CurrentRow?.Tag is Firearm f) EditFirearm(f); });
        B(Lang.T("Retire"), () => { if (_fa.CurrentRow?.Tag is Firearm f) { f.Retired = true; _db.UpsertFirearm(f); RefreshFirearms(); } });

        _fa.ReadOnly = true; _fa.AllowUserToAddRows = false; _fa.RowHeadersVisible = false;
        _fa.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _fa.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _fa.Dock = DockStyle.Fill;
        _fa.CellDoubleClick += (_, _) => { if (_fa.CurrentRow?.Tag is Firearm f) EditFirearm(f); };
        _fa.SelectionChanged += (_, _) => ShowBarrelChart();
        foreach (var (n, h) in new[] { ("name", Lang.T("Firearm")), ("cal", Lang.T("Caliber")), ("tot", Lang.T("Total rounds")), ("ent", Lang.T("MV entries")), ("last", Lang.T("Last used")) })
            _fa.Columns.Add(n, h);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(180);
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
        _faChart.SetData(_fa.CurrentRow?.Tag is Firearm f ? (f.Name, _db.FirearmMvHistory(f.Id)) : (null, null));
    }

    private void EditFirearm(Firearm f)
    {
        using var d = new Form { Text = Lang.T(f.Id == 0 ? "Add firearm" : "Edit firearm"), ClientSize = new Size(320, 210), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Left = 110, Top = 12, Width = 190, Text = f.Name };
        var cal = new TextBox { Left = 110, Top = 42, Width = 190, Text = f.Caliber };
        var rb = new NumericUpDown { Left = 110, Top = 72, Width = 90, Maximum = 1_000_000, Value = f.RoundsBefore };
        var notes = new TextBox { Left = 110, Top = 102, Width = 190, Height = 44, Multiline = true, Text = f.Notes };
        void L(string t, int y) => d.Controls.Add(new Label { Text = t, Left = 12, Top = y + 3, AutoSize = true });
        L(Lang.T("Name"), 12); L(Lang.T("Caliber"), 42); L(Lang.T("Rounds before"), 72); L(Lang.T("Notes"), 102);
        var ok = new Button { Text = Lang.T("Save"), DialogResult = DialogResult.OK, Left = 145, Top = 160, Width = 75 };
        var ca = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 225, Top = 160, Width = 75 };
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
        var page = new TabPage(Lang.T("Inventory"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); return b; }
        B(Lang.T("Add"), AddComponent);
        B(Lang.T("Edit"), EditComponent);
        B(Lang.T("Restock"), RestockComponent);
        B(Lang.T("Archive"), ArchiveComponent);

        _inv.Dock = DockStyle.Fill;
        _inv.ReadOnly = true; _inv.AllowUserToAddRows = false; _inv.RowHeadersVisible = false;
        _inv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _inv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _inv.CellDoubleClick += (_, _) => EditComponent();
        foreach (var (n, h) in new[] { ("k", Lang.T("Kind")), ("d", Lang.T("Component")), ("qty", Lang.T("Left")), ("pct", "%"), ("cu", Lang.T("Cost/unit")), ("note", Lang.T("Notes")) })
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
            int i = _inv.Rows.Add(c.Kind.ToString(), c.Display + c.BarrelSpec,
                c.QtyLeftText,
                (c.FractionRemaining * 100).ToString("0", CultureInfo.InvariantCulture),
                c.CostPerUnit > 0
                    ? c.CostPerUnitIn.ToString("0.0000", CultureInfo.InvariantCulture) + $" {c.Currency}/{c.Unit}"
                    : "",
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
        if (d.ShowDialog(this) == DialogResult.OK) { _db.UpsertComponent(d.Result); RefreshInventory(); RefreshBrassLife(); }
    }

    private void EditComponent()
    {
        if (SelectedComponent is not { } c) return;
        using var d = new ComponentDialog(c);
        if (d.ShowDialog(this) == DialogResult.OK) { _db.UpsertComponent(d.Result); RefreshInventory(); RefreshBrassLife(); }
    }

    private void RestockComponent()
    {
        if (SelectedComponent is not { } c) return;
        string? s = Prompt(string.Format(Lang.T("Add how many {0} to '{1}'? (negative to correct down)"), c.Unit, c.Display));
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            // The prompt asked in the unit the lot is counted in; the ledger moves stored units.
            _db.AdjustStock(c.Id, StockUnit.ToStore(d, c.Unit), "manual restock");
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

    private static string? Prompt(string text)
    {
        using var f = new Form { Text = text, ClientSize = new Size(320, 90), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var tb = new TextBox { Left = 12, Top = 12, Width = 296 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 148, Top = 46, Width = 75 };
        var ca = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 233, Top = 46, Width = 75 };
        f.Controls.AddRange(new Control[] { tb, ok, ca });
        f.AcceptButton = ok; f.CancelButton = ca;
        return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
    }
}
