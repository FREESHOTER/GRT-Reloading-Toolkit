using System.Globalization;

namespace GrtReloadingToolkit.Log;

public enum AlertKind { NewPowder, NewPrimer, VelocityDrift, SeatingChanged, BrassChanged, BarrelLimit }

/// <summary>
/// An alert on a load that was already worked up: something changed, or drifted, since the last session, and the load should be looked at again.
/// The text is an English translation key with {0}, {1}... for the numbers (the window translates it and fills them in invariantly);
/// <see cref="ReturnToPhase"/> is the phase of the recommended path to go back to (2: the charge, 3: the seating depth).
/// </summary>
public sealed record LoadAlert(AlertKind Kind, string Caliber, string LoadName, string Template, object[] Args, int ReturnToPhase);

/// <summary>What the alerts read: the journal, the components it points at, and the firearms with the rounds through each.</summary>
public sealed record AlertContext(
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyDictionary<long, Component> Components,
    IReadOnlyList<(Firearm Firearm, int Rounds)> Firearms,
    Func<double, string> Vel);

/// <summary>
/// Alerts "to review the load" (step 22 of the recommended path), read from the journal over time. Each rests on a fact in the journal and
/// says which; none is a verdict. Experimental like the guided diagnosis, and for the same reason: the community is asked to try them.
/// A1 a different powder or primer than in the last session of the same load; A2 a velocity drift across its sessions (the toolkit's
/// own session trend); A3 the barrel past the limit the shooter set for it (no number of ours: it depends on the barrel); A4 a changed
/// seating depth or different brass since the last session.
/// </summary>
public static class LoadAlerts
{
    /// <summary>A change of seating depth (CBTO, or COAL when CBTO is missing) at or above this is worth a new look. A comparator repeats
    /// to about 0.01 mm and seating tests are stepped in 0.05 mm or more, so a smaller change is noise, not a decision.</summary>
    public const double SeatingChangeMm = 0.05;

    /// <summary>Sessions the velocity trend needs before a drift is claimed.</summary>
    public const int MinSessionsForDrift = 3;

    /// <summary>From this share of the limit on, the barrel alert says "approaching" rather than "past".</summary>
    public const double ApproachingShare = 0.9;

    private static string F(double v, string fmt = "0.00") => v.ToString(fmt, CultureInfo.InvariantCulture);

    private static string Lot(Component? c) =>
        c is null ? "?" : string.IsNullOrWhiteSpace(c.Lot) ? $"{c.Brand} {c.Name}".Trim() : $"{c.Brand} {c.Name} (lot {c.Lot})".Trim();

