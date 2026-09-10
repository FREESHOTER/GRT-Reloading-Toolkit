using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

internal sealed class OcwForm : LadderAnalyzerForm
{
    public OcwForm(GrtClient? grt) : base(grt) => Init();

    protected override LadderMode Mode => LadderMode.Charge;
    protected override string WindowTitle => AppVersion.Title("GRT Ladder / OCW Analyzer");
    protected override string XHeader => "Charge gr";
    protected override string XUnit => "gr";
    protected override string NoteTitle => "OCW Analysis";
    protected override string GalleryPictureName => "ocw_chart";
    protected override string SiblingSuffix => "ocw";
    protected override string? PresetEnvVar => "OCW_FOLDER";
}
