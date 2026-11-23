using System.Globalization;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Measuring;
using GrtReloadingToolkit.Shooting;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Reads the pattern of the shots on a target for the mistakes of the shooter: the zero of the sight, a sideways spread bigger than the vertical
/// one, shots far from their group, the point of impact drifting during the series, the time kept between shots, the first shot of each
/// series, a group that gets worse. The shots come from the groups of the archive or are typed; nothing here changes the archive.
/// </summary>
internal sealed class GroupReadingForm : Form
{
    private readonly ArchiveLink? _link;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Label _source = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(10, 7, 0, 0) };
    private readonly NumericUpDown _distance = new() { Minimum = 5, Maximum = 2200, Value = 100, Width = 70 };
    private readonly ComboBox _distUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
    private readonly ComboBox _coordUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
    private readonly ComboBox _angleUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private static UnitPrefs Prefs => AppUnits.Prefs;
    private string _distCode = "m", _coordCode = "mm", _angleCode = "moa";

    /// <summary>The distance in metres, from what is in the box in the unit chosen for it.</summary>
    private double DistanceM => UnitSystem.ToBase((double)_distance.Value, _distCode);
    private readonly ComboBox _rest = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _click = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly CheckBox _aim = new() { AutoSize = true, Margin = new Padding(12, 5, 0, 0) };
    private readonly CheckBox _match = new() { AutoSize = true, Margin = new Padding(0, 5, 14, 0) };
    private readonly Label _distanceLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
    private readonly ShotsTargetChart _chart = new() { Dock = DockStyle.Fill };
    private readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font("Consolas", 9.5f) };

    private static readonly (ShootingMode Mode, string Label)[] Rests =
    {
        (ShootingMode.Unknown, "not said"), (ShootingMode.Mono, "front rest only"), (ShootingMode.DoubleRest, "front and rear rest"), (ShootingMode.Bipod, "bipod"),
    };
    private static readonly string?[] Clicks = new string?[] { null }.Concat(ScopeClicks.All).ToArray();

    private List<PatternShot> _shots = new();
    private string _typed = "";
    private List<MatchShot> _matchShots = new();
    private string _matchTyped = "";

    public GroupReadingForm(ArchiveLink? link, bool match = false)
    {
        _link = link;
        Text = AppVersion.Title(Lang.T(match ? "Match reading (one shot per target)" : "Group reading (shooter errors)"));
        Width = 1100; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 520);

        var fromArchive = new Button { Text = Lang.T("From the archive…"), AutoSize = true, Enabled = link != null };
        var typed = new Button { Text = Lang.T("Type the shots…"), AutoSize = true };
        var read = new Button { Text = Lang.T("Read the shots"), AutoSize = true };
        var copy = new Button { Text = Lang.T("Copy"), AutoSize = true };
        fromArchive.Click += (_, _) => LoadFromArchive();
        typed.Click += (_, _) => TypeShots();
        read.Click += (_, _) => ReadShots();
        copy.Click += (_, _) => { if (_report.Text.Length > 0) Clipboard.SetText(_report.Text); };

        foreach (var (_, label) in Rests) _rest.Items.Add(Lang.T(label));
        _rest.SelectedIndex = 0;
        foreach (string? c in Clicks) _click.Items.Add(c ?? Lang.T("not known"));
        _click.SelectedIndex = Math.Max(0, Array.IndexOf(Clicks, Prefs.Click));        // the click chosen in the units of the program
        SetUpUnits();
        _aim.Text = Lang.T("the coordinates are measured from the point of aim");
        _aim.Checked = true;
        _match.Text = Lang.T("Match: one shot for each target, several targets");
        _match.CheckedChanged += (_, _) => MatchModeChanged();

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(6, 6, 6, 0), WrapContents = true };
        top.Controls.Add(fromArchive);
        top.Controls.Add(typed);
        top.Controls.Add(read);
        top.Controls.Add(copy);
        top.Controls.Add(_source);
        top.SetFlowBreak(_source, true);
        top.Controls.Add(_match);
        top.SetFlowBreak(_match, true);
        UpdateDistanceLabel();
        top.Controls.Add(_distanceLabel);
        top.Controls.Add(_distance);
        top.Controls.Add(_distUnit);
        top.Controls.Add(new Label { Text = Lang.T("Coordinates in:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        top.Controls.Add(_coordUnit);
        top.Controls.Add(new Label { Text = Lang.T("Angles in:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        top.Controls.Add(_angleUnit);
        top.SetFlowBreak(_angleUnit, true);
        top.Controls.Add(new Label { Text = Lang.T("The rifle rests:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        top.Controls.Add(_rest);
        top.Controls.Add(new Label { Text = Lang.T("One click of the scope:"), AutoSize = true, Margin = new Padding(14, 6, 4, 0) });
        top.Controls.Add(_click);
        top.Controls.Add(_aim);
        _source.Text = Lang.T("Nothing loaded yet: take groups from the archive or type the shots.");

        // the picture beside the words: a chart explains where the shots went faster than the numbers do
        var split = new SplitContainer { Dock = DockStyle.Fill }.WithDistance(470);
        split.Panel1.Controls.Add(_chart);
        split.Panel2.Controls.Add(_report);
        Controls.Add(split);
        Controls.Add(top);
        if (match) _match.Checked = true;
        else _report.Text = Lang.T("The reading looks for the mistakes the shooter makes, from where the shots went: the zero of the sight, sideways against vertical spread, shots far from their group, the point of impact moving during the series, the time kept between shots, the first shot of each series, a group that gets worse from one series to the next. At least 5 shots are needed; with several series each series is centred on itself.");
        _report.Select(0, 0);
    }

    private void LoadFromArchive()
    {
        if (_link == null) return;
        using var pick = new ArchivePickForm(_link, ArchivePick.Groups);
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        if (_match.Checked)
        {
            var m = PatternQueries.MatchFromGroups(_link.Db, pick.PickedGroups, DistanceM);
            _matchShots = m.Shots.ToList();
            _matchTyped = "";
            _source.Text = string.Format(Inv, Lang.T("{0} targets of the archive, {1} shots."), pick.PickedGroups.Count, _matchShots.Count)
                + (m.Notes.Count > 0 ? " " + string.Join(" ", m.Notes) : "");
            ReadShots();
            return;
        }
        var result = PatternQueries.FromGroups(_link.Db, pick.PickedGroups);
        _shots = result.Shots.ToList();
        _typed = "";
        if (result.DistanceM is { } d) SetDistance(d);
        _aim.Checked = result.AimKnown;
        _source.Text = string.Format(Inv, Lang.T("{0} groups of the archive, {1} shots."), pick.PickedGroups.Count, _shots.Count)
            + (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "");
        ReadShots();
    }

    private void TypeShots()
    {
        using var d = new Form
        {
            Text = Lang.T("Type the shots"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(560, 420), MinimumSize = new Size(420, 300),
        };
        bool match = _match.Checked;
        var help = new Label
        {
            Dock = DockStyle.Top, Height = match ? 118 : 74, Padding = new Padding(8, 8, 8, 0),
            Text = match
                ? string.Format(Inv, Lang.T("One line for each shot, in the order fired: x y distance, with x to the right and y UP from the point of aim of that target, in {0}, and the distance in {1}. Then, if you have them: v=831 (velocity m/s), w=+3 (across wind m/s, + when it pushes the bullet to the right), pos=prone (position), lot=A (cartridge lot), name=T4 (the target) and the time (10:14:02). The distance may be left out when it is the one in the window. A line that begins with # is a comment."),
                    UnitSystem.Label(_coordCode), UnitSystem.Label(_distCode))
                : string.Format(Inv, Lang.T("One shot per line: x y in {0} (x to the right, y UP), then, if you have them, the velocity in m/s and the time (10:14:02). A blank line starts a new series; a line that begins with # starts one and names it."),
                    UnitSystem.Label(_coordCode)),
        };
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10f), Text = match ? _matchTyped : _typed };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var no = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.AddRange(new Control[] { no, ok });
        d.Controls.Add(box); d.Controls.Add(bar); d.Controls.Add(help);
        d.CancelButton = no;
        GrtTheme.Apply(d);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (match)
        {
            var mp = MatchInput.Parse(box.Text, DistanceM, UnitSystem.ToBase(1, _coordCode), UnitSystem.ToBase(1, _distCode));
            _matchTyped = box.Text;
            _matchShots = mp.Shots.ToList();
            _source.Text = string.Format(Inv, Lang.T("Typed: {0} shots on {1} targets."), _matchShots.Count, _matchShots.Select(s => s.Target).Distinct().Count())
                + (mp.Problems.Count > 0 ? " " + string.Join(" ", mp.Problems) : "");
            ReadShots();
            return;
        }
        var parsed = PatternInput.Parse(box.Text, UnitSystem.ToBase(1, _coordCode));
        _typed = box.Text;
        _shots = parsed.Shots.ToList();
        int series = _shots.Select(s => s.Series).Distinct().Count();
        _source.Text = string.Format(Inv, Lang.T("Typed: {0} shots in {1} series."), _shots.Count, series) + (parsed.Problems.Count > 0 ? " " + string.Join(" ", parsed.Problems) : "");
        _aim.Checked = true;
        ReadShots();
    }

    private const string MatchIntro = "A match: one shot for each target, targets at different distances, in different wind or position. Each shot is turned into MOA from the point of aim of its own target and taken apart into the cartridge, the settings, the wind and the shooting; every shot is named for what weighs most on it, and the whole match is read for problems of the cartridges, the settings, the conditions or the shooter. At least 6 shots are needed. Each group of the archive is one target.";
    private const string GroupIntro = "The reading looks for the mistakes the shooter makes, from where the shots went: the zero of the sight, sideways against vertical spread, shots far from their group, the point of impact moving during the series, the time kept between shots, the first shot of each series, a group that gets worse from one series to the next. At least 5 shots are needed; with several series each series is centred on itself.";

    // ---- the units: the starting choice of the program, and a choice of this window for each of its fields that is remembered ----

    private void SetUpUnits()
    {
        foreach (var u in UnitSystem.Of(UnitKind.Distance)) _distUnit.Items.Add(u.Label);
        foreach (var u in UnitSystem.Of(UnitKind.Length)) _coordUnit.Items.Add(u.Label);
        foreach (var u in UnitSystem.Of(UnitKind.Angle)) _angleUnit.Items.Add(u.Label);
        _distCode = Prefs.For("reading.distance", UnitKind.Distance);
        _coordCode = Prefs.For("reading.coords", UnitKind.Length);
        _angleCode = Prefs.For("reading.angle", UnitKind.Angle);
        _distUnit.SelectedIndex = UnitSystem.Of(UnitKind.Distance).ToList().FindIndex(u => u.Code == _distCode);
        _coordUnit.SelectedIndex = UnitSystem.Of(UnitKind.Length).ToList().FindIndex(u => u.Code == _coordCode);
        _angleUnit.SelectedIndex = UnitSystem.Of(UnitKind.Angle).ToList().FindIndex(u => u.Code == _angleCode);
        SetDistance(100);
        _distUnit.SelectedIndexChanged += (_, _) =>
        {
            double m = DistanceM;
            _distCode = UnitSystem.Of(UnitKind.Distance)[_distUnit.SelectedIndex].Code;
            Prefs.SetField("reading.distance", UnitKind.Distance, _distCode);
            SetDistance(m);
            UpdateDistanceLabel();
            if (_shots.Count > 0 || _matchShots.Count > 0) ReadShots();
        };
        _coordUnit.SelectedIndexChanged += (_, _) =>
        {
            _coordCode = UnitSystem.Of(UnitKind.Length)[_coordUnit.SelectedIndex].Code;
            Prefs.SetField("reading.coords", UnitKind.Length, _coordCode);
        };
        _angleUnit.SelectedIndexChanged += (_, _) =>
        {
            _angleCode = UnitSystem.Of(UnitKind.Angle)[_angleUnit.SelectedIndex].Code;
            Prefs.SetField("reading.angle", UnitKind.Angle, _angleCode);
            if (_shots.Count > 0 || _matchShots.Count > 0) ReadShots();
        };
        _click.SelectedIndexChanged += (_, _) =>
        {
            if (_click.SelectedIndex <= 0 || Clicks[_click.SelectedIndex] is not { } name) return;
            Prefs.Click = name;                                                          // the click is the same for every window
            // a click in milliradians is read in milliradians, one in MOA in MOA: the angles follow it unless they are changed afterwards
            string unit = ScopeClicks.UnitOf(name);
            int at = UnitSystem.Of(UnitKind.Angle).ToList().FindIndex(u => u.Code == unit);
            if (at >= 0 && _angleUnit.SelectedIndex != at) _angleUnit.SelectedIndex = at;
            else if (_shots.Count > 0 || _matchShots.Count > 0) ReadShots();
        };
    }

    /// <summary>Puts a distance given in metres in the box, in the unit the box is in.</summary>
    private void SetDistance(double metres)
    {
        _distance.Minimum = (decimal)Math.Floor(UnitSystem.FromBase(5, _distCode));
        _distance.Maximum = (decimal)Math.Ceiling(UnitSystem.FromBase(2000, _distCode));
        _distance.Value = Math.Clamp((decimal)Math.Round(UnitSystem.FromBase(metres, _distCode)), _distance.Minimum, _distance.Maximum);
    }

    private void UpdateDistanceLabel() =>
        _distanceLabel.Text = string.Format(Inv, Lang.T(_match.Checked ? "Distance when not typed ({0}):" : "Distance ({0}):"), UnitSystem.Label(_distCode));

    private void MatchModeChanged()
    {
        bool m = _match.Checked;
        _aim.Visible = !m;
        UpdateDistanceLabel();
        _source.Text = Lang.T("Nothing loaded yet: take groups from the archive or type the shots.");
        _report.Text = Lang.T(m ? MatchIntro : GroupIntro);
        _report.Select(0, 0);
    }

    private void ReadShots()
    {
        if (_match.Checked)
        {
            var mo = new MatchOptions { Mode = Rests[Math.Max(0, _rest.SelectedIndex)].Mode, ScopeClick = Clicks[Math.Max(0, _click.SelectedIndex)], AngleUnit = _angleCode };
            var mr = MatchReading.Analyze(_matchShots, mo);
            _report.Text = mr == null
                ? string.Format(Inv, Lang.T("At least {0} shots are needed to read them."), MatchReading.MinShots)
                : MatchText.Report(mr, Measuring.ProgramUnits.Current);
            _report.Select(0, 0);
            DrawMatch(mr);
            return;
        }
        var options = new PatternOptions
        {
            DistanceM = DistanceM, AimKnown = _aim.Checked, AngleUnit = _angleCode,
            Mode = Rests[Math.Max(0, _rest.SelectedIndex)].Mode, ScopeClick = Clicks[Math.Max(0, _click.SelectedIndex)],
        };
        var report = ShooterErrors.Analyze(_shots, options);
        _report.Text = report == null
            ? string.Format(Inv, Lang.T("At least {0} shots are needed to read them."), ShooterErrors.MinShots)
            : ShooterText.Report(report, Measuring.ProgramUnits.Current);
        _report.Select(0, 0);
        DrawGroup(report, options.DistanceM);
    }

    // ---- the picture ----

    private static readonly (string Cause, string Name, Color Color)[] Causes =
    {
        ("normal", "normal", Color.FromArgb(120, 135, 155)), ("cartridge", "cartridge", Color.FromArgb(56, 189, 248)), ("settings", "settings", Color.FromArgb(251, 191, 36)),
        ("wind", "wind", Color.FromArgb(45, 212, 191)), ("execution", "shooting", Color.FromArgb(248, 113, 113)), ("target", "same target", Color.FromArgb(244, 114, 182)),
    };

    /// <summary>The shots of a match, each coloured for the part that weighs most on it; the ones with a cause are ringed.</summary>
    private void DrawMatch(MatchReport? r)
    {
        if (r == null) { _chart.SetData(Array.Empty<TargetPoint>(), Array.Empty<(string, Color)>(), "MOA", ""); return; }
        var points = r.Shots.Select(s => new TargetPoint(s.XMoa, s.UpMoa, s.Number.ToString(Inv), Math.Max(0, Array.FindIndex(Causes, c => c.Cause == s.Cause)), s.Cause != "normal")).ToList();
        var used = points.Select(q => q.Class).Distinct().OrderBy(i => i).ToList();
        // only the causes that are there are named in the legend; the class of a point is its place in that list
        var classes = Causes.Select(c => (Name: Lang.T(c.Name), c.Color)).ToList();
        _chart.SetData(points, classes, UnitSystem.Label(r.AngleUnit), Lang.T("Where each shot went, from the aim of its own target"));
    }

    /// <summary>The shots of a group (or of several series), coloured by series; the ones far from their group are ringed.</summary>
    private void DrawGroup(ShooterReport? r, double distanceM)
    {
        if (r == null) { _chart.SetData(Array.Empty<TargetPoint>(), Array.Empty<(string, Color)>(), "MOA", ""); return; }
        double mmPerUnit = r.AngleMmAt100 * distanceM / 100;
        var names = _shots.Select(s => s.Series).Distinct().ToList();
        var flyers = r.Flyers.Select(f => (f.Series, f.Number)).ToHashSet();
        var points = _shots.Select(s => new TargetPoint(s.XMm / mmPerUnit, s.UpMm / mmPerUnit, s.Number.ToString(Inv), names.IndexOf(s.Series), flyers.Contains((s.Series, s.Number)))).ToList();
        var classes = names.Select((n, i) => (Name: n, Color: ChartBase.Series[i % ChartBase.Series.Length])).ToList();
        _chart.SetData(points, classes, UnitSystem.Label(r.AngleUnit), Lang.T("Where the shots went, from the aim"), _aim.Checked);
    }

    /// <summary>The window drawn for a picture of the match reading (<c>--anteprima gara</c>), with an invented match.</summary>
    public static GroupReadingForm PreviewMatch()
    {
        var f = new GroupReadingForm(null);
        var rows = new List<string>();
        double[] ds = { 300, 400, 500, 600, 700, 800 };
        double[] wind = { 2, -1, 3, 0, -2, 3, 1, -3, 2, 0, -1, 3 };
        double[] vel = { 833, 831, 828, 835, 826, 832, 830, 829, 834, 819, 831, 833 };
        double[] right = { 0.3, -0.2, 1.9, 0.1, -1.4, 3.3, 0.5, -0.3, 2.2, 0.0, -0.8, 3.0 };      // in MOA, with the wind not corrected
        double[] up = { 0.35, 0.28, 0.55, 0.62, 1.10, 1.05, 0.30, 0.40, 0.45, -0.05, 1.20, 1.60 };
        for (int i = 0; i < 12; i++)
        {
            double mm = ShooterErrors.MoaMmAt100 * ds[i % 6] / 100;
            rows.Add((right[i] * mm).ToString("0.0", Inv) + " " + (up[i] * mm).ToString("0.0", Inv) + " " + ds[i % 6].ToString("0", Inv) + " v=" + vel[i].ToString("0", Inv)
                + " w=" + wind[i].ToString("+0;-0;0", Inv) + " pos=" + (i < 6 ? "prone" : "kneeling") + " 10:" + (14 + i).ToString("00", Inv) + ":" + ((i * 7) % 60).ToString("00", Inv));
        }
        f._matchTyped = string.Join(Environment.NewLine, rows);
        f._match.Checked = true;
        f._matchShots = MatchInput.Parse(f._matchTyped).Shots.ToList();
        f._source.Text = string.Format(Inv, Lang.T("Typed: {0} shots on {1} targets."), 12, 12);
        f._rest.SelectedIndex = 2;
        f._click.SelectedIndex = 2;
        f.ReadShots();
        return f;
    }

    /// <summary>The window drawn for a picture (<c>--anteprima lettura</c>), with an invented target.</summary>
    public static GroupReadingForm Preview()
    {
        var f = new GroupReadingForm(null);
        f._typed = string.Join(Environment.NewLine, new[]
        {
            "# Serie 1", "4.2 -3.1 831.0 10:14:02", "5.9 -5.2 829.5 10:14:40", "3.1 -4.4 830.2 10:15:20", "6.8 -2.9 832.1 10:16:10", "2.4 -6.0 828.7 10:16:50", "5.5 -3.8 831.4 10:17:30",
            "", "# Serie 2", "7.1 -3.0 833.0 10:25:02", "9.0 -4.4 834.1 10:25:41", "6.2 -2.2 832.8 10:26:30", "8.4 -5.1 835.0 10:27:15", "10.3 -3.6 833.9 10:28:00", "7.7 -4.8 834.6 10:28:41",
            "", "# Serie 3", "11.8 -4.1 836.0 10:40:10", "14.5 -5.6 835.2 10:40:55", "9.9 -3.2 837.1 10:41:40", "15.2 -2.8 836.4 10:42:30", "26.0 -9.5 834.0 10:43:15", "12.6 -4.9 836.8 10:44:00",
        });
        f._shots = PatternInput.Parse(f._typed).Shots.ToList();
        f._source.Text = string.Format(Inv, Lang.T("Typed: {0} shots in {1} series."), 18, 3);
        f._rest.SelectedIndex = 1;
        f._click.SelectedIndex = 2;
        f.ReadShots();
        return f;
    }
}
