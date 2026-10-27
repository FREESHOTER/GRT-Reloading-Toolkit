using System.Globalization;

namespace GrtReloadingToolkit.Log;

/// <summary>From the outings stored for a load to the sessions <see cref="LoadFollowUp"/> analyses.</summary>
internal static class FollowSessions
{
    public static List<FollowSession> From(IEnumerable<Outing> outings)
    {
        var list = new List<FollowSession>();
        foreach (var o in outings)
        {
            if (!DateTime.TryParse(o.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            list.Add(new FollowSession(
                date, o.TempC, o.Velocities(), o.GroupMoa, o.DistanceM,
                Text(o.PowderLot), Text(o.PrimerLot), o.SeatingMm));
        }
        return list;
    }

    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
