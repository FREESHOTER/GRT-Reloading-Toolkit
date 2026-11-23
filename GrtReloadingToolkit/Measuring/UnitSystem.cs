using System.Globalization;

namespace GrtReloadingToolkit.Measuring;

/// <summary>The kinds of quantity whose unit the shooter chooses.</summary>
public enum UnitKind { Mass, Length, Distance, Angle }

/// <summary>One unit. <paramref name="PerBase"/> is how many of it make one unit of the base of its kind (grain, millimetre, metre, minute of angle).</summary>
public sealed record UnitDef(string Code, string Label, UnitKind Kind, double PerBase, int Decimals);

/// <summary>
/// The units the program can show and read, and how to go from one to the other. The archive and every calculation keep ONE unit for each kind
/// (grains, millimetres, metres, MOA): the choice only changes what is shown and how a typed number is understood, so changing it can never change a stored value.
/// </summary>
public static class UnitSystem
{
    public const double GramsPerGrain = 0.06479891;
    public const double MmPerInch = 25.4;
    public const double MetresPerYard = 0.9144;
    public const double MoaPerMrad = 3.437746771;

    private static readonly UnitDef[] All =
    {
        new("gr", "gr", UnitKind.Mass, 1, 3),
        new("g", "g", UnitKind.Mass, GramsPerGrain, 5),
        new("mm", "mm", UnitKind.Length, 1, 3),
        new("in", "in", UnitKind.Length, 1 / MmPerInch, 5),
        new("m", "m", UnitKind.Distance, 1, 2),
        new("yd", "yd", UnitKind.Distance, 1 / MetresPerYard, 2),
        new("moa", "MOA", UnitKind.Angle, 1, 3),
        new("mrad", "mrad", UnitKind.Angle, 1 / MoaPerMrad, 3),
    };

    public static IReadOnlyList<UnitDef> Of(UnitKind kind) => All.Where(u => u.Kind == kind).ToList();

    public static UnitDef Find(string code) =>
        All.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("unknown unit: " + code, nameof(code));

    public static bool IsKnown(string code) => All.Any(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>The unit a kind starts with: the one the archive keeps.</summary>
    public static UnitDef BaseOf(UnitKind kind) => All.First(u => u.Kind == kind);

    /// <summary>The value as it is shown in <paramref name="code"/>, from the base unit.</summary>
    public static double FromBase(double baseValue, string code) => baseValue * Find(code).PerBase;

    /// <summary>The value in the base unit, from what was typed in <paramref name="code"/>.</summary>
    public static double ToBase(double shown, string code) => shown / Find(code).PerBase;

    public static string Label(string code) => Find(code).Label;

    /// <summary>A value of the base unit written in <paramref name="code"/>, with the decimals of that unit (or <paramref name="decimals"/>), without the unit.</summary>
    public static string Show(double baseValue, string code, int? decimals = null) =>
        FromBase(baseValue, code).ToString("0." + new string('#', Math.Max(0, decimals ?? Find(code).Decimals)), CultureInfo.InvariantCulture);

    /// <summary>The kind of quantity a unit written at the end of a field name belongs to ("gr" is a mass, "mm" a length), or null for the fields that have no choice.</summary>
    public static UnitKind? KindOfFieldUnit(string fieldUnit) => fieldUnit switch
    {
        "gr" => UnitKind.Mass,
        "mm" => UnitKind.Length,
        "m" => UnitKind.Distance,
        "MOA" => UnitKind.Angle,
        _ => null,
    };
}

/// <summary>
/// The click of a scope: an angle written as a fraction of a minute of angle or of a milliradian. The names are what the program shows and stores;
/// the old decimal names ("0.1 mrad") still read.
/// </summary>
public static class ScopeClicks
{
    public static readonly IReadOnlyList<string> Moa = new[] { "1/8 MOA", "1/4 MOA", "1/2 MOA" };
    public static readonly IReadOnlyList<string> Mrad = new[] { "1/20 mrad", "1/10 mrad" };
    public static IReadOnlyList<string> All => Moa.Concat(Mrad).ToList();

    public const string Default = "1/4 MOA";

    /// <summary>The click in minutes of angle, or null when the name is not one the program knows.</summary>
    public static double? InMoa(string? name) => Canonical(name) switch
    {
        "1/8 MOA" => 0.125,
        "1/4 MOA" => 0.25,
        "1/2 MOA" => 0.5,
        "1/20 mrad" => 0.05 * UnitSystem.MoaPerMrad,
        "1/10 mrad" => 0.1 * UnitSystem.MoaPerMrad,
        _ => null,
    };

    /// <summary>The click written the way the program shows it ("0.1 mrad" is "1/10 mrad"), or null when it is not a known click.</summary>
    public static string? Canonical(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string n = name.Trim();
        if (All.Contains(n, StringComparer.OrdinalIgnoreCase)) return All.First(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase));
        return n.ToLowerInvariant().Replace(',', '.') switch
        {
            "0.05 mrad" => "1/20 mrad",
            "0.1 mrad" => "1/10 mrad",
            "0.125 moa" => "1/8 MOA",
            "0.25 moa" => "1/4 MOA",
            "0.5 moa" => "1/2 MOA",
            _ => null,
        };
    }

