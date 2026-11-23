using System.Globalization;
using GrtReloadingToolkit.Archive;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The component library of the archive: what each bullet, cartridge, powder, case, primer and rifle is, every value with
/// its source, the shooter's own measurements and the maker's figures added by hand, and the proposed matches with the
/// Empirical Precision library confirmed or set aside one by one.
/// </summary>
internal sealed class LibraryForm : Form
{
    private readonly ArchiveLink _link;
    private ArchiveDb Db => _link.Db;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // kinds as stored, with the words the shooter reads
    private static readonly (string Kind, string Label)[] Kinds =
    {
        (ComponentLibrary.Bullet, "Bullets"), (ComponentLibrary.Cartridge, "Cartridges"), (ComponentLibrary.Powder, "Powders"),
        (ComponentLibrary.Case, "Cases"), (ComponentLibrary.Primer, "Primers"), (ComponentLibrary.Rifle, "Rifles"),
    };

    // ---- components tab
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TextBox _search = new() { Width = 220 };
    private readonly CheckBox _onlyMine = new() { AutoSize = true, Checked = true, Margin = new Padding(12, 6, 0, 0) };
    private readonly ListView _items = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly Label _itemTitle = new() { Dock = DockStyle.Top, Height = 28, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(4, 6, 0, 0) };
    private readonly DataGridView _values = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly CheckBox _allSources = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly CheckBox _raw = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly Button _measure = new() { AutoSize = true, Enabled = false };
    private readonly Button _maker = new() { AutoSize = true, Enabled = false };
    private List<LibItem> _list = new();

    // ---- matches tab
    private readonly ListView _pendingItems = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly Label _mine = new() { Dock = DockStyle.Top, Height = 46, Padding = new Padding(4, 4, 0, 0) };
    private readonly DataGridView _cands = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private List<(long Item, string Kind, string Text, int Count)> _pending = new();
    private List<LibSuggestion> _shown = new();

