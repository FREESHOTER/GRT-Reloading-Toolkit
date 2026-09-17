using GrtReloadingToolkit.Ocw;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

internal sealed class OcwForm : LadderAnalyzerForm
{
    public OcwForm(GrtClient? grt) : base(grt) => Init();

    protected override LadderMode Mode => LadderMode.Charge;
    protected override string WindowTitle => AppVersion.Title(Lang.T("GRT Ladder / OCW Analyzer"));
    protected override string XHeader => Lang.T("Charge") + " " + GrtUnits.Current.ChargeUnitName;
    protected override string NoteTitle => "OCW Analysis";
    protected override string GalleryPictureName => "ocw_chart";
    protected override string SiblingSuffix => "ocw";
    protected override string? PresetEnvVar => "OCW_FOLDER";
}
