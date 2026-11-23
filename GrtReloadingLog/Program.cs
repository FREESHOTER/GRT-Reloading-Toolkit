using System.Windows.Forms;
using GrtPluginKit.Ipc;
using GrtReloadingLog.Data;
using GrtReloadingLog.Ui;

namespace GrtReloadingLog;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--dbtest")
        {
            DbTest(args.Length >= 2 ? args[1] : Db.DefaultPath);
            return;
        }

        ApplicationConfiguration.Initialize();

        int port = 0;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--ipcport" && int.TryParse(args[i + 1], out int p)) port = p;

        GrtClient? grt = null;
        if (port > 0)
        {
            grt = new GrtClient(port);
            try { grt.Connect(); } catch { }
        }

        string dbPath = Environment.GetEnvironmentVariable("RELOADING_LOG_DB") ?? Db.DefaultPath;
        using var db = new Db(dbPath);

        using var form = new MainForm(grt, db);
        if (grt != null)
            grt.MenuOrToolbarActivated += () =>
            {
                if (form.IsHandleCreated) form.BeginInvoke(form.BringForward);
            };

        Application.Run(form);
        grt?.Dispose();
    }

    private static void DbTest(string path)
    {
        try { AttachConsole(-1); } catch { }
        if (File.Exists(path)) File.Delete(path);
        using var db = new Db(path);

        long pw = db.UpsertComponent(new Component { Kind = ComponentKind.Powder, Brand = "Vihtavuori", Name = "N550", Lot = "L1", Unit = "g", QtyInitial = 1000, QtyCurrent = 1000, CostTotal = 62, Currency = "EUR" });
        long pr = db.UpsertComponent(new Component { Kind = ComponentKind.Primer, Brand = "CCI", Name = "450", Unit = "pcs", QtyInitial = 1000, QtyCurrent = 1000, CostTotal = 90 });
        long bu = db.UpsertComponent(new Component { Kind = ComponentKind.Bullet, Brand = "Lapua", Name = "Scenar 139gr", Unit = "pcs", QtyInitial = 500, QtyCurrent = 500, CostTotal = 235, BulletWeightGr = 139 });
        long br = db.UpsertComponent(new Component { Kind = ComponentKind.Brass, Brand = "Lapua", Name = "6.5 Creedmoor", Unit = "pcs", QtyInitial = 100, QtyCurrent = 100, CostTotal = 95, ExpectedUses = 10 });

        var e = new JournalEntry { Date = "2026-09-08", LoadName = "N550 40.2 test", Caliber = "6.5 Creedmoor", PowderId = pw, PrimerId = pr, BulletId = bu, BrassId = br, ChargeGr = 40.2, Rounds = 20, VelocityAvgMs = 830.5, SdMs = 2.7 };
        long id = db.SaveEntry(e, applyStock: true);

        var comps = db.Components().ToDictionary(c => c.Id);
        var cb = Costing.PerRound(e, i => comps.GetValueOrDefault(i));
        Console.WriteLine($"entry {id}: cost/round = {Costing.Format(cb.PerRound, cb.Currency)}");
        Console.WriteLine($"  powder {cb.Powder:0.0000}  primer {cb.Primer:0.0000}  bullet {cb.Bullet:0.0000}  brass {cb.Brass:0.0000}");
        Console.WriteLine($"  total for 20 rounds = {Costing.Format(cb.PerRound * 20, cb.Currency)}");
        Console.WriteLine("stock after logging 20 rounds @ 40.2 gr:");
        foreach (var c in db.Components())
            Console.WriteLine($"  {c.Kind,-7} {c.Display,-28} {c.QtyCurrent:0.###} / {c.QtyInitial:0.###} {c.Unit}");

        db.DeleteEntry(id);
        Console.WriteLine("after deleting the entry (stock restored):");
        foreach (var c in db.Components())
            Console.WriteLine($"  {c.Kind,-7} {c.Display,-28} {c.QtyCurrent:0.###} / {c.QtyInitial:0.###} {c.Unit}");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
