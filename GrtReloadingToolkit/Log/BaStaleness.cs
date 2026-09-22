namespace GrtReloadingToolkit.Log;

/// <summary>
/// Flags when a load's own Ba differs enough from the most recently CALIBRATED Ba the Journal has on
/// file for the same caliber+powder to be worth a second look before trusting the load's number as-is.
///
/// This is deliberately NOT a comparison against GRT's own factory value for that powder: the plugin
/// IPC has no call to read it (see reference_grt_plugin_ipc — the propellant database is a single
/// opaque binary GRT ships), so the only "known good" number this plugin can compare against is the
/// user's OWN calibration history, logged via Barrel Calibration into the Journal. A load whose Ba
/// nobody has ever calibrated has nothing to compare against and is never flagged.
/// </summary>
public static class BaStaleness
{
    public sealed record Result(double FileBa, double LastCalibratedBa, double DiffPct, bool Stale);

    /// <summary>Null if either value is missing/non-positive, or there is nothing to compare
    /// against (the powder has never been calibrated). <paramref name="thresholdPct"/> is a
    /// heuristic default, not a value the user has confirmed — see the class doc.</summary>
    public static Result? Check(double? fileBa, double? lastCalibratedBa, double thresholdPct = 5.0)
    {
        if (fileBa is not { } f || f <= 0 || lastCalibratedBa is not { } l || l <= 0) return null;
        // Rounded before comparing, not just before display -- a raw floating-point difference that
        // lands a hair above an exact-percent threshold (0.525 vs 0.50 -> 5.000000000000001%) must
        // not flag as stale when the number shown to the user reads exactly "5.0%".
        double diffPct = Math.Round(Math.Abs(f - l) / l * 100.0, 1);
        return new Result(f, l, diffPct, diffPct > thresholdPct);
    }
}
