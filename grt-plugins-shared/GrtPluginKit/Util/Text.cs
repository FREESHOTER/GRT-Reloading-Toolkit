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
}
