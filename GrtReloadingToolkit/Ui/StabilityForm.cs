using System.Globalization;
using System.Text;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Gyroscopic stability of the bullet in the barrel's twist, by the Miller rule (see <see cref="GyroStability"/> for the rule and
/// what it leaves out): one Sg for the conditions typed in, the slowest twist that would still give Sg 1.5 and 1.4, and Sg across
/// a range of air temperatures at the same altitude, since a cold morning is what takes a marginal bullet under.
///
/// Each number has its OWN unit choice (the same per-field rule as the other converters), starting from the units GRT is set to.
/// "Read from GRT load" fills mass, length, diameter and twist from the open load, and the velocity from its measured shots when it
/// has some; nothing is guessed when a field is not in the file.
/// </summary>
internal sealed class StabilityForm : Form
{
    private const double MmPerIn = 25.4, FtPerM = 1.0 / 0.3048, GramsPerGrain = 0.06479891;
    private const string NoteTitle = "Gyroscopic Stability";

    /// <summary>One number with its own unit choice; the canonical value is what the maths uses, whatever is shown.</summary>
    private sealed class UnitField
    {
        public required NumericUpDown Nud { get; init; }
        public required ComboBox Unit { get; init; }
        /// <summary>Canonical → shown, and shown → canonical, per unit in the same order as the combo.</summary>
        public required (string Name, Func<double, double> FromCanon, Func<double, double> ToCanon, int Decimals)[] Units { get; init; }
        public int Current { get; set; }

        public double Canonical => Units[Current].ToCanon((double)Nud.Value);
        public void SetCanonical(double v) =>
            Nud.Value = Math.Clamp((decimal)Units[Current].FromCanon(v), Nud.Minimum, Nud.Maximum);
    }

