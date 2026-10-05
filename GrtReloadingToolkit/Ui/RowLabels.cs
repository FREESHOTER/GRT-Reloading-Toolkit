using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// GRT names every Measurement it creates "Misurazione", so a load with several sessions at the same
/// charge produces several strings with the very same display label. Tools that find a row by its
/// label (the compare pickers, the stored per-shot temperatures) would silently pick the first one.
/// </summary>
internal static class RowLabels
{
    /// <summary>The label, or "label #2", "label #3"... if it is already taken.</summary>
    public static string Unique(IEnumerable<string> taken, string label)
    {
        var set = new HashSet<string>(taken, StringComparer.Ordinal);
        if (!set.Contains(label)) return label;
        for (int i = 2; ; i++)
        {
            string cand = $"{label} #{i}";
            if (!set.Contains(cand)) return cand;
        }
    }

    /// <summary>A short stable id of the shots themselves (order matters, 0.1 resolution): the same
    /// string always gets the same id, a different string at the same charge never shares it.</summary>
    public static string Fingerprint(IReadOnlyList<double> velocities)
    {
        uint h = 2166136261;
        foreach (double v in velocities)
            foreach (char c in v.ToString("0.0", CultureInfo.InvariantCulture)) { h ^= c; h *= 16777619; }
        return h.ToString("x8", CultureInfo.InvariantCulture);
    }
}
