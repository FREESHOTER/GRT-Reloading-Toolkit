using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

internal sealed class SeatingForm : LadderAnalyzerForm
{
    public SeatingForm(GrtClient? grt, ArchiveLink? link = null) : base(grt, link) => Init();

    public static SeatingForm Preview()
    {
        var f = new SeatingForm(null);
        f.LoadPreviewData();
        return f;
    }

    protected override LadderMode Mode => LadderMode.Seating;
    protected override string WindowTitle => AppVersion.Title(Lang.T("GRT Seating-Depth Analyzer"));
    protected override string XHeader => Lang.T("Seat/jump") + " " + Measuring.ProgramUnits.Current.LengthUnitName;
    protected override string NoteTitle => "Seating Depth Analysis";
    protected override string GalleryPictureName => "seating_chart";
    protected override string SiblingSuffix => "seating";
    protected override string? PresetEnvVar => "SEATING_FOLDER";
}
