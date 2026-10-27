using GrtReloadingToolkit.Log;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ui;

/// <summary>Defines a load to follow: a name, the caliber, the bullet and the powder as the shooter calls them, and the charge. The
/// outings read from GRT with the same caliber, a charge within 0.05 gr and a matching bullet and powder belong to it.</summary>
internal sealed class FollowedLoadDialog : Form
{
    private static readonly GrtUnits U = GrtUnits.Current;
    private readonly TrackedLoad _l;

    private readonly TextBox _name = new() { Width = 300 };
    private readonly TextBox _cal = new() { Width = 300 };
    private readonly TextBox _bullet = new() { Width = 300 };
    private readonly TextBox _powder = new() { Width = 300 };
    private readonly NumericUpDown _charge = new()
    {
        DecimalPlaces = U.ChargeInGrams ? 4 : 2,
        Increment = U.ChargeInGrams ? 0.01m : 0.1m,
        Maximum = (decimal)U.ChargeValue(500),
        Width = 90,
    };
    private readonly TextBox _notes = new() { Width = 300, Multiline = true, Height = 56, ScrollBars = ScrollBars.Vertical };

    public TrackedLoad Result => _l;

    public FollowedLoadDialog(TrackedLoad l)
    {
        _l = l;
        Text = l.Id == 0 ? "Segui un carico" : "Modifica il carico";
        AppIcon.Apply(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(470, 340);
        AutoScroll = true;

        _name.Text = l.Name; _cal.Text = l.Caliber; _bullet.Text = l.Bullet; _powder.Text = l.Powder; _notes.Text = l.Notes;
        _charge.Value = (decimal)Math.Clamp(U.ChargeValue(l.ChargeGr), 0, (double)_charge.Maximum);

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        void Row(string label, Control c) { t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 8, 0) }); t.Controls.Add(c); }
        Row("Nome", _name);
        Row("Calibro", _cal);
        Row("Proiettile", _bullet);
        Row("Polvere", _powder);
        Row("Carica " + U.ChargeUnitName, _charge);
        Row("Note", _notes);
        var hint = new Label
        {
            Text = "Le uscite lette da GRT con stesso calibro, una carica entro 0,05 gr e proiettile e polvere compatibili entrano nella storia di questo carico. " +
                   "Proiettile e polvere possono restare vuoti.",
            AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 0),
        };
        t.Controls.Add(hint);
        t.SetColumnSpan(hint, 2);

        var ok = new Button { Text = "Salva", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 80 };
        // a failed check keeps the dialog open: the button has already set OK by the time a Click handler runs, so it is FormClosing that can still refuse
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !Commit()) e.Cancel = true; };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(t);
        Controls.Add(buttons);
        NudFix.ApplyTo(this);
    }

    private bool Commit()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_cal.Text))
        {
            MessageBox.Show(this, "Un carico seguito ha bisogno di un nome e di un calibro.", Text);
            return false;
        }
        _l.Name = _name.Text.Trim();
        _l.Caliber = _cal.Text.Trim();
        _l.Bullet = _bullet.Text.Trim();
        _l.Powder = _powder.Text.Trim();
        _l.ChargeGr = U.ChargeToGrains((double)_charge.Value);
        _l.Notes = _notes.Text.Trim();
        return true;
    }
}
