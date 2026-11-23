namespace GrtReloadingToolkit.Ui;

/// <summary>
/// A message whose details may run to hundreds of lines: a short summary on top, the details in a scrolling box, and the
/// window never taller than the screen. A MessageBox grows with its text and with a long list its OK button ends up below
/// the screen, leaving the window that opened it disabled ("it is stuck").
/// </summary>
internal sealed class LongTextDialog : Form
{
    internal LongTextDialog(string title, string summary, string details)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(LogicalToDeviceUnits(640), work.Width * 9 / 10), Math.Min(LogicalToDeviceUnits(460), work.Height * 8 / 10));
        MinimumSize = new Size(LogicalToDeviceUnits(360), LogicalToDeviceUnits(240));

        var top = new Label { Text = summary, Dock = DockStyle.Top, AutoSize = true, UseMnemonic = false, Padding = new Padding(10, 10, 10, 8),
            MaximumSize = new Size(Width - LogicalToDeviceUnits(30), 0) };
        var box = new TextBox { Text = details, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = LogicalToDeviceUnits(42), Padding = new Padding(8) };
        bottom.Controls.Add(ok);
        Controls.Add(box);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = ok;
        Shown += (_, _) => { box.SelectionLength = 0; ok.Focus(); };
    }

    public static void Show(IWin32Window? owner, string title, string summary, IEnumerable<string> details)
    {
        using var d = AppIcon.Apply(new LongTextDialog(title, summary, string.Join(Environment.NewLine, details)));
        GrtTheme.Apply(d);
        d.ShowDialog(owner);
    }
}
