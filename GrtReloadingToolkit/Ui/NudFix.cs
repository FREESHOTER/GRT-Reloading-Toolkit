using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Makes every <see cref="NumericUpDown"/> on a form accept BOTH "." and "," as the decimal
/// separator, whatever the Windows locale is. Without this, an Italian/German user typing
/// "39.20" gets "3920" (the "." is read as a thousands group) which then clamps to the maximum.
/// </summary>
internal static class NudFix
{
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
