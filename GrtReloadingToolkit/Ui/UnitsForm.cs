using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The units of the program: one starting choice for each kind of quantity (grains or grams, millimetres or inches, metres or yards, MOA or milliradians) and the
/// click of the scope. Every window uses them; a column or a field that has its own unit keeps it. The archive keeps grains, millimetres, metres and MOA whatever
/// is chosen here: the choice changes only what is shown and how a typed number is understood.
/// </summary>
internal sealed class UnitsForm : Form
{
    private static UnitPrefs Prefs => AppUnits.Prefs;
    private readonly Label _own = new() { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 8, 0, 0) };
    private readonly ComboBox _click = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Dictionary<string, RadioButton> _angle = new();

    private static readonly (UnitKind Kind, string Title, (string Code, string Text)[] Units)[] Groups =
    {
        (UnitKind.Mass, "Weights (cases, bullets, primers, powder charges)", new[] { ("gr", "grains (gr)"), ("g", "grams (g)") }),
        (UnitKind.Length, "Lengths (case, bullet, cartridge, neck, position of the shots on the target)", new[] { ("mm", "millimetres (mm)"), ("in", "inches (in)") }),
        (UnitKind.Distance, "Distances (shooting distance)", new[] { ("m", "metres (m)"), ("yd", "yards (yd)") }),
        (UnitKind.Angle, "Angles (errors on the target, the zero, the clicks)", new[] { ("moa", "minutes of angle (MOA)"), ("mrad", "milliradians (mrad)") }),
    };

    public UnitsForm()
    {
        Text = AppVersion.Title(Lang.T("Units of measure"));
        Width = 640; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 520);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        flow.Controls.Add(new Label
        {
            Text = Lang.T("Choose the unit each kind of number is shown and typed in. The archive keeps its own units whatever you choose here (grains, millimetres, metres, MOA): nothing stored changes."),
            AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 0, 0, 10),
        });
        foreach (var (kind, title, units) in Groups)
        {
            var box = new GroupBox { Text = Lang.T(title), Width = 580, Height = 62 };
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 0, 0) };
            foreach (var (code, text) in units)
            {
                var r = new RadioButton { Text = Lang.T(text), AutoSize = true, Margin = new Padding(0, 4, 24, 0), Checked = Prefs.Default(kind) == code };
                string c = code;
                UnitKind k = kind;
                r.CheckedChanged += (_, _) => { if (r.Checked) { Prefs.SetDefault(k, c); ShowOwn(); } };
                if (kind == UnitKind.Angle) _angle[code] = r;
                row.Controls.Add(r);
            }
            box.Controls.Add(row);
            flow.Controls.Add(box);
        }

        var clickBox = new GroupBox { Text = Lang.T("Click of the scope"), Width = 580, Height = 90 };
        var clickRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 2, 0, 0), WrapContents = true };
        foreach (string c in ScopeClicks.All) _click.Items.Add(c);
        _click.SelectedItem = Prefs.Click;
        _click.SelectedIndexChanged += (_, _) =>
        {
            if (_click.SelectedItem is not string name) return;
            Prefs.Click = name;
            // a click in milliradians goes with angles in milliradians, one in MOA with MOA: the angles follow it (and can be changed again above)
            if (_angle.TryGetValue(ScopeClicks.UnitOf(name), out var r) && !r.Checked) r.Checked = true;
        };
        clickRow.Controls.Add(_click);
        clickRow.Controls.Add(new Label
        {
            Text = Lang.T("MOA: 1/8, 1/4, 1/2. Milliradians: 1/20, 1/10. The readings of the shots use it to say how many clicks to give."),
            AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(12, 4, 0, 0),
        });
        clickBox.Controls.Add(clickRow);
        flow.Controls.Add(clickBox);

        var reset = new Button { Text = Lang.T("Every field back to the starting unit"), AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        reset.Click += (_, _) => { Prefs.ClearAllFields(); ShowOwn(); };
        flow.Controls.Add(_own);
        flow.Controls.Add(reset);
        flow.Controls.Add(new Label
        {
            Text = Lang.T("A column or a field can have its own unit: right-click the title of a column in the tables of the cartridges, or use the unit box beside the distance and the coordinates in the readings of the shots."),
            AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 10, 0, 0),
        });
        Controls.Add(flow);
        ShowOwn();
    }

    /// <summary>Says which fields have a unit of their own.</summary>
    private void ShowOwn()
    {
        var own = Prefs.FieldChoices();
        _own.Text = own.Count == 0
            ? Lang.T("No field has a unit of its own.")
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, Lang.T("Fields with a unit of their own ({0}): {1}"), own.Count,
                string.Join(", ", own.Select(c => c.Field.Replace("measure.", "").Replace("reading.", "") + " → " + UnitSystem.Label(c.Code))));
    }
}