    private readonly GrtClient? _grt;
    private readonly GrtUnits _u = GrtUnits.Current;
    private readonly List<UnitField> _fields = new();
    private readonly UnitField _mass, _length, _diameter, _twist, _velocity, _temp, _altitude;
    private readonly TextBox _out = new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 9.5f),
    };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 40, Padding = new Padding(8, 4, 8, 4), ForeColor = SystemColors.GrayText };
    private readonly Button _writeBtn = new() { AutoSize = true };
    private string _lastText = "";
    private bool _building = true;

    public StabilityForm(GrtClient? grt)
    {
        _grt = grt;
        Text = AppVersion.Title(Lang.T("Gyroscopic Stability"));
        Width = 880; Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);

        // canonical units: grains, inches, inches, inches per turn, ft/s, °C, metres
        _mass = Field(_u.BulletMassInGrams ? 1 : 0, 0.1M, 1M, 1000M, 139M,
            ("gr", x => x, x => x, 1), ("g", x => x * GramsPerGrain, x => x / GramsPerGrain, 2));
        _length = Field(_u.LengthInInch ? 1 : 0, 0.001M, 0.5M, 200M, 1.36M,
            ("mm", x => x * MmPerIn, x => x / MmPerIn, 2), ("in", x => x, x => x, 3));
        _diameter = Field(_u.LengthInInch ? 1 : 0, 0.001M, 0.1M, 50M, 0.264M,
            ("mm", x => x * MmPerIn, x => x / MmPerIn, 3), ("in", x => x, x => x, 4));
        _twist = Field(_u.TwistInInch ? 0 : 1, 0.01M, 1M, 1000M, 8M,
            ("in", x => x, x => x, 2), ("mm", x => x * MmPerIn, x => x / MmPerIn, 1));
        _velocity = Field(_u.VelocityInFps ? 0 : 1, 1M, 100M, 6000M, 2700M,
            ("ft/s", x => x, x => x, 0), ("m/s", x => x * 0.3048, x => x / 0.3048, 1));
        _temp = Field(_u.TemperatureInF ? 1 : 0, 0.5M, -60M, 130M, 15M,
            ("°C", x => x, x => x, 1), ("°F", x => x * 9 / 5 + 32, x => (x - 32) * 5 / 9, 1));
        _altitude = Field(_u.DistanceInYards ? 1 : 0, 10M, -500M, 30000M, 0M,
            ("m", x => x, x => x, 0), ("ft", x => x * FtPerM, x => x / FtPerM, 0));

        Build();
        NudFix.ApplyTo(this);
        _building = false;
        Recalc();
    }

    private UnitField Field(int startUnit, decimal step, decimal min, decimal max, decimal canonicalDefault,
        params (string Name, Func<double, double> FromCanon, Func<double, double> ToCanon, int Decimals)[] units)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
        foreach (var u in units) combo.Items.Add(u.Name);
        // the range is given in the first unit's terms and then widened for the others, so a value is never refused for its unit
        decimal lo = Math.Min(min, -10000M), hi = Math.Max(max, 100000M);
        var nud = new NumericUpDown { Width = 90, Minimum = min >= 0 ? 0 : lo, Maximum = hi, Increment = step, DecimalPlaces = units[startUnit].Decimals };
        var f = new UnitField { Nud = nud, Unit = combo, Units = units, Current = startUnit };
        combo.SelectedIndex = startUnit;
        nud.Value = Math.Clamp((decimal)units[startUnit].FromCanon((double)canonicalDefault), nud.Minimum, nud.Maximum);
        nud.ValueChanged += (_, _) => Recalc();
        combo.SelectedIndexChanged += (_, _) =>
        {
            // The canonical value is read BEFORE the decimals or the value change: touching DecimalPlaces first can round away
            // precision the conversion still needs (the same hazard as every other per-field unit switch here).
            double canon = f.Canonical;
            f.Current = combo.SelectedIndex;
            nud.DecimalPlaces = units[f.Current].Decimals;
            nud.Increment = (decimal)Math.Max(Math.Pow(10, -units[f.Current].Decimals), 0.0001);
            f.SetCanonical(canon);
            Recalc();
        };
        _fields.Add(f);
        return f;
    }

    private void Build()
    {
        var left = new TableLayoutPanel { Dock = DockStyle.Left, Width = 330, ColumnCount = 1, Padding = new Padding(10), AutoScroll = true };
        void Add(Control c) { left.Controls.Add(c); }
        void Row(string label, UnitField f)
        {
            Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            f.Unit.Margin = new Padding(4, 1, 0, 0);
            row.Controls.Add(f.Nud);
            row.Controls.Add(f.Unit);
            Add(row);
        }

        var readBtn = new Button { Text = Lang.T("Read from GRT load"), AutoSize = true, Margin = new Padding(0, 0, 0, 4), Enabled = _grt is { Connected: true } };
        readBtn.Click += async (_, _) => await ReadFromGrtAsync();
        Add(readBtn);
        Row(Lang.T("Bullet mass"), _mass);
        Row(Lang.T("Bullet length"), _length);
        Row(Lang.T("Bullet diameter"), _diameter);
        Row(Lang.T("Barrel twist (one turn in)"), _twist);
        Row(Lang.T("Muzzle velocity"), _velocity);
        Row(Lang.T("Air temperature"), _temp);
        Row(Lang.T("Altitude"), _altitude);

        _writeBtn.Text = Lang.T("Write stability note to GRT load");
        _writeBtn.Margin = new Padding(0, 18, 0, 0);
        _writeBtn.Enabled = _grt is { Connected: true };
        _writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        Add(_writeBtn);

        Controls.Add(_out);
        Controls.Add(left);
        Controls.Add(_status);
        Shown += (_, _) => { ActiveControl = _mass.Nud; _out.Select(0, 0); };
        _status.Text = _grt is { Connected: true } ? "" : Lang.T("Standalone: not connected to GRT, type the values in.");
    }

    // ── Calculation ──────────────────────────────────────────────────────

    private void Recalc()
    {
        if (_building) return;
        var r = GyroStability.Compute(_mass.Canonical, _length.Canonical, _diameter.Canonical, _twist.Canonical, _velocity.Canonical,
            _temp.Canonical, _altitude.Canonical, out string? problem);
        if (r is null)
        {
            _lastText = "";
            _out.Text = problem switch
            {
                "length" => Lang.T("The bullet must be longer than it is wide: check the units of the length and the diameter."),
                "altitude" => Lang.T("The altitude must be between -500 and 9000 m."),
                _ => Lang.T("Every number must be above zero."),
            };
            return;
        }
        _lastText = Compose(r);
        _out.Text = _lastText;
        _out.Select(0, 0);                  // a read-only box that takes the focus selects all of its text; leave it unselected
    }

    private static string LevelName(StabilityLevel l) => l switch
    {
        StabilityLevel.Stable => Lang.T("stable"),
        StabilityLevel.Adequate => Lang.T("adequate"),
        StabilityLevel.Marginal => Lang.T("marginal"),
        _ => Lang.T("unstable"),
    };

    private string Twist(double inches) => "1:" + _twist.Units[_twist.Current].FromCanon(inches).ToString(_twist.Current == 0 ? "0.0#" : "0.#", CultureInfo.InvariantCulture)
        + " " + _twist.Units[_twist.Current].Name;

    private string Compose(GyroStabilityResult r)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Lang.T("Sg {0}: {1}"), r.Sg.ToString("0.00", ci), LevelName(r.Level)));
        sb.AppendLine(string.Format(Lang.T("On the standard day (15 °C, sea level, 2800 ft/s) the same bullet gives Sg {0}."), r.SgStandard.ToString("0.00", ci)));
        sb.AppendLine(string.Format(Lang.T("Velocity factor {0}, air factor {1}. Twist {2} calibres per turn, bullet {3} calibres long."),
            r.VelocityFactor.ToString("0.000", ci), r.AtmosphereFactor.ToString("0.000", ci),
            r.TwistCalibers.ToString("0.0", ci), r.LengthCalibers.ToString("0.00", ci)));
        sb.AppendLine();
        sb.AppendLine(string.Format(Lang.T("Slowest twist that gives Sg 1.5 in these conditions: {0}; Sg 1.4: {1}."), Twist(r.TwistFor15In), Twist(r.TwistFor14In)));
        sb.AppendLine(string.Format(Lang.T("Greenhill's older rule, as a cross-check only: {0}."), Twist(r.GreenhillTwistIn)));
        sb.AppendLine();

        sb.AppendLine(string.Format(Lang.T("Sg for other air temperatures (same bullet, velocity and altitude):")));
        foreach (double c in new[] { -20.0, -10.0, 0.0, 10.0, 20.0, 30.0 })
        {
            var t = GyroStability.Compute(_mass.Canonical, _length.Canonical, _diameter.Canonical, _twist.Canonical, _velocity.Canonical,
                c, _altitude.Canonical, out _);
            if (t is null) continue;
            sb.AppendLine(string.Format(ci, "  {0,4:0} °C {1,4:0} °F   Sg {2:0.00}  {3}", c, c * 9 / 5 + 32, t.Sg, LevelName(t.Level)));
        }
        sb.AppendLine();
        if (r.Level is StabilityLevel.Marginal or StabilityLevel.Unstable)
            sb.AppendLine(Lang.T("Below Sg 1.4 the bullet flies but its drag rises, and a colder day can take it under 1: check on the target (oval holes mean it is not stable) before trusting the load."));
        sb.AppendLine(Lang.T("A theoretical estimate for a spitzer or boat-tail bullet: the bullet's real concentricity and the muzzle velocity on a cold day (lower than typed here) move the figure."));
        return sb.ToString();
    }

    // ── GRT ──────────────────────────────────────────────────────────────

    private async Task ReadFromGrtAsync()
    {
        if (_grt is not { Connected: true }) { MessageBox.Show(this, Lang.T("Not connected to GRT.")); return; }
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT.")); return; }
            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            var filled = new List<string>();
            _building = true;
            if (doc.BulletMassGr is { } m && m > 0) { _mass.SetCanonical(m); filled.Add(Lang.T("mass")); }
            if (doc.BulletLengthMm is { } l && l > 0) { _length.SetCanonical(l / MmPerIn); filled.Add(Lang.T("length")); }
            if (doc.BulletDiameterMm is { } d && d > 0) { _diameter.SetCanonical(d / MmPerIn); filled.Add(Lang.T("diameter")); }
            if (doc.TwistLengthMm is { } t && t > 0) { _twist.SetCanonical(t / MmPerIn); filled.Add(Lang.T("twist")); }
            var shots = doc.ChargeMatchingRecipe()?.Shots;
            if (shots is { Count: > 0 })
            {
                _velocity.SetCanonical(shots.Average(s => s.VelocityMps) * FtPerM);
                filled.Add(Lang.T("velocity (mean of the measured shots)"));
            }
            _building = false;
            Recalc();
            _status.Text = string.Format(Lang.T("Read from the open load: {0}. Check the air temperature and the altitude, which a load does not carry."),
                filled.Count == 0 ? Lang.T("nothing usable") : string.Join(", ", filled));
        }
        catch (Exception ex)
        {
            _building = false;
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || _lastText.Length == 0) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            { MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write stability note to GRT load")); return; }

            var text = new StringBuilder();
            text.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            text.AppendLine(new string('-', 20));
            text.AppendLine(string.Format(Lang.T("Bullet {0} gr, length {1}, diameter {2}, twist {3}, {4} ft/s, {5} °C, altitude {6} m."),
                _mass.Canonical.ToString("0.#", CultureInfo.InvariantCulture), _length.Canonical.ToString("0.000", CultureInfo.InvariantCulture) + " in",
                _diameter.Canonical.ToString("0.000", CultureInfo.InvariantCulture) + " in", Twist(_twist.Canonical),
                _velocity.Canonical.ToString("0", CultureInfo.InvariantCulture), _temp.Canonical.ToString("0.#", CultureInfo.InvariantCulture),
                _altitude.Canonical.ToString("0", CultureInfo.InvariantCulture)));
            text.AppendLine(_lastText);

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, text.ToString());
            string outPath = writeDoc.SaveSibling("stability");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Stability note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write stability note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
