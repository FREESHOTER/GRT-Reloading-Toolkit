using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ocw;

/// <summary>
/// The leaderboard's score for one step of a charge ladder, from what the ladder analysis already holds: the velocity SD
/// and ES, the group's extreme spread and the number of rounds. The same <see cref="LoadScoring"/> tiers the Journal
/// leaderboard uses -- one definition of "a good load" -- without the Journal, which only knows what was logged by hand.
///
/// <para>A ladder step is one session, and the leaderboard scores a single-session load with the neutral consistency of 7.0
/// (there is nothing yet to be inconsistent with). The step is scored that way too: without that term the same SD, ES, group and
/// rounds came out one number here and another in the leaderboard (found when the two were compared, the ladder's average
/// being over four sub-scores and the leaderboard's over five), and the "rank and score" a chosen charge carries into its
/// note is meant to be the figure the leaderboard would give.</para>
/// </summary>
public static class LadderScore
{
    public static LoadScoring.Breakdown Of(LadderStep s) => LoadScoring.LeaderboardScore(
        s.HasVel ? s.SdMps : null,
        s.HasVel ? s.EsMps : null,
        s.HasTarget ? s.GroupEsMoa : null,
        Math.Max(s.VelN, s.Impacts),
        Array.Empty<double>());   // one session: the neutral consistency, as the leaderboard gives a single-session load
}
