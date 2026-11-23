using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtModelLab.SensitivitySweep;

/// <summary>One value <paramref name="Param"/> should be set to for one sweep run.</summary>
internal readonly record struct SweepVariant(string Label, double Value, bool Absolute);

/// <summary>Pure GRT-load-editing logic for the sensitivity sweep — no IPC, no UI, so it can be
/// exercised head-on against a real .grtload fixture from a CLI test.</summary>
internal static class SweepEngine
{
    public static SweepVariant[] BuildVariants(SweepParam p, double delta)
    {
        if (p.IsAbsolute)
            return p.AbsoluteValues!.Select(v => new SweepVariant(FormattableString.Invariant($"{v:0.#} {p.Unit}"), v, true)).ToArray();
        return new[]
        {
            new SweepVariant(FormattableString.Invariant($"+{delta:0.###} {p.Unit}"), delta, false),
            new SweepVariant(FormattableString.Invariant($"-{delta:0.###} {p.Unit}"), -delta, false),
        };
    }

    /// <summary>Nudges one GRT input by <paramref name="value"/> (or sets it to <paramref name="value"/>
    /// outright when <paramref name="absolute"/>), preserving the file's own unit for mass (mp).
    /// Returns the raw value actually written, for verification/logging.</summary>
    public static double ApplyVariant(GrtLoadDoc doc, SweepParam p, double value, bool absolute)
    {
        if (p.Name == "mp")
        {
            double curGr = doc.BulletMassGr ?? 0;
            double newGr = absolute ? value : curGr + value;
            double newG = newGr * 0.06479891;
            string unit = doc.InputUnit(p.Section, p.Name);
            bool kg = string.Equals(unit, "kg", StringComparison.OrdinalIgnoreCase);
            double raw = kg ? newG / 1000.0 : newG;
            doc.SetInput(p.Section, p.Name, raw.ToString("0.######", CultureInfo.InvariantCulture), kg ? "kg" : "g");
            return raw;
        }

        double cur = doc.InputNumber(p.Section, p.Name) ?? 0;
        double newRaw = absolute ? value : cur + value;
        doc.SetInput(p.Section, p.Name, newRaw.ToString("0.######", CultureInfo.InvariantCulture), p.Unit);
        return newRaw;
    }
}
