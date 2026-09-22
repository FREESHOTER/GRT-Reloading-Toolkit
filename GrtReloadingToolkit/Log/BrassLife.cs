namespace GrtReloadingToolkit.Log;

/// <summary>
/// Brass firing count and anneal reminder, built from data the Journal already collects — no new
/// per-shot event log needed. Ballistic Lab v2 punted on this exact feature ("Brass Life Tracker")
/// because its old software's inventory tool was a stateless textarea with nothing to aggregate; this
/// project already logs <see cref="JournalEntry.BrassId"/> + <see cref="JournalEntry.Rounds"/> per
/// session, so the total is just a sum, not a new log to design and maintain.
///
/// The one approximation this makes, stated plainly: a lot's rounds are logged per LOT, not per
/// individual case, so "average firings per case" (<see cref="AvgUsesPerCase"/>) assumes the pieces
/// in the lot are rotated through evenly. A shooter who always grabs the same handful of cases off
/// the top will see this under-count those specific pieces — the same honest limitation
/// <see cref="Component.ExpectedUses"/> (retirement) already lives with, not a new one.
/// </summary>
public static class BrassLife
{
    public sealed record Status(long ComponentId, int Pieces, int TotalRoundsFired,
        double AvgUsesPerCase, double UsesSinceAnneal, bool AnnealDue, bool RetirementDue);

    /// <param name="brass">Must be <see cref="ComponentKind.Brass"/> — the caller's job to filter,
    /// same convention as the rest of Log/ (no kind check here, so a future non-brass caller isn't
    /// silently coerced).</param>
    /// <param name="totalRoundsFired">Sum of <see cref="JournalEntry.Rounds"/> across every journal
    /// entry logged against this lot — the caller supplies it rather than a Db, so this stays a pure
    /// function testable without a database, like every other Log/ calculation.</param>
    public static Status Compute(Component brass, int totalRoundsFired)
    {
        double avgUses = brass.QtyInitial > 0 ? totalRoundsFired / brass.QtyInitial : 0;
        double sinceAnneal = avgUses - (brass.AnnealedAtUses ?? 0);
        bool annealDue = brass.AnnealEveryUses is { } every && every > 0 && sinceAnneal >= every;
        bool retirementDue = brass.ExpectedUses > 0 && avgUses >= brass.ExpectedUses;

        return new Status(brass.Id, (int)Math.Round(brass.QtyInitial), totalRoundsFired,
            Math.Round(avgUses, 2), Math.Round(sinceAnneal, 2), annealDue, retirementDue);
    }
}
