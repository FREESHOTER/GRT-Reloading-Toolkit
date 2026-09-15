using System.Globalization;
using System.Text.Json;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Remembers what you typed into a tool window.
///
/// Every tool window is disposed when it closes (see <c>Program.Open</c>) and rebuilt from its
/// field initialisers next time, so without this the measurements you entered are gone the moment
/// the window shuts — you re-measure and re-type a case length you already had. The values live in
/// a small JSON file beside the journal database.
///
/// Measurements only. Nothing here is a setting you would go looking for in a preferences dialog;
/// it is the state of the boxes as you left them.
/// </summary>
internal static class UiState
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GRTPlugins", "toolkit-ui.json");

    private static Dictionary<string, string>? _v;

    private static Dictionary<string, string> Values => _v ??= Read();

    private static Dictionary<string, string> Read()
    {
        // A missing, truncated or hand-edited file must never stop a tool opening: the cost of
        // losing it is retyping a measurement, which is exactly where we started.
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path))
                       ?? new Dictionary<string, string>();
        }
        catch (Exception) { /* fall through to an empty store */ }
        return new Dictionary<string, string>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(Values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* not worth interrupting the user over */ }
    }

    /// <summary>
    /// Restores the given controls from the store now, and writes them back when the form closes.
    ///
    /// Order matters: list a unit picker BEFORE the boxes it converts. Restoring a picker fires its
    /// change handler, which rescales whatever is in the boxes at the time; doing it first means it
    /// rescales the harmless defaults, and the real values land afterwards already in the right unit.
    /// </summary>
    public static void Bind(Form form, string prefix, params (string Key, Control Control)[] fields)
    {
        foreach (var (key, c) in fields) Restore(prefix + "." + key, c);
        form.FormClosed += (_, _) =>
        {
            foreach (var (key, c) in fields) Capture(prefix + "." + key, c);
            Save();
        };
    }

    private static void Restore(string key, Control c)
    {
        if (!Values.TryGetValue(key, out string? s) || s.Length == 0) return;
        try
        {
            switch (c)
            {
                case NumericUpDown n when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d):
                    n.Value = Math.Clamp(d, n.Minimum, n.Maximum);
                    break;
                case ComboBox cb when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i):
                    if (i >= 0 && i < cb.Items.Count) cb.SelectedIndex = i;
                    break;
                case CheckBox ck when bool.TryParse(s, out bool b):
                    ck.Checked = b;
                    break;
                case TextBox tb:
                    tb.Text = s;
                    break;
            }
        }
        catch (Exception) { /* a stored value that no longer fits the control is not worth a crash */ }
    }

    private static void Capture(string key, Control c)
    {
        string? s = c switch
        {
            NumericUpDown n => n.Value.ToString(CultureInfo.InvariantCulture),
            ComboBox cb => cb.SelectedIndex.ToString(CultureInfo.InvariantCulture),
            CheckBox ck => ck.Checked ? "true" : "false",
            TextBox tb => tb.Text,
            _ => null,
        };
        if (s != null) Values[key] = s;
    }
}
