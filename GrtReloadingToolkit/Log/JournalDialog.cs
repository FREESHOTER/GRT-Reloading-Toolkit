using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Log;

internal sealed class JournalDialog : Form
{
    private readonly JournalEntry _e;
    private readonly List<Component> _components;

    private readonly TextBox _date = new() { Width = 100 };
    private readonly TextBox _load = new() { Width = 240 };
    private readonly TextBox _cal = new() { Width = 160 };
    private readonly TextBox _firearm = new() { Width = 160 };
    private readonly ComboBox _powder = Combo(), _primer = Combo(), _brass = Combo(), _bullet = Combo();
    private readonly NumericUpDown _charge = new() { DecimalPlaces = 2, Maximum = 500, Width = 90 };
    private readonly NumericUpDown _rounds = new() { Maximum = 100000, Width = 90 };
    private readonly NumericUpDown _mv = new() { DecimalPlaces = 1, Maximum = 3000, Width = 90 };
    private readonly NumericUpDown _sd = new() { DecimalPlaces = 1, Maximum = 500, Width = 80 };
    private readonly NumericUpDown _grp = new() { DecimalPlaces = 2, Maximum = 50, Width = 80 };
    private readonly NumericUpDown _dist = new() { Maximum = 3000, Width = 90 };
    private readonly TextBox _notes = new() { Width = 380, Multiline = true, Height = 44, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _applyStock = new() { Text = "deduct components from inventory on save", Checked = true, AutoSize = true };
    private readonly Label _cost = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

    public JournalEntry Result => _e;
    public bool ApplyStock => _applyStock.Checked;

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DisplayMember = "Text", ValueMember = "Id" };

    public JournalDialog(JournalEntry e, List<Component> components)
    {
        _e = e;
        _components = components;
        Text = e.Id == 0 ? "New journal entry" : "Edit journal entry";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(560, 560);

        _date.Text = e.Date;
        _load.Text = e.LoadName; _cal.Text = e.Caliber; _firearm.Text = e.Firearm;
        _charge.Value = (decimal)Math.Min(500, e.ChargeGr);
        _rounds.Value = Math.Min(100000, e.Rounds);
        if (e.VelocityAvgMs is { } v) _mv.Value = (decimal)Math.Min(3000, v);
        if (e.SdMs is { } s) _sd.Value = (decimal)Math.Min(500, s);
        if (e.GroupMoa is { } g) _grp.Value = (decimal)Math.Min(50, g);
        if (e.DistanceM is { } d) _dist.Value = (decimal)Math.Min(3000, d);
        _notes.Text = e.Notes;
        _applyStock.Checked = e.Id == 0;

        FillCombo(_powder, ComponentKind.Powder, e.PowderId);
        FillCombo(_primer, ComponentKind.Primer, e.PrimerId);
        FillCombo(_brass, ComponentKind.Brass, e.BrassId);
        FillCombo(_bullet, ComponentKind.Bullet, e.BulletId);

        foreach (var c in new Control[] { _charge, _rounds, _powder, _primer, _brass, _bullet })
            if (c is ComboBox cb) cb.SelectedIndexChanged += (_, _) => RecalcCost();
            else ((NumericUpDown)c).ValueChanged += (_, _) => RecalcCost();

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        void Row(string l, Control ctl) { t.Controls.Add(new Label { Text = l, AutoSize = true, Padding = new Padding(0, 6, 8, 0) }); t.Controls.Add(ctl); }
        Row("Date", _date);
        Row("Load name", _load);
        Row("Caliber", _cal);
        Row("Firearm", _firearm);
        Row("Powder", _powder);
        Row("Primer", _primer);
        Row("Brass", _brass);
        Row("Bullet", _bullet);
        Row("Charge gr", _charge);
        Row("Rounds", _rounds);
        Row("MV m/s", _mv);
        Row("SD m/s", _sd);
        Row("Group MOA", _grp);
        Row("Distance m", _dist);
        Row("Notes", _notes);
        Row("", _applyStock);
        Row("Cost", _cost);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        ok.Click += (_, _) => Commit();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(t);
        Controls.Add(buttons);
        Ui.NudFix.ApplyTo(this);
        RecalcCost();
    }

    private void FillCombo(ComboBox cb, ComponentKind kind, long? selected)
    {
        cb.Items.Add(new Item(0, "— none —"));
        foreach (var c in _components.Where(x => x.Kind == kind))
            cb.Items.Add(new Item(c.Id, $"{c.Display}  ({c.QtyCurrent:0.#} {c.Unit} left)"));
        cb.SelectedIndex = 0;
        if (selected is { } id)
            for (int i = 0; i < cb.Items.Count; i++)
                if (((Item)cb.Items[i]!).Id == id) { cb.SelectedIndex = i; break; }
    }

    private long? Sel(ComboBox cb) => cb.SelectedItem is Item { Id: > 0 } it ? it.Id : null;

    private void RecalcCost()
    {
        var probe = new JournalEntry
        {
            PowderId = Sel(_powder), PrimerId = Sel(_primer), BrassId = Sel(_brass), BulletId = Sel(_bullet),
            ChargeGr = (double)_charge.Value, Rounds = (int)_rounds.Value,
        };
        var cb = Costing.PerRound(probe, id => _components.FirstOrDefault(c => c.Id == id));
        double total = cb.PerRound * probe.Rounds;
        _cost.Text = $"{Costing.Format(cb.PerRound, cb.Currency)} / round   " +
                     $"(powder {cb.Powder:0.000}  primer {cb.Primer:0.000}  bullet {cb.Bullet:0.000}  brass {cb.Brass:0.000})" +
                     (probe.Rounds > 0 ? $"   →  {Costing.Format(total, cb.Currency)} for {probe.Rounds}" : "");
    }

    private void Commit()
    {
        _e.Date = _date.Text.Trim();
        _e.LoadName = _load.Text.Trim();
        _e.Caliber = _cal.Text.Trim();
        _e.Firearm = _firearm.Text.Trim();
        _e.PowderId = Sel(_powder); _e.PrimerId = Sel(_primer); _e.BrassId = Sel(_brass); _e.BulletId = Sel(_bullet);
        _e.ChargeGr = (double)_charge.Value;
        _e.Rounds = (int)_rounds.Value;
        _e.VelocityAvgMs = _mv.Value > 0 ? (double)_mv.Value : null;
        _e.SdMs = _sd.Value > 0 ? (double)_sd.Value : null;
        _e.GroupMoa = _grp.Value > 0 ? (double)_grp.Value : null;
        _e.DistanceM = _dist.Value > 0 ? (double)_dist.Value : null;
        _e.Notes = _notes.Text.Trim();
    }

    private sealed record Item(long Id, string Text) { public override string ToString() => Text; }
}
