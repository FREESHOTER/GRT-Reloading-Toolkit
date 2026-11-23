using GrtAthlonImporter.Athlon;
using GrtAthlonImporter.Core;
using GrtAthlonImporter.Grt;

namespace GrtAthlonImporter;

/// <summary>Console dump of the Athlon parser + writer, for quick verification.</summary>
internal static class DebugParse
{
    public static void Run(string path, string? basePath = null)
    {
        AttachConsole();
        try
        {
            AthlonString s = AthlonParser.Parse(path);
            var v = s.Velocities.ToList();
            var st = StringStats.From(v);

            Console.WriteLine($"file          : {s.FileName}");
            Console.WriteLine($"unit          : {s.SourceUnit}");
            Console.WriteLine($"shots         : {s.Shots.Count}");
            Console.WriteLine($"velocities m/s: {string.Join(", ", v.Select(x => Str.Num(x, 1)))}");
            Console.WriteLine($"computed      : AVG {Str.Num(st.Mean, 1)}  SD {Str.Num(st.Sd, 2)}  ES {Str.Num(st.Es, 1)}  min {Str.Num(st.Min, 1)}  max {Str.Num(st.Max, 1)}");
            Console.WriteLine($"device        : AVG {s.DeviceAvg}  SD {s.DeviceSd}  ES {s.DeviceEs}  PF {s.DevicePowerFactor}  KE {s.DeviceEnergy}");
            Console.WriteLine($"session note  : {s.SessionNote}");
            Console.WriteLine($"charge (gr)   : {s.ChargeGrains}");
            Console.WriteLine($"session temp  : {(s.SessionTempC is { } t ? Str.Num(t, 1) + " C" : "(none)")}");
            for (int i = 0; i < s.Shots.Count; i++)
                Console.WriteLine($"  shot {i + 1,2}: {Str.Num(s.Shots[i].VelocityMps, 1),7}  time={s.Shots[i].Time}  note={s.Shots[i].Note}");
            foreach (string w in s.Warnings) Console.WriteLine("  WARN: " + w);

            var charges = ImportBuilder.Build(
                new[] { new ImportBuilder.Row(s, s.SessionTempC ?? 21, true) }, null, false);
            var res = GrtLoadWriter.Write(basePath, charges, new WriteOptions
            {
                Title = $"{AthlonMarker.TitlePrefix}Measurement {DateTime.Now:yyyy-MM-dd}",
                MinimalOutputPath = Path.Combine(Path.GetTempPath(), "Athlon_debug.grtload"),
            });
            Console.WriteLine();
            Console.WriteLine($"---- {(res.MergedIntoBase ? "merged" : "minimal")} .grtload -> {res.OutputPath} ----");
            Console.WriteLine(File.ReadAllText(res.OutputPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private static void AttachConsole() { try { AttachConsole(-1); } catch { } }
    internal static void AttachConsoleForTest() => AttachConsole();
}
