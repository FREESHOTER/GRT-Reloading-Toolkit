using GrtAthlonImporter.Athlon;
using GrtAthlonImporter.Core;
using GrtAthlonImporter.Grt;
using GrtAthlonImporter.Ipc;

namespace GrtAthlonImporter;

/// <summary>
/// Exercises the full import path against a (mock or real) GRT on <paramref name="port"/>,
/// using the same GrtClient calls the UI makes. Prints a PASS/FAIL summary.
/// </summary>
internal static class SelfTest
{
    public static async Task Run(int port, string[] xlsxFiles)
    {
        DebugParse.AttachConsoleForTest();
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) failures++;
        }

        using var grt = new GrtClient(port);
        grt.Log += l => Console.WriteLine("  [ipc] " + l);

        try
        {
            grt.Connect();
            await Task.Delay(300); // let Event_Attached arrive
            Check(grt.Connected, "TCP connected to GRT");

            var top = await grt.GetTabOnTopAsync();
            Check(top.handle != 0, $"Get_TabOnTop -> handle={top.handle} caption='{top.caption}'");
            Check(!string.IsNullOrWhiteSpace(top.file), $"Get_TabOnTop -> file='{top.file}'");

            var rows = new List<ImportBuilder.Row>();
            foreach (string f in xlsxFiles)
            {
                AthlonString s = AthlonParser.Parse(f);
                Check(s.Shots.Count > 0, $"parsed {s.FileName}: {s.Shots.Count} shots, charge {s.ChargeGrains} gr");
                rows.Add(new ImportBuilder.Row(s, s.SessionTempC ?? 21, true));
            }

            List<ChargeInput> charges = ImportBuilder.Build(rows, null, perShotTemp: false);
            Check(charges.Count == xlsxFiles.Length, $"built {charges.Count} charge(s)");

            string baseFile = File.Exists(top.file) ? top.file : "";
            var opt = new WriteOptions
            {
                Title = $"{AthlonMarker.TitlePrefix}{(charges.Count > 1 ? "Ladder" : "Measurement")} {DateTime.Now:yyyy-MM-dd}",
                MinimalOutputPath = Path.Combine(Path.GetTempPath(), $"Athlon_selftest_{DateTime.Now:HHmmss}.grtload"),
            };
            GrtLoadWriteResult res = GrtLoadWriter.Write(baseFile.Length > 0 ? baseFile : null, charges, opt);
            Check(File.Exists(res.OutputPath), $"wrote {Path.GetFileName(res.OutputPath)} (merged={res.MergedIntoBase})");

            await grt.LoadFileAsync(res.OutputPath);
            Check(true, "Load_File acknowledged by GRT");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  EXCEPTION: " + ex);
            failures++;
        }

        Console.WriteLine(failures == 0 ? "\nSELFTEST PASSED" : $"\nSELFTEST FAILED ({failures})");
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
