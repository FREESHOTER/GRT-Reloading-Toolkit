namespace GrtReloadingToolkit.Cards;

/// <summary>Console harness:  --card &lt;base.grtload&gt; [outDir]</summary>
internal static class CardCli
{
    public static void Run(string basePath, string? outDir)
    {
        try { AttachConsole(-1); } catch { }
        outDir ??= Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".";

        var card = LoadCard.FromGrtload(basePath);
        Console.WriteLine($"caliber={card.Caliber}  powder={card.Powder}  charge={card.ChargeGr}  MV={card.MvMs}");
        Console.WriteLine("--- QR text ---");
        Console.WriteLine(card.QrText());

        using var qr = CardRenderer.QrBitmap(card.QrText());
        var cbmp = new Bitmap(1050, 1480);
        using (var g = Graphics.FromImage(cbmp)) { g.Clear(Color.White); CardRenderer.DrawRecipeCard(g, new RectangleF(20, 20, 1010, 1440), card, qr); }
        string p1 = Path.Combine(outDir, "card_recipe.png");
        cbmp.Save(p1, System.Drawing.Imaging.ImageFormat.Png);

        var lbmp = new Bitmap(680, 340);
        using (var g = Graphics.FromImage(lbmp)) { g.Clear(Color.White); CardRenderer.DrawBoxLabel(g, new RectangleF(20, 20, 640, 300), card, qr); }
        string p2 = Path.Combine(outDir, "card_label.png");
        lbmp.Save(p2, System.Drawing.Imaging.ImageFormat.Png);

        Console.WriteLine($"\nwrote {p1}\n      {p2}");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
