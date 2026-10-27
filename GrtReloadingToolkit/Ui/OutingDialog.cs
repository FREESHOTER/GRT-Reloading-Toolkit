using System.Globalization;
using GrtReloadingToolkit.Log;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ui;

/// <summary>One outing: the shots (pasted or typed, in GRT's velocity unit), the conditions and what is different from last time. The mean, the SD
/// and the ES are worked out from the shots as they are typed, the sample SD that GRT shows. Everything but the date and the shots may stay empty.</summary>
internal sealed class OutingDialog : Form
{
    private static readonly GrtUnits U = GrtUnits.Current;
    private readonly Outing _o;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 110 };
    private readonly TextBox _shots = new() { Width = 360, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
    private readonly Label _preview = new() { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = SystemColors.GrayText };
    private readonly TextBox _temp = new() { Width = 80 }, _press = new() { Width = 80 }, _hum = new() { Width = 80 };
    private readonly TextBox _wind = new() { Width = 80 }, _windDir = new() { Width = 80 };
    private readonly TextBox _group = new() { Width = 80 }, _dist = new() { Width = 80 };
    private readonly TextBox _firearm = new() { Width = 220 }, _powderLot = new() { Width = 220 }, _primerLot = new() { Width = 220 };
    private readonly TextBox _seating = new() { Width = 80 }, _firings = new() { Width = 80 }, _barrel = new() { Width = 80 };
    private readonly TextBox _notes = new() { Width = 360, Multiline = true, Height = 50, ScrollBars = ScrollBars.Vertical };

    public Outing Result => _o;

    public OutingDialog(Outing o, string loadName)
    {
        _o = o;
        Text = (o.Id == 0 ? "Nuova uscita" : "Modifica uscita") + " — " + loadName;
        AppIcon.Apply(this);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(600, 700);
        AutoScroll = true;

        if (DateTime.TryParse(o.Date, Inv, DateTimeStyles.None, out var d)) _date.Value = d;
        _shots.Text = string.Join(" ", o.Velocities().Select(v => U.VelocityValue(v).ToString("0.##", Inv)));
        _shots.TextChanged += (_, _) => UpdatePreview();
        _temp.Text = o.TempC is { } tc ? U.TemperatureValue(tc).ToString("0.#", Inv) : "";
        _press.Text = Opt(o.PressureHpa, "0"); _hum.Text = Opt(o.HumidityPct, "0");
        _wind.Text = o.WindMps is { } w ? U.VelocityValue(w).ToString("0.#", Inv) : ""; _windDir.Text = Opt(o.WindDeg, "0");
        _group.Text = Opt(o.GroupMoa, "0.00"); _dist.Text = o.DistanceM is { } dm ? U.DistanceValue(dm).ToString("0", Inv) : "";
        _firearm.Text = o.Firearm; _powderLot.Text = o.PowderLot; _primerLot.Text = o.PrimerLot;
        _seating.Text = Opt(o.SeatingMm, "0.00"); _firings.Text = o.BrassFirings?.ToString(Inv) ?? ""; _barrel.Text = o.BarrelRounds?.ToString(Inv) ?? "";
        _notes.Text = o.Notes;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        void Row(string label, Control c) { t.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 8, 0) }); t.Controls.Add(c); }
        Row("Data", _date);
        Row("Colpi, velocità " + U.VelocityUnitName, _shots);
        Row("", _preview);
        Row("Temperatura " + U.TemperatureUnitName, _temp);
        Row("Pressione hPa", _press);
        Row("Umidità %", _hum);
        Row("Vento " + U.VelocityUnitName, _wind);
        Row("Direzione del vento (gradi dalle ore 12)", _windDir);
        Row("Gruppo MOA", _group);
        Row("Distanza " + U.DistanceUnitName, _dist);
        Row("Arma", _firearm);
        Row("Lotto di polvere", _powderLot);
        Row("Lotto di inneschi", _primerLot);
        Row("Profondità di seduta mm", _seating);
        Row("Ricariche del bossolo", _firings);
        Row("Colpi già sparati dalla canna", _barrel);
        Row("Note", _notes);

        var ok = new Button { Text = "Salva", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 80 };
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !Commit()) e.Cancel = true; };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        AcceptButton = ok; CancelButton = cancel;

        Controls.Add(t);
        Controls.Add(buttons);
        UpdatePreview();
        Shown += (_, _) =>
        {
            var wa = Screen.FromControl(this).WorkingArea;
            var h = Math.Min(Height, wa.Height);
            if (h != Height || Bottom > wa.Bottom) SetBounds(Left, Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h)), Width, h);
        };
    }

    private static string Opt(double? v, string fmt) => v?.ToString(fmt, Inv) ?? "";

    /// <summary>A number typed with either decimal separator; empty is null; text that is not a number is false.</summary>
    private static bool TryOpt(string text, out double? value)
    {
        value = null;
        text = text.Trim();
        if (text.Length == 0) return true;
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, Inv, out double v)) { value = v; return true; }
        return false;
    }

    private List<double> ShotsMps() => Outing.ParseShots(_shots.Text).Select(U.VelocityToMps).Where(Outing.IsPlausibleShotMps).ToList();
    private List<double> Ignored() => Outing.ParseShots(_shots.Text).Where(v => !Outing.IsPlausibleShotMps(U.VelocityToMps(v))).ToList();

    private void UpdatePreview()
    {
        var v = ShotsMps();
        var ign = Ignored();
        string text = v.Count == 0 ? "nessun colpo" : $"{v.Count} colpi · media {U.Velocity(StringStats.From(v).Mean)}";
        if (v.Count > 1) { var st = StringStats.From(v); text += $" · SD {U.VelocitySd(st.Sd)} · ES {U.VelocitySd(st.Es)}"; }
        if (ign.Count > 0) text += $"  ⚠ {ign.Count} valori non sono velocità di un colpo e non contano";
        _preview.Text = text;
    }

    private bool Commit()
    {
        var fields = new (TextBox box, string name)[]
        {
            (_temp, "Temperatura"), (_press, "Pressione"), (_hum, "Umidità"), (_wind, "Vento"), (_windDir, "Direzione del vento"),
            (_group, "Gruppo"), (_dist, "Distanza"), (_seating, "Seduta"), (_firings, "Ricariche"), (_barrel, "Colpi della canna"),
        };
        foreach (var (box, name) in fields)
            if (!TryOpt(box.Text, out _)) { MessageBox.Show(this, $"{name}: «{box.Text}» non è un numero.", Text); box.Focus(); return false; }

        double? N(TextBox b) { TryOpt(b.Text, out var v); return v; }
        var ignored = Ignored();
        if (ignored.Count > 0)
            MessageBox.Show(this, "Questi valori nel riquadro dei colpi non sono velocità di un colpo e sono stati esclusi: " +
                                  string.Join(" ", ignored.Select(x => x.ToString("0.##", Inv))), Text);

        _o.Date = _date.Value.ToString("yyyy-MM-dd", Inv);
        _o.ShotsMs = Outing.FormatShots(ShotsMps());
        _o.TempC = N(_temp) is { } t ? (U.TemperatureInF ? (t - 32) * 5.0 / 9.0 : t) : null;
        _o.PressureHpa = N(_press); _o.HumidityPct = N(_hum);
        _o.WindMps = N(_wind) is { } w ? U.VelocityToMps(w) : null;
        _o.WindDeg = _o.WindMps is null ? null : N(_windDir);
        _o.GroupMoa = N(_group);
        _o.DistanceM = N(_dist) is { } dd ? U.DistanceToMetres(dd) : null;
        _o.Firearm = _firearm.Text.Trim(); _o.PowderLot = _powderLot.Text.Trim(); _o.PrimerLot = _primerLot.Text.Trim();
        _o.SeatingMm = N(_seating);
        _o.BrassFirings = N(_firings) is { } bf ? (int)Math.Round(bf) : null;
        _o.BarrelRounds = N(_barrel) is { } br ? (int)Math.Round(br) : null;
        _o.Notes = _notes.Text.Trim();
        return true;
    }
}
