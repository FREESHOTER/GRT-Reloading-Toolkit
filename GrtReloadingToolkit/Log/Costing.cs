namespace GrtReloadingToolkit.Log;

public sealed record CostBreakdown(double Powder, double Primer, double Brass, double Bullet, string Currency)
{
    public double PerRound => Powder + Primer + Brass + Bullet;
}

public static class Costing
{
    private const double GrainToGram = 0.06479891;

    /// <summary>Cost of one loaded round for this entry, using the referenced component lots.</summary>
    public static CostBreakdown PerRound(JournalEntry e, Func<long, Component?> lookup)
    {
        Component? pw = e.PowderId is { } a ? lookup(a) : null;
        Component? pr = e.PrimerId is { } b ? lookup(b) : null;
        Component? br = e.BrassId is { } c ? lookup(c) : null;
        Component? bu = e.BulletId is { } d ? lookup(d) : null;

        string cur = pw?.Currency ?? pr?.Currency ?? bu?.Currency ?? br?.Currency ?? "EUR";

        double powder = pw is not null ? e.ChargeGr * GrainToGram * pw.CostPerUnit : 0; // CostPerUnit = per gram
        double primer = pr?.CostPerUnit ?? 0;
        double bullet = bu?.CostPerUnit ?? 0;
        double brass = br?.CostPerUnit ?? 0; // already amortised over ExpectedUses

        return new CostBreakdown(powder, primer, brass, bullet, cur);
    }

    public static string Format(double v, string currency) => $"{v:0.000} {currency}";
}
