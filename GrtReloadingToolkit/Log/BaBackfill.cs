namespace GrtReloadingToolkit.Log;

/// <summary>
/// Fills in the Ba (and a0) of journal entries written before those columns existed, by reading
/// them back out of the .grtload each entry already points at.
///
/// A journal from before those columns has a caliber on every row and a Ba on none, which leaves
/// "Find best Ba" with an empty caliber list and no way to type into it. The numbers aren't lost:
/// Barrel Calibration wrote them into the load file, and the row records that file's path, so they
/// can be read back instead of retyped.
///
/// Deliberately two steps. <see cref="Scan"/> only reads, and reports on every candidate including
/// the ones it can't help; <see cref="Apply"/> only writes what a caller hands back to it. Nothing
/// here runs by itself -- it fills in numbers a user is equally entitled to have left empty, so it
/// is offered rather than performed.
/// </summary>
public static class BaBackfill
{
    /// <summary>What reading one entry's load turned up.</summary>
    public enum Result
    {
        /// <summary>The load carries a Ba, and the entry has room for it.</summary>
        Found,

        /// <summary>The load reads fine but holds no Ba -- never calibrated, or saved before the fit.</summary>
        NoBaInLoad,

        /// <summary>The path the entry recorded no longer resolves to a file.</summary>
        FileMissing,

        /// <summary>The file is there but wouldn't parse.</summary>
        Unreadable,
    }

    /// <summary>One candidate row and what its load said. Ba/A0 are set only when <see cref="Result.Found"/>.</summary>
    public sealed record Candidate(
        long Id, string Date, string LoadName, string Caliber, string Path,
        Result Result, double? Ba, double? A0, string? Error);

    /// <summary>Reads the load behind every Ba-less entry. Writes nothing.</summary>
    public static List<Candidate> Scan(Db db) => db.EntriesMissingBa().Select(Examine).ToList();

    /// <summary>
    /// Writes the found values back, and returns how many rows actually took one. That can be
    /// fewer than it was handed: <see cref="Db.FillBa"/> refuses a row that has since gained a Ba,
    /// which is what keeps a second run -- or a run against a journal edited in between -- honest.
    /// </summary>
    public static int Apply(Db db, IEnumerable<Candidate> found)
    {
        int n = 0;
        foreach (var c in found)
            if (c.Result == Result.Found && c.Ba is { } ba && db.FillBa(c.Id, ba, c.A0))
                n++;
        return n;
    }

    private static Candidate Examine(JournalEntry e)
    {
        Candidate With(Result r, double? ba = null, double? a0 = null, string? err = null) =>
            new(e.Id, e.Date, e.LoadName, e.Caliber, e.GrtloadPath, r, ba, a0, err);

        if (!File.Exists(e.GrtloadPath)) return With(Result.FileMissing);
        try
        {
            var load = LoadSnapshot.FromGrtload(e.GrtloadPath);
            return load.Ba is { } ba ? With(Result.Found, ba, load.A0) : With(Result.NoBaInLoad);
        }
        catch (Exception ex)
        {
            // Broad on purpose. A .grtload can be truncated, half-written, malformed or locked by
            // GRT itself, and one unreadable file must not stop the sweep reaching the rest -- the
            // row is reported as unreadable and left exactly as it was.
            return With(Result.Unreadable, err: ex.Message);
        }
    }
}
