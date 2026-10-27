using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The person whose work the toolkit shows. The shooter reloads for the club too, so inventory, firearms, journal and followed loads each
/// belong to one person; the launcher's picker narrows every tool to one of them ("Everyone" shows all, and tells whose each row is).
/// What is picked is remembered between runs.
/// </summary>
internal static class ShooterState
{
    private const string Key = "shooter.current";

    /// <summary>The remembered person, or null for everybody (also when that person has been archived or no longer exists).</summary>
    public static long? Saved(Db db)
    {
        if (!long.TryParse(UiState.Get(Key, ""), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id)) return null;
        return db.Shooters().Any(s => s.Id == id) ? id : null;
    }

    public static void Save(long? id) => UiState.Set(Key, id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");

    /// <summary>Id → name for every person, archived ones included (a row of a person archived later still has an owner).</summary>
    public static Dictionary<long, string> Names(Db db) => db.Shooters(includeArchived: true).ToDictionary(s => s.Id, s => s.Name);

    /// <summary>Whether the lists should add a "Shooter" column: everybody is shown and there is more than one person to tell apart.</summary>
    public static bool ShowColumn(Db db) => db.CurrentShooterId is null && db.Shooters(includeArchived: true).Count > 1;
}

/// <summary>The picker at the top of the launcher: choose the person, or manage the list.</summary>
internal sealed class ShooterBar : Panel
{
    private readonly Db _db;
    private readonly Action<long?> _changed;
    private readonly ComboBox _combo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Label _note = new() { AutoSize = true, ForeColor = SystemColors.GrayText, UseMnemonic = false };
    private bool _loading;

    public ShooterBar(Db db, Action<long?> changed)
    {
        _db = db; _changed = changed;
        Height = 54;
        var title = new Label { Text = Lang.T("Shooter"), AutoSize = true, UseMnemonic = false, Left = 2, Top = 7 };
        _combo.Left = 62; _combo.Top = 3;
        var manage = new Button { Text = Lang.T("Manage…"), AutoSize = true, Top = 2, Left = 218 };
        _note.Left = 2; _note.Top = 33;
        manage.Click += (_, _) =>
        {
            using var d = new ShooterDialog(_db);
            d.ShowDialog(FindForm());
            Reload(_db.CurrentShooterId);
        };
        _combo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            long? id = (_combo.SelectedItem as Shooter)?.Id;
            ShooterState.Save(id);
            Describe(id);
            _changed(id);
        };
        Controls.AddRange(new Control[] { title, _combo, manage, _note });
        Reload(_db.CurrentShooterId);
    }

    /// <summary>Fills the list; keeps <paramref name="select"/> picked when it still exists, falls back to everybody.</summary>
    private void Reload(long? select)
    {
        _loading = true;
        _combo.Items.Clear();
        _combo.Items.Add(new Shooter { Id = 0, Name = Lang.T("Everyone") });
        foreach (var s in _db.Shooters()) _combo.Items.Add(s);
        int at = select is { } id ? _combo.Items.Cast<Shooter>().ToList().FindIndex(s => s.Id == id) : 0;
        _combo.SelectedIndex = Math.Max(0, at);
        _loading = false;
        long? now = (_combo.SelectedItem as Shooter) is { Id: > 0 } sel ? sel.Id : null;
        Describe(now);
        if (now != _db.CurrentShooterId) { ShooterState.Save(now); _changed(now); }
    }

    private void Describe(long? id)
    {
        _note.Text = id is null
            ? string.Format(Lang.T("Everything is shown; new rows go to {0}."), ShooterState.Names(_db).GetValueOrDefault(1, "?"))
            : Lang.T("Only this person's inventory, firearms, journal and followed loads.");
    }
}

