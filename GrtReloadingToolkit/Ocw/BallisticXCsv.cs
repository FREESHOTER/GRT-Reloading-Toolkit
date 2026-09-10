using System.Globalization;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Ocw;

public sealed record Impact(double XMoa, double YMoa);

/// <summary>One target group parsed from a Ballistic-X "Carica &lt;n&gt;.csv" export.</summary>
public sealed class TargetGroup
{
    public required string SourceFile { get; init; }
    public double? ChargeGrains { get; set; }
    public double DistanceM { get; set; }
    public double AimXMoa { get; set; }
    public double AimYMoa { get; set; }

    /// <summary>Group centre reported by the app (Center X/Y). May differ slightly from the impact mean.</summary>
    public double AppCenterXMoa { get; set; }
    public double AppCenterYMoa { get; set; }

    public List<Impact> Impacts { get; } = new();

    public double CenterXMoa => Impacts.Count > 0 ? Impacts.Average(i => i.XMoa) : AppCenterXMoa;
    public double CenterYMoa => Impacts.Count > 0 ? Impacts.Average(i => i.YMoa) : AppCenterYMoa;

    /// <summary>Extreme spread (max centre-to-centre distance between impacts), MOA.</summary>
    public double GroupEsMoa
    {
        get
        {
            double max = 0;
            for (int a = 0; a < Impacts.Count; a++)
                for (int b = a + 1; b < Impacts.Count; b++)
                {
                    double d = Dist(Impacts[a], Impacts[b]);
                    if (d > max) max = d;
                }
            return max;
        }
    }

    /// <summary>Mean radius: average impact distance from the group centre, MOA.</summary>
    public double MeanRadiusMoa
    {
        get
        {
            if (Impacts.Count == 0) return 0;
            double cx = CenterXMoa, cy = CenterYMoa;
            return Impacts.Average(i => Math.Sqrt((i.XMoa - cx) * (i.XMoa - cx) + (i.YMoa - cy) * (i.YMoa - cy)));
        }
    }

    /// <summary>Vertical spread of impacts (max Y − min Y), MOA — the OCW-relevant number.</summary>
    public double VerticalSpreadMoa
        => Impacts.Count > 0 ? Impacts.Max(i => i.YMoa) - Impacts.Min(i => i.YMoa) : 0;

    private static double Dist(Impact a, Impact b)
        => Math.Sqrt((a.XMoa - b.XMoa) * (a.XMoa - b.XMoa) + (a.YMoa - b.YMoa) * (a.YMoa - b.YMoa));

    /// <summary>1 true MOA at 100 m ≈ 29.089 mm.</summary>
    public static double MoaToMm(double moa, double distanceM) => moa * 29.0888 * (distanceM / 100.0);
}

public static class BallisticXCsv
{
    // header: Project Title,Group,Ammunition,Distance,Aim X,Aim Y,Center X,Center Y,Point X,Point Y,Velocity
    public static TargetGroup Parse(string path)
    {
        string[] lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length < 2)
            throw new InvalidDataException($"{Path.GetFileName(path)}: empty target CSV.");

        string[] header = SplitCsv(lines[0]);
        int Col(params string[] names)
        {
            for (int c = 0; c < header.Length; c++)
                if (names.Any(n => string.Equals(Str.Normalize(header[c]), Str.Normalize(n), StringComparison.Ordinal)))
                    return c;
            return -1;
        }

        int cDist = Col("Distance");
        int cAimX = Col("Aim X"), cAimY = Col("Aim Y");
        int cCenX = Col("Center X", "Centre X"), cCenY = Col("Center Y", "Centre Y");
        int cPtX = Col("Point X", "Impact X", "Hit X"), cPtY = Col("Point Y", "Impact Y", "Hit Y");

        if (cPtX < 0 || cPtY < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: no Point X / Point Y columns (got: {string.Join(", ", header)}).");

        var g = new TargetGroup { SourceFile = path };
        g.ChargeGrains = GuessChargeGrains(path);

        for (int r = 1; r < lines.Length; r++)
        {
            string[] f = SplitCsv(lines[r]);
            double? px = Num(f, cPtX), py = Num(f, cPtY);
            if (px is null || py is null) continue;

            g.Impacts.Add(new Impact(px.Value, py.Value));
            if (Num(f, cDist) is { } d && d > 0) g.DistanceM = d;
            if (Num(f, cAimX) is { } ax) g.AimXMoa = ax;
            if (Num(f, cAimY) is { } ay) g.AimYMoa = ay;
            if (Num(f, cCenX) is { } cx) g.AppCenterXMoa = cx;
            if (Num(f, cCenY) is { } cy) g.AppCenterYMoa = cy;
        }

        if (g.Impacts.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: no impact rows.");
        if (g.DistanceM <= 0) g.DistanceM = 100;
        return g;
    }

    private static double? GuessChargeGrains(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileNameWithoutExtension(path), @"(\d{1,3}[.,]\d{1,2})");
        return m.Success ? Str.ParseNumber(m.Groups[1].Value) : null;
    }

    private static double? Num(string[] f, int col)
        => col >= 0 && col < f.Length ? Str.ParseNumber(f[col]) : null;

    private static string[] SplitCsv(string line)
    {
        var outp = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = !q; }
            else if (ch == ',' && !q) { outp.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(ch);
        }
        outp.Add(cur.ToString().Trim());
        return outp.ToArray();
    }
}
