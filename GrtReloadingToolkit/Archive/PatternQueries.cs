using GrtReloadingToolkit.Shooting;

namespace GrtReloadingToolkit.Archive;

/// <summary>Shot groups of the archive as the reading of the shooter's errors takes them.</summary>
public sealed record PatternFromArchive(IReadOnlyList<PatternShot> Shots, bool AimKnown, double? DistanceM, IReadOnlyList<string> Notes);

/// <summary>The groups of the archive as a match: one target for each group, the shots in the order of the groups.</summary>
public sealed record MatchFromArchive(IReadOnlyList<MatchShot> Shots, bool AimKnown, IReadOnlyList<string> Notes);

public static class PatternQueries
{
    /// <summary>
    /// A match from the archive: each group is a target (with its own distance, or <paramref name="defaultDistanceM"/> when it has none), its hits are the shots
    /// in the order they were fired and are numbered one after the other over the groups; a hit linked to a chronographed shot also brings the
    /// velocity and the time of that shot. Hits with no scale in mm are left out and said so.
    /// </summary>
    public static MatchFromArchive MatchFromGroups(ArchiveDb a, IReadOnlyList<ArchiveGroup> groups, double defaultDistanceM)
    {
        var shots = new List<MatchShot>();
        var notes = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            string name = (g.Tab.Length > 0 && g.Tab != g.Label ? g.Tab + " · " : "") + g.Label;
            if (name.Length == 0) name = "#" + g.Id;
            for (string unique = name; !used.Add(unique);) name = unique = name + "'";
            var linked = HitShots(a, g.Id);
            int skipped = 0;
            for (int i = 0; i < g.Hits.Count; i++)
            {
                var h = g.Hits[i];
                if (h.XMm is not { } x || h.YMm is not { } y) { skipped++; continue; }
                linked.TryGetValue(i, out var s);
                shots.Add(new MatchShot(shots.Count + 1, name, g.DistanceM is > 0 ? g.DistanceM.Value : defaultDistanceM, x, y, s.Velocity, ArchiveQueries.ClockSeconds(s.Note)));
            }
            if (skipped > 0) notes.Add(Tr.F("{0}: {1} hits without a scale in mm were left out.", name, skipped));
            if (g.DistanceM is not > 0) notes.Add(Tr.F("{0}: no distance, {1} m is used.", name, defaultDistanceM.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
        }
        bool aim = groups.Count > 0 && groups.All(g => g.OriginPoint == "punto di mira");
        if (!aim && groups.Count > 0) notes.Add(Tr.T("Some groups have no point of aim marked: a match is read from the point of aim of each target, so this reading is not reliable."));
        return new MatchFromArchive(shots, aim, notes);
    }

    /// <summary>
    /// One series for each group, its hits in the order they were fired; a hit linked to a chronographed shot also brings that shot's velocity and
    /// time of day. The coordinates are the archive's (mm, x to the right, y up) from the point of aim when every group has one marked.
    /// Hits with no scale (position on the picture only) are left out and said so.
    /// </summary>
    public static PatternFromArchive FromGroups(ArchiveDb a, IReadOnlyList<ArchiveGroup> groups)
    {
        var shots = new List<PatternShot>();
        var notes = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            string name = (g.Tab.Length > 0 && g.Tab != g.Label ? g.Tab + " · " : "") + g.Label;
            if (name.Length == 0) name = "#" + g.Id;
            for (string unique = name; !used.Add(unique);) name = unique = name + "'";
            var linked = HitShots(a, g.Id);
            int order = 0, skipped = 0;
            for (int i = 0; i < g.Hits.Count; i++)
            {
                var h = g.Hits[i];
                if (h.XMm is not { } x || h.YMm is not { } y) { skipped++; continue; }
                linked.TryGetValue(i, out var s);
                shots.Add(new PatternShot(name, order, order + 1, x, y, s.Velocity, ArchiveQueries.ClockSeconds(s.Note)));
                order++;
            }
            if (skipped > 0) notes.Add(Tr.F("{0}: {1} hits without a scale in mm were left out.", name, skipped));
        }
        var distances = groups.Where(g => g.DistanceM is > 0).Select(g => g.DistanceM!.Value).ToList();
        double? distance = distances.Count == 0 ? null : distances.GroupBy(d => d).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        if (distances.Distinct().Count() > 1) notes.Add(Tr.F("The groups were shot at different distances: {0} m is used.", distance!.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
        bool aim = groups.Count > 0 && groups.All(g => g.OriginPoint == "punto di mira");
        return new PatternFromArchive(shots, aim, distance, notes);
    }

    /// <summary>For each hit of a group, by its place in the group, the velocity and the note of the shot it is linked to (when it is).</summary>
    private static Dictionary<int, (double? Velocity, string? Note)> HitShots(ArchiveDb a, long groupId)
    {
        var d = new Dictionary<int, (double?, string?)>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT sh.velocity_mps, sh.note FROM hits h LEFT JOIN shots sh ON sh.id=h.shot_id WHERE h.group_id=$g ORDER BY h.idx";
        cmd.Parameters.AddWithValue("$g", groupId);
        using var r = cmd.ExecuteReader();
        int i = 0;
        while (r.Read())
        {
            double? v = r.IsDBNull(0) ? null : r.GetDouble(0);
            d[i++] = (v is > 0 ? v : null, r.IsDBNull(1) ? null : r.GetString(1));
        }
        return d;
    }
}