/// <summary>Add, rename and archive the people in the list, and see how much each one holds.</summary>
internal sealed class ShooterDialog : Form
{
    private readonly Db _db;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public ShooterDialog(Db db)
    {
        _db = db;
        Text = Lang.T("Shooters");
        StartPosition = FormStartPosition.CenterParent;
        Width = 560; Height = 360;
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (_, _) => a(); bar.Controls.Add(b); return b; }
        B(Lang.T("Add"), Add);
        B(Lang.T("Rename"), Rename);
        B(Lang.T("Archive / restore"), ToggleArchive);
        B(Lang.T("Close"), Close);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0), ForeColor = SystemColors.GrayText, UseMnemonic = false,
            Text = Lang.T("Each person keeps their own inventory, firearms, journal and followed loads. To move something already entered, use \"Assign to…\" in the Journal and in the Inventory."),
        };
        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(bar);
        CancelButton = null;
        Fill();
    }

    private Shooter? Selected => (_list.SelectedItem as Row)?.Person;

    private sealed record Row(Shooter Person, string Text) { public override string ToString() => Text; }

    private void Fill()
    {
        int keep = Math.Max(0, _list.SelectedIndex);
        _list.Items.Clear();
        foreach (var s in _db.Shooters(includeArchived: true))
        {
            var (c, f, j) = _db.ShooterCounts(s.Id);
            _list.Items.Add(new Row(s, string.Format(Lang.T("{0}{1}   —   {2} components, {3} firearms, {4} journal entries"),
                s.Name, s.Archived ? " " + Lang.T("(archived)") : "", c, f, j)));
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(keep, _list.Items.Count - 1);
    }

    private void Add()
    {
        string? name = Ask(Lang.T("Name of the new person"), "");
        if (name == null) return;
        if (_db.Shooters(includeArchived: true).Any(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, Lang.T("There is already someone with this name."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _db.UpsertShooter(new Shooter { Name = name });
        Fill();
        _list.SelectedIndex = _list.Items.Count - 1;
    }

    private void Rename()
    {
        if (Selected is not { } s) return;
        string? name = Ask(Lang.T("New name"), s.Name);
        if (name == null) return;
        s.Name = name.Trim();
        _db.UpsertShooter(s);
        Fill();
    }

    private void ToggleArchive()
    {
        if (Selected is not { } s) return;
        if (s.Id == 1 && !s.Archived)
        {
            MessageBox.Show(this, Lang.T("The first person receives everything entered while \"Everyone\" is shown: it cannot be archived."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        s.Archived = !s.Archived;
        _db.UpsertShooter(s);
        Fill();
    }

    private string? Ask(string caption, string value)
    {
        using var d = new Form
        {
            Text = caption, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(320, 84),
        };
        var box = new TextBox { Left = 10, Top = 12, Width = 298, Text = value };
        var ok = new Button { Text = Lang.T("OK"), DialogResult = DialogResult.OK, Left = 148, Top = 46, Width = 75 };
        var ca = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 233, Top = 46, Width = 75 };
        d.Controls.AddRange(new Control[] { box, ok, ca });
        d.AcceptButton = ok; d.CancelButton = ca;
        GrtTheme.Apply(d);
        return d.ShowDialog(this) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}

/// <summary>"Assign to…": asks which person the selected rows should belong to.</summary>
internal static class ShooterPicker
{
    /// <summary>Returns the chosen person's id, or null when cancelled.</summary>
    public static long? Ask(IWin32Window owner, Db db, string caption)
    {
        var people = db.Shooters();
        using var d = new Form
        {
            Text = caption, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(320, 84),
        };
        var combo = new ComboBox { Left = 10, Top = 12, Width = 298, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in people) combo.Items.Add(s);
        if (combo.Items.Count == 0) return null;
        combo.SelectedIndex = 0;
        var ok = new Button { Text = Lang.T("OK"), DialogResult = DialogResult.OK, Left = 148, Top = 46, Width = 75 };
        var ca = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 233, Top = 46, Width = 75 };
        d.Controls.AddRange(new Control[] { combo, ok, ca });
        d.AcceptButton = ok; d.CancelButton = ca;
        GrtTheme.Apply(d);
        return d.ShowDialog(owner) == DialogResult.OK && combo.SelectedItem is Shooter pick ? pick.Id : null;
    }
}
