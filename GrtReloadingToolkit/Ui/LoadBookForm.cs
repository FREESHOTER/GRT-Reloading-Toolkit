using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Idea #5 from the original next-plugin brainstorm: walk a folder of .grtload files and emit one
/// document -- every recipe found, its GRT propellant-model coefficients, and whatever measured
/// results the Journal (or the file's own embedded shot data) has for it, grouped by caliber. See
/// <see cref="LoadBook"/> for the folder-scan/merge logic and <see cref="LoadBookHtml"/> for the
/// rendered output. No GRT connection needed -- same standalone shape as <see cref="TargetForm"/>,
/// reading only local files and this plugin's own Db.
/// </summary>
internal sealed class LoadBookForm : Form
{
    private readonly Db _db;
    private readonly TextBox _folder = new() { Width = 440, ReadOnly = true, Margin = new Padding(6, 3, 0, 0) };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private IReadOnlyList<LoadBook.Entry> _entries = Array.Empty<LoadBook.Entry>();

    public LoadBookForm(Db db)
    {
        _db = db;
        Text = AppVersion.Title(Lang.T("Load Book Export"));
        Width = 1040; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 420);
        Build();
        NudFix.ApplyTo(this);
    }

    private void Build()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 0, 0) };
        var pick = new Button { Text = Lang.T("Pick folder…"), AutoSize = true };
        pick.Click += (_, _) => PickFolder();
        bar.Controls.Add(pick);
        bar.Controls.Add(_folder);
        var exportBtn = new Button { Text = Lang.T("Export HTML…"), AutoSize = true, Margin = new Padding(10, 0, 0, 0) };
        exportBtn.Click += (_, _) => Export();
        bar.Controls.Add(exportBtn);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[]
                 {
                     ("cal", Lang.T("Caliber")), ("load", Lang.T("Load")), ("pw", Lang.T("Powder")),
                     ("chg", Lang.T("Charge")), ("bul", Lang.T("Bullet")), ("mv", Lang.T("Velocity")),
                     ("grp", Lang.T("Group")), ("src", Lang.T("Source")),
                 })
            _grid.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom;
        _status.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(bar);
    }

    private void PickFolder()
    {
        using var d = new FolderBrowserDialog { Description = Lang.T("Folder with .grtload files to include (searched recursively)") };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _folder.Text = d.SelectedPath;
        Scan(d.SelectedPath);
    }

    private void Scan(string folder)
    {
        _grid.Rows.Clear();
        var u = GrtUnits.Current;
        var journal = _db.Journal();
        var componentsById = _db.Components(includeArchived: true).ToDictionary(c => c.Id);
        var entries = new List<LoadBook.Entry>();

        foreach (string f in LoadBook.DiscoverFamilyRepresentatives(folder))
        {
            try
            {
                var e = LoadBook.BuildEntry(f, journal, componentsById);
                entries.Add(e);
                int i = _grid.Rows.Add(e.Caliber, e.LoadName, e.PowderName,
                    e.ChargeGr is { } c ? u.Charge(c) : "",
                    e.BulletName ?? "",
                    e.VelocityAvgMs is { } v ? u.Velocity(v) : "",
                    e.GroupMoa is { } g ? FormattableString.Invariant($"{g:0.00} MOA") : "",
                    e.FromJournal ? Lang.T("Journal") : Lang.T("file only"));
                if (!e.FromJournal) _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
            catch (Exception ex)
            {
                // A malformed or half-written .grtload shouldn't stop the whole scan -- skip it and
                // say so, the same "one bad file doesn't cost the batch" convention as every other
                // folder-scanning tool in this plugin already follows.
                int i = _grid.Rows.Add(Lang.T("(error)"), Path.GetFileName(f), ex.Message, "", "", "", "", "");
                _grid.Rows[i].DefaultCellStyle.ForeColor = Color.DarkRed;
            }
        }

        _entries = entries;
        _status.Text = string.Format(Lang.T("{0} recipes found."), entries.Count);
    }

    private void Export()
    {
        if (_entries.Count == 0)
        {
            MessageBox.Show(this, Lang.T("Nothing to export yet — pick a folder first."), Text);
            return;
        }
        using var d = new SaveFileDialog { Filter = "HTML|*.html", FileName = "load_book.html" };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        string html = LoadBookHtml.Render(_entries, Lang.T("Load Book"));
        File.WriteAllText(d.FileName, html);
        _status.Text = Lang.T("saved") + " " + Path.GetFileName(d.FileName);
        // Best-effort: opens in the default browser, where the user can print it (to PDF or paper)
        // exactly like TargetForm.SavePdf's "Microsoft Print to PDF" path -- no failure here should
        // undo the save that already succeeded above.
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.FileName) { UseShellExecute = true }); }
        catch { }
    }
}
