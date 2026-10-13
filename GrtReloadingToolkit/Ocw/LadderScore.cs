using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ocw;

/// <summary>
/// The leaderboard's score for one step of a charge ladder, from what the ladder analysis already holds: the velocity SD
/// and ES, the group's extreme spread and the number of rounds. The same <see cref="LoadScoring"/> tiers the Journal
/// leaderboard uses -- one definition of "a good load" -- without the Journal, which only knows what was logged by hand.
/// </summary>
public static class LadderScore
{
    public static LoadScoring.Breakdown Of(LadderStep s) => LoadScoring.CompositeQualityScore(
        s.HasVel ? s.SdMps : null,
        s.HasVel ? s.EsMps : null,
        s.HasTarget ? s.GroupEsMoa : null,
        Math.Max(s.VelN, s.Impacts));
}
