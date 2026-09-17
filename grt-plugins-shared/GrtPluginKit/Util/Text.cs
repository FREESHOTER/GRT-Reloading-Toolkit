using System.Globalization;
using System.Text;

namespace GrtPluginKit.Util;

public static class Str
{
    /// <summary>lowercase, strip diacritics, collapse whitespace — for header matching.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        string decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        string t = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => n.Length > 0 && haystack.Contains(n, StringComparison.Ordinal));

    /// <summary>
    /// Parses a number that may use "." or "," as the decimal mark and may carry a
    /// trailing unit ("806.6 MPS", "367,9 KGR-FT/S"). Returns null if not numeric.
    /// </summary>
    public static double? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string s = raw.Trim();
        // keep the leading numeric token only
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-' || s[i] == '−')) i++;
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ',' || s[i] == ' ')) i++;
        string token = s[..i].Replace("−", "-").Replace(" ", "");
        if (token.Length == 0 || (start == i)) return null;

        bool hasComma = token.Contains(',');
        bool hasDot = token.Contains('.');
        if (hasComma && hasDot)
            token = token.LastIndexOf(',') > token.LastIndexOf('.')
                ? token.Replace(".", "").Replace(',', '.')
                : token.Replace(",", "");
        else if (hasComma)
            // comma-only: "2,885" is a thousands group (US), "2885,8" is a decimal (EU).
            token = System.Text.RegularExpressions.Regex.IsMatch(token, @",\d{3}$")
                    && !System.Text.RegularExpressions.Regex.IsMatch(token, @",\d{1,2}$")
                ? token.Replace(",", "")
                : token.Replace(',', '.');

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? v
            : null;
    }

    public static double FahrenheitToCelsius(double f) => (f - 32.0) * 5.0 / 9.0;

    /// <summary>Formats with invariant '.', trimmed to at most <paramref name="decimals"/> places.</summary>
    public static string Num(double v, int decimals = 1)
        => v.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);

    /// <summary>
    /// How GRT shows a length: four decimals, trailing zeros kept, never rounded shorter. GRT
    /// stores lengths as full doubles and its own fields read to four places, so anything we
    /// display beside them has to match or it looks like a different measurement. Four places is
    /// also what the tools resolve - a comparator reads to 0.0001 in, finer than 0.001 mm.
    /// Lengths only: charges, velocities and volumes have their own, coarser precision.
    /// </summary>
    public const string LengthFormat = "0.0000";

    /// <summary>A length, formatted the way GRT shows lengths. See <see cref="LengthFormat"/>.</summary>
    public static string Len(double v) => v.ToString(LengthFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// A ladder step whose unit the call site cannot know - it is a seating depth in a seating
    /// ladder and a powder weight in a charge ladder. Four places are available so a length is
    /// never rounded, but trailing zeros are dropped so a charge still reads the short way it
    /// always has: 41.5 stays "41.5", 2.8125 stays "2.8125". Where the mode IS known, use
    /// <see cref="LengthFormat"/> or the charge format directly rather than this.
    /// </summary>
    public const string StepFormat = "0.0###";
}
