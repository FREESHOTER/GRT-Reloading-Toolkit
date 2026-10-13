using System.Globalization;
using System.Linq;
using ShotMarker.Core.Faces.Tables;

namespace ShotMarker.Core.Faces;

/// <summary>
/// The built-in target faces, built once from the tables in <c>Faces/Tables</c>.
///
/// Every face is authored there from the governing body's own published ring dimensions, with
/// the citation beside it; <see cref="FaceTable"/> holds the handful of house rules for what no
/// rulebook publishes. Face ids and display names are a public interface — a ShotMarker CSV
/// names its target in prose and <see cref="FindByName"/> is what resolves it — so they are
/// reproduced exactly as the device writes them, typos included, while the geometry is ours.
/// </summary>
public static class TargetFaceLibrary
{
    private static readonly Dictionary<string, TargetFace> Faces = Load();

    public static int Count => Faces.Count;

    public static TargetFace? Find(string faceId) =>
        Faces.TryGetValue(faceId, out var f) ? f : null;

    /// <summary>Matches a face by its display <c>Name</c> or <c>ShortName</c> — what a
    /// source that names faces in prose rather than by id (e.g. the ShotMarker CSV export,
    /// "NRA Long Range FC") has to go on. Exact match (either field, case-insensitive)
    /// first, then either string containing the other, so "NRA Long Range FC" still finds
    /// a face whose recorded name has extra punctuation or a parenthetical. Returns null
    /// when nothing matches; callers fall back to <see cref="Generic"/>.</summary>
    public static TargetFace? FindByName(string displayName)
    {
        string needle = displayName?.Trim() ?? "";
        if (needle.Length == 0) return null;

        TargetFace? exact = Faces.Values.FirstOrDefault(f =>
            string.Equals(f.Name, needle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.ShortName, needle, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        return Faces.Values.FirstOrDefault(f =>
            f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            needle.Contains(f.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A blank board of the given size carrying a centre cross and a labelled scale
    /// bar. What an unrecognised face_id renders as, so an unknown target never aborts an
    /// import.
    ///
    /// The furniture is the whole point of this face. With no rings to judge against, shots
    /// floating on blank white give the shooter no way to read group size or even which way is
    /// up; the cross fixes the point of aim and the bar fixes the scale.</summary>
    public static TargetFace Generic(double widthMm, double heightMm)
    {
        double w = widthMm > 0 ? widthMm : 1000;
        double h = heightMm > 0 ? heightMm : 1000;
        double span = Math.Min(w, h);

        // "bl", not "b". The renderer reads the trailing "l" as "line only": a "b" poly is
        // drawn as a FILL, and these are two-point paths with zero area, so they would draw
        // nothing at all; and Stroke("b") is white, so a "b" label would be white on a white
        // board. Both mistakes are silent — the face still loads and the PNG still renders.
        double arm = span * 0.04;
        var cross = new[]
        {
            new TargetPoly("bl", 1, new[] { new TargetPoint(-arm, 0), new TargetPoint(arm, 0) }),
            new TargetPoly("bl", 1, new[] { new TargetPoint(0, -arm), new TargetPoint(0, arm) }),
        };

        // The longest round length that still fits comfortably across the board, so the bar
        // reads as a useful ruler on a 300 yd face and on a 1000 yd one alike.
        double bar = new[] { 1000.0, 500, 200, 100, 50, 20, 10 }
            .FirstOrDefault(d => d <= w * 0.25, 10);

        double margin = span * 0.05;
        double x0 = -w / 2 + margin;
        double y0 = -h / 2 + margin;
        double tick = span * 0.012;
        var scale = new[]
        {
            new TargetPoly("bl", 1, new[] { new TargetPoint(x0, y0), new TargetPoint(x0 + bar, y0) }),
            new TargetPoly("bl", 1, new[] { new TargetPoint(x0, y0 - tick), new TargetPoint(x0, y0 + tick) }),
            new TargetPoly("bl", 1, new[] { new TargetPoint(x0 + bar, y0 - tick), new TargetPoint(x0 + bar, y0 + tick) }),
        };

        var label = new TargetText(
            x0 + bar / 2, y0 + tick + span * 0.025, span * 0.03, "bl",
            bar.ToString("0", CultureInfo.InvariantCulture) + " mm");

        return new TargetFace("GENERIC", "Unknown target", "Unknown", w, h, 2,
            Array.Empty<TargetRing>(), cross.Concat(scale).ToArray(), new[] { label });
    }

    private static Dictionary<string, TargetFace> Load()
    {
        var result = new Dictionary<string, TargetFace>(StringComparer.Ordinal);
        foreach (TargetFace f in IcfraTables.Faces()
                     .Concat(DcraTables.Faces())
                     .Concat(NraTables.Faces())
                     .Concat(UkTables.Faces())
                     .Concat(SabuTables.Faces())
                     .Concat(IbsTables.Faces())
                     .Concat(AuTables.Faces())
                     .Concat(SwissTables.Faces())
                     .Concat(NorwayTables.Faces())
                     .Concat(MiscTables.Faces()))
        {
            // Two tables claiming one id would silently shadow each other, and the loser would
            // simply stop being drawn — nothing else in the program would notice.
            if (!result.TryAdd(f.Id, f))
                throw new InvalidOperationException($"duplicate target face id '{f.Id}'");
        }
        return result;
    }
}
