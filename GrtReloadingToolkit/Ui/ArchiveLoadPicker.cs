using GrtReloadingToolkit.Archive;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// "Load in the archive:" — the load an import is filed under: the one open in GRT (when GRT started the program), one of
/// the shooter's archive loads, a load read from a GRT file (all of it: components, charge, COAL, the file itself), or a
/// new one typed on the spot. Without GRT nothing is picked by default: a session filed
/// on a load nobody chose is worse than an import that asks.
/// </summary>
internal sealed class ArchiveLoadPicker : FlowLayoutPanel
{
    private readonly ArchiveLink _link;
    private readonly bool _grtAvailable;
    private readonly ComboBox _combo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, DropDownWidth = 560 };
    private bool _choosing;

    private sealed record Choice(long? Id, string Label, bool FromGrt = false, bool New = false, bool FromFile = false)
    {
        public override string ToString() => Label;
    }

    public ArchiveLoadPicker(ArchiveLink link, bool grtAvailable)
    {
        _link = link;
        _grtAvailable = grtAvailable;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(12, 2, 0, 0);
        Controls.Add(new Label { Text = Lang.T("Load in the archive:"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        Controls.Add(_combo);
        _combo.SelectedIndexChanged += (_, _) => Picked();
        Refill();
    }

    public bool IsFromGrt => _combo.SelectedItem is Choice { FromGrt: true };
    public bool IsNone => _combo.SelectedItem is Choice { Id: null, FromGrt: false };

    /// <summary>
    /// "From a GRT file (.grtload)…": the shooter picks the file of a load and it enters the archive as an import from GRT
    /// would file it (the same load again when that file is already there). Null when nothing was picked.
    /// </summary>
    public static long? PickGrtFile(IWin32Window? owner, ArchiveDb db, long shooterId, List<string> log)
    {
        using var d = new OpenFileDialog { Filter = Lang.T("GRT load") + " (*.grtload)|*.grtload", Title = Lang.T("From a GRT file (.grtload)…") };
        // where the last GRT load came from, so the dialog opens among the shooter's loads
        if (db.Scalar("SELECT grtload_path FROM loads WHERE IFNULL(grtload_path,'')<>'' ORDER BY id DESC LIMIT 1") is string last
            && Path.GetDirectoryName(last) is { } dir && Directory.Exists(dir))
            d.InitialDirectory = dir;
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        try { return ArchiveImports.LoadForGrtFile(db, d.FileName, shooterId, log); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, Lang.T("From a GRT file (.grtload)…"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }
    public long? LoadId => (_combo.SelectedItem as Choice)?.Id;

    public void Refill(long? select = null)
    {
        _combo.BeginUpdate();
        _combo.Items.Clear();
        _combo.Items.Add(_grtAvailable ? new Choice(null, Lang.T("(the load open in GRT)"), FromGrt: true) : new Choice(null, Lang.T("(choose the load…)")));
        foreach (var (id, label) in ArchiveImports.LoadsOf(_link.Db, _link.ShooterId()))
            _combo.Items.Add(new Choice(id, label));
        _combo.Items.Add(new Choice(null, Lang.T("From a GRT file (.grtload)…"), FromFile: true));
        _combo.Items.Add(new Choice(null, Lang.T("New load…"), New: true));
        _combo.EndUpdate();
        int idx = select is { } s ? _combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == s) : -1;
        _choosing = true;
        _combo.SelectedIndex = idx >= 0 ? idx : 0;
        _choosing = false;
    }

    private void Picked()
    {
        if (_choosing || _combo.SelectedItem is not Choice c || !(c.New || c.FromFile)) return;
        _choosing = true;
        try
        {
            if (c.FromFile)
            {
                long? fromFile = PickGrtFile(FindForm(), _link.Db, _link.ShooterId(), new List<string>());
                if (fromFile == null) { _combo.SelectedIndex = 0; return; }
                _choosing = false;
                Refill(fromFile);
                return;
            }
            var (name, caliber) = AskNewLoad();
            if (name.Length == 0) { _combo.SelectedIndex = 0; return; }
            long id = ArchiveImports.NewLoad(_link.Db, _link.ShooterId(), name, caliber);
            _choosing = false;
            Refill(id);
        }
        finally { _choosing = false; }
    }

    private (string Name, string Caliber) AskNewLoad()
    {
        using var f = new Form
        {
            Text = Lang.T("New load"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 130),
        };
        var name = new TextBox { Left = 100, Top = 14, Width = 300 };
        var cal = new TextBox { Left = 100, Top = 46, Width = 300 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 244, Top = 88, Width = 75 };
        var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 325, Top = 88, Width = 75 };
        f.Controls.AddRange(new Control[]
        {
            new Label { Text = Lang.T("Name"), Left = 12, Top = 17, AutoSize = true }, name,
            new Label { Text = Lang.T("Caliber"), Left = 12, Top = 49, AutoSize = true }, cal, ok, cancel,
        });
        f.AcceptButton = ok; f.CancelButton = cancel;
        GrtTheme.Apply(f);
        return f.ShowDialog(FindForm()) == DialogResult.OK ? (name.Text.Trim(), cal.Text.Trim()) : ("", "");
    }
}
