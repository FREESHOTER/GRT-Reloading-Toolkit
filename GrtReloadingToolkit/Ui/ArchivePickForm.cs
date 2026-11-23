using System.Globalization;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Archive;

namespace GrtReloadingToolkit.Ui;

/// <summary>The archive as the analysis tools see it, and whose sessions they list.</summary>
internal sealed class ArchiveLink
{
    private readonly Func<long?> _toolkitShooter;

    public ArchiveLink(ArchiveDb db, Func<long?> toolkitShooter)
    {
        Db = db;
        _toolkitShooter = toolkitShooter;
    }

    public ArchiveDb Db { get; }

    /// <summary>The archive shooter for the launcher's current one.</summary>
    public long ShooterId() => ArchiveImports.ShooterFor(Db, _toolkitShooter());

    /// <summary>Keeps a tool's report in the archive and says so; false (with the reason shown) when it could not.</summary>
    public bool Save(IWin32Window owner, string tool, long? loadId, long? sessionId, string inputs, string report)
    {
        try
        {
            ArchiveQueries.SaveAnalysis(Db, tool, AppVersion.Short, loadId, sessionId, inputs, report);
            MessageBox.Show(owner, Lang.T("Saved in the archive."), tool, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, tool, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}

internal enum ArchivePick { Strings, Groups, Both }

/// <summary>
/// "From the archive…": the shooter picks a load (or all of them), one of its sessions, and the strings and/or groups of
/// that session to analyse. Nothing is changed in the archive here.
/// </summary>
internal sealed class ArchivePickForm : Form
{
    private readonly ArchiveLink _link;
    private readonly ArchivePick _mode;
    private readonly ComboBox _load = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly ListView _sessions = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    private readonly CheckedListBox _items = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
    private List<ArchiveSessionInfo> _list = new();
    private List<ArchiveString> _strings = new();
    private List<ArchiveGroup> _groups = new();

    public ArchiveSessionInfo? Session { get; private set; }
    public List<ArchiveString> PickedStrings { get; } = new();
    public List<ArchiveGroup> PickedGroups { get; } = new();

    private sealed record Item(string Text, ArchiveString? S, ArchiveGroup? G, bool Usable)
    {
        public override string ToString() => Text;
    }

    public ArchivePickForm(ArchiveLink link, ArchivePick mode)
    {
        _link = link;
        _mode = mode;
        Text = Lang.T("From the archive");
        StartPosition = FormStartPosition.CenterParent;
        Width = 820; Height = 620;
        MinimumSize = new Size(640, 480);
        MinimizeBox = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
        top.Controls.Add(new Label { Text = Lang.T("Load:"), AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
        top.Controls.Add(_load);
        _load.SelectedIndexChanged += (_, _) => FillSessions();

        foreach (var (h, w) in new[] { (Lang.T("Date"), 90), (Lang.T("Load"), 300), (Lang.T("Caliber"), 150), (Lang.T("Strings"), 60), (Lang.T("Shots"), 60), (Lang.T("Groups"), 60) })
            _sessions.Columns.Add(h, w);
        _sessions.SelectedIndexChanged += (_, _) => FillItems();

        var itemsBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        var itemsHead = new Label
        {
            Dock = DockStyle.Top, Height = 22,
            Text = mode switch
            {
                ArchivePick.Strings => Lang.T("Strings of the session (tick the ones to analyse):"),
                ArchivePick.Groups => Lang.T("Groups of the session (tick the ones to analyse):"),
                _ => Lang.T("Strings and groups of the session (tick the ones to use):"),
            },
        };
        itemsBox.Controls.Add(_items);
        itemsBox.Controls.Add(itemsHead);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal }.WithDistance(260);
        var sessBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        sessBox.Controls.Add(_sessions);
        split.Panel1.Controls.Add(sessBox);
        split.Panel2.Controls.Add(itemsBox);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = Lang.T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var all = new Button { Text = Lang.T("Tick all"), AutoSize = true };
        ok.Click += (_, _) => Accept();
        all.Click += (_, _) => { for (int i = 0; i < _items.Items.Count; i++) if (_items.Items[i] is Item { Usable: true }) _items.SetItemChecked(i, true); };
        bottom.Controls.AddRange(new Control[] { cancel, ok, all, _hint });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(top);

        _load.Items.Add(new LoadItem(null, Lang.T("(all loads)")));
        foreach (var (id, label) in ArchiveQueries.LoadsWithSessions(_link.Db, _link.ShooterId()))
            _load.Items.Add(new LoadItem(id, label));
        _load.SelectedIndex = 0;
        GrtTheme.Apply(this);
    }

    private sealed record LoadItem(long? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void FillSessions()
    {
        long? load = (_load.SelectedItem as LoadItem)?.Id;
        _list = ArchiveQueries.Sessions(_link.Db, _link.ShooterId(), load)
            .Where(s => _mode switch { ArchivePick.Strings => s.Strings > 0, ArchivePick.Groups => s.Groups > 0, _ => true })
            .ToList();
        _sessions.BeginUpdate();
        _sessions.Items.Clear();
        foreach (var s in _list)
            _sessions.Items.Add(new ListViewItem(new[]
            {
                s.Date + (s.DateEstimated ? " ~" : ""), s.LoadName, s.Caliber,
                s.Strings.ToString(CultureInfo.InvariantCulture), s.Shots.ToString(CultureInfo.InvariantCulture), s.Groups.ToString(CultureInfo.InvariantCulture),
            }));
        _sessions.EndUpdate();
        _items.Items.Clear();
        _hint.Text = _list.Count == 0 ? Lang.T("No sessions with these data.") : Lang.T("~ = date estimated (from the file)");
        if (_list.Count > 0) _sessions.Items[0].Selected = true;
    }

    private void FillItems()
    {
        _items.Items.Clear();
        if (_sessions.SelectedIndices.Count == 0) return;
        var s = _list[_sessions.SelectedIndices[0]];
        var u = GrtUnits.Current;
        _strings = _mode == ArchivePick.Groups ? new() : ArchiveQueries.Strings(_link.Db, s.Id);
        _groups = _mode == ArchivePick.Strings ? new() : ArchiveQueries.Groups(_link.Db, s.Id);
        foreach (var st in _strings)
        {
            var stats = StringStats.From(st.Velocities);
            string text = string.Format(CultureInfo.InvariantCulture, "{0}{1} · {2} {3} · {4} {5:0.0} {6}",
                _mode == ArchivePick.Both ? Lang.T("String:") + " " : "", st.Label, stats.N, Lang.T("shots"), Lang.T("mean"),
                u.VelocityValue(stats.Mean), u.VelocityUnitName);
            _items.Items.Add(new Item(text, st, null, st.Shots.Count > 0), true);
        }
        foreach (var g in _groups)
        {
            bool usable = ArchiveQueries.ToTargetGroup(g) != null;
            string text = (_mode == ArchivePick.Both ? Lang.T("Group:") + " " : "") + (g.Tab.Length > 0 && g.Tab != g.Label ? g.Tab + " · " : "") + g.Label
                + string.Format(CultureInfo.InvariantCulture, " · {0} {1}", g.Hits.Count, Lang.T("hits"))
                + (g.DistanceM is { } d ? " · " + u.Distance(d) : "")
                + (g.Step is { } st2 ? " · " + Lang.T("step") + " " + st2.ToString("0.###", CultureInfo.InvariantCulture) : "")
                + (usable ? "" : "  — " + Lang.T("no scale or distance: cannot be measured"));
            _items.Items.Add(new Item(text, null, g, usable), usable);
        }
    }

    private void Accept()
    {
        if (_sessions.SelectedIndices.Count == 0) { MessageBox.Show(this, Lang.T("Pick a session first.")); return; }
        Session = _list[_sessions.SelectedIndices[0]];
        PickedStrings.Clear(); PickedGroups.Clear();
        foreach (Item it in _items.CheckedItems)
        {
            if (!it.Usable) continue;
            if (it.S != null) PickedStrings.Add(it.S);
            if (it.G != null) PickedGroups.Add(it.G);
        }
        if (PickedStrings.Count + PickedGroups.Count == 0) { MessageBox.Show(this, Lang.T("Tick at least one string or group.")); return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}
