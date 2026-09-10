using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit;

internal static class DbSelfTest
{
    public static void Run(string[] args)
    {
        try { AttachConsole(-1); } catch { }
        string path = args.Length >= 2 ? args[1] : Db.DefaultPath;
        if (File.Exists(path)) File.Delete(path);
        using var db = new Db(path);

        long pw = db.UpsertComponent(new Component { Kind = ComponentKind.Powder, Brand = "Vihtavuori", Name = "N550", Lot = "L1", Unit = "g", QtyInitial = 1000, QtyCurrent = 1000, CostTotal = 62 });
        long pr = db.UpsertComponent(new Component { Kind = ComponentKind.Primer, Brand = "CCI", Name = "450", Unit = "pcs", QtyInitial = 1000, QtyCurrent = 1000, CostTotal = 90 });
        long bu = db.UpsertComponent(new Component { Kind = ComponentKind.Bullet, Brand = "Lapua", Name = "Scenar 139gr", Unit = "pcs", QtyInitial = 500, QtyCurrent = 500, CostTotal = 235, BulletWeightGr = 139 });
        long br = db.UpsertComponent(new Component { Kind = ComponentKind.Brass, Brand = "Lapua", Name = "6.5 Creedmoor", Unit = "pcs", QtyInitial = 100, QtyCurrent = 100, CostTotal = 95, ExpectedUses = 10 });

        var e = new JournalEntry { Date = "2026-09-08", LoadName = "N550 40.2 test", Caliber = "6.5 Creedmoor", PowderId = pw, PrimerId = pr, BulletId = bu, BrassId = br, ChargeGr = 40.2, Rounds = 20 };
        long id = db.SaveEntry(e, applyStock: true);

        var comps = db.Components().ToDictionary(c => c.Id);
        var cb = Costing.PerRound(e, i => comps.GetValueOrDefault(i));
        Console.WriteLine($"cost/round = {Costing.Format(cb.PerRound, cb.Currency)}  (pw {cb.Powder:0.0000} pr {cb.Primer:0.0000} bu {cb.Bullet:0.0000} br {cb.Brass:0.0000})");
        foreach (var c in db.Components()) Console.WriteLine($"  {c.Kind,-7} {c.Display,-26} {c.QtyCurrent:0.###}/{c.QtyInitial:0.###} {c.Unit}");
        db.DeleteEntry(id);
        Console.WriteLine("after delete (restored):");
        foreach (var c in db.Components()) Console.WriteLine($"  {c.Kind,-7} {c.Display,-26} {c.QtyCurrent:0.###}/{c.QtyInitial:0.###} {c.Unit}");

        // firearms / barrel-life
        Console.WriteLine("\n--- firearms ---");
        long fa = db.ResolveFirearm("Tikka T3x 6.5 CM", "6.5 Creedmoor");
        db.UpsertFirearm(new Firearm { Id = fa, Name = "Tikka T3x 6.5 CM", Caliber = "6.5 Creedmoor", RoundsBefore = 250 });
        int i2 = 0;
        foreach (var (d, mv) in new[] { ("2026-01-10", 830.0), ("2026-03-15", 828.5), ("2026-06-20", 826.0), ("2026-09-01", 823.5) })
        {
            var je = new JournalEntry { Date = d, LoadName = "N550 40.2", Caliber = "6.5 Creedmoor", Firearm = "Tikka T3x 6.5 CM",
                FirearmId = fa, ChargeGr = 40.2, Rounds = 60, VelocityAvgMs = mv, PowderId = pw, PrimerId = pr, BulletId = bu, BrassId = br };
            db.SaveEntry(je, applyStock: false);
            i2++;
        }
        Console.WriteLine($"total rounds: {db.FirearmRoundCount(fa)}  (250 before + {i2}×60)");
        foreach (var h in db.FirearmMvHistory(fa))
            Console.WriteLine($"  {h.date}  {h.cumRounds,5} rd   {h.mv:0.0} m/s");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