    public static IReadOnlyList<LoadAlert> All(AlertContext ctx)
    {
        var list = new List<LoadAlert>();
        // a "load" here: same caliber, bullet, load name and charge (to a tenth); the powder and primer are what is compared inside it
        var loads = ctx.Journal.GroupBy(e => (Caliber: e.Caliber.ToUpperInvariant(), e.BulletId, Identity: LoadIdentity.Normalize(e.LoadName), Charge: Math.Round(e.ChargeGr, 1)));
        foreach (var g in loads)
        {
            var ordered = g.OrderBy(e => e.Id).ToList();
            if (ordered.Count < 2) continue;
            var last = ordered[^1];
            var before = ordered[^2];
            string name = LoadIdentity.Display(last.LoadName);

            if (last.PowderId is { } pl && before.PowderId is { } pb && pl != pb)
                list.Add(new LoadAlert(AlertKind.NewPowder, last.Caliber, last.LoadName,
                    "{0}: the powder is not the one of the last session ({1} before, {2} now): check the velocity before trusting the charge.",
                    new object[] { name, Lot(ctx.Components.GetValueOrDefault(pb)), Lot(ctx.Components.GetValueOrDefault(pl)) }, 2));
            if (last.PrimerId is { } ql && before.PrimerId is { } qb && ql != qb)
                list.Add(new LoadAlert(AlertKind.NewPrimer, last.Caliber, last.LoadName,
                    "{0}: the primer is not the one of the last session ({1} before, {2} now): check the velocity and its spread before trusting the load.",
                    new object[] { name, Lot(ctx.Components.GetValueOrDefault(qb)), Lot(ctx.Components.GetValueOrDefault(ql)) }, 2));

            // CBTO when both sessions have it, COAL when both have that: never one against the other
            double? depthNow = null, depthBefore = null;
            if (last.CbtoMm is not null && before.CbtoMm is not null) { depthNow = last.CbtoMm; depthBefore = before.CbtoMm; }
            else if (last.CoalMm is not null && before.CoalMm is not null) { depthNow = last.CoalMm; depthBefore = before.CoalMm; }
            if (depthNow is { } dn && depthBefore is { } db && Math.Abs(dn - db) >= SeatingChangeMm - 1e-9)
                list.Add(new LoadAlert(AlertKind.SeatingChanged, last.Caliber, last.LoadName,
                    "{0}: the seating depth changed since the last session ({1} mm before, {2} mm now): check the depth again.",
                    new object[] { name, F(db), F(dn) }, 3));
            if (last.BrassId is { } bl && before.BrassId is { } bb && bl != bb)
                list.Add(new LoadAlert(AlertKind.BrassChanged, last.Caliber, last.LoadName,
                    "{0}: the brass is not the one of the last session ({1} before, {2} now): check the seating depth and the velocity.",
                    new object[] { name, Lot(ctx.Components.GetValueOrDefault(bb)), Lot(ctx.Components.GetValueOrDefault(bl)) }, 3));
        }

        // velocity drift: the session trend the toolkit already has, on loads with enough sessions
        var byId = ctx.Journal.ToDictionary(e => e.Id);
        foreach (var t in SessionTrend.AnalyzeChargeTrends(ctx.Journal))
        {
            if (t.NSessions < MinSessionsForDrift) continue;
            if (!(t.VelocityMismatch || t.VelocityDeclining)) continue;
            var first = byId[t.EntryIds[0]];
            string name = LoadIdentity.Display(first.LoadName);
            list.Add(new LoadAlert(AlertKind.VelocityDrift, first.Caliber, first.LoadName,
                "{0}: across {1} sessions the mean velocity moves by {2} (a slope of {3} per session): a drift to look at.",
                new object[] { name, t.NSessions, ctx.Vel(t.VelocitySpreadMps ?? 0), ctx.Vel(t.VelocityTrendSlopeMps ?? 0) }, 2));
        }

        foreach (var (fa, rounds) in ctx.Firearms)
        {
            if (fa.RoundsLimit is not { } limit || limit <= 0) continue;
            if (rounds >= limit)
                list.Add(new LoadAlert(AlertKind.BarrelLimit, fa.Caliber, "", "{0}: {1} rounds through the barrel, past the limit of {2} you set for it.",
                    new object[] { fa.Name, rounds, limit }, 2));
            else if (rounds >= limit * ApproachingShare)
                list.Add(new LoadAlert(AlertKind.BarrelLimit, fa.Caliber, "", "{0}: {1} rounds through the barrel, close to the limit of {2} you set for it.",
                    new object[] { fa.Name, rounds, limit }, 2));
        }
        return list;
    }

    /// <summary>The alerts that concern one load: its own, matched by caliber and load name (the barrel alerts concern every load of the caliber).</summary>
    public static IReadOnlyList<LoadAlert> ForLoad(IEnumerable<LoadAlert> all, string caliber, string loadName)
    {
        string id = LoadIdentity.Normalize(loadName);
        return all.Where(a => string.Equals(a.Caliber, caliber, StringComparison.OrdinalIgnoreCase)
                              && (a.LoadName.Length == 0 || LoadIdentity.Normalize(a.LoadName) == id)).ToList();
    }
}
