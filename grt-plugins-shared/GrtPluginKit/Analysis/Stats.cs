namespace GrtPluginKit.Analysis;

public readonly record struct StringStats(int N, double Mean, double Sd, double Es, double Min, double Max)
{
    /// <summary>Population SD (÷n) — matches the Athlon device and how GRT recomputes it.</summary>
    public static StringStats From(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return new StringStats(0, 0, 0, 0, 0, 0);
        double mean = v.Average();
        double var = v.Sum(x => (x - mean) * (x - mean)) / v.Count;
        double min = v.Min(), max = v.Max();
        return new StringStats(v.Count, mean, Math.Sqrt(var), max - min, min, max);
    }
}
