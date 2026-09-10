namespace GrtReloadingToolkit.Log;

public enum ComponentKind { Powder, Primer, Brass, Bullet }

public sealed class Component
{
    public long Id { get; set; }
    public ComponentKind Kind { get; set; }
    public string Brand { get; set; } = "";
    public string Name { get; set; } = "";
    public string Lot { get; set; } = "";
    /// <summary>"g" for powder, "pcs" for the rest.</summary>
    public string Unit { get; set; } = "pcs";
    public double QtyInitial { get; set; }
    public double QtyCurrent { get; set; }
    public double CostTotal { get; set; }          // purchase price of this lot, user's currency
    public string Currency { get; set; } = "EUR";
    /// <summary>Expected firings before retirement — brass amortisation (1 for consumables).</summary>
    public int ExpectedUses { get; set; } = 1;
    public double? BulletWeightGr { get; set; }
    public string Notes { get; set; } = "";
    public string AcquiredAt { get; set; } = "";
    public bool Archived { get; set; }

    /// <summary>Cost of one unit (one gram, or one piece), amortised over ExpectedUses.</summary>
    public double CostPerUnit => QtyInitial > 0 && ExpectedUses > 0
        ? CostTotal / QtyInitial / ExpectedUses
        : 0;

    public double FractionRemaining => QtyInitial > 0 ? QtyCurrent / QtyInitial : 0;

    public string Display => string.Join(" ", new[] { Brand, Name, Lot is "" ? "" : $"[{Lot}]" }.Where(s => s.Length > 0));
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
