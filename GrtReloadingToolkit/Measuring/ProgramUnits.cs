using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Measuring;

/// <summary>
/// The units the printed and shown texts of the program use. A kind the shooter chose in "Units of measure" (weights, lengths, distances) follows that choice; a kind
/// never chosen keeps showing the units GRT shows, so a card still reads like the load it came from. Velocity, temperature and twist are GRT's. It has the
/// members of <see cref="GrtUnits"/> that the windows use, so a window that used to ask GRT now asks this and nothing else in it changes.
/// </summary>
public sealed class ProgramUnitView
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly UnitPrefs? _prefs;
    private readonly GrtUnits _grt;
    private readonly string? _field;

    public ProgramUnitView(UnitPrefs? prefs, GrtUnits grt, string? field = null)
    {
        _prefs = prefs;
        _grt = grt;
        _field = field;
    }

    /// <summary>The same view for one field that can have a unit of its own (a column of charges): its own choice wins over the starting one.</summary>
    public ProgramUnitView ForField(string field) => new(_prefs, _grt, field);

    /// <summary>True when the shooter chose a unit for this kind in "Units of measure" (or for this field), so the texts do not just follow GRT.</summary>
    public bool IsChosen(UnitKind kind) => Chosen(kind) is not null;

    private string? Chosen(UnitKind kind)
    {
        if (_prefs is not { } p) return null;
        if (_field is not null && p.HasOwnChoice(_field, kind)) return p.For(_field, kind);
        return p.HasDefaultChoice(kind) ? p.Default(kind) : null;
    }

    /// <summary>The code of the unit this view uses for a kind (for a menu that marks the one in use).</summary>
    public string CodeOf(UnitKind kind) => Chosen(kind) ?? kind switch
    {
        UnitKind.Mass => _grt.ChargeInGrams ? "g" : "gr",
        UnitKind.Length => _grt.LengthInInch ? "in" : "mm",
        UnitKind.Distance => _grt.DistanceInYards ? "yd" : "m",
        _ => "",
    };

    // ---- weights (stored in grains) ----

    public bool ChargeInGrams => Chosen(UnitKind.Mass) is { } c ? c == "g" : _grt.ChargeInGrams;
    public string ChargeUnitName => Chosen(UnitKind.Mass) is { } c ? UnitSystem.Label(c) : _grt.ChargeUnitName;
    public string ChargeFormat => ChargeInGrams ? "0.0000" : "0.0##";
    public double ChargeValue(double grains) => Chosen(UnitKind.Mass) is { } c ? UnitSystem.FromBase(grains, c) : _grt.ChargeValue(grains);
    public double ChargeToGrains(double shown) => Chosen(UnitKind.Mass) is { } c ? UnitSystem.ToBase(shown, c) : _grt.ChargeToGrains(shown);
    public string Charge(double grains) => ChargeValue(grains).ToString(ChargeFormat, Inv) + " " + ChargeUnitName;

    public bool BulletMassInGrams => Chosen(UnitKind.Mass) is { } c ? c == "g" : _grt.BulletMassInGrams;
    public string BulletMassUnitName => Chosen(UnitKind.Mass) is { } c ? UnitSystem.Label(c) : _grt.BulletMassUnitName;
    public double BulletMassValue(double grains) => Chosen(UnitKind.Mass) is { } c ? UnitSystem.FromBase(grains, c) : _grt.BulletMassValue(grains);
    public double BulletMassToGrains(double shown) => Chosen(UnitKind.Mass) is { } c ? UnitSystem.ToBase(shown, c) : _grt.BulletMassToGrains(shown);
    public string BulletMass(double grains) => BulletMassValue(grains).ToString(BulletMassInGrams ? "0.00" : "0.#", Inv) + " " + BulletMassUnitName;

    // ---- lengths (stored in millimetres) ----

    public bool LengthInInch => Chosen(UnitKind.Length) is { } c ? c == "in" : _grt.LengthInInch;
    public string LengthUnitName => Chosen(UnitKind.Length) is { } c ? UnitSystem.Label(c) : _grt.LengthUnitName;
    public double LengthValue(double mm) => Chosen(UnitKind.Length) is { } c ? UnitSystem.FromBase(mm, c) : _grt.LengthValue(mm);
    public double LengthToMm(double shown) => Chosen(UnitKind.Length) is { } c ? UnitSystem.ToBase(shown, c) : _grt.LengthToMm(shown);
    public string Length(double mm) => Chosen(UnitKind.Length) is { } c
        ? UnitSystem.FromBase(mm, c).ToString("0.####", Inv) + " " + UnitSystem.Label(c)
        : _grt.Length(mm);

    // ---- distances (stored in metres) ----

    public bool DistanceInYards => Chosen(UnitKind.Distance) is { } c ? c == "yd" : _grt.DistanceInYards;
    public string DistanceUnitName => Chosen(UnitKind.Distance) is { } c ? UnitSystem.Label(c) : _grt.DistanceUnitName;
    public double DistanceValue(double metres) => Chosen(UnitKind.Distance) is { } c ? UnitSystem.FromBase(metres, c) : _grt.DistanceValue(metres);
    public double DistanceToMetres(double shown) => Chosen(UnitKind.Distance) is { } c ? UnitSystem.ToBase(shown, c) : _grt.DistanceToMetres(shown);
    public string Distance(double metres) => Chosen(UnitKind.Distance) is { } c
        ? UnitSystem.FromBase(metres, c).ToString("0.##", Inv) + " " + UnitSystem.Label(c)
        : _grt.Distance(metres);

    // ---- the seating depth / jump of a seating ladder ----
    // Its number is the shooter's own, typed in the file names or notes of the ladder, so what was stored is not in a known unit: the shooter says which one he
    // writes in (field <see cref="SeatingTypedField"/>); until he does, it is GRT's length unit and the number is shown as it is.

    public const string SeatingTypedField = "seating.typed";

    /// <summary>"mm" or "in": the unit the numbers of the seating ladder were typed in.</summary>
    public string SeatingTypedCode => _prefs is { } p && p.HasOwnChoice(SeatingTypedField, UnitKind.Length) ? p.For(SeatingTypedField, UnitKind.Length) : _grt.LengthInInch ? "in" : "mm";

    /// <summary>A number of the seating ladder as typed, in the length unit shown.</summary>
    public double SeatingValue(double typed)
    {
        string from = SeatingTypedCode, to = CodeOf(UnitKind.Length);
        return from == to ? typed : UnitSystem.FromBase(UnitSystem.ToBase(typed, from), to);
    }

    // ---- the neck tension of the advanced diagnostics ----
    // A number the shooter puts in the name of a string ("0.05"): which unit he writes it in is his to say (mm until he says inches), the analysis works in mm,
    // and it is shown in the length unit of the program once he chose one, else in the unit he typed.

    public const string TensionTypedField = "neck.typed";

    public string TensionTypedCode => _prefs is { } p && p.HasOwnChoice(TensionTypedField, UnitKind.Length) ? p.For(TensionTypedField, UnitKind.Length) : "mm";
    public string TensionShownCode => Chosen(UnitKind.Length) ?? TensionTypedCode;
    public string TensionShownName => UnitSystem.Label(TensionShownCode);
    public double TensionToMm(double typed) => UnitSystem.ToBase(typed, TensionTypedCode);
    public string TensionShown(double mm) => UnitSystem.FromBase(mm, TensionShownCode).ToString(TensionShownCode == "in" ? "0.0000" : "0.000", Inv);

    // ---- angles (stored in MOA) ----

    /// <summary>"moa" or "mrad"; the windows that read in MOA before the choice existed keep MOA until one is made.</summary>
    public string AngleCode => Chosen(UnitKind.Angle) ?? "moa";
    public bool AngleInMrad => AngleCode == "mrad";
    public string AngleUnitName => UnitSystem.Label(AngleCode);
    public double AngleValue(double moa) => UnitSystem.FromBase(moa, AngleCode);
    public double AngleToMoa(double shown) => UnitSystem.ToBase(shown, AngleCode);
    /// <summary>A text written with "MOA" in it, with the word of the unit in use (the numbers in it are the caller's to convert).</summary>
    public string AngleText(string text) => UnitText.Angle(text, AngleCode);

    private static readonly System.Text.RegularExpressions.Regex NumberOfMoa =
        new(@"(?<![\d/.])([-+]?\d+(?:\.\d+)?)( ?)MOA\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// A text the engines wrote in MOA (a verdict with "0.42 MOA" inside it) in the angle unit of the program: each number followed by MOA is converted
    /// (a click like "1/4 MOA" is left alone) and the word is changed. Without a choice of mrad the text is returned as it is.
    /// </summary>
    public string AngleAll(string text)
    {
        if (!AngleInMrad) return text;
        string converted = NumberOfMoa.Replace(text, m =>
        {
            string raw = m.Groups[1].Value;
            int decimals = raw.Contains('.') ? raw.Length - raw.IndexOf('.') - 1 : 0;
            double v = AngleValue(double.Parse(raw, Inv));
            return v.ToString("0." + new string('0', Math.Max(2, decimals)), Inv) + m.Groups[2].Value + "mrad";
        });
        return AngleText(converted);
    }

    private static readonly System.Text.RegularExpressions.Regex NumberOfMm =
        new(@"(?<![\d/.])([-+]?\d+(?:\.\d+)?)( ?/ ?[-+]?\d+(?:\.\d+)?)? mm\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex NumberOfMetres =
        new(@"(?<![\d/.])(\d+(?:\.\d+)?) m(?![\w/²])", System.Text.RegularExpressions.RegexOptions.Compiled);

    private string Convert(string raw, Func<double, double> f, int extraDecimals)
    {
        string t = raw.Trim(' ', '/');
        int decimals = t.Contains('.') ? t.Length - t.IndexOf('.') - 1 : 0;
        return f(double.Parse(t, Inv)).ToString("0." + new string('0', Math.Max(0, decimals + extraDecimals)).TrimEnd('.'), Inv);
    }

    /// <summary>
    /// A text the engines wrote with lengths in millimetres ("shots spread 12.4 mm sideways", "(3.1 / 4.0 mm)") in the length unit of the program: each number
    /// followed by mm is converted and the unit changed. With millimetres chosen (or nothing chosen) the text is returned as it is.
    /// </summary>
    public string LengthAll(string text)
    {
        if (!LengthInInch) return text;
        text = text.Replace("(mm)", "(" + LengthUnitName + ")");
        return NumberOfMm.Replace(text, m =>
        {
            string first = Convert(m.Groups[1].Value, LengthValue, 2);
            string second = m.Groups[2].Success ? " / " + Convert(m.Groups[2].Value, LengthValue, 2) : "";
            return first + second + " " + LengthUnitName;
        });
    }

    /// <summary>The same for the distances written in metres ("at 300 m", "from 100 m to 300 m"): in yards when the shooter chose yards.</summary>
    public string DistanceAll(string text)
    {
        if (!DistanceInYards) return text;
        return NumberOfMetres.Replace(text, m => Convert(m.Groups[1].Value, DistanceValue, 0) + " " + DistanceUnitName);
    }

    // ---- what the shooter does not choose here: GRT's ----

    public bool VelocityInFps => _grt.VelocityInFps;
    public string VelocityUnitName => _grt.VelocityUnitName;
    public double VelocityValue(double ms) => _grt.VelocityValue(ms);
    public double VelocityToMps(double shown) => _grt.VelocityToMps(shown);
    public string Velocity(double ms) => _grt.Velocity(ms);
    public string VelocitySd(double ms) => _grt.VelocitySd(ms);
    public double VelocityPerDegreeValue(double mpsPerCelsius) => _grt.VelocityPerDegreeValue(mpsPerCelsius);
    public string VelocityPerDegreeUnitName => _grt.VelocityPerDegreeUnitName;
    public bool TemperatureInF => _grt.TemperatureInF;
    public string TemperatureUnitName => _grt.TemperatureUnitName;
    public double TemperatureValue(double celsius) => _grt.TemperatureValue(celsius);
    public double TemperatureToCelsius(double shown) => _grt.TemperatureToCelsius(shown);
    public string Temperature(double celsius) => _grt.Temperature(celsius);
    public bool TwistInInch => _grt.TwistInInch;
    public string TwistUnitName => _grt.TwistUnitName;
    public double TwistValue(double mm) => _grt.TwistValue(mm);
    public double TwistToMm(double shown) => _grt.TwistToMm(shown);
    public string Twist(double mm) => _grt.Twist(mm);
}

public static class ProgramUnits
{
    /// <summary>Set by the program when it starts; without it (a command line, a test) the texts keep GRT's units.</summary>
    public static UnitPrefs? Prefs { get; set; }

    public static ProgramUnitView Current => new(Prefs, GrtUnits.Current);
}
