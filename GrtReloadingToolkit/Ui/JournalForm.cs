using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The range-day log on its own: every other tool that reads logged sessions (Inventory's stock/
/// firearm/brass-life tabs, Find best Ba) is a separate window now, so this form only needs to keep
/// its own grid current. Those siblings pick up a new/edited/deleted entry the next time they get
/// focus (see their own Activated handlers), not live from here.
/// </summary>
internal sealed class JournalForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly DataGridView _jrn = new();
    private readonly Label _status = new();

    public JournalForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("Journal"));
        Width = 1000; Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 420);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); return b; }
        B(Lang.T("New"), () => NewEntry(null));
        var g = B(Lang.T("Log from GRT"), async () => await LogFromGrtAsync());
        g.Enabled = _grt is { Connected: true };
        B(Lang.T("Edit"), EditEntry);
        B(Lang.T("Delete"), DeleteEntry);
        B(Lang.T("Fill Ba from loads"), FillBaFromLoads);

        _jrn.Dock = DockStyle.Fill;
        _jrn.ReadOnly = true; _jrn.AllowUserToAddRows = false; _jrn.RowHeadersVisible = false;
        _jrn.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _jrn.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _jrn.CellDoubleClick += (_, _) => EditEntry();
        foreach (var (n, h) in new[] { ("date", Lang.T("Date")), ("load", Lang.T("Load")), ("cal", Lang.T("Caliber")), ("chg", Lang.T("Charge") + " " + GrtUnits.Current.ChargeUnitName),
                     ("rnd", Lang.T("Rounds")), ("mv", "MV " + GrtUnits.Current.VelocityUnitName),
                     ("sd", "SD " + GrtUnits.Current.VelocityUnitName), ("grp", "MOA"), ("cpr", Lang.T("Cost/rd")), ("tot", Lang.T("Total")) })
            _jrn.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.ForeColor = SystemColors.GrayText;
        _status.Padding = new Padding(8, 2, 0, 0);
        _status.Text = $"DB: {_db.Path}   " + (_grt == null ? Lang.T("stand-alone") : _grt.Connected ? $"GRT :{_grt.Port}" : Lang.T("GRT lost"));

        Controls.Add(_jrn);
        Controls.Add(bar);
        Controls.Add(_status);
        NudFix.ApplyTo(this);

        RefreshJournal();

        // Stock, firearm round counts and brass-life all move when a journal entry is saved or
        // deleted, but those now live in a different window -- so pick up anything IT changed
        // (e.g. a component archived from Inventory) the moment this window gets focus back.
        Activated += (_, _) => RefreshJournal();
    }

    private void RefreshJournal()
    {
        _jrn.Rows.Clear();
        var gu = GrtUnits.Current;
        var comps = _db.Components(includeArchived: true).ToDictionary(c => c.Id);
        foreach (var e in _db.Journal())
        {
            var cb = Costing.PerRound(e, id => comps.GetValueOrDefault(id));
            int i = _jrn.Rows.Add(e.Date, e.LoadName, e.Caliber,
                e.ChargeGr > 0 ? gu.ChargeValue(e.ChargeGr).ToString(gu.ChargeFormat, CultureInfo.InvariantCulture) : "",
                e.Rounds,
                e.VelocityAvgMs is { } v ? gu.VelocityValue(v).ToString("0", CultureInfo.InvariantCulture) : "",
                e.SdMs is { } sd ? gu.VelocitySd(sd) : "",
                e.GroupMoa?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                cb.PerRoundText,
                cb.Mixed ? cb.MixedNote
                    : cb.PerRound > 0 && e.Rounds > 0
                        ? (cb.PerRound * e.Rounds).ToString("0.00", CultureInfo.InvariantCulture) + " " + cb.Currency
                        : "");
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
            RefreshJournal();
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
            RefreshJournal();
        }
    }

    private void DeleteEntry()
    {
        if (SelectedEntry is not { } e) return;
        if (MessageBox.Show(this, string.Format(Lang.T("Delete '{0}' ({1})? Deducted stock will be restored."), e.LoadName, e.Date), Lang.T("Delete"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            _db.DeleteEntry(e.Id);
            RefreshJournal();
        }
    }

    /// <summary>
    /// Offers to read the Ba back out of the .grtload files that Ba-less entries already point at,
    /// for a journal written before the Ba column existed. Shows what it found before writing any
    /// of it: an empty Ba can equally mean the user left it empty on purpose.
    /// </summary>
    private void FillBaFromLoads()
    {
        string title = Lang.T("Fill Ba from loads");
        var scan = BaBackfill.Scan(_db);
        var found = scan.Where(c => c.Result == BaBackfill.Result.Found).ToList();

        // Every candidate is accounted for, not only the ones that worked. A load that has moved,
        // or was never calibrated, is exactly what the user needs told -- staying quiet about it
        // would read as "there was nothing else to do".
        var left = scan.Where(c => c.Result != BaBackfill.Result.Found)
            .GroupBy(c => c.Result)
            .Select(g => "  " + g.Count() + " × " + Reason(g.Key))
            .ToList();
        string tail = left.Count == 0 ? "" : "\n\n" + Lang.T("Left alone:") + "\n" + string.Join("\n", left);

        if (found.Count == 0)
        {
            MessageBox.Show(this, Lang.T("No entry could be filled in from its load file.") + tail, title);
            return;
        }

        string list = string.Join("\n", found.Select(c => "  " + c.Date + "  " + c.LoadName + "  " + c.Caliber
            + "  Ba " + c.Ba!.Value.ToString("0.#######", CultureInfo.InvariantCulture)));
        if (MessageBox.Show(this,
                Lang.T("Read Ba back into these journal entries, from the load each one points at?")
                + "\n\n" + list + tail,
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int n = BaBackfill.Apply(_db, found);
        RefreshJournal();
        MessageBox.Show(this, string.Format(Lang.T("Filled in {0}. To undo one, edit that entry and set its Ba back to 0."), n), title);

        static string Reason(BaBackfill.Result r) => r switch
        {
            BaBackfill.Result.NoBaInLoad => Lang.T("the load carries no Ba"),
            BaBackfill.Result.FileMissing => Lang.T("the load file is no longer there"),
            _ => Lang.T("the load file would not open"),
        };
    }

    private async Task LogFromGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Log from GRT"));
                return;
            }
            var snap = LoadSnapshot.FromGrtload(top.file);
            var entry = snap.ToEntry();
            entry.PowderId = _db.ResolvePowderId(snap.PowderName);
            NewEntry(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Log from GRT"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
