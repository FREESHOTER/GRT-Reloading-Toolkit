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
    private readonly ComboBox _psFirearm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _psPowder = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly DataGridView _ps = new();
    private readonly PressureSignChart _psChart = new();
    private readonly Label _psFindings = new() { Dock = DockStyle.Bottom, Height = 60, AutoEllipsis = true };
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
        tabs.TabPages.Add(BuildPressureSignsTab());

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
        RefreshPressureSignFirearms();

        // Stock, firearm round counts and brass-life all move when a journal entry is logged in the
        // separate Journal window -- pick that up whenever this window gets focus back.
        Activated += (_, _) => { RefreshInventory(); RefreshFirearms(); RefreshBrassLife(); RefreshPressureSignFirearms(); };
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

    // ---- pressure signs (case-head expansion) ------------------------------

    private readonly PressureSignConfig _psCfg = new();
    private sealed record PsOption(long Id, string Text) { public override string ToString() => Text; }

    /// <summary>
    /// Manual fired-case head-diameter log per firearm+powder, with <see cref="PressureSign"/>'s
    /// acceleration check run on whatever ladder is currently filtered in -- see
    /// <see cref="CaseHeadMeasurement"/> for why this needed a brand new log rather than reusing the
    /// Journal (a reading is taken case-in-hand at the bench, often for a charge that never gets a
    /// full logged session at all, e.g. one abandoned mid-ladder for showing early pressure).
    /// </summary>
    private TabPage BuildPressureSignsTab()
    {
        var page = new TabPage(Lang.T("Pressure Signs"));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        void L(string t) => bar.Controls.Add(new Label { Text = t, AutoSize = true, Margin = new Padding(6, 9, 2, 0) });
        L(Lang.T("Firearm")); bar.Controls.Add(_psFirearm);
        L(Lang.T("Powder")); bar.Controls.Add(_psPowder);
        void B(string t, Action a) { var b = new Button { Text = t, AutoSize = true, Margin = new Padding(10, 6, 0, 0) }; b.Click += (_, _) => a(); bar.Controls.Add(b); }
        B(Lang.T("Add"), () => EditCaseHeadMeasurement(new CaseHeadMeasurement()));
        B(Lang.T("Edit"), () => { if (_ps.CurrentRow?.Tag is CaseHeadMeasurement m) EditCaseHeadMeasurement(m); });
        B(Lang.T("Delete"), () =>
        {
            if (_ps.CurrentRow?.Tag is CaseHeadMeasurement m &&
                MessageBox.Show(this, Lang.T("Delete this reading?"), Lang.T("Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { _db.DeleteCaseHeadMeasurement(m.Id); RefreshPressureSignPowders(); }
        });

        _psFirearm.SelectedIndexChanged += (_, _) => RefreshPressureSignPowders();
        _psPowder.SelectedIndexChanged += (_, _) => RefreshPressureSignGrid();

        _ps.ReadOnly = true; _ps.AllowUserToAddRows = false; _ps.RowHeadersVisible = false;
        _ps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _ps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _ps.CellDoubleClick += (_, _) => { if (_ps.CurrentRow?.Tag is CaseHeadMeasurement m) EditCaseHeadMeasurement(m); };
        foreach (var (n, h) in new[] { ("date", Lang.T("Date")), ("chg", Lang.T("Charge")), ("dia", Lang.T("Head Ø (0.200\" line)")), ("note", Lang.T("Notes")) })
            _ps.Columns.Add(n, h);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(220);
        split.Panel1.Controls.Add(_ps);
        _psChart.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_psChart);

        _psFindings.ForeColor = SystemColors.GrayText;
        _psFindings.Padding = new Padding(8, 4, 8, 4);

        page.Controls.Add(split);
        page.Controls.Add(_psFindings);
        page.Controls.Add(bar);
        return page;
    }

    /// <summary>Re-lists every firearm (including retired ones -- past pressure-sign history stays
    /// relevant even for a firearm no longer in use) whenever this window regains focus, since a
    /// firearm may have been added/renamed from the Firearms tab in the meantime.</summary>
    private void RefreshPressureSignFirearms()
    {
        long? keep = (_psFirearm.SelectedItem as PsOption)?.Id;
        _psFirearm.Items.Clear();
        foreach (var f in _db.Firearms(includeRetired: true)) _psFirearm.Items.Add(new PsOption(f.Id, f.Name));
        if (_psFirearm.Items.Count == 0) { _psPowder.Items.Clear(); RefreshPressureSignGrid(); return; }
        var opts = _psFirearm.Items.Cast<PsOption>().ToList();
        _psFirearm.SelectedIndex = keep is { } id ? Math.Max(0, opts.FindIndex(o => o.Id == id)) : 0;
    }

    /// <summary>Only offers powders that already have at least one reading for the selected firearm --
    /// <see cref="EditCaseHeadMeasurement"/> is where a brand-new powder gets its first one.</summary>
    private void RefreshPressureSignPowders()
    {
        long? keep = (_psPowder.SelectedItem as PsOption)?.Id;
        _psPowder.Items.Clear();
        if (_psFirearm.SelectedItem is PsOption fa)
        {
            var powders = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Powder).ToDictionary(c => c.Id);
            foreach (long pid in _db.CaseHeadPowderIds(fa.Id))
                if (powders.TryGetValue(pid, out var p)) _psPowder.Items.Add(new PsOption(pid, p.Display));
        }
        if (_psPowder.Items.Count == 0) { RefreshPressureSignGrid(); return; }
        var opts = _psPowder.Items.Cast<PsOption>().ToList();
        _psPowder.SelectedIndex = keep is { } id ? Math.Max(0, opts.FindIndex(o => o.Id == id)) : 0;
    }

    private void RefreshPressureSignGrid()
    {
        _ps.Rows.Clear();
        var u = GrtUnits.Current;
        if (_psFirearm.SelectedItem is not PsOption fa || _psPowder.SelectedItem is not PsOption pw)
        {
            _psChart.SetData(null, null);
            _psFindings.Text = _psFirearm.Items.Count == 0
                ? Lang.T("Add a firearm in the Firearms tab first.")
                : Lang.T("No readings logged yet for this firearm. Click Add to log the first one.");
            return;
        }

        var measurements = _db.CaseHeadMeasurements(fa.Id, pw.Id);
        foreach (var m in measurements)
        {
            int i = _ps.Rows.Add(m.Date, u.Charge(m.ChargeGr), u.Length(m.HeadDiameterMm), m.Notes);
            _ps.Rows[i].Tag = m;
        }

        var series = PressureSign.BuildSeries(measurements);
        var flags = PressureSign.Diagnose(series, _psCfg);
        _psChart.SetData(series, flags);

        if (series.Count < _psCfg.MinPoints)
            _psFindings.Text = string.Format(Lang.T("Not enough distinct charges yet for a trend ({0} of {1} needed)."), series.Count, _psCfg.MinPoints);
        else if (flags.Count == 0)
            _psFindings.Text = Lang.T("No accelerating expansion detected across this ladder.");
        else
            // The ratio is pre-formatted with FormattableString.Invariant before it ever reaches
            // string.Format: a {2:0.0} format SPEC inside a template string.Format only sees AFTER
            // Lang.T has already picked the Italian text would render with a comma decimal on this
            // user's own Italian-locale Windows -- a real bug caught live (screenshot showed "5,0x"),
            // not by CultureFormattingTests, whose scanner only looks at $"..." interpolations and
            // has no way to see into a runtime template string coming out of Lang.T.
            _psFindings.Text = string.Join("   ", flags.Select(f => string.Format(
                Lang.T("⚠ {0}→{1}: expansion rate {2}× the ladder's own prior average."),
                u.Charge(f.FromChargeGr), u.Charge(f.ToChargeGr), FormattableString.Invariant($"{f.Ratio:0.0}"))));
    }

    private void EditCaseHeadMeasurement(CaseHeadMeasurement m)
    {
        if (_psFirearm.SelectedItem is not PsOption fa) return;
        var powders = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Powder).ToList();
        if (powders.Count == 0) { MessageBox.Show(this, Lang.T("Add a powder in the Inventory tab first."), Text); return; }

        var u = GrtUnits.Current;
        using var d = new Form { Text = Lang.T(m.Id == 0 ? "Add reading" : "Edit reading"), ClientSize = new Size(400, 250), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var date = new TextBox { Left = 155, Top = 12, Width = 190, Text = m.Id == 0 ? DateTime.Now.ToString("yyyy-MM-dd") : m.Date };
        var powder = new ComboBox { Left = 155, Top = 42, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in powders) powder.Items.Add(new PsOption(p.Id, p.Display));
        powder.SelectedIndex = 0;
        long? preselectPowder = m.PowderId ?? (_psPowder.SelectedItem as PsOption)?.Id;
        if (preselectPowder is { } ppid)
        {
            int idx = powder.Items.Cast<PsOption>().ToList().FindIndex(o => o.Id == ppid);
            if (idx >= 0) powder.SelectedIndex = idx;
        }
        var charge = new TextBox { Left = 155, Top = 72, Width = 100, Text = m.Id == 0 ? "" : u.ChargeValue(m.ChargeGr).ToString("0.00", CultureInfo.InvariantCulture) };
        var chargeUnit = new Label { Left = 261, Top = 75, AutoSize = true, Text = u.ChargeUnitName };
        var dia = new TextBox { Left = 155, Top = 102, Width = 100, Text = m.Id == 0 ? "" : u.LengthValue(m.HeadDiameterMm).ToString("0.0000", CultureInfo.InvariantCulture) };
        var diaUnit = new Label { Left = 261, Top = 105, AutoSize = true, Text = u.LengthUnitName };
        var notes = new TextBox { Left = 155, Top = 132, Width = 190, Height = 50, Multiline = true, Text = m.Notes };
        void L(string t, int y) => d.Controls.Add(new Label { Text = t, Left = 12, Top = y + 3, Width = 138, AutoSize = false });
        L(Lang.T("Date"), 12); L(Lang.T("Powder"), 42); L(Lang.T("Charge"), 72); L(Lang.T("Head Ø (0.200\" line)"), 100);
        var ok = new Button { Text = Lang.T("Save"), DialogResult = DialogResult.OK, Left = 225, Top = 200, Width = 75 };
        var ca = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 305, Top = 200, Width = 75 };
        d.Controls.AddRange(new Control[] { date, powder, charge, chargeUnit, dia, diaUnit, notes, ok, ca });
        d.AcceptButton = ok; d.CancelButton = ca;
        NudFix.ApplyTo(d);

        if (d.ShowDialog(this) == DialogResult.OK &&
            double.TryParse(charge.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double chgShown) &&
            double.TryParse(dia.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double diaShown) &&
            powder.SelectedItem is PsOption pw)
        {
            m.Date = date.Text.Trim();
            m.FirearmId = fa.Id;
            m.PowderId = pw.Id;
            m.ChargeGr = u.ChargeToGrains(chgShown);
            m.HeadDiameterMm = u.LengthToMm(diaShown);
            m.Notes = notes.Text.Trim();
            _db.UpsertCaseHeadMeasurement(m);
            RefreshPressureSignPowders();
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
