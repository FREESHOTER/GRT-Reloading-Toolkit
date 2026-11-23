namespace GrtReloadingToolkit.Cartridges;

/// <summary>A piece (a case, a bullet, a primer) with the measures taken of it.</summary>
public sealed record ClassPiece(long Id, string Mark, IReadOnlyDictionary<string, double> Measures);

/// <summary>Pieces that may be loaded together: inside the class none differs from another by more than the band on any criterion.</summary>
public sealed record MeasureClass(string Label, IReadOnlyList<ClassPiece> Pieces, IReadOnlyDictionary<string, (double Min, double Max)> Ranges);

public sealed record ClassResult(IReadOnlyList<MeasureClass> Classes, IReadOnlyList<ClassPiece> Unclassified);

/// <summary>
/// Sorting pieces into classes of like pieces. Each criterion is a measure and a band (how far apart the biggest and the smallest piece of a
/// class may be, e.g. the 0.73 gr that puts no more than 1 m/s into the velocity). The pieces are taken in order of the measure and a class
/// is closed when the next piece would go beyond the band: that gives the fewest classes the band allows. With several criteria each class of
/// the first is divided again by the second, and so on. Pieces without one of the measures are left out and listed apart.
/// </summary>
public static class MeasureClasses
{
    public static ClassResult Build(IReadOnlyList<ClassPiece> pieces, IReadOnlyList<(string Key, double Width)> criteria)
    {
        if (criteria.Count == 0) throw new ArgumentException("At least one criterion is needed.", nameof(criteria));
        if (criteria.Any(c => !(c.Width > 0))) throw new ArgumentException("A band must be wider than zero.", nameof(criteria));
        var have = pieces.Where(p => criteria.All(c => p.Measures.ContainsKey(c.Key))).ToList();
        var missing = pieces.Except(have).ToList();

        var groups = new List<(string Label, List<ClassPiece> Items)> { ("", have) };
        for (int level = 0; level < criteria.Count; level++)
        {
            var (key, width) = criteria[level];
            var next = new List<(string, List<ClassPiece>)>();
            foreach (var (label, items) in groups)
            {
                int n = 0;
                foreach (var cut in Cut(items, key, width))
                    next.Add((label + Name(level, n++), cut));
            }
            groups = next;
        }
        var classes = groups.Select(g => new MeasureClass(g.Label, g.Items,
            criteria.ToDictionary(c => c.Key, c => (g.Items.Min(p => p.Measures[c.Key]), g.Items.Max(p => p.Measures[c.Key]))))).ToList();
        return new ClassResult(classes, missing);
    }

    private static IEnumerable<List<ClassPiece>> Cut(List<ClassPiece> items, string key, double width)
    {
        var sorted = items.OrderBy(p => p.Measures[key]).ThenBy(p => p.Mark, StringComparer.Ordinal).ToList();
        List<ClassPiece>? current = null;
        double start = 0;
        foreach (var p in sorted)
        {
            double v = p.Measures[key];
            if (current is null || v - start > width + 1e-9) { if (current is not null) yield return current; current = new(); start = v; }
            current.Add(p);
        }
        if (current is not null) yield return current;
    }

    /// <summary>A, B, C … for the first criterion, 1, 2, 3 … for the second, a, b, c … for the third (past Z or z the number continues: AA, AB).</summary>
    private static string Name(int level, int index)
    {
        if (level == 1) return (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        char first = level == 0 ? 'A' : 'a';
        string s = "";
        int i = index;
        do { s = (char)(first + i % 26) + s; i = i / 26 - 1; } while (i >= 0);
        return s;
    }
}
