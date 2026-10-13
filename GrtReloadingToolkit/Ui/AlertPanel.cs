using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The alerts "to review the load" (<see cref="LoadAlerts"/>), as a panel that sits above the journal grid and shows itself only when
/// there is something to say. Experimental, and says so on its first line, like the guided diagnosis.
/// </summary>
internal sealed class AlertPanel : Panel
{
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), UseMnemonic = false };
    private readonly Label _body = new() { AutoSize = false, UseMnemonic = false, Dock = DockStyle.Fill };

    public AlertPanel()
    {
        Dock = DockStyle.Top;
        BackColor = Color.FromArgb(255, 244, 214);
        Padding = new Padding(10, 6, 10, 6);
        Visible = false;
        _title.Dock = DockStyle.Top;
        Controls.Add(_body);
        Controls.Add(_title);
        Resize += (_, _) => Fit();
    }

    /// <summary>How many alerts the load open in GRT has, by caliber and load name; set once by the toolkit context (the launcher
    /// marks the phase of the final load with it). Null when there is no journal.</summary>
    public static Func<string, string, int>? CountForOpenLoad;

    /// <summary>Everything the alerts read, from the toolkit's own database. Units as GRT shows them.</summary>
    internal static AlertContext ContextFor(Db db)
    {
        var u = GrtPluginKit.Grt.GrtUnits.Current;
        var firearms = db.Firearms().Select(f => (f, db.FirearmRoundCount(f.Id))).ToList();
        return new AlertContext(db.Journal().ToList(), db.Components(includeArchived: true).ToDictionary(c => c.Id), firearms,
            mps => u.VelocitySd(mps) + " " + u.VelocityUnitName);
    }

    /// <summary>The alert in the interface's language, numbers filled in invariantly.</summary>
    internal static string Text(LoadAlert a) => string.Format(CultureInfo.InvariantCulture, Lang.T(a.Template), a.Args);

    public void Show(IReadOnlyList<LoadAlert> alerts)
    {
        Visible = alerts.Count > 0;
        if (alerts.Count == 0) return;
        _title.Text = string.Format(CultureInfo.InvariantCulture, Lang.T("Loads to review ({0}) - experimental: the community is asked to try these alerts and report where they are wrong"), alerts.Count);
        _body.Text = string.Join(Environment.NewLine, alerts.Select(a => "• " + Text(a) + "  [" + string.Format(CultureInfo.InvariantCulture, Lang.T("back to phase {0}"), a.ReturnToPhase) + "]"));
        Fit();
    }

    private void Fit()
    {
        if (!Visible) return;
        int width = Math.Max(200, ClientSize.Width - Padding.Horizontal);
        int text = TextRenderer.MeasureText(_body.Text, _body.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        Height = Padding.Vertical + _title.PreferredHeight + text + 4;
    }
}
