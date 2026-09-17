using System.Globalization;
using GrtReloadingToolkit.Brass;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Estimates expected bullet-seating (press-fit) force from neck-tension prep parameters, for a QC
/// press's own "max pressure" safety threshold -- see <see cref="SeatingForceCalc"/> for the math
/// and why it is NOT tied to any GRT input. Its own top-level tool (not a tab inside
/// <see cref="BrassForm"/>) at the user's explicit request, given its importance as a standalone QC
/// step -- promoted from a 4th Brass Prep tab 2026-09-16.
///
/// A standalone window, not wired to GRT or to BrassForm's other calculators: values are typed in
/// directly rather than read live from another open tool, the same "read here, type there" handoff
/// already used between the Reloading Toolkit and GRT Sensitivity Lab -- decoupled tools stay
/// simpler than live cross-tool wiring for a rarely-changing set of inputs.
/// </summary>
internal sealed class SeatingForceForm : Form
{
    private const double MmPerIn = 25.4;

    /// <summary>One length field with its OWN mm/in choice -- per-field, not a single
    /// form-wide toggle, per explicit user feedback after GRT Sensitivity Lab's What If/Sensitivity
    /// Sweep forms shipped with one global toggle and had to be redone (2026-09-16): a user
    /// measuring bullet diameter with a caliper in mm and neck ID with one in inches, say, wants
    /// both at once.</summary>
    private sealed class LengthField
    {
        public required NumericUpDown Nud { get; init; }
        public required ComboBox Unit { get; init; }
        public bool WasImperial { get; set; }
    }

    private readonly List<LengthField> _lengthFields = new();

    private readonly LengthField _bulletDia;
    private readonly LengthField _neckId;
    private readonly LengthField _seatingDepth;
    private readonly LengthField _typicalInterf;
    private readonly LengthField _typicalDepth;

