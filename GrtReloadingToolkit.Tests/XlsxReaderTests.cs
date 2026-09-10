using System.IO.Compression;
using System.Text;
using GrtPluginKit.Import;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// An .xlsx is a ZIP. Anything else that reaches the reader used to surface as an opaque
/// "End of Central Directory record could not be found", most often for a legacy .xls.
/// </summary>
public sealed class XlsxReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-xlsx-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string Write(string name, byte[] bytes)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void LegacyXlsIsNamedInTheError()
    {
        // OLE2 compound-file signature — every Excel 97-2003 workbook starts with it
        byte[] ole2 = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00, 0x00, 0x00 };
        string path = Write("session.xls", ole2);

        var ex = Assert.Throws<InvalidDataException>(() => XlsxReader.ReadFirstSheet(path));
        Assert.Contains("session.xls", ex.Message);
        Assert.Contains("97-2003", ex.Message);
        Assert.Contains(".csv", ex.Message);          // tells the user what to do about it
    }

    [Fact]
    public void SomethingThatIsNotAWorkbookAtAllIsRejected()
    {
        string path = Write("notes.txt", Encoding.UTF8.GetBytes("shot 1, 850 m/s\n"));

        var ex = Assert.Throws<InvalidDataException>(() => XlsxReader.ReadFirstSheet(path));
        Assert.Contains("notes.txt", ex.Message);
        Assert.Contains(".xlsx", ex.Message);
    }

    [Fact]
    public void EmptyFileIsRejected()
    {
        string path = Write("empty.xlsx", Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => XlsxReader.ReadFirstSheet(path));
    }

    [Fact]
    public void RealWorkbookStillReads()
    {
        string path = Path.Combine(_dir, "chrono.xlsx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()))
            w.Write("""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                  <row r="1"><c r="A1" t="inlineStr"><is><t>#</t></is></c><c r="B1" t="inlineStr"><is><t>Speed (m/s)</t></is></c></row>
                  <row r="2"><c r="A2"><v>1</v></c><c r="B2"><v>850.5</v></c></row>
                </sheetData></worksheet>
                """);

        List<string[]> rows = XlsxReader.ReadFirstSheet(path);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "#", "Speed (m/s)" }, rows[0]);
        Assert.Equal(new[] { "1", "850.5" }, rows[1]);
    }
}
