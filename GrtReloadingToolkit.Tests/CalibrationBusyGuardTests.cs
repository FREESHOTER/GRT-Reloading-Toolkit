using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The calibration window must run one operation on GRT at a time. Found when the verification button, which turns on before
/// the sweep that enables it has finished, was pressed in the middle of that sweep: the two interleaved, a temporary folder
/// vanished under one of them and the window raised an error naming a file that was not there. A source check, like the launcher's,
/// since the window cannot be built without a display.
/// </summary>
public sealed class CalibrationBusyGuardTests
{
    private static string Source()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md"))) dir = dir.Parent;
        Assert.True(dir is not null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, "GrtReloadingToolkit", "Ui", "CalibrationForm.cs"));
    }

    [Theory]
    [InlineData("CaptureAllAsync")]
    [InlineData("CaptureCurrentAsync")]
    [InlineData("CaptureAllPertKAsync")]
    [InlineData("VerifyJointAsync")]
    [InlineData("CheckSebertAsync")]
    [InlineData("WriteShapeAsync")]
    public void EveryOperationThatDrivesGrtGoesThroughTheGuard(string method)
        => Assert.Matches(new Regex(@"Click \+= async \(_, _\) => await Exclusive\(" + method + @"\);"), Source());

    [Fact]
    public void TheWritesGoThroughItToo()
        => Assert.Equal(2, Regex.Matches(Source(), @"await Exclusive\(\(\) => WriteAsync\((true|false)\)\)").Count);

    [Fact]
    public void NoClickHandlerCallsAnOperationDirectly()
        => Assert.DoesNotMatch(new Regex(@"Click \+= async \(_, _\) => await (CaptureAllAsync|CaptureCurrentAsync|CaptureAllPertKAsync|VerifyJointAsync|CheckSebertAsync|WriteShapeAsync)\("), Source());
}