namespace GrtModelLab.Ui;

/// <summary>
/// The application icon, for windows to wear in their title bar and on the taskbar.
///
/// &lt;ApplicationIcon&gt; in the csproj does NOT reach either of those. It writes an icon into the
/// exe's Win32 resources, which is what Explorer, the Start menu and shortcuts read - but a
/// window's title bar and its taskbar button follow the *window's* icon, and WinForms defaults
/// that to a hardcoded "wfc.ico" resource inside System.Windows.Forms.dll, never to the host exe.
/// Same fix as the GRT Reloading Toolkit's sibling plugin (PR #15) found the hard way: assigning
/// the icon is the only way it reaches a window.
///
/// Loaded from the embedded copy of app.ico rather than Icon.ExtractAssociatedIcon, because that
/// returns a single 32x32 and app.ico carries separately-sized renditions (16/32/48/256); handing
/// Windows the whole file lets it pick the rendition drawn for each size.
/// </summary>
internal static class AppIcon
{
    // One process-wide instance. Form does not dispose an Icon assigned to it (it only disposes
    // the small variant it derives), so every window can share this one safely.
    private static readonly Lazy<Icon?> _shared = new(() =>
    {
        try
        {
            using Stream? s = typeof(AppIcon).Assembly.GetManifestResourceStream("GrtModelLab.app.ico");
            return s is null ? null : new Icon(s);
        }
        catch
        {
            // An icon is decoration: a window with the stock glyph still works. Never take the
            // app down over one.
            return null;
        }
    });

    /// <summary>The app icon, or null if the embedded resource is missing or unreadable.</summary>
    public static Icon? Shared => _shared.Value;

    /// <summary>Gives <paramref name="f"/> the app icon, and returns it so calls can be chained.</summary>
    public static T Apply<T>(T f) where T : Form
    {
        if (Shared is { } icon) f.Icon = icon;
        return f;
    }
}
