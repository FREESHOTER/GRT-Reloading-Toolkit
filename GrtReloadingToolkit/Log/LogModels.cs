namespace GrtReloadingToolkit.Log;

/// <summary>
/// What a lot is. Barrel is last because the value is stored by name, so the order here is free,
/// and because the four before it are the ones a journal entry consumes per round -- a barrel is
/// something you own and paid for, not something a round is loaded from.
/// </summary>
public enum ComponentKind { Powder, Primer, Brass, Bullet, Barrel }

public sealed class Component
{
    public long Id { get; set; }
    public ComponentKind Kind { get; set; }
    public string Brand { get; set; } = "";
    public string Name { get; set; } = "";
    public string Lot { get; set; } = "";
    /// <summary>
    /// The unit this lot is counted in -- grams, grains, pounds or kilos for powder, pieces for
    /// everything else. Powder stock is stored in grams whatever this says; see
    /// <see cref="StockUnit"/> for why, and for the arithmetic between the two.
    /// </summary>
    public string Unit { get; set; } = StockUnit.Pieces;
    public double QtyInitial { get; set; }
    public double QtyCurrent { get; set; }
    public double CostTotal { get; set; }          // purchase price of this lot, user's currency
    public string Currency { get; set; } = Money.Default;
    /// <summary>Expected firings before retirement — brass amortisation (1 for consumables).</summary>
    public int ExpectedUses { get; set; } = 1;
    public double? BulletWeightGr { get; set; }

    /// <summary>
    /// Barrel twist, stored as the length of one full turn in mm — the same metric-inside rule the
    /// rest of the toolkit follows, and what GRT's <c>twistlen</c> holds. 1:8 in is 203.2 mm.
    /// </summary>
    public double? TwistMm { get; set; }

    /// <summary>Barrel length in mm. Null on every other kind, and on a barrel not measured yet.</summary>
    public double? BarrelLengthMm { get; set; }
    public string Notes { get; set; } = "";
    public string AcquiredAt { get; set; } = "";
    public bool Archived { get; set; }

    /// <summary>Cost of one unit (one gram, or one piece), amortised over ExpectedUses.</summary>
    public double CostPerUnit => QtyInitial > 0 && ExpectedUses > 0
        ? CostTotal / QtyInitial / ExpectedUses
        : 0;

    public double FractionRemaining => QtyInitial > 0 ? QtyCurrent / QtyInitial : 0;

    /// <summary>Stock left, counted in <see cref="Unit"/> -- what a grid cell or a prompt shows.</summary>
    public double QtyCurrentIn => StockUnit.FromStore(QtyCurrent, Unit);

    /// <summary>The opening count, in <see cref="Unit"/>.</summary>
    public double QtyInitialIn => StockUnit.FromStore(QtyInitial, Unit);

    /// <summary>Stock left with its unit, e.g. "1.4 lb" or "250 pcs".</summary>
    public string QtyLeftText => QtyCurrentIn.ToString(StockUnit.Format(Unit)) + " " + Unit;

    /// <summary>
    /// <see cref="CostPerUnit"/> priced in <see cref="Unit"/>. Cost per gram is the number the
    /// round costing needs; cost per pound is the number that means something on a shelf.
    /// </summary>
    public double CostPerUnitIn => CostPerUnit * StockUnit.Factor(Unit);

    public string Display => string.Join(" ", new[] { Brand, Name, Lot is "" ? "" : $"[{Lot}]" }.Where(s => s.Length > 0));

    /// <summary>
    /// Twist and length in GRT's units, as they identify a barrel — the two numbers you would ask
    /// for if someone said "which tube is that?". Empty for every other kind and for a barrel with
    /// neither recorded, so a caller can append it unconditionally.
    /// </summary>
    public string BarrelSpec
    {
        get
        {
            if (Kind != ComponentKind.Barrel) return "";
            var u = GrtPluginKit.Grt.GrtUnits.Current;
            var parts = new List<string>(2);
            if (TwistMm is > 0 and { } t) parts.Add(u.Twist(t));
            if (BarrelLengthMm is > 0 and { } l) parts.Add(u.Length(l));
            return parts.Count == 0 ? "" : "  " + string.Join("  ", parts);
        }
    }
}

public sealed class JournalEntry
{
    public long Id { get; set; }
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public string LoadName { get; set; } = "";
    public string Caliber { get; set; } = "";
    public string Firearm { get; set; } = "";
    public long? FirearmId { get; set; }
    public long? PowderId { get; set; }
    public long? PrimerId { get; set; }
    public long? BrassId { get; set; }
    public long? BulletId { get; set; }
    public double ChargeGr { get; set; }
    public double? CoalMm { get; set; }
    public double? CbtoMm { get; set; }
    public int Rounds { get; set; }
    public double? VelocityAvgMs { get; set; }
    public double? SdMs { get; set; }
    public double? EsMs { get; set; }
    public double? GroupMoa { get; set; }
    public double? DistanceM { get; set; }
    public string Notes { get; set; } = "";
    public string GrtloadPath { get; set; } = "";
    public string CreatedAt { get; set; } = "";

    /// <summary>Stock already deducted for this entry (so edits/deletes can reconcile).</summary>
    public bool StockApplied { get; set; }
}

public sealed class Firearm
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Caliber { get; set; } = "";
    public string Notes { get; set; } = "";
    public int RoundsBefore { get; set; }
    public bool Retired { get; set; }
}
