using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

internal sealed class OcwForm : LadderAnalyzerForm
{
    public OcwForm(GrtClient? grt) : base(grt) => Init();

    protected override LadderMode Mode => LadderMode.Charge;
    protected override string WindowTitle => "GRT Ladder / OCW Analyzer  (v0.1)";
    protected override string XHeader => "Charge gr";
    protected override string XUnit => "gr";
    protected override string NoteTitle => "OCW Analysis";
    protected override string GalleryPictureName => "ocw_chart";
    protected override string SiblingSuffix => "ocw";
    protected override string? PresetEnvVar => "OCW_FOLDER";
}
