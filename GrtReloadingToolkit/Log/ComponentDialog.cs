using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Log;

internal sealed class ComponentDialog : Form
{
    private readonly Component _c;
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly TextBox _brand = new() { Width = 160 };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly TextBox _lot = new() { Width = 120 };
    private readonly NumericUpDown _qtyInit = new() { DecimalPlaces = 1, Maximum = 1_000_000, Width = 110 };
    private readonly NumericUpDown _qtyCur = new() { DecimalPlaces = 1, Maximum = 1_000_000, Width = 110 };
    private readonly NumericUpDown _cost = new() { DecimalPlaces = 2, Maximum = 1_000_000, Width = 110 };
    private readonly TextBox _currency = new() { Width = 60, Text = "EUR" };
    private readonly NumericUpDown _expUses = new() { Minimum = 1, Maximum = 100, Width = 70, Value = 1 };
    private readonly NumericUpDown _bulletW = new() { DecimalPlaces = 1, Maximum = 1000, Width = 90 };
    private readonly TextBox _notes = new() { Width = 320 };
    private readonly Label _unitInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public Component Result => _c;

    public ComponentDialog(Component c)
    {
        _c = c;
        Text = c.Id == 0 ? "Add component" : "Edit component";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(470, 340);

        foreach (var k in Enum.GetValues<ComponentKind>()) _kind.Items.Add(k);
        _kind.SelectedItem = c.Kind;
        _kind.SelectedIndexChanged += (_, _) => SyncUnit();
        _brand.Text = c.Brand; _name.Text = c.Name; _lot.Text = c.Lot;
        _qtyInit.Value = (decimal)Math.Min(1_000_000, c.QtyInitial);
        _qtyCur.Value = (decimal)Math.Min(1_000_000, c.Id == 0 ? c.QtyInitial : c.QtyCurrent);
        _cost.Value = (decimal)Math.Min(1_000_000, c.CostTotal);
        _currency.Text = string.IsNullOrWhiteSpace(c.Currency) ? "EUR" : c.Currency;
        _expUses.Value = Math.Clamp(c.ExpectedUses, 1, 100);
        _bulletW.Value = (decimal)(c.BulletWeightGr ?? 0);
        _notes.Text = c.Notes;
        _qtyInit.ValueChanged += (_, _) => { if (c.Id == 0) _qtyCur.Value = _qtyInit.Value; };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        void Row(string l, Control ctl) { t.Controls.Add(new Label { Text = l, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }); t.Controls.Add(ctl); }
        Row("Kind", _kind);
        Row("Brand", _brand);
        Row("Name", _name);
        Row("Lot", _lot);
        Row("Qty initial", Flow(_qtyInit, _unitInfo));
        Row("Qty current", _qtyCur);
        Row("Lot cost", Flow(_cost, _currency));
        Row("Expected uses", Flow(_expUses, new Label { Text = "(brass: firings before retirement)", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 4, 0, 0) }));
        Row("Bullet weight gr", _bulletW);
        Row("Notes", _notes);

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        ok.Click += (_, _) => Commit();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(t);
        Controls.Add(buttons);
        Ui.NudFix.ApplyTo(this);
        SyncUnit();
    }

    private static Control Flow(params Control[] c)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        f.Controls.AddRange(c);
        return f;
    }

    private void SyncUnit()
    {
        bool powder = (ComponentKind)_kind.SelectedItem! == ComponentKind.Powder;
        bool bullet = (ComponentKind)_kind.SelectedItem! == ComponentKind.Bullet;
        bool brass = (ComponentKind)_kind.SelectedItem! == ComponentKind.Brass;
        _unitInfo.Text = powder ? "grams" : "pieces";
        _bulletW.Enabled = bullet;
        _expUses.Enabled = brass;
        if (!brass && _c.Id == 0) _expUses.Value = 1;
    }

    private void Commit()
    {
        _c.Kind = (ComponentKind)_kind.SelectedItem!;
        _c.Brand = _brand.Text.Trim();
        _c.Name = _name.Text.Trim();
        _c.Lot = _lot.Text.Trim();
        _c.Unit = _c.Kind == ComponentKind.Powder ? "g" : "pcs";
        _c.QtyInitial = (double)_qtyInit.Value;
        _c.QtyCurrent = (double)_qtyCur.Value;
        _c.CostTotal = (double)_cost.Value;
        _c.Currency = _currency.Text.Trim().ToUpperInvariant();
        _c.ExpectedUses = _c.Kind == ComponentKind.Brass ? (int)_expUses.Value : 1;
        _c.BulletWeightGr = _c.Kind == ComponentKind.Bullet && _bulletW.Value > 0 ? (double)_bulletW.Value : null;
        _c.Notes = _notes.Text.Trim();
        if (_c.Name.Length == 0) { DialogResult = DialogResult.None; MessageBox.Show(this, "Name is required."); }
    }
}
