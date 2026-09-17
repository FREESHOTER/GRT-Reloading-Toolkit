using System.Globalization;
using GrtPluginKit.Grt;
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
    // Entered and shown in GRT's unit, stored in m/s. The ceilings convert with the boxes, or a
    // ft/s shooter could not type a 3000 ft/s load.
    private static readonly GrtUnits U = GrtUnits.Current;
    private static readonly decimal MvMax = (decimal)U.VelocityValue(3000), SdMax = (decimal)U.VelocityValue(500);
    private readonly NumericUpDown _mv = new() { DecimalPlaces = 1, Maximum = MvMax, Width = 90 };
    private readonly NumericUpDown _sd = new() { DecimalPlaces = 1, Maximum = SdMax, Width = 80 };
    private readonly NumericUpDown _grp = new() { DecimalPlaces = 2, Maximum = 50, Width = 80 };
    private readonly NumericUpDown _dist = new() { Maximum = 3000, Width = 90 };
    private readonly TextBox _notes = new() { Width = 380, Multiline = true, Height = 44, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _applyStock = new() { Text = "deduct components from inventory on save", Checked = true, AutoSize = true };
    private readonly Label _cost = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

    // Environmental conditions (GRT tracks none of these -- always manual) + the propellant
    // coefficients this session was calibrated to (auto-filled from the load by "Log from GRT" if
    // Barrel Calibration already wrote them there, editable either way) -- together let "Find best
    // Ba" later search by powder+bullet, filtered/sorted by group size, velocity or temperature.
    // "Recorded" checkbox, not a 0-means-unset sentinel: 0C is a real, plausible reading (unlike
    // Ba/pressure/humidity, where 0 is never a valid value and can safely mean "not entered").
    // Caught before it shipped, not after: a normal "> 0 ? value : null" pattern here would have
    // silently discarded every entry someone logged at exactly 0C.
    private readonly CheckBox _envRecorded = new() { Text = "environment recorded", AutoSize = true };
    // Metric AND imperial, per-field toggle -- same convention as every other unit choice this
    // session (GRT Sensitivity Lab, Seating Force): temperature in C or F, pressure in hPa or
    // inHg, each with its own dropdown, not one global switch.
    private readonly NumericUpDown _temp = new() { DecimalPlaces = 1, Minimum = -40, Maximum = 60, Width = 80 };
    private readonly ComboBox _tempUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50, Items = { "C", "F" } };
    private readonly NumericUpDown _pressure = new() { DecimalPlaces = 0, Minimum = 800, Maximum = 1100, Value = 1013, Width = 80 };
    private readonly ComboBox _pressureUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55, Items = { "hPa", "inHg" } };
    private readonly NumericUpDown _humidity = new() { DecimalPlaces = 0, Minimum = 0, Maximum = 100, Width = 90 };
    private readonly NumericUpDown _ba = new() { DecimalPlaces = 6, Increment = 0.001M, Minimum = 0, Maximum = 10, Width = 90 };
    private readonly NumericUpDown _a0 = new() { DecimalPlaces = 4, Increment = 0.001M, Minimum = 0, Maximum = 10, Width = 90 };
    private bool _tempWasF;
    private bool _pressureWasInHg;

    public JournalEntry Result => _e;
    public bool ApplyStock => _applyStock.Checked;

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, DisplayMember = "Text", ValueMember = "Id" };

    public JournalDialog(JournalEntry e, List<Component> components)
    {
        _e = e;
        _components = components;
        Text = e.Id == 0 ? "New journal entry" : "Edit journal entry";
        Ui.AppIcon.Apply(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        // 700 still clipped "Cost" and part of the stock checkbox off the bottom with the 5 new
        // environment/calibration rows -- caught by rendering the real dialog, not by counting
        // rows on paper. AutoScroll is a safety net on top of the taller size, same combination
        // used everywhere else this session.
        ClientSize = new Size(560, 820);
        AutoScroll = true;

        _date.Text = e.Date;
        _load.Text = e.LoadName; _cal.Text = e.Caliber; _firearm.Text = e.Firearm;
        _charge.Value = (decimal)Math.Min(500, e.ChargeGr);
        _rounds.Value = Math.Min(100000, e.Rounds);
        if (e.VelocityAvgMs is { } v) _mv.Value = Math.Min(MvMax, (decimal)U.VelocityValue(v));
        if (e.SdMs is { } s) _sd.Value = Math.Min(SdMax, (decimal)U.VelocityValue(s));
        if (e.GroupMoa is { } g) _grp.Value = (decimal)Math.Min(50, g);
        if (e.DistanceM is { } d) _dist.Value = (decimal)Math.Min(3000, d);
        _envRecorded.Checked = e.TemperatureC.HasValue || e.PressureHpa.HasValue || e.HumidityPct.HasValue;
        _tempUnit.SelectedIndex = 0; _pressureUnit.SelectedIndex = 0;
        if (e.TemperatureC is { } tc) _temp.Value = (decimal)Math.Clamp(tc, (double)_temp.Minimum, (double)_temp.Maximum);
        if (e.PressureHpa is { } ph) _pressure.Value = (decimal)Math.Clamp(ph, (double)_pressure.Minimum, (double)_pressure.Maximum);
        if (e.HumidityPct is { } hp) _humidity.Value = (decimal)Math.Clamp(hp, (double)_humidity.Minimum, (double)_humidity.Maximum);
        if (e.Ba is { } ba) _ba.Value = (decimal)Math.Min(10, ba);
        if (e.A0 is { } a0) _a0.Value = (decimal)Math.Min(10, a0);
        _tempUnit.SelectedIndexChanged += (_, _) => OnTempUnitChanged();
        _pressureUnit.SelectedIndexChanged += (_, _) => OnPressureUnitChanged();
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
        Row("MV " + U.VelocityUnitName, _mv);
        Row("SD " + U.VelocityUnitName, _sd);
        Row("Group MOA", _grp);
        Row("Distance m", _dist);
        Row("", _envRecorded);
        Row("Temperature", UnitField(_temp, _tempUnit));
        Row("Pressure", UnitField(_pressure, _pressureUnit));
        Row("Humidity %", _humidity);
        Row("Ba (calibrated)", _ba);
        Row("a0 (calibrated)", _a0);
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
            cb.Items.Add(new Item(c.Id, $"{c.Display}  ({c.QtyLeftText} left)"));
        cb.SelectedIndex = 0;
        if (selected is { } id)
            for (int i = 0; i < cb.Items.Count; i++)
                if (((Item)cb.Items[i]!).Id == id) { cb.SelectedIndex = i; break; }
    }

    private long? Sel(ComboBox cb) => cb.SelectedItem is Item { Id: > 0 } it ? it.Id : null;

    private static Control UnitField(NumericUpDown nud, ComboBox unit)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        row.Controls.Add(nud);
        unit.Margin = new Padding(4, 3, 0, 0);
        row.Controls.Add(unit);
        return row;
    }

    private static double CToF(double c) => c * 9.0 / 5.0 + 32.0;
    private static double FToC(double f) => (f - 32.0) * 5.0 / 9.0;
    private static double HpaToInHg(double hpa) => hpa * 0.0295299830714;
    private static double InHgToHpa(double inHg) => inHg / 0.0295299830714;

    private bool TempIsF => _tempUnit.SelectedIndex == 1;
    private bool PressureIsInHg => _pressureUnit.SelectedIndex == 1;

    /// <summary>The temperature field's current value, converted to Celsius (what the DB stores)
    /// regardless of which unit its own dropdown currently shows.</summary>
    private double CurrentTempC() => TempIsF ? FToC((double)_temp.Value) : (double)_temp.Value;

    /// <summary>Same idea for pressure, converted to hPa.</summary>
    private double CurrentPressureHpa() => PressureIsInHg ? InHgToHpa((double)_pressure.Value) : (double)_pressure.Value;

    /// <summary>
    /// Converts the temperature field between C and F when ITS OWN dropdown changes -- per-field,
    /// not a form-wide toggle. Reads the current value converted to the CANONICAL unit (C) BEFORE
    /// touching Minimum/Maximum/DecimalPlaces -- the same read-before-clamp-or-truncate hazard as
    /// every other per-field unit toggle in this codebase (narrowing Minimum/Maximum first clamps
    /// Value on the spot, so converting afterwards would convert the already-clamped number).
    /// </summary>
    private void OnTempUnitChanged()
    {
        bool toF = TempIsF;
        double c = _tempWasF ? FToC((double)_temp.Value) : (double)_temp.Value;
        _tempWasF = toF;

        double display = toF ? CToF(c) : c;
        _temp.Minimum = toF ? -40 : -40;   // -40 C == -40 F, so the low end is the same number either way
        _temp.Maximum = toF ? 140 : 60;
        _temp.Value = Math.Clamp((decimal)display, _temp.Minimum, _temp.Maximum);
    }

    private void OnPressureUnitChanged()
    {
        bool toInHg = PressureIsInHg;
        double hpa = _pressureWasInHg ? InHgToHpa((double)_pressure.Value) : (double)_pressure.Value;
        _pressureWasInHg = toInHg;

        double display = toInHg ? HpaToInHg(hpa) : hpa;
        _pressure.DecimalPlaces = toInHg ? 2 : 0;
        _pressure.Minimum = toInHg ? 23 : 800;
        _pressure.Maximum = toInHg ? 33 : 1100;
        _pressure.Value = Math.Clamp((decimal)display, _pressure.Minimum, _pressure.Maximum);
    }

    private void RecalcCost()
    {
        var probe = new JournalEntry
        {
            PowderId = Sel(_powder), PrimerId = Sel(_primer), BrassId = Sel(_brass), BulletId = Sel(_bullet),
            ChargeGr = (double)_charge.Value, Rounds = (int)_rounds.Value,
        };
        var cb = Costing.PerRound(probe, id => _components.FirstOrDefault(c => c.Id == id));
        double total = cb.PerRound * probe.Rounds;
        // The bare component figures stay either way -- they are each right in their own lot's
        // currency, and this is the window where you can see which lot to change.
        string lines = $"(powder {cb.Powder:0.000}  primer {cb.Primer:0.000}  bullet {cb.Bullet:0.000}  brass {cb.Brass:0.000})";
        _cost.Text = cb.Mixed
            ? $"{cb.MixedNote} — no total until the lots match   {lines}"
            : $"{Costing.Format(cb.PerRound, cb.Currency)} / round   " + lines +
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
        _e.VelocityAvgMs = _mv.Value > 0 ? U.VelocityToMps((double)_mv.Value) : null;
        _e.SdMs = _sd.Value > 0 ? U.VelocityToMps((double)_sd.Value) : null;
        _e.GroupMoa = _grp.Value > 0 ? (double)_grp.Value : null;
        _e.DistanceM = _dist.Value > 0 ? (double)_dist.Value : null;
        _e.TemperatureC = _envRecorded.Checked ? CurrentTempC() : null;
        _e.PressureHpa = _envRecorded.Checked ? CurrentPressureHpa() : null;
        _e.HumidityPct = _envRecorded.Checked ? (double)_humidity.Value : null;
        _e.Ba = _ba.Value > 0 ? (double)_ba.Value : null;
        _e.A0 = _a0.Value > 0 ? (double)_a0.Value : null;
        _e.Notes = _notes.Text.Trim();
    }

    private sealed record Item(long Id, string Text) { public override string ToString() => Text; }
}
