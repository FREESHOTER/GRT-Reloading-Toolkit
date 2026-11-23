using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtSensitivityLab.WhatIf;

/// <summary>Pure GRT-load-editing logic for the "what if" tool -- no IPC, no UI, so it can be
/// exercised head-on against a real .grtload fixture from a CLI test.</summary>
internal static class WhatIfEngine
{
    /// <summary>The parameter's current value in <paramref name="doc"/>, in
    /// <see cref="WhatIfParam.Unit"/> -- what the form shows next to the measured-value field so
    /// the user knows what they're about to replace.</summary>
    public static double? CurrentValue(GrtLoadDoc doc, WhatIfParam p) =>
        p.Name == "mp" ? doc.BulletMassGr : doc.InputNumber(p.Section, p.Name);

    /// <summary>Writes <paramref name="measured"/> into <paramref name="doc"/> in place of the
    /// file's own value for <paramref name="p"/>, preserving the file's own unit for mass (mp) --
    /// same conversion GrtModelLab's SweepEngine used for its own absolute-value variants.</summary>
    public static void Substitute(GrtLoadDoc doc, WhatIfParam p, double measured)
    {
        if (p.Name == "mp")
        {
            double newG = measured * 0.06479891;
            string unit = doc.InputUnit(p.Section, p.Name);
            bool kg = string.Equals(unit, "kg", StringComparison.OrdinalIgnoreCase);
            double raw = kg ? newG / 1000.0 : newG;
            doc.SetInput(p.Section, p.Name, raw.ToString("0.######", CultureInfo.InvariantCulture), kg ? "kg" : "g");
            return;
        }

        doc.SetInput(p.Section, p.Name, measured.ToString("0.######", CultureInfo.InvariantCulture), p.Unit);
    }
}
