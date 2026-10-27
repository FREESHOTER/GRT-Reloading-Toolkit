using System.Globalization;
using System.Text;
using GrtReloadingToolkit.Log;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Segui un carico: the loads the shooter follows over time (one per caliber, or several charges of one), and for the one picked only its own
/// history: the outings, the load over time, the effect of temperature and whether the latest outing is normal for it. It has its own file of
/// data and nothing else to set up; GRT is only needed to read an outing from the load that is open and to leave a note and charts in it.
/// </summary>
internal sealed class FollowLoadForm : Form
{
    private const string NoteTitle = "Segui un carico";

    private readonly GrtClient? _grt;
    private readonly TrackerDb _db;
    private readonly GrtUnits _u = GrtUnits.Current;

    private readonly TextBox _filter = new() { Dock = DockStyle.Top };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly FollowLoadChart _chart = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 9.5f), BackColor = Color.White };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = SystemColors.GrayText, Padding = new Padding(8, 2, 0, 0) };
    private readonly Label _title = new() { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Padding = new Padding(4, 2, 0, 0) };
    private readonly List<Button> _needsLoad = new();
    private FollowUpResult? _last;
    private bool _refreshing;

    public FollowLoadForm(GrtClient? grt, TrackerDb db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title("Segui un carico");
        Width = 1000; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 460);

        // a sinistra: quale carico
        var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(6) };
        var follow = new Button { Text = "Segui un carico…", Dock = DockStyle.Top, Height = 30 };
        follow.Click += async (_, _) => await FollowNewAsync();
        var delete = new Button { Text = "Elimina il carico", Dock = DockStyle.Bottom, Height = 28 };
        delete.Click += (_, _) => DeleteLoad();
        var edit = new Button { Text = "Modifica il carico…", Dock = DockStyle.Bottom, Height = 28 };
        edit.Click += (_, _) => EditLoad();
        _needsLoad.AddRange(new[] { delete, edit });
        var filterLabel = new Label { Text = "Filtro (nome, calibro, carica)", Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 6, 0, 0) };
        _filter.TextChanged += (_, _) => RefreshLoads(SelectedLoad?.Id);
        _list.SelectedIndexChanged += (_, _) => { if (!_refreshing) RefreshSelected(); };
        left.Controls.Add(_list);
        left.Controls.Add(_filter);
        left.Controls.Add(filterLabel);
        left.Controls.Add(follow);
        left.Controls.Add(edit);
        left.Controls.Add(delete);

        // a destra: barra e schede
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4, 4, 0, 0) };
        Button B(string t, Func<Task> a, bool needsLoad = true)
        {
            var b = new Button { Text = t, AutoSize = true };
            b.Click += async (_, _) => await a();
            bar.Controls.Add(b);
            if (needsLoad) _needsLoad.Add(b);
            return b;
        }
        B("Nuova uscita…", () => { if (SelectedLoad is { } l) NewOuting(l, Seed(l)); return Task.CompletedTask; });
        var fromGrt = B("Registra da GRT", FromGrtAsync, needsLoad: false);
        fromGrt.Enabled = _grt is { Connected: true };
        B("Modifica uscita", () => { EditOuting(); return Task.CompletedTask; });
        B("Elimina uscita", () => { DeleteOuting(); return Task.CompletedTask; });
        var toGrt = B("Scrivi nota e grafici nel carico GRT", WriteToGrtAsync);
        toGrt.Enabled = _grt is { Connected: true };
        _writeBtn = toGrt;

        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.CellDoubleClick += (_, _) => EditOuting();
        foreach (var (n, h) in new[]
                 {
                     ("date", "Data"), ("temp", "Temperatura"), ("n", "Colpi"), ("mv", "MV " + _u.VelocityUnitName), ("sd", "SD"), ("es", "ES"),
                     ("grp", "MOA"), ("dist", "Distanza " + _u.DistanceUnitName), ("wind", "Vento"), ("notes", "Note"),
                 })
            _grid.Columns.Add(n, h);

        foreach (var m in new[] { "Velocità media nel tempo", "SD ed ES nel tempo", "Velocità contro temperatura", "Gruppo nel tempo" }) _mode.Items.Add(m);
        _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += (_, _) => RefreshSelected();
        var chartPanel = new Panel { Dock = DockStyle.Fill };
        chartPanel.Controls.Add(_chart);
        chartPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4, 4, 0, 0), Controls = { _mode } });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tOut = new TabPage("Uscite") { Controls = { _grid } };
        var tTrend = new TabPage("Nel tempo") { Controls = { chartPanel } };
        var tVerdict = new TabPage("Cosa dice") { Controls = { _report } };
        tabs.TabPages.AddRange(new[] { tOut, tTrend, tVerdict });
        tabs.SelectedTab = tVerdict;

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(tabs);
        right.Controls.Add(bar);
        right.Controls.Add(_title);

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(_status);
        NudFix.ApplyTo(this);

        RefreshLoads(null);
    }

    private readonly Button _writeBtn;

    private TrackedLoad? SelectedLoad => _list.SelectedItem as TrackedLoad;

    // ---- l'elenco dei carichi seguiti

    private void RefreshLoads(long? keep)
    {
        _refreshing = true;
        try
        {
            var tokens = _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _list.Items.Clear();
            foreach (var l in _db.Loads())
            {
                string hay = $"{l.Name} {l.Caliber} {l.Bullet} {l.Powder} {l.ChargeGr.ToString("0.0#", CultureInfo.InvariantCulture)} {l.Notes}";
                if (tokens.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) _list.Items.Add(l);
            }
            for (int i = 0; i < _list.Items.Count; i++)
                if (((TrackedLoad)_list.Items[i]!).Id == keep) { _list.SelectedIndex = i; break; }
            if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }
        finally { _refreshing = false; }
        RefreshSelected();
    }

    private async Task FollowNewAsync()
    {
        var l = new TrackedLoad();
        // con un carico aperto in GRT si parte da quello: calibro, carica, proiettile e polvere sono ciò che il riconoscimento confronta
        if (_grt is { Connected: true })
        {
            try
            {
                var open = await ReadOpenLoadAsync();
                if (open is not null)
                {
                    // a ladder has the charge of its recipe and ten chronographed ones, and the load to follow is one of them: ask which
                    // (the one nearest the recipe is offered), instead of following a charge nobody shot
                    var set = open.Sets.Count switch { 0 => null, 1 => open.Sets[0], _ => PickCharge(open, open.RecipeChargeGr, "Quale carica del carico vuoi seguire?") };
                    l.Name = open.Name; l.Caliber = open.Caliber; l.Bullet = open.Bullet; l.Powder = open.Powder;
                    l.ChargeGr = set?.ChargeGr ?? open.RecipeChargeGr ?? 0;
                }
            }
            catch (Exception) { /* un modulo vuoto è la via di ripiego */ }
        }
        SaveNewLoad(l);
    }

    private TrackedLoad? SaveNewLoad(TrackedLoad l)
    {
        using var d = new FollowedLoadDialog(l);
        if (d.ShowDialog(this) != DialogResult.OK) return null;
        _db.SaveLoad(d.Result);
        RefreshLoads(d.Result.Id);
        return d.Result;
    }

    private void EditLoad()
    {
        if (SelectedLoad is not { } l) return;
        using var d = new FollowedLoadDialog(l);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveLoad(d.Result);
        RefreshLoads(d.Result.Id);
    }

    private void DeleteLoad()
    {
        if (SelectedLoad is not { } l) return;
        int n = _db.OutingsOf(l.Id).Count;
        if (MessageBox.Show(this, $"Eliminare il carico «{l.Name}» e le sue {n} uscite? Non si può annullare.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _db.DeleteLoad(l.Id);
        RefreshLoads(null);
    }

    // ---- il carico scelto

    private void RefreshSelected()
    {
        var l = SelectedLoad;
        foreach (var b in _needsLoad) b.Enabled = l is not null;
        if (_grt is not { Connected: true }) _writeBtn.Enabled = false;
        long keepOuting = (_grid.CurrentRow?.Tag as Outing)?.Id ?? 0;
        _grid.Rows.Clear();
        _last = null;
        if (l is null)
        {
            _title.Text = "Nessun carico seguito";
            _report.Text = "Premi «Segui un carico…». Se un carico è aperto in GRT è il punto di partenza; altrimenti scrivi calibro, proiettile, polvere e carica.";
            _chart.SetData(null, FollowChartMode.MeanOverTime);
            _status.Text = $"Dati: {_db.Path}   " + (_grt is { Connected: true } ? $"GRT :{_grt.Port}" : "senza GRT");
            return;
        }

        var outings = _db.OutingsOf(l.Id);
        _title.Text = $"{l.Name}   ·   {l.Caliber}   ·   {_u.Charge(l.ChargeGr)}";
        var inv = CultureInfo.InvariantCulture;
        foreach (var o in outings)
        {
            var v = o.Velocities();
            var st = StringStats.From(v);
            int i = _grid.Rows.Add(
                o.Date,
                o.TempC is { } tc ? _u.Temperature(tc) : "",
                v.Count > 0 ? v.Count.ToString(inv) : "",
                v.Count > 0 ? _u.VelocityValue(st.Mean).ToString("0.0", inv) : "",
                v.Count > 1 ? _u.VelocitySd(st.Sd) : "",
                v.Count > 1 ? _u.VelocitySd(st.Es) : "",
                o.GroupMoa?.ToString("0.00", inv) ?? "",
                o.DistanceM is { } dm ? _u.DistanceValue(dm).ToString("0", inv) : "",
                o.WindMps is { } w ? _u.VelocityValue(w).ToString("0.0", inv) + (o.WindDeg is { } wd ? $" @{wd:0}" : "") : "",
                o.Notes);
            _grid.Rows[i].Tag = o;
            if (v.Count < LoadFollowUp.MinShots) _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            if (o.Id == keepOuting) _grid.CurrentCell = _grid.Rows[i].Cells[0];
        }

        _last = LoadFollowUp.Analyze(FollowSessions.From(outings));
        _chart.SetData(_last, (FollowChartMode)Math.Max(0, _mode.SelectedIndex));
        _report.Text = outings.Count == 0
            ? "Nessuna uscita ancora. «Nuova uscita…» ne scrive una; con un carico aperto in GRT, «Registra da GRT» porta dentro i suoi colpi."
            : FollowUpReport.Build(_last, l.Name, Lang.T, Format());
        int unused = outings.Count(o => o.Velocities().Count < LoadFollowUp.MinShots);
        _status.Text = $"Dati: {_db.Path}   " + (unused > 0 ? $"{unused} uscite in grigio non hanno i colpi scritti (o ne hanno meno di {LoadFollowUp.MinShots}) e non vengono analizzate." : "");
    }

    private FollowUpFormat Format() => new(
        _u.Velocity,
        _u.VelocitySd,
        _u.VelocityUnitName,
        _u.Temperature,
        v => _u.VelocityPerDegreeValue(v).ToString("0.00", CultureInfo.InvariantCulture) + " " + _u.VelocityPerDegreeUnitName,
        v => _u.VelocityValue(v).ToString("0.0", CultureInfo.InvariantCulture) + " " + _u.VelocityUnitName + " ogni 30 giorni",
        c => (_u.TemperatureInF ? c * 9.0 / 5.0 : c).ToString("0.#", CultureInfo.InvariantCulture) + " " + _u.TemperatureUnitName);

    // ---- le uscite

    private Outing? SelectedOuting => _grid.CurrentRow?.Tag as Outing;

    /// <summary>Una nuova uscita del carico, con ciò che cambia di rado preso dall'ultima (arma, lotti, seduta, distanza).</summary>
    private Outing Seed(TrackedLoad l)
    {
        var o = new Outing { LoadId = l.Id };
        if (_db.OutingsOf(l.Id).LastOrDefault() is { } last)
        {
            o.Firearm = last.Firearm; o.PowderLot = last.PowderLot; o.PrimerLot = last.PrimerLot; o.SeatingMm = last.SeatingMm;
            o.DistanceM = last.DistanceM; o.BrassFirings = last.BrassFirings;
        }
        return o;
    }

    /// <summary>Il carico viene passato, non letto dall'elenco: l'elenco può essere filtrato su altro quando questo gira, e un'uscita non deve mai finire sul carico che capita di essere selezionato.</summary>
    private void NewOuting(TrackedLoad l, Outing o)
    {
        o.LoadId = l.Id;
        using var d = new OutingDialog(o, l.Name);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveOuting(d.Result);
        RefreshSelected();
    }

    private void EditOuting()
    {
        if (SelectedLoad is not { } l || SelectedOuting is not { } o) return;
        using var d = new OutingDialog(o, l.Name);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveOuting(d.Result);
        RefreshSelected();
    }

    private void DeleteOuting()
    {
        if (SelectedOuting is not { } o) return;
        if (MessageBox.Show(this, $"Eliminare l'uscita del {o.Date}?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.DeleteOuting(o.Id);
        RefreshSelected();
    }

    // ---- GRT

    /// <summary>Registra da GRT: il carico aperto in GRT viene riconosciuto tra quelli seguiti (calibro, proiettile, polvere, carica); senza una selezione
    /// sceglie quello che combacia, e se nessuno combacia propone di seguirlo.</summary>
    private async Task FromGrtAsync()
    {
        if (_grt is not { Connected: true }) return;
        try
        {
            var open = await ReadOpenLoadAsync();
            if (open is null) return;

            ChargeSet? set = null;
            if (open.Sets.Count == 1) set = open.Sets[0];
            else if (open.Sets.Count > 1)
            {
                if (SelectedLoad is { } sel)
                {
                    var fits = open.Sets.Where(s => s.ChargeGr is { } g && Math.Abs(g - sel.ChargeGr) <= 0.05 + 1e-9).ToList();
                    if (fits.Count == 1) set = fits[0];
                }
                if (set is null && (set = PickCharge(open, SelectedLoad?.ChargeGr, "Quale carica vuoi registrare?")) is null) return;
            }
            else
                MessageBox.Show(this, "Il carico aperto in GRT non ha stringhe cronografate: l'uscita si registra senza colpi, li puoi incollare tu.", Text);

            double charge = set?.ChargeGr ?? open.RecipeChargeGr ?? 0;
            var load = _db.Find(open.Caliber, open.Bullet, open.Powder, charge);
            if (load is null)
            {
                // nessuno combacia, o ne combaciano più d'uno: con uno scelto nell'elenco decide quello, altrimenti si chiede
                if (SelectedLoad is { } picked && TrackerDb.Matches(picked, open.Caliber, open.Bullet, open.Powder, charge)) load = picked;
                else if (SelectedLoad is { } other)
                {
                    if (MessageBox.Show(this, $"Il carico aperto in GRT ({open.Caliber}, {_u.Charge(charge)}) non corrisponde a «{other.Name}». Registrarlo comunque come uscita di «{other.Name}»?",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    load = other;
                }
                else
                {
                    if (MessageBox.Show(this, $"Nessuno dei carichi seguiti corrisponde al carico aperto in GRT ({open.Caliber}, {_u.Charge(charge)}). Lo seguo?",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    load = SaveNewLoad(new TrackedLoad { Name = open.Name, Caliber = open.Caliber, Bullet = open.Bullet, Powder = open.Powder, ChargeGr = charge });
                    if (load is null) return;
                }
            }
            if (_filter.Text.Length > 0) _filter.Text = "";   // il carico appena riconosciuto deve essere nell'elenco
            RefreshLoads(load.Id);

            var o = Seed(load);
            if (string.IsNullOrWhiteSpace(o.Firearm)) o.Firearm = open.Firearm;
            if (set is not null) o.ShotsMs = Outing.FormatShots(set.Shots);
            NewOuting(load, o);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Registra da GRT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Il carico aperto in GRT; null se non ce n'è uno salvato.</summary>
    private async Task<OpenLoad?> ReadOpenLoadAsync()
    {
        var top = await _grt!.GetTabOnTopAsync();
        if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
        {
            MessageBox.Show(this, "Nessun carico salvato è aperto in GRT.", Text);
            return null;
        }
        return OpenLoad.Read(top.file);
    }

    private ChargeSet? PickCharge(OpenLoad open, double? nearTo, string question)
    {
        using var dlg = new Form
        {
            Text = "Quale carica?", Width = 560, Height = 170, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
        };
        var label = new Label { Text = "Questo carico ha più di una carica cronografata. " + question, Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0) };
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in open.Sets) combo.Items.Add(s.Label);
        // preselected: the charge nearest the one asked about, else the last (the top of the ladder)
        combo.SelectedIndex = nearTo is { } near
            ? open.Sets.Select((x, i) => (i, d: x.ChargeGr is { } g ? Math.Abs(g - near) : double.MaxValue)).OrderBy(t => t.d).First().i
            : open.Sets.Count - 1;
        var comboPanel = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 8, 0) };
        comboPanel.Controls.Add(combo);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        bar.Controls.Add(cancel); bar.Controls.Add(ok);
        dlg.Controls.Add(comboPanel); dlg.Controls.Add(label); dlg.Controls.Add(bar);
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK && combo.SelectedIndex >= 0 ? open.Sets[combo.SelectedIndex] : null;
    }

    /// <summary>Scrive nel carico aperto in GRT una nota con il verdetto e i grafici: come fanno gli altri strumenti, GRT non può modificare il carico
    /// aperto, quindi si scrive un file gemello e lo si apre.</summary>
    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || SelectedLoad is not { } l || _last is not { } result) return;
        try
        {
            if (result.Sessions.Count == 0) { MessageBox.Show(this, "Questo carico non ha ancora uscite da scrivere.", Text); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, "Nessun carico salvato è aperto in GRT.", Text); return; }

            var open = OpenLoad.Read(top.file);
            double charge = open.RecipeChargeGr ?? l.ChargeGr;
            // a ladder's recipe carries one charge and its measurements ten: the load matches when any of them is this load's charge
            bool same = TrackerDb.Matches(l, open.Caliber, open.Bullet, open.Powder, charge)
                        || open.Sets.Any(x => x.ChargeGr is { } g && TrackerDb.Matches(l, open.Caliber, open.Bullet, open.Powder, g));
            if (!same &&
                MessageBox.Show(this, $"Il carico aperto in GRT ({open.Caliber}, {_u.Charge(charge)}) non corrisponde a «{l.Name}». Scrivere comunque la nota qui?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var text = new StringBuilder();
            text.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd} — {l.Name}");
            text.AppendLine(new string('-', 20));
            text.Append(FollowUpReport.Build(result, l.Name, Lang.T, Format()));

            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, text.ToString());
            if (_chart.RenderPng(FollowChartMode.MeanOverTime) is { } mean) doc.AddGalleryPicture($"{NoteTitle} — velocità media", "followload_mean", mean);
            if (result.Sessions.Any(s => s.Session.TempC is not null) && _chart.RenderPng(FollowChartMode.VelocityVsTemperature) is { } temp)
                doc.AddGalleryPicture($"{NoteTitle} — velocità e temperatura", "followload_temperature", temp);
            string outPath = doc.SaveSibling("followload");
            await _grt.LoadFileAsync(outPath);
            _status.Text = "Nota e grafici scritti nel carico e aperti in GRT.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scrivi nota e grafici", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
