using System.Diagnostics;
using GrtReloadingToolkit.Archive;
using Xunit;
namespace GrtReloadingToolkit.Tests;
public sealed class ZzImportTimingTests
{
    [Fact]
    public void Time()
    {
        string copy = @"C:\Users\User\AppData\Local\Temp\claude\47dfb3c408738649\ad0bbd8b-71e4-4cb2-9047-d833ab71748c\scratchpad\arc_copy.db";
        string work = copy + ".work";
        File.Copy(copy, work, true);
        var outp = new List<string>();
        using (var a = new ArchiveDb(work))
        {
            foreach (string f in File.ReadAllLines(copy + ".files"))
            {
                var log = new List<string>();
                var sw = Stopwatch.StartNew();
                string res;
                try { long id = ArchiveImports.LoadForGrtFile(a, f, 1, log); res = "id " + id; }
                catch (Exception ex) { res = "ERRORE " + ex.GetType().Name + ": " + ex.Message; }
                outp.Add($"{sw.ElapsedMilliseconds,7} ms  {new FileInfo(f).Length / 1024,6} KB  {res}  {Path.GetFileName(f)}  {string.Join(" / ", log).Replace("\n", " ")}");
            }
        }
        File.WriteAllLines(copy + ".risultati.txt", outp);
    }
}
