using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Makes every <see cref="NumericUpDown"/> on a form accept BOTH "." and "," as the decimal
/// separator, whatever the Windows locale is. Without this, an Italian/German user typing
/// "39.20" gets "3920" (the "." is read as a thousands group) which then clamps to the maximum.
/// </summary>
internal static class NudFix
{
    /// <summary>
    /// Assigns a stored value to a spinner without ever throwing, by clamping to the control's
    /// own range. <see cref="NumericUpDown.Value"/> throws if the value falls outside
    /// Minimum..Maximum, and every number the toolkit puts in one of these comes from a database
    /// row or a .grtload — sources with a wider range than the spinner. The idiom this replaces,
    /// <c>Value = (decimal)Math.Min(max, x)</c>, guards only the top, so one negative row in the
    /// inventory took the edit dialog down in its constructor before it could be shown. A
    /// spinner that has to represent negatives says so in its Minimum; this only stops a crash.
    /// </summary>
    public static void Set(NumericUpDown nud, double value) =>
        nud.Value = Math.Clamp((decimal)value, nud.Minimum, nud.Maximum);

    public static void ApplyTo(Control root)
    {
        foreach (var nud in Descendants(root).OfType<NumericUpDown>())
        {
            nud.KeyPress -= Swap;
            nud.KeyPress += Swap;
        }
    }

    private static void Swap(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar is '.' or ',')
            e.KeyChar = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
