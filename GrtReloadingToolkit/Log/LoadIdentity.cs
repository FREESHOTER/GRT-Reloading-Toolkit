using System.Text.RegularExpressions;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// What tells two Journal loads apart when their powder or bullet is not an Inventory component.
/// The rankings group by (caliber, powder id, bullet id, charge); an entry whose powder or bullet was
/// never linked to a component has a NULL id, and every NULL looked like every other one, so two
/// completely different loads at the same charge became one averaged row. The load's own name
/// (toolkit write-back suffixes stripped) is the next best thing that identifies it.
/// </summary>
public static class LoadIdentity
{
    private static readonly Regex Suffix = new(
        @"_toolkit(_\d{8}_\d{4,6})?$|_[a-z]+_\d{8}_\d{4}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Empty when both components are linked (the ids already say it all).</summary>
    public static string Of(long? powderId, long? bulletId, string? loadName) =>
        powderId is null || bulletId is null ? Normalize(loadName) : "";

    public static string Of(JournalEntry e) => Of(e.PowderId, e.BulletId, e.LoadName);

    public static string Normalize(string? loadName) =>
        Suffix.Replace((loadName ?? "").Trim(), "").Trim().ToLowerInvariant();

    /// <summary>The load's name for display next to a "none" component, suffix stripped.</summary>
    public static string Display(string? loadName) => Suffix.Replace((loadName ?? "").Trim(), "").Trim();
}
