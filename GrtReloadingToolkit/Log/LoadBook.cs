using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// Idea #5 from the original next-plugin brainstorm ("walk a folder of .grtload, emit one document:
/// every recipe + predicted numbers + measured journal results, grouped by cartridge"). "Predicted"
/// here means the propellant model coefficients (<see cref="Entry.Ba"/>/<see cref="Entry.A0"/>) GRT's
/// own simulation is calibrated to for that recipe -- not a freshly re-simulated velocity, which would
/// need GRT itself open and connected per file, defeating the point of walking a folder of files GRT
/// has never seen open at once. "Measured" prefers a Journal entry logged against that exact file
/// (richer: bullet, group size, distance, notes) over the file's own embedded shot data, falling back
/// to the latter when nothing was ever logged for it.
/// </summary>
public static class LoadBook
{
    public sealed record Entry(
        string EffectivePath, string LoadName, string Caliber, string Firearm,
        string PowderName, string? BulletName, double? ChargeGr, double? CoalMm,
        double? Ba, double? A0,
        double? VelocityAvgMs, double? SdMs, double? EsMs, int Shots,
        double? GroupMoa, double? DistanceM, string? JournalDate, string? Notes, bool FromJournal);

    /// <summary>
    /// One file per real recipe, not per file on disk: a folder of real range work typically holds a
    /// pristine load plus several of this toolkit's own write-back siblings (or GRT's own auto-saved
    /// ones, e.g. "..._calibration_20260101_1200.grtload") next to it, and counting each of those as
    /// its own row would multiply every recipe several times over. Collapses each "family" (GRT's own
    /// generated siblings excluded outright, this toolkit's own <c>_toolkit_*</c> siblings collapsed
    /// via <see cref="GrtLoadDoc.PristineBasePath"/>) down to the single newest file worth reading,
    /// the same <see cref="GrtLoadDoc.EffectiveReadPath"/> every other tool already reads a load
    /// through -- just applied to every family in a folder instead of to GRT's one currently-open tab.
    /// </summary>
    public static IReadOnlyList<string> DiscoverFamilyRepresentatives(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(folder, "*.grtload", SearchOption.AllDirectories)
            .Where(f => !GrtLoadDoc.LooksLikeGeneratedSibling(f))
            .Select(GrtLoadDoc.PristineBasePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(GrtLoadDoc.EffectiveReadPath)
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Pure function, no Db dependency, like every other Log/ calculation -- the caller reads
    /// the Journal and the component list once for a whole folder, not once per file.</summary>
    public static Entry BuildEntry(string effectivePath, IReadOnlyList<JournalEntry> journal,
        IReadOnlyDictionary<long, Component> componentsById)
    {
        var snap = LoadSnapshot.FromGrtload(effectivePath);
        string pristine = GrtLoadDoc.PristineBasePath(effectivePath);

        // Prefers the most recently logged entry against this exact file (or its pristine base, in
        // case the effective path resolved to a newer toolkit sibling than whatever was open at log
        // time). A file renamed by a LATER save after being logged won't match -- the same accepted
        // limitation every other GrtloadPath-keyed lookup in this codebase already lives with (see
        // ShotMeasurement's own doc comment).
        var je = journal
            .Where(e => !string.IsNullOrEmpty(e.GrtloadPath) &&
                        (PathsEqual(e.GrtloadPath, effectivePath) || PathsEqual(e.GrtloadPath, pristine)))
            .OrderByDescending(e => e.Id)
            .FirstOrDefault();

        if (je is null)
            return new Entry(effectivePath, snap.LoadName, snap.Caliber, snap.Firearm,
                snap.PowderName, snap.BulletName.Length > 0 ? snap.BulletName : null, snap.ChargeGr, snap.CoalMm,
                snap.Ba, snap.A0, snap.VelocityAvgMs, snap.SdMs, snap.EsMs, snap.Shots,
                null, null, null, null, FromJournal: false);

        string? bullet = je.BulletId is { } bid && componentsById.TryGetValue(bid, out var b)
            ? b.Display
            : snap.BulletName.Length > 0 ? snap.BulletName : null;

        return new Entry(effectivePath,
            je.LoadName.Length > 0 ? je.LoadName : snap.LoadName,
            je.Caliber.Length > 0 ? je.Caliber : snap.Caliber,
            je.Firearm.Length > 0 ? je.Firearm : snap.Firearm,
            snap.PowderName, bullet,
            je.ChargeGr > 0 ? je.ChargeGr : snap.ChargeGr,
            je.CoalMm ?? snap.CoalMm,
            je.Ba ?? snap.Ba, je.A0 ?? snap.A0,
            je.VelocityAvgMs ?? snap.VelocityAvgMs, je.SdMs ?? snap.SdMs, je.EsMs ?? snap.EsMs,
            je.Rounds > 0 ? je.Rounds : snap.Shots,
            je.GroupMoa, je.DistanceM, je.Date, je.Notes.Length > 0 ? je.Notes : null, FromJournal: true);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(NormalizeFullPath(a), NormalizeFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFullPath(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    /// <summary>Groups for the report's own sectioning -- caliber names come straight from GRT/Journal
    /// text, not a controlled vocabulary, so grouping is by exact string, case-insensitively (".308
    /// Winchester" and ".308 winchester" collapse; ".308 Win" does not, and is its own section).</summary>
    public static IReadOnlyList<IGrouping<string, Entry>> GroupByCaliber(IEnumerable<Entry> entries) =>
        entries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Caliber) ? "(unknown caliber)" : e.Caliber,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