    /// <summary>The unit a click is written in: "moa" or "mrad".</summary>
    public static string UnitOf(string canonicalName) => canonicalName.EndsWith("mrad", StringComparison.OrdinalIgnoreCase) ? "mrad" : "moa";
}

/// <summary>Words of a finished report that name a unit.</summary>
public static class UnitText
{
    /// <summary>
    /// A report is written with the angles in MOA; when the shooter reads them in milliradians the numbers were already worked out in that unit, and only the
    /// name of the unit changes. A click written as a fraction ("1/4 MOA") keeps its own name.
    /// </summary>
    public static string Angle(string text, string unit) =>
        string.Equals(unit, "mrad", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\b1/\d{1,2} )\bMOA\b", "mrad")
            : text;
}

/// <summary>Where the choices are kept: the program keeps them beside its other small settings; the tests keep them in memory.</summary>
public interface IUnitStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
    IEnumerable<string> Keys(string prefix);
}

public sealed class MemoryUnitStore : IUnitStore
{
    private readonly Dictionary<string, string> _v = new(StringComparer.Ordinal);
    public string? Get(string key) => _v.GetValueOrDefault(key);
    public void Set(string key, string value) => _v[key] = value;
    public void Remove(string key) => _v.Remove(key);
    public IEnumerable<string> Keys(string prefix) => _v.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
}

/// <summary>
/// The shooter's units: one starting choice for each kind of quantity and, for any single field, a choice of its own that wins over it ("the case length in
/// inches but every other length in millimetres"). Both are remembered. A choice that is no longer a unit of that kind (a file edited by hand) is ignored.
/// </summary>
public sealed class UnitPrefs
{
    private readonly IUnitStore _store;
    public UnitPrefs(IUnitStore store) => _store = store;

    private static string DefaultKey(UnitKind k) => "unit.default." + k.ToString().ToLowerInvariant();
    private static string FieldKey(string field) => "unit.field." + field;
    private const string ClickKey = "unit.click";

    private bool Valid(string? code, UnitKind kind) => code is not null && UnitSystem.IsKnown(code) && UnitSystem.Find(code).Kind == kind;

    /// <summary>True when the shooter chose a starting unit for this kind (as opposed to never having opened the choice).</summary>
    public bool HasDefaultChoice(UnitKind kind) => Valid(_store.Get(DefaultKey(kind)), kind);

    /// <summary>The unit a kind starts with when a field has no choice of its own.</summary>
    public string Default(UnitKind kind)
    {
        string? c = _store.Get(DefaultKey(kind));
        return Valid(c, kind) ? UnitSystem.Find(c!).Code : UnitSystem.BaseOf(kind).Code;
    }

    public void SetDefault(UnitKind kind, string code)
    {
        if (!Valid(code, kind)) throw new ArgumentException($"{code} is not a unit of {kind}", nameof(code));
        _store.Set(DefaultKey(kind), UnitSystem.Find(code).Code);
    }

    /// <summary>The unit a field is shown in: its own choice, else the starting one of its kind.</summary>
    public string For(string field, UnitKind kind)
    {
        string? c = _store.Get(FieldKey(field));
        return Valid(c, kind) ? UnitSystem.Find(c!).Code : Default(kind);
    }

    public void SetField(string field, UnitKind kind, string code)
    {
        if (!Valid(code, kind)) throw new ArgumentException($"{code} is not a unit of {kind}", nameof(code));
        _store.Set(FieldKey(field), UnitSystem.Find(code).Code);
    }

    /// <summary>The field goes back to the starting unit of its kind.</summary>
    public void ClearField(string field) => _store.Remove(FieldKey(field));

    /// <summary>Every field that has a choice of its own, with the unit chosen.</summary>
    public IReadOnlyList<(string Field, string Code)> FieldChoices() =>
        _store.Keys("unit.field.").Select(k => (Field: k["unit.field.".Length..], Code: _store.Get(k) ?? "")).Where(c => UnitSystem.IsKnown(c.Code)).OrderBy(c => c.Field, StringComparer.Ordinal).ToList();

    /// <summary>Every field goes back to the starting unit of its kind.</summary>
    public void ClearAllFields() { foreach (var c in FieldChoices()) ClearField(c.Field); }

    public bool HasOwnChoice(string field, UnitKind kind) => Valid(_store.Get(FieldKey(field)), kind);

    /// <summary>The click of the scope: a name of <see cref="ScopeClicks"/>.</summary>
    public string Click
    {
        get => ScopeClicks.Canonical(_store.Get(ClickKey)) ?? ScopeClicks.Default;
        set => _store.Set(ClickKey, ScopeClicks.Canonical(value) ?? throw new ArgumentException("not a click: " + value, nameof(value)));
    }

    /// <summary>The unit angles are shown in: the one of the click chosen, unless the shooter picked the other on purpose.</summary>
    public string AngleUnit => Default(UnitKind.Angle);
}
