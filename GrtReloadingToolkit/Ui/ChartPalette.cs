namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The colours a chart draws with. <see cref="Dark"/> is the look of the program and of the pictures it puts in GRT's
/// gallery; <see cref="Print"/> is for paper (the PDF reports): white ground, dark text, and the blue and amber made
/// deeper so they still read on white and in a black-and-white print.
/// </summary>
internal sealed record ChartPalette(Color Back, Color Text, Color TextDim, Color Axis, Color AxisFaint, Color Grid,
    Color Primary, Color Accent, Color AccentTrend, Color Band, Color Aim)
{
    public static readonly ChartPalette Dark = new(
        Back: Color.FromArgb(18, 24, 36), Text: Color.FromArgb(210, 220, 230), TextDim: Color.FromArgb(150, 165, 180),
        Axis: Color.FromArgb(120, 148, 163, 184), AxisFaint: Color.FromArgb(90, 148, 163, 184), Grid: Color.FromArgb(30, 148, 163, 184),
        Primary: Color.FromArgb(56, 189, 248), Accent: Color.FromArgb(251, 191, 36), AccentTrend: Color.FromArgb(120, 251, 191, 36),
        Band: Color.FromArgb(40, 120, 200, 120), Aim: Color.FromArgb(160, 210, 220, 230));

    public static readonly ChartPalette Print = new(
        Back: Color.White, Text: Color.FromArgb(33, 37, 41), TextDim: Color.FromArgb(90, 98, 108),
        Axis: Color.FromArgb(170, 80, 90, 105), AxisFaint: Color.FromArgb(130, 80, 90, 105), Grid: Color.FromArgb(50, 80, 90, 105),
        Primary: Color.FromArgb(3, 105, 161), Accent: Color.FromArgb(217, 119, 6), AccentTrend: Color.FromArgb(190, 217, 119, 6),
        Band: Color.FromArgb(60, 34, 160, 80), Aim: Color.FromArgb(190, 40, 40, 40));

    public static ChartPalette For(bool print) => print ? Print : Dark;
}
