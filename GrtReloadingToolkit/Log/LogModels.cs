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
    /// <summary>Brass only: anneal every this many AVERAGE firings per case in the lot (null = no
    /// reminder set). "Average" because rounds are logged per lot, not per individual case — a lot
    /// rotated evenly through is the assumption, same one <see cref="BrassLife"/> makes explicit.</summary>
    public int? AnnealEveryUses { get; set; }
    /// <summary>The average-firings-per-case value the lot was at when last marked annealed (see
    /// <see cref="BrassLife"/>). Null means never annealed — everything fired counts toward the
    /// first reminder.</summary>
    public double? AnnealedAtUses { get; set; }
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
    public string QtyLeftText => QtyCurrentIn.ToString(StockUnit.Format(Unit), System.Globalization.CultureInfo.InvariantCulture) + " " + Unit;

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

    // Environmental conditions at test time -- GRT tracks none of these itself (checked every
    // field in a real .grtload: no temperature/pressure/humidity tag anywhere), so they're always
    // manual entry, never auto-filled from a load.
    public double? TemperatureC { get; set; }
    public double? PressureHpa { get; set; }
    public double? HumidityPct { get; set; }

    /// <summary>The propellant coefficients THIS session was calibrated to (e.g. via Barrel
    /// Calibration's Ba or Ba+a0 fit) -- lets a later search find "what worked before" for this
    /// exact powder+bullet combo, filtered/sorted by group size, velocity or temperature.</summary>
    public double? Ba { get; set; }
    public double? A0 { get; set; }
}

/// <summary>
/// A manually-measured per-shot barrel temperature (a probe clipped to the barrel, read at the
/// moment of that shot) — GRT's own plugin API carries no per-shot data beyond velocity (see
/// <c>GrtPluginKit.Grt.GrtShot</c>), so this is the toolkit's own record, keyed by
/// (<see cref="Caliber"/>, <see cref="Label"/>, <see cref="ShotIndex"/>) rather than by .grtload
/// file path: the write-back pattern renames the file on every save, which would silently orphan
/// every entered temperature on the very next write. It is orphaned instead only if the user
/// renames the Measurement/charge in GRT — a smaller, rarer edit than every save.
/// </summary>
public sealed class ShotMeasurement
{
    public long Id { get; set; }
    public string Caliber { get; set; } = "";
    public string Label { get; set; } = "";
    public int ShotIndex { get; set; }
    public double? TemperatureC { get; set; }
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

/// <summary>
/// One fired-case head-diameter reading at the 0.200"-from-head line, taken with a case gauge or
/// micrometer -- the classic manual "pressure sign" measurement: read enough charges of the same
/// powder through the same firearm and a sudden acceleration in expansion, well before GRT's own
/// simulated pressure numbers say anything, is a real warning a chronograph and a target alone won't
/// show. See <see cref="PressureSign"/> for the trend/acceleration logic built on top of this log.
/// Keyed by <see cref="FirearmId"/> + <see cref="PowderId"/> (not by JournalEntry): a reading is
/// normally taken case-in-hand at the bench, often before a session is even logged, and mixing two
/// different powders' expansion into one trend would be meaningless -- the same reason
/// <see cref="PressureSign"/> only ever compares readings within one firearm+powder group.
/// </summary>
public sealed class CaseHeadMeasurement
{
    public long Id { get; set; }
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public long FirearmId { get; set; }
    public long? PowderId { get; set; }
    public double ChargeGr { get; set; }
    public double HeadDiameterMm { get; set; }
    public string Notes { get; set; } = "";
}