    private readonly ComboBox _reference = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _baselineForce = new() { DecimalPlaces = 1, Increment = 0.5M, Maximum = 200, Value = 14.0M, Width = 90 };
    private readonly NumericUpDown _baselineStd = new() { DecimalPlaces = 1, Increment = 0.1M, Maximum = 50, Value = 2.5M, Width = 90 };
    private readonly ComboBox _annealing = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _neckSizing = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _lube = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _coating = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _boatTail = new() { Text = Lang.T("Boat-tail"), Checked = true, AutoSize = true };
    private readonly Label _out = new() { AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 9f) };

    public SeatingForceForm()
    {
        Text = AppVersion.Title(Lang.T("GRT Seating Force Estimate (QC)"));
        // Tall enough that every row fits without the panel's own vertical scrollbar at the
        // default size (checked by rendering the real window, same lesson as GRT Sensitivity
        // Lab's WhatIfForm/SensitivitySweepForm layout fix).
        Width = 640; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 420);

        _bulletDia = MakeLengthField(6.5M, 30M);
        _neckId = MakeLengthField(6.45M, 30M);
        _seatingDepth = MakeLengthField(6.7M, 60M);
        _typicalInterf = MakeLengthField(0.05M, 5M);
        _typicalDepth = MakeLengthField(5.0M, 30M);

        Build();
        NudFix.ApplyTo(this);
        Recalc();
    }

    private LengthField MakeLengthField(decimal defaultMm, decimal maxMm)
    {
        var nud = new NumericUpDown { DecimalPlaces = 3, Increment = 0.01M, Maximum = maxMm, Value = defaultMm, Width = 80 };
        var unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Items = { "mm", "in" } };
        unit.SelectedIndex = 0;
        var f = new LengthField { Nud = nud, Unit = unit };
        nud.ValueChanged += (_, _) => Recalc();
        unit.SelectedIndexChanged += (_, _) => OnFieldUnitChanged(f);
        _lengthFields.Add(f);
        return f;
    }

    private double ToMm(LengthField f) => f.Unit.SelectedIndex == 1 ? (double)f.Nud.Value * MmPerIn : (double)f.Nud.Value;

    /// <summary>
    /// Converts one field's own NumericUpDown between metric and imperial when ITS OWN combo
    /// changes. Reads the control's current value converted to its CANONICAL unit (mm) BEFORE
    /// touching DecimalPlaces/Value -- the same read-before-clamp-or-truncate hazard as every other
    /// per-field unit toggle in this codebase (GrtModelLab's BarrelProfileForm/BrassForm's own Neck
    /// tab, GRT Sensitivity Lab's WhatIfForm): changing DecimalPlaces first can silently round away
    /// precision the conversion still needs.
    /// </summary>
    private void OnFieldUnitChanged(LengthField f)
    {
        bool toImperial = f.Unit.SelectedIndex == 1;
        double mm = f.WasImperial ? (double)f.Nud.Value * MmPerIn : (double)f.Nud.Value;
        f.WasImperial = toImperial;

        double display = toImperial ? mm / MmPerIn : mm;
        f.Nud.DecimalPlaces = toImperial ? 4 : 3;
        f.Nud.Increment = toImperial ? 0.0005M : 0.01M;
        f.Nud.Value = (decimal)Math.Clamp(display, (double)f.Nud.Minimum, (double)f.Nud.Maximum);
        Recalc();
    }

    private void SetMm(LengthField f, double mm)
    {
        double display = f.WasImperial ? mm / MmPerIn : mm;
        f.Nud.Value = (decimal)Math.Clamp(display, (double)f.Nud.Minimum, (double)f.Nud.Maximum);
    }

    private void Build()
    {
        var root = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
        void Skip() => t.Controls.Add(new Label { Text = "", AutoSize = true });
        void Row(string l, Control c) { t.Controls.Add(new Label { Text = l, AutoSize = true, Padding = new Padding(0, 5, 8, 0) }); t.Controls.Add(c); }
        // A single-cell Add leaves column 1 of that row empty for the NEXT Add to fall into,
        // silently shifting every row after it one column over -- caught by rendering the actual
        // window when this lived as a BrassForm tab, not from reading the TableLayoutPanel code.
        // Skip() after it keeps rows aligned.
        void SectionLabel(string s)
        {
            t.Controls.Add(new Label
            {
                Text = s, AutoSize = true, Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
                ForeColor = SystemColors.GrayText, Margin = new Padding(0, 10, 0, 2),
            });
            Skip();
        }
        Control FieldRow(LengthField f)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
            row.Controls.Add(f.Nud);
            f.Unit.Margin = new Padding(4, 3, 0, 0);
            row.Controls.Add(f.Unit);
            return row;
        }

        SectionLabel(Lang.T("From this bore (bullet + prepped neck)"));
        Row(Lang.T("Bullet diameter"), FieldRow(_bulletDia));
        Row(Lang.T("Neck ID after sizing"), FieldRow(_neckId));
        Row(Lang.T("Actual seating depth"), FieldRow(_seatingDepth));

        SectionLabel(Lang.T("Baseline for a \"typical\" setup in this bore"));
        _reference.Items.Add(Lang.T("-- custom / type your own --"));
        foreach (var r in SeatingForceCalc.ReferenceBaselines) _reference.Items.Add(r.Caliber);
        _reference.SelectedIndex = 0;
        _reference.SelectedIndexChanged += (_, _) =>
        {
            if (_reference.SelectedIndex <= 0) return;
            var r = SeatingForceCalc.ReferenceBaselines[_reference.SelectedIndex - 1];
            SetMm(_bulletDia, r.BulletDiaMm);
            _baselineForce.Value = (decimal)r.ForceKg;
            _baselineStd.Value = (decimal)r.StdKg;
            SetMm(_typicalInterf, r.InterferenceMm);
            SetMm(_typicalDepth, r.SeatingDepthMm);
            Recalc();
        };
        Row(Lang.T("Quick-fill reference (optional)"), _reference);
        Row(Lang.T("Baseline force, typical prep (kg)"), _baselineForce);
        Row(Lang.T("Baseline std dev (kg)"), _baselineStd);
        Row(Lang.T("...at this typical interference"), FieldRow(_typicalInterf));
        Row(Lang.T("...and this typical seating depth"), FieldRow(_typicalDepth));

        SectionLabel(Lang.T("Actual prep for this batch"));
        foreach (var cb in new[] { _annealing, _neckSizing, _lube, _coating })
            cb.SelectedIndexChanged += (_, _) => Recalc();
        FillFactorCombo(_annealing, SeatingForceCalc.AnnealingOptions, "annealed_5plus");
        FillFactorCombo(_neckSizing, SeatingForceCalc.NeckSizingOptions, "bushing");
        FillFactorCombo(_lube, SeatingForceCalc.LubeOptions, "graphite");
        FillFactorCombo(_coating, SeatingForceCalc.CoatingOptions, "copper_bare");
        Row(Lang.T("Annealing"), _annealing);
        Row(Lang.T("Neck sizing"), _neckSizing);
        Row(Lang.T("Neck lube"), _lube);
        Row(Lang.T("Bullet coating"), _coating);
        _boatTail.CheckedChanged += (_, _) => Recalc();
        t.Controls.Add(_boatTail); Skip();

        _baselineForce.ValueChanged += (_, _) => Recalc();
        _baselineStd.ValueChanged += (_, _) => Recalc();

        var help = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 50, ForeColor = SystemColors.GrayText, Padding = new Padding(10, 4, 10, 4),
            Text = Lang.T("Empirical estimate, not a measurement -- a QC baseline for THIS bore. The bar/psi " +
                   "figure is just the same force expressed over the bullet's cross-section, not a GRT input."),
        };
        _out.Location = new Point(10, 4);
        var outBox = new Panel { Dock = DockStyle.Top, Height = 160, Padding = new Padding(6) };
        outBox.Controls.Add(_out);

        root.Controls.Add(outBox);
        root.Controls.Add(t);
        Controls.Add(root);
        Controls.Add(help);
    }

    private static void FillFactorCombo(ComboBox cb, IReadOnlyList<(string Key, string Label, SeatingForceCalc.Factor F)> options, string defaultKey)
    {
        foreach (var (key, label, _) in options) cb.Items.Add(new FactorItem(key, Lang.T(label)));
        int idx = options.ToList().FindIndex(o => o.Key == defaultKey);
        cb.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static SeatingForceCalc.Factor SelectedFactor(ComboBox cb, IReadOnlyList<(string Key, string Label, SeatingForceCalc.Factor F)> options)
    {
        var item = cb.SelectedItem as FactorItem;
        var found = item is null ? default : options.FirstOrDefault(o => o.Key == item.Key);
        return found.F;
    }

    private sealed record FactorItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private void Recalc()
    {
        double bulletDiaMm = ToMm(_bulletDia), neckIdMm = ToMm(_neckId), seatingDepthMm = ToMm(_seatingDepth);
        double typicalInterfMm = ToMm(_typicalInterf), typicalDepthMm = ToMm(_typicalDepth);

        if (bulletDiaMm <= 0 || neckIdMm <= 0 || seatingDepthMm <= 0
            || _baselineForce.Value <= 0 || typicalInterfMm <= 0 || typicalDepthMm <= 0)
        {
            _out.Text = Lang.T("fill in bullet diameter, neck ID, seating depth and the baseline fields");
            return;
        }

        var est = SeatingForceCalc.Compute(
            bulletDiaMm, neckIdMm,
            (double)_baselineForce.Value, (double)_baselineStd.Value,
            typicalInterfMm, typicalDepthMm,
            seatingDepthMm,
            SelectedFactor(_annealing, SeatingForceCalc.AnnealingOptions),
            SelectedFactor(_neckSizing, SeatingForceCalc.NeckSizingOptions),
            SelectedFactor(_lube, SeatingForceCalc.LubeOptions),
            SelectedFactor(_coating, SeatingForceCalc.CoatingOptions),
            _boatTail.Checked);

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Format(ci, Lang.T("interference           : {0:0.000} mm\n"), est.InterferenceMm));
        sb.Append(string.Format(ci, Lang.T("estimated mean force   : {0:0.0} kg  (+/- {1:0.0} kg SD)\n"), est.MeanKg, est.StdKg));
        sb.Append(string.Format(ci, Lang.T("  68% band (1-sigma)    : {0:0.0} - {1:0.0} kg\n"), est.P1LowKg, est.P1HighKg));
        sb.Append(string.Format(ci, Lang.T("  95% band (2-sigma)    : {0:0.0} - {1:0.0} kg\n"), est.P2LowKg, est.P2HighKg));
        sb.Append(string.Format(ci, Lang.T("  suggested max (QC)    : {0:0.0} kg\n\n"), est.MaxRecommendedKg));
        sb.Append(string.Format(ci, Lang.T("equivalent pressure     : {0:0} bar  ({1:0} psi)\n"), est.EquivalentPressureBar, est.EquivalentPressurePsi));
        foreach (var note in est.Notes) sb.Append("  ! ").Append(note).Append('\n');
        _out.Text = sb.ToString();
    }
}
