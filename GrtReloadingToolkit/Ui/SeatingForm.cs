using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

internal sealed class SeatingForm : LadderAnalyzerForm
{
    public SeatingForm(GrtClient? grt) : base(grt) => Init();

    protected override LadderMode Mode => LadderMode.Seating;
    protected override string WindowTitle => AppVersion.Title("GRT Seating-Depth Analyzer");
    protected override string XHeader => "Seat/jump";
    protected override string XUnit => "mm";
    protected override string NoteTitle => "Seating Depth Analysis";
    protected override string GalleryPictureName => "seating_chart";
    protected override string SiblingSuffix => "seating";
    protected override string? PresetEnvVar => "SEATING_FOLDER";
}
