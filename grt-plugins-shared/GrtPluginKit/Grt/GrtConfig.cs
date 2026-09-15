namespace GrtPluginKit.Grt;

/// <summary>
/// GRT's own settings file, <c>GordonsReloadingTool.cfg</c>, next to the exe.
///
/// The part worth reading is <c>ValueUnits</c>: a single line holding GRT's per-field unit map,
/// which is what decides the unit GRT shows you for each quantity. It is user-configurable and
/// people really do differ — one install reads
/// <c>caselen=in;oal=in;gdepth=in;pressure=psi;velocity=ft/s;pt=F;range=yard</c> while another
/// reads <c>caselen=mm;oal=mm;gdepth=mm;pressure=bar;velocity=m/s;pt=°C;range=m</c>. A plugin that
/// hardcodes either one is wrong for half its users, so we read this and follow.
///
/// Display precision is NOT in here. GRT's decimal places live in the compiled binary, so the
/// four-decimal length rule rests on what GRT stores and shows, not on anything configurable.
/// </summary>
public sealed class GrtConfig
{
    private readonly Dictionary<string, string> _units;

    private GrtConfig(Dictionary<string, string> units) => _units = units;

    /// <summary>Field id (as used in a .grtload input, e.g. <c>gdepth</c>) to the unit GRT displays it in.</summary>
    public IReadOnlyDictionary<string, string> ValueUnits => _units;

    /// <summary>The unit GRT shows <paramref name="field"/> in, or null when GRT has no opinion.</summary>
    public string? UnitFor(string field) => _units.TryGetValue(field, out string? u) && u.Length > 0 ? u : null;

    /// <summary>
    /// True when GRT displays <paramref name="field"/> in inches. Only lengths answer this
    /// meaningfully; a field GRT has no entry for, or that is not a length, gives false.
    /// </summary>
    public bool IsInch(string field) => UnitFor(field) is "in" or "inch" or "\"";

    /// <summary>
    /// The GRT install root — the folder holding the exe, the cfg and <c>doku/</c>.
    /// Found by walking up from the running plugin, which lives in <c>&lt;root&gt;/plugins/&lt;name&gt;/</c>.
    /// <c>GRT_DIR</c> overrides it, which is how this gets exercised off a real install.
    /// Null when the plugin is running somewhere else entirely, e.g. a dev box.
    /// </summary>
    public static string? Root()
    {
        string? env = Environment.GetEnvironmentVariable("GRT_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 5 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "doku")))
                return dir.FullName;
        return null;
    }

    /// <summary>
    /// Reads the config from the GRT install, or null if there is no install to read — in which
    /// case the caller keeps its own defaults rather than guessing at units.
    /// </summary>
    public static GrtConfig? Load(string? grtRoot = null)
    {
        grtRoot ??= Root();
        if (grtRoot == null) return null;
        string path = Path.Combine(grtRoot, "GordonsReloadingTool.cfg");
        if (!File.Exists(path)) return null;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq < 0 || line.AsSpan(0, eq).Trim() is not "ValueUnits") continue;
                return new GrtConfig(ParseUnits(line[(eq + 1)..]));
            }
        }
        catch (Exception) { /* an unreadable config is the same as no config */ }
        return null;
    }

    /// <summary>Parses <c>mc=grain;caselen=in;oal=in;…</c>. Values may be empty or contain spaces
    /// (<c>grain H2O</c>) and non-ASCII (<c>mm²</c>, <c>°C</c>).</summary>
    internal static Dictionary<string, string> ParseUnits(string value)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            map[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
        }
        return map;
    }
}
