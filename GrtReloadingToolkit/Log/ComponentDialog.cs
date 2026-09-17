using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Log;

internal sealed class ComponentDialog : Form
{
    private readonly Component _c;
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    // Editable, not a whitelist: the shortlist covers the makers most shooters hold and anything
    // else is still typed in. Same bargain as the currency box.
    private readonly ComboBox _brand = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 160 };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly TextBox _lot = new() { Width = 120 };
    // Stock goes negative and that is not a corruption: AdjustStock has no floor, the restock
    // prompt offers "negative to correct down", and logging rounds against a lot you never
    // recorded buying leaves you owing the ledger. The spinner has to be able to show that,
    // or the edit dialog throws on the way up and a 0 floor would quietly zero the debt on save.
    private readonly NumericUpDown _qtyInit = new() { DecimalPlaces = 1, Minimum = -1_000_000, Maximum = 1_000_000, Width = 110 };
    private readonly NumericUpDown _qtyCur = new() { DecimalPlaces = 1, Minimum = -1_000_000, Maximum = 1_000_000, Width = 110 };
    private readonly NumericUpDown _cost = new() { DecimalPlaces = 2, Maximum = 1_000_000, Width = 110 };
    private readonly ComboBox _currency = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 80 };
    // Powder is stored in grams and bought in pounds or kilos, so the count the user types needs a
    // unit beside it. Every other kind has exactly one, which is why this disables itself.
    private readonly ComboBox _qtyUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly NumericUpDown _expUses = new() { Minimum = 1, Maximum = 100, Width = 70, Value = 1 };
    // Stored in grains, shown and typed in whatever GRT weighs a projectile in -- mp, which a
    // user sets apart from the powder charge. Grams want two places where grains want one: a
    // bullet comes in whole grains, or tenths of a gram.
    private static readonly GrtUnits BulletU = GrtUnits.Current;
    private static readonly decimal BulletWMax = (decimal)BulletU.BulletMassValue(1000);
    private readonly NumericUpDown _bulletW = new()
    {
        DecimalPlaces = BulletU.BulletMassInGrams ? 2 : 1,
        Maximum = BulletWMax,
        Width = 90,
    };
    // A twist is written as the denominator of 1:n, so this holds n in GRT's twist unit. Three
    // places carry the 1:7.875 that exists without pretending a barrel is measured to a micron.
    private readonly NumericUpDown _twist = new() { DecimalPlaces = 3, Maximum = 1000, Width = 90 };
    // Four places, like every other length the toolkit shows -- GRT does not round one off either.
    private readonly NumericUpDown _barrelLen = new() { DecimalPlaces = 4, Maximum = 10_000, Width = 110 };
    private readonly Label _twistUnit = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 4, 0, 0) };
    private readonly Label _lenUnit = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 4, 0, 0) };
    private readonly TextBox _notes = new() { Width = 320 };
    private readonly Label _unitInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 4, 0, 0) };

    /// <summary>What the two quantity spinners are currently counting in.</summary>
    private string _unit;

    /// <summary>Set while the combos are being refilled, so their events do not read as user edits.</summary>
    private bool _loading;

    public Component Result => _c;

    public ComponentDialog(Component c)
    {
        _c = c;
        _unit = string.IsNullOrWhiteSpace(c.Unit) ? StockUnit.DefaultFor(c.Kind) : c.Unit.Trim();
        Text = Ui.Lang.T(c.Id == 0 ? "Add component" : "Edit component");
        Ui.AppIcon.Apply(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(470, 400);

        foreach (var k in Enum.GetValues<ComponentKind>()) _kind.Items.Add(k);
        _kind.SelectedItem = c.Kind;
        _kind.SelectedIndexChanged += (_, _) => SyncUnit();
        _qtyUnit.SelectedIndexChanged += (_, _) => ChangeUnit();
        _brand.Text = c.Brand; _name.Text = c.Name; _lot.Text = c.Lot;
        // The store holds powder in grams; these two boxes hold whatever the lot is counted in.
        double init = StockUnit.FromStore(c.QtyInitial, _unit);
        Ui.NudFix.Set(_qtyInit, init);
        Ui.NudFix.Set(_qtyCur, c.Id == 0 ? init : StockUnit.FromStore(c.QtyCurrent, _unit));
        Ui.NudFix.Set(_cost, c.CostTotal);
        foreach (string cur in Money.Common) _currency.Items.Add(cur);
        _currency.Text = string.IsNullOrWhiteSpace(c.Currency) ? Money.Default : c.Currency;
        _expUses.Value = Math.Clamp(c.ExpectedUses, 1, 100);
        Ui.NudFix.Set(_bulletW, c.BulletWeightGr is { } bw ? BulletU.BulletMassValue(bw) : 0);
        // Stored in mm, shown in whatever GRT shows a twist and a length in.
        var u = GrtPluginKit.Grt.GrtUnits.Current;
        Ui.NudFix.Set(_twist, c.TwistMm is { } tw ? u.TwistValue(tw) : 0);
        Ui.NudFix.Set(_barrelLen, c.BarrelLengthMm is { } bl ? u.LengthValue(bl) : 0);
        _twistUnit.Text = u.TwistUnitName;
        _lenUnit.Text = u.LengthUnitName;
        _notes.Text = c.Notes;
        _qtyInit.ValueChanged += (_, _) => { if (c.Id == 0) _qtyCur.Value = _qtyInit.Value; };

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        void Row(string l, Control ctl) { t.Controls.Add(new Label { Text = l, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) }); t.Controls.Add(ctl); }
        Row(Ui.Lang.T("Kind"), _kind);
        Row(Ui.Lang.T("Brand"), _brand);
        Row(Ui.Lang.T("Name"), _name);
        Row(Ui.Lang.T("Lot"), _lot);
        Row(Ui.Lang.T("Qty initial"), Flow(_qtyInit, _qtyUnit));
        Row(Ui.Lang.T("Qty current"), Flow(_qtyCur, _unitInfo));
        Row(Ui.Lang.T("Lot cost"), Flow(_cost, _currency));
        Row(Ui.Lang.T("Expected uses"), Flow(_expUses, new Label { Text = Ui.Lang.T("(brass: firings before retirement)"), AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 4, 0, 0) }));
        Row(Ui.Lang.T("Bullet weight") + " " + BulletU.BulletMassUnitName, _bulletW);
        Row(Ui.Lang.T("Twist 1:"), Flow(_twist, _twistUnit));
        Row(Ui.Lang.T("Barrel length"), Flow(_barrelLen, _lenUnit));
        Row(Ui.Lang.T("Notes"), _notes);

        var ok = new Button { Text = Ui.Lang.T("Save"), DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = Ui.Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Width = 80 };
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

    /// <summary>Refills everything that follows the kind: brand suggestions, units, and which
    /// of the kind-specific fields apply.</summary>
    private void SyncUnit()
    {
        var kind = (ComponentKind)_kind.SelectedItem!;
        bool brass = kind == ComponentKind.Brass;

        _loading = true;
        string typed = _brand.Text;              // a brand already typed survives the swap
        _brand.Items.Clear();
        foreach (string b in Brands.For(kind)) _brand.Items.Add(b);
        _brand.Text = typed;

        var units = StockUnit.For(kind);
        // Match case-insensitively so a row stored as "G" selects "g" instead of nothing, and fall
        // back when the kind changes out from under a unit that no longer applies.
        _unit = units.FirstOrDefault(u => string.Equals(u, _unit, StringComparison.OrdinalIgnoreCase))
                ?? StockUnit.DefaultFor(kind);
        _qtyUnit.Items.Clear();
        foreach (string u in units) _qtyUnit.Items.Add(u);
        _qtyUnit.SelectedItem = _unit;
        _qtyUnit.Enabled = units.Count > 1;
        _loading = false;

        ShowUnit();
        _bulletW.Enabled = kind == ComponentKind.Bullet;
        _twist.Enabled = _barrelLen.Enabled = kind == ComponentKind.Barrel;
        _expUses.Enabled = brass;
        if (!brass && _c.Id == 0) _expUses.Value = 1;
    }

    /// <summary>
    /// The lot did not change, only the label on it: 1 lb and 453.6 g are the same powder, so the
    /// number in the box is converted rather than reinterpreted.
    /// </summary>
    private void ChangeUnit()
    {
        if (_loading || _qtyUnit.SelectedItem is not string to || to == _unit) return;
        double init = StockUnit.FromStore(StockUnit.ToStore((double)_qtyInit.Value, _unit), to);
        double cur = StockUnit.FromStore(StockUnit.ToStore((double)_qtyCur.Value, _unit), to);
        _unit = to;
        ShowUnit();
        Ui.NudFix.Set(_qtyInit, init);
        Ui.NudFix.Set(_qtyCur, cur);
    }

    private void ShowUnit()
    {
        _unitInfo.Text = _unit;
        // A pound of powder goes down by about 0.006 of one per round, so one place would show a
        // full loading session as no change at all.
        _qtyInit.DecimalPlaces = _qtyCur.DecimalPlaces = StockUnit.Decimals(_unit);
    }

    private void Commit()
    {
        _c.Kind = (ComponentKind)_kind.SelectedItem!;
        _c.Brand = _brand.Text.Trim();
        _c.Name = _name.Text.Trim();
        _c.Lot = _lot.Text.Trim();
        _c.Unit = _unit;
        _c.QtyInitial = StockUnit.ToStore((double)_qtyInit.Value, _unit);
        _c.QtyCurrent = StockUnit.ToStore((double)_qtyCur.Value, _unit);
        _c.CostTotal = (double)_cost.Value;
        // An empty box means "leave it alone", not "blank the column": the grid and the
        // cost-per-round line both print this string straight through.
        string cur = _currency.Text.Trim().ToUpperInvariant();
        _c.Currency = cur.Length > 0 ? cur : Money.Default;
        _c.ExpectedUses = _c.Kind == ComponentKind.Brass ? (int)_expUses.Value : 1;
        _c.BulletWeightGr = _c.Kind == ComponentKind.Bullet && _bulletW.Value > 0 ? BulletU.BulletMassToGrains((double)_bulletW.Value) : null;
        // Zero means "not recorded", the same bargain the bullet weight makes: a barrel with no
        // twist measured yet is a barrel you still want in the inventory.
        var units = GrtPluginKit.Grt.GrtUnits.Current;
        bool barrel = _c.Kind == ComponentKind.Barrel;
        _c.TwistMm = barrel && _twist.Value > 0 ? units.TwistToMm((double)_twist.Value) : null;
        _c.BarrelLengthMm = barrel && _barrelLen.Value > 0 ? units.LengthToMm((double)_barrelLen.Value) : null;
        _c.Notes = _notes.Text.Trim();
        if (_c.Name.Length == 0) { DialogResult = DialogResult.None; MessageBox.Show(this, Ui.Lang.T("Name is required.")); }
    }
}
