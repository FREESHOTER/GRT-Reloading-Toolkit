namespace GrtReloadingToolkit.Ui;

/// <summary>
/// A launcher button caption without the emoji that used to lead it. The captions keep their translation keys (emoji
/// included) and the emoji is dropped here: everything before the first letter or digit goes -- emoji are surrogate pairs
/// and symbols, followed by spaces and sometimes a variation selector.
/// </summary>
internal static class LauncherCaption
{
    public static string From(string text)
    {
        int i = 0;
        while (i < text.Length && !char.IsLetterOrDigit(text, i)) i += char.IsSurrogatePair(text, i) ? 2 : 1;
        return i >= text.Length ? text : text[i..];
    }
}