    public LibraryForm(ArchiveLink link)
    {
        _link = link;
        Text = AppVersion.Title(Lang.T("Component library"));
        Width = 1150; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var compPage = new TabPage(Lang.T("Components"));
        var matchPage = new TabPage(Lang.T("Matches to confirm"));
        compPage.Controls.Add(BuildComponents());
        matchPage.Controls.Add(BuildMatches());
        tabs.TabPages.Add(compPage);
        tabs.TabPages.Add(matchPage);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == matchPage) FillPending(); };
        Controls.Add(tabs);

        foreach (var (_, label) in Kinds) _kind.Items.Add(Lang.T(label));
        _kind.SelectedIndex = 0;
        // filled now as well: a tab chosen before the window exists raises no SelectedIndexChanged
        FillPending();
    }

    /// <summary>The window opened on its second tab (for the preview of the matches).</summary>
    public static LibraryForm OnMatches(ArchiveLink link)
    {
        var f = new LibraryForm(link);
        if (f.Controls.OfType<TabControl>().FirstOrDefault() is { } t) t.SelectedIndex = 1;
        return f;
    }

    // ═══════════════ Components ═══════════════

    private Control BuildComponents()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
        top.Controls.Add(new Label { Text = Lang.T("Kind:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        top.Controls.Add(_kind);
        top.Controls.Add(new Label { Text = Lang.T("Search:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        top.Controls.Add(_search);
        _onlyMine.Text = Lang.T("only mine (from my loads, measured or entered)");
        top.Controls.Add(_onlyMine);
        _kind.SelectedIndexChanged += (_, _) => FillItems();
        _search.TextChanged += (_, _) => FillItems();
        _onlyMine.CheckedChanged += (_, _) => FillItems();

        foreach (var (h, w) in new[] { (Lang.T("Maker"), 120), (Lang.T("Name"), 230), (Lang.T("Caliber"), 120), (Lang.T("Sources"), 160) })
            _items.Columns.Add(h, w);
        _items.SelectedIndexChanged += (_, _) => FillValues();

        foreach (var (n, h, w) in new[] { ("f", Lang.T("Field"), 30), ("v", Lang.T("Value"), 14), ("u", Lang.T("Unit"), 8),
                     ("s", Lang.T("Source"), 14), ("r", Lang.T("From"), 24), ("d", Lang.T("Date"), 12), ("use", Lang.T("In use"), 8) })
            _values.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = h, FillWeight = w });

        _allSources.Text = Lang.T("every source (not only the value in use)");
        _raw.Text = Lang.T("also GRT's and EP's own fields");
        _allSources.CheckedChanged += (_, _) => FillValues();
        _raw.CheckedChanged += (_, _) => FillValues();
        _measure.Text = Lang.T("Add my measurement…");
        _maker.Text = Lang.T("Add the maker's figure…");
        _measure.Click += (_, _) => AddFigure(ComponentLibrary.Measured);
        _maker.Click += (_, _) => AddFigure(ComponentLibrary.Maker);
        var valBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        valBar.Controls.AddRange(new Control[] { _measure, _maker, _allSources, _raw });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 6, 6) };
        right.Controls.Add(_values);
        right.Controls.Add(valBar);
        right.Controls.Add(_itemTitle);

        var split = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(470);
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 6) };
        left.Controls.Add(_items);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        root.Controls.Add(split);
        root.Controls.Add(top);
        return root;
    }

    private string Kind => Kinds[Math.Max(0, _kind.SelectedIndex)].Kind;

    private void FillItems()
    {
        _list = LibraryBrowse.Items(Db, Kind, _search.Text, _onlyMine.Checked);
        _items.BeginUpdate();
        _items.Items.Clear();
        foreach (var i in _list) _items.Items.Add(new ListViewItem(new[] { i.Maker, i.Name, i.Caliber, SourcesText(i.Sources) }));
        _items.EndUpdate();
        if (_list.Count > 0) _items.Items[0].Selected = true; else FillValues();
    }

    private static string SourcesText(string s) => string.Join(", ", s.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .OrderBy(x => ComponentLibrary.Priority(x)).Select(x => Lang.T(x)));

    private LibItem? Selected => _items.SelectedIndices.Count > 0 ? _list[_items.SelectedIndices[0]] : null;

    private void FillValues()
    {
        _values.Rows.Clear();
        var item = Selected;
        _measure.Enabled = _maker.Enabled = item != null && LibraryBrowse.FieldsFor(item.Kind).Count > 0;
        if (item == null) { _itemTitle.Text = ""; return; }
        _itemTitle.Text = (item.Maker + " " + item.Name).Trim() + (item.Caliber.Length > 0 ? "  ·  " + item.Caliber : "");
        foreach (var v in LibraryBrowse.Values(Db, item.Id))
        {
            bool raw = v.Field.StartsWith("grt.", StringComparison.Ordinal) || v.Field.StartsWith("ep.", StringComparison.Ordinal);
            if (raw && !_raw.Checked) continue;
            if (!v.InUse && !_allSources.Checked) continue;
            string label = v.Label.Length > 0 ? v.Label : v.Field;
            string value = v.Value is { } d ? d.ToString("0.####", Inv) : Short(v.Text);
            int r = _values.Rows.Add(label, value, v.Unit, Lang.T(v.Source), Short(v.SourceRef, 60), v.RecordedAt.Length >= 10 ? v.RecordedAt[..10] : v.RecordedAt,
                v.InUse ? "✓" : "");
            if (!v.InUse) _values.Rows[r].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            if (v.Source == ComponentLibrary.Measured) _values.Rows[r].DefaultCellStyle.Font = new Font(_values.Font, FontStyle.Bold);
        }
    }

    private static string Short(string? s, int max = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= max ? s : "…" + s[^(max - 1)..];
    }

    private void AddFigure(string source)
    {
        if (Selected is not { } item) return;
        var fields = LibraryBrowse.FieldsFor(item.Kind);
        using var f = new FigureDialog(fields, source == ComponentLibrary.Measured ? Lang.T("My measurement") : Lang.T("Maker's figure"),
            (item.Maker + " " + item.Name).Trim());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (!LibraryBrowse.AddFigure(Db, item.Id, f.Field, f.StoredValue, f.StoredUnit, source, f.Note))
                MessageBox.Show(this, Lang.T("The same figure from that source is already there."), Text);
            FillValues();
            FillItems();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // ═══════════════ Matches ═══════════════

    private Control BuildMatches()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0), ForeColor = SystemColors.GrayText,
            Text = Lang.T("Nothing is joined on its own: for each of your components, say whether the EP library's candidate is the same component. Confirmed, its figures go next to yours (yours still count first)."),
        };
        foreach (var (h, w) in new[] { (Lang.T("Kind"), 80), (Lang.T("Your component"), 300), (Lang.T("Candidates"), 80) })
            _pendingItems.Columns.Add(h, w);
        _pendingItems.SelectedIndexChanged += (_, _) => FillCandidates();

        foreach (var (n, h, w) in new[] { ("c", Lang.T("Candidate"), 24), ("w", Lang.T("Weight gr"), 14), ("l", Lang.T("Length mm"), 14),
                     ("g7", "BC G7", 11), ("b", Lang.T("BC from"), 12), ("sc", Lang.T("Score"), 9), ("why", Lang.T("Why"), 30) })
            _cands.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = h, FillWeight = w });

        var yes = new Button { Text = Lang.T("It is the same: confirm"), AutoSize = true };
        var no = new Button { Text = Lang.T("Not this one"), AutoSize = true };
        var none = new Button { Text = Lang.T("None of these"), AutoSize = true };
        yes.Click += (_, _) => Decide(confirm: true);
        no.Click += (_, _) => Decide(confirm: false);
        none.Click += (_, _) => RejectAll();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4, 6, 4, 0) };
        bar.Controls.AddRange(new Control[] { yes, no, none });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 6, 6) };
        right.Controls.Add(_cands);
        right.Controls.Add(bar);
        right.Controls.Add(_mine);

        var split = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(470);
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 6) };
        left.Controls.Add(_pendingItems);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        root.Controls.Add(split);
        root.Controls.Add(hint);
        return root;
    }

    private void FillPending(long? keep = null)
    {
        var all = LibraryBrowse.Pending(Db);
        _pending = all.GroupBy(s => s.ItemId).Select(g =>
        {
            var info = Db.ScalarP("SELECT kind || '|' || TRIM(IFNULL(maker,'') || ' ' || name) FROM lib_items WHERE id=$i", ("$i", g.Key)) as string ?? "|";
            var parts = info.Split('|', 2);
            return (g.Key, parts[0], parts[1], g.Count());
        }).OrderBy(x => x.Item2).ThenBy(x => x.Item3, StringComparer.CurrentCultureIgnoreCase).ToList();
        _pendingItems.BeginUpdate();
        _pendingItems.Items.Clear();
        foreach (var p in _pending) _pendingItems.Items.Add(new ListViewItem(new[] { Lang.T(p.Kind), p.Text, p.Count.ToString(Inv) }));
        _pendingItems.EndUpdate();
        int idx = keep is { } k ? _pending.FindIndex(p => p.Item == k) : -1;
        if (_pending.Count > 0) _pendingItems.Items[Math.Max(0, Math.Min(idx >= 0 ? idx : 0, _pending.Count - 1))].Selected = true;
        else { _cands.Rows.Clear(); _mine.Text = Lang.T("No matches left to confirm."); }
    }

    private static string Fig(Dictionary<string, LibValue> v, string field, string fmt)
        => v.TryGetValue(field, out var x) ? x.Value is { } d ? d.ToString(fmt, Inv) : x.Text ?? "" : "–";

    private static Dictionary<string, LibValue> InUse(ArchiveDb db, long item)
        => LibraryBrowse.Values(db, item).Where(v => v.InUse).GroupBy(v => v.Field).ToDictionary(g => g.Key, g => g.First());

    private void FillCandidates()
    {
        _cands.Rows.Clear();
        if (_pendingItems.SelectedIndices.Count == 0) return;
        var p = _pending[_pendingItems.SelectedIndices[0]];
        // the four figures that tell two candidates apart: a bullet's weight, length and BC; a case's drawing and pressures
        var cols = p.Kind == ComponentLibrary.Cartridge
            ? new[] { ("case_length_max_mm", Lang.T("Case length mm"), "0.00"), ("base_to_shoulder_mm", Lang.T("Base-shoulder mm"), "0.00"),
                      ("pmax_cip_bar", "C.I.P. bar", "0"), ("pmax_saami_psi", "SAAMI psi", "0") }
            : new[] { ("weight_gr", Lang.T("Weight gr"), "0.0"), ("length_mm", Lang.T("Length mm"), "0.00"),
                      ("bc_g7", "BC G7", "0.000"), ("bc_basis", Lang.T("BC from"), "") };
        string[] names = { "w", "l", "g7", "b" };
        for (int i = 0; i < 4; i++) _cands.Columns[names[i]].HeaderText = cols[i].Item2;
        var mine = InUse(Db, p.Item);
        _mine.Text = Lang.T("Yours:") + " " + p.Text + Environment.NewLine
            + string.Join(" · ", cols.Where(c => c.Item3.Length > 0).Select(c => c.Item2 + " " + Fig(mine, c.Item1, c.Item3)));
        _shown = LibraryBrowse.Pending(Db, p.Item);
        foreach (var s in _shown)
        {
            var c = InUse(Db, s.CandidateId);
            _cands.Rows.Add((s.CandidateMaker + " " + s.CandidateName).Trim(), Fig(c, cols[0].Item1, cols[0].Item3), Fig(c, cols[1].Item1, cols[1].Item3),
                Fig(c, cols[2].Item1, cols[2].Item3), Fig(c, cols[3].Item1, cols[3].Item3), s.Score?.ToString("0.00", Inv) ?? "", s.Reason);
        }
        if (_cands.Rows.Count > 0) _cands.Rows[0].Selected = true;
    }

    private void Decide(bool confirm)
    {
        if (_pendingItems.SelectedIndices.Count == 0 || _cands.SelectedRows.Count == 0) return;
        var p = _pending[_pendingItems.SelectedIndices[0]];
        var s = _shown[_cands.SelectedRows[0].Index];
        try
        {
            if (confirm) LibraryBrowse.Confirm(Db, s.Id); else LibraryBrowse.Reject(Db, s.Id);
            FillPending(p.Item);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RejectAll()
    {
        if (_pendingItems.SelectedIndices.Count == 0) return;
        foreach (var s in _shown) LibraryBrowse.Reject(Db, s.Id);
        FillPending();
    }
}

/// <summary>One figure typed by hand: which field, its value in the unit the shooter measured in, and a note.</summary>
internal sealed class FigureDialog : Form
{
    private readonly ComboBox _field = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 40, Width = 300 };
    private readonly TextBox _value = new() { Left = 110, Top = 74, Width = 120 };
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 240, Top = 74, Width = 170 };
    private readonly TextBox _note = new() { Left = 110, Top = 108, Width = 300 };
    private readonly IReadOnlyList<(string Field, string Unit)> _fields;

    public string Field { get; private set; } = "";
    public double StoredValue { get; private set; }
    public string StoredUnit { get; private set; } = "";
    public string Note => _note.Text.Trim();

    private sealed record FieldItem(string Field, string Unit, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Units the shooter may type in, with the factor to the stored one.</summary>
    private static IReadOnlyList<(string Label, double Factor)> UnitsFor(string stored) => stored switch
    {
        "mm" => new[] { ("mm", 1.0), (Lang.T("inches"), 25.4) },
        "gr" => new[] { (Lang.T("grains"), 1.0), (Lang.T("grams"), 1.0 / 0.06479891) },
        "cm³" => new[] { ("cm³ (= g " + Lang.T("of water") + ")", 1.0), (Lang.T("grains of water"), 0.06479891) },
        "" => new[] { ("", 1.0) },
        _ => new[] { (stored, 1.0) },
    };

    public FigureDialog(IReadOnlyList<(string Field, string Unit)> fields, string title, string component)
    {
        _fields = fields;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(430, 190);
        foreach (var (f, u) in fields)
            _field.Items.Add(new FieldItem(f, u, ComponentLibrary.Labels.TryGetValue(f, out var l) ? l : f));
        _field.SelectedIndexChanged += (_, _) => FillUnits();
        var ok = new Button { Text = "OK", Left = 254, Top = 148, Width = 75 };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 335, Top = 148, Width = 75 };
        ok.Click += (_, _) => Accept();
        Controls.AddRange(new Control[]
        {
            new Label { Text = component, Left = 12, Top = 12, AutoSize = true, Font = new Font(Font, FontStyle.Bold) },
            new Label { Text = Lang.T("Field"), Left = 12, Top = 43, AutoSize = true }, _field,
            new Label { Text = Lang.T("Value"), Left = 12, Top = 77, AutoSize = true }, _value, _unit,
            new Label { Text = Lang.T("Note"), Left = 12, Top = 111, AutoSize = true }, _note, ok, cancel,
        });
        AcceptButton = ok; CancelButton = cancel;
        if (_field.Items.Count > 0) _field.SelectedIndex = 0;
        GrtTheme.Apply(this);
    }

    private void FillUnits()
    {
        _unit.Items.Clear();
        if (_field.SelectedItem is not FieldItem fi) return;
        foreach (var (l, _) in UnitsFor(fi.Unit)) _unit.Items.Add(l);
        _unit.SelectedIndex = 0;
        _unit.Enabled = _unit.Items.Count > 1;
    }

    private void Accept()
    {
        if (_field.SelectedItem is not FieldItem fi) return;
        string t = _value.Text.Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0 || !double.IsFinite(v))
        {
            MessageBox.Show(this, Lang.T("Type a number greater than zero."), Text);
            return;
        }
        var units = UnitsFor(fi.Unit);
        double factor = units[Math.Max(0, _unit.SelectedIndex)].Factor;
        Field = fi.Field;
        StoredValue = Math.Round(v * factor, 6);
        StoredUnit = fi.Unit;
        if (_unit.SelectedIndex > 0)
            _note.Text = (Note + " " + string.Format(CultureInfo.InvariantCulture, "(misurato: {0} {1})", t, units[_unit.SelectedIndex].Label)).Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
