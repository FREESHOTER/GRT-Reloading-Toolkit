namespace GrtPluginKit.Analysis;

public readonly record struct StringStats(int N, double Mean, double Sd, double Es, double Min, double Max)
{
    /// <summary>
    /// Sample SD (÷(n−1)): the figure GRT shows in the "sd" row of a measurement tab, checked against a real GRT
    /// (a 4-shot string of 810.3 / 803.5 / 805.8 / 806.9 shows 2.8 there; the population SD, which some
    /// chronographs report and the Athlon import note carries, is 2.5). A single shot has no spread: 0.
    /// </summary>
    public static StringStats From(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return new StringStats(0, 0, 0, 0, 0, 0);
        double mean = v.Average();
        double var = v.Count < 2 ? 0 : v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1);
        double min = v.Min(), max = v.Max();
        return new StringStats(v.Count, mean, Math.Sqrt(var), max - min, min, max);
    }
}
