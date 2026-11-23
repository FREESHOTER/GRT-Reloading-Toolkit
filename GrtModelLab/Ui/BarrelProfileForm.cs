using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtPluginKit.Util;

namespace GrtModelLab.Ui;

/// <summary>
/// Capture the physical barrel data GRT never asks for: steel (elastic modulus + density) and the
/// outer-diameter profile along the barrel's length. Feeds the future barrel-vibration model —
/// nothing here talks to GRT's IPC, it's pure data entry and local storage.
/// </summary>
internal sealed class BarrelProfileForm : Form
{
    private const double MmPerInch = 25.4;

    private readonly BarrelProfileDb _db;
    private BarrelProfileRecord _rec = new() { SteelLabel = SteelLibrary.Known[SteelType.ChromeMoly].Label, ElasticModulusGPa = 205, DensityKgM3 = 7850, BoreDiameterMm = 6.71, LengthMm = 660 };
    private bool _inches;
    private bool _suppress;

    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _name = new() { Width = 160 };
    private readonly ComboBox _steel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly NumericUpDown _modulus = new() { DecimalPlaces = 0, Minimum = 50, Maximum = 500, Width = 65 };
    private readonly NumericUpDown _density = new() { DecimalPlaces = 0, Minimum = 1000, Maximum = 20000, Increment = 10, Width = 75 };
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Items = { "mm", "in" } };
    private readonly NumericUpDown _bore = new() { DecimalPlaces = 3, Increment = 0.01M, Maximum = 50, Width = 70 };
    private readonly NumericUpDown _length = new() { DecimalPlaces = 1, Increment = 1M, Maximum = 2000, Width = 75 };
    private readonly DataGridView _grid = new();
    private readonly BarrelProfileChart _chart = new();
    private readonly Label _status = new();

    public BarrelProfileForm(BarrelProfileDb db)
    {
        _db = db;
        Text = "Profilo Fisico Canna  (v0.1)";
        Width = 980; Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 500);
        Build();
        NudFix.ApplyTo(this);
        _unit.SelectedIndex = 0;
        RefreshProfileList();   // suppressed while it sets the combo, so this alone won't populate the form
        NewProfile();           // pushes _rec's starting values (bore/length/steel/points) into every control
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(6, 6, 0, 0), WrapContents = true };

        top.Controls.Add(new Label { Text = "Profilo salvato", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        top.Controls.Add(_profiles);
        _profiles.SelectedIndexChanged += (_, _) => { if (!_suppress) LoadSelected(); };
        var newBtn = new Button { Text = "Nuovo", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
        newBtn.Click += (_, _) => NewProfile();
        top.Controls.Add(newBtn);
        var delBtn = new Button { Text = "Elimina", AutoSize = true, Margin = new Padding(4, 2, 0, 0) };
        delBtn.Click += (_, _) => DeleteSelected();
        top.Controls.Add(delBtn);

        top.Controls.Add(new Label { Text = "  Nome", AutoSize = true, Padding = new Padding(10, 6, 4, 0) });
        top.Controls.Add(_name);
        top.Controls.Add(new Label { Text = "  unità", AutoSize = true, Padding = new Padding(10, 6, 4, 0) });
        top.Controls.Add(_unit);
        _unit.SelectedIndexChanged += (_, _) => SwitchUnit();

        top.SetFlowBreak(_unit, true);   // second row
        top.Controls.Add(new Label { Text = "Acciaio", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        foreach (var kv in SteelLibrary.Known) _steel.Items.Add(kv.Value.Label);
        _steel.SelectedIndexChanged += (_, _) => SteelSelected();
        top.Controls.Add(_steel);
        top.Controls.Add(new Label { Text = "  E (GPa)", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_modulus);
        top.Controls.Add(new Label { Text = "  densità (kg/m³)", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_density);
        top.Controls.Add(new Label { Text = "  Foro Ø", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
        top.Controls.Add(_bore);
        var lengthLabel = new Label { Text = "  Lunghezza", AutoSize = true, Padding = new Padding(8, 8, 0, 0) };
        top.Controls.Add(lengthLabel);
        top.Controls.Add(_length);
        var tip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };
        const string lengthHint = "Misura dalla volata alla faccia dell'otturatore (lunghezza totale), poi sottrai\n" +
                                   "il CBTO della cartuccia caricata (base-ogiva) per ottenere la lunghezza della canna.";
        tip.SetToolTip(lengthLabel, lengthHint);
        tip.SetToolTip(_length, lengthHint);

        // Bore/length/steel feed the future vibration model, but previously only the grid (via
        // CellEndEdit) triggered a recompute — typing in these spinners didn't even refresh the
        // chart. Wired here so the picture always matches what's actually in the fields.
        _bore.ValueChanged += (_, _) => { if (!_suppress) RecomputeChart(); };
        _length.ValueChanged += (_, _) => { if (!_suppress) RecomputeChart(); };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Posizione dalla culatta" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "od", HeaderText = "Diametro esterno" });
        _grid.CellEndEdit += (_, _) => { if (_suppress) return; SyncFromGrid(); RecomputeChart(); };

        // A fixed-height Dock=Top panel for the grid, Dock=Fill below it for the chart — no
        // SplitContainer. Setting SplitterDistance on a SplitContainer before it has its real,
        // laid-out size (as happens in an object initializer, or right after construction) gets
        // silently clamped to whatever tiny size the control has at that instant, and the clamp
        // sticks even once Dock=Fill later gives it its true size — this bit twice in this same
        // form (once with a vertical split, once with a horizontal one). Plain docking has no such
        // trap: the barrel picture is a long, wide shape and wants the full window width, not a
        // side column, so the grid takes a fixed height at the top and the chart fills the rest.
        var gridPanel = new Panel { Dock = DockStyle.Top, Height = 200 };
        gridPanel.Controls.Add(_grid);
        var gridButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 4, 0, 0) };
        var addPt = new Button { Text = "Aggiungi punto", AutoSize = true };
        addPt.Click += (_, _) => AddPoint();
        gridButtons.Controls.Add(addPt);
        var rmPt = new Button { Text = "Rimuovi punto", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        rmPt.Click += (_, _) => RemovePoint();
        gridButtons.Controls.Add(rmPt);
        gridPanel.Controls.Add(gridButtons);

        _chart.Dock = DockStyle.Fill;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var save = new Button { Text = "Salva profilo", AutoSize = true };
        save.Click += (_, _) => SaveProfile();
        bottom.Controls.Add(save);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);
        _status.Text = "Non ancora collegato a un modello di vibrazione — qui vengono solo catturati e salvati i dati che servirà usare.";

        // same-edge docked controls stack with the LAST one added landing outermost (confirmed by
        // testing both orders, not just recalled from docs) — so the main toolbar must be added
        // after the grid strip to end up above it, at the true top edge.
        Controls.Add(_chart);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(gridPanel);
        Controls.Add(top);
    }

    // ---- unit helpers --------------------------------------------------
    private double ToDisplay(double mm) => _inches ? mm / MmPerInch : mm;
    private double ToMm(double displayVal) => _inches ? displayVal * MmPerInch : displayVal;

    private void SwitchUnit()
    {
        bool toInches = _unit.SelectedIndex == 1;
        if (toInches == _inches) return;
        // read every displayed value BEFORE touching Maximum/DecimalPlaces (lowering Maximum
        // clamps Value immediately — converting afterwards would convert the clamped number,
        // not what was actually on screen; the same bug fixed elsewhere in the toolkit family).
        decimal bore = _bore.Value, length = _length.Value;
        _inches = toInches;
        decimal f = _inches ? 1m / (decimal)MmPerInch : (decimal)MmPerInch;
        _bore.Maximum = _inches ? 2m : 50m; _bore.DecimalPlaces = _inches ? 4 : 3; _bore.Increment = _inches ? 0.0005M : 0.01M;
        _length.Maximum = _inches ? 80m : 2000m; _length.DecimalPlaces = _inches ? 2 : 1; _length.Increment = _inches ? 0.1M : 1M;
        _bore.Value = Math.Round(Math.Clamp(bore * f, _bore.Minimum, _bore.Maximum), _bore.DecimalPlaces);
        _length.Value = Math.Round(Math.Clamp(length * f, _length.Minimum, _length.Maximum), _length.DecimalPlaces);
        RefreshGrid();
        RecomputeChart();
    }

    private void SteelSelected()
    {
        var kv = SteelLibrary.Known.Values.FirstOrDefault(v => v.Label == (string)(_steel.SelectedItem ?? ""));
        if (kv is null && _steel.Items.Count > 0) { _steel.SelectedIndex = 0; return; }
        if (kv is null) return;
        // Custom keeps whatever the user already has; the known alloys overwrite with their typical values.
        bool isCustom = kv.Label.StartsWith("Custom", StringComparison.Ordinal);
        if (!isCustom) { _modulus.Value = (decimal)kv.ElasticModulusGPa; _density.Value = (decimal)kv.DensityKgM3; }
    }

    // ---- profile list ---------------------------------------------------
    private void RefreshProfileList()
    {
        _suppress = true;
        _profiles.Items.Clear();
        _profiles.Items.Add("(nuovo profilo)");
        foreach (var (id, name) in _db.ListProfiles()) _profiles.Items.Add(new ProfileItem(id, name));
        _profiles.SelectedIndex = 0;
        _suppress = false;
    }

    private sealed record ProfileItem(long Id, string Name) { public override string ToString() => Name; }

    private void LoadSelected()
    {
        if (_profiles.SelectedItem is not ProfileItem pi) { NewProfile(); return; }
        var rec = _db.Load(pi.Id);
        if (rec is null) return;
        _rec = rec;
        _suppress = true;
        _name.Text = rec.Name;
        int idx = SteelLibrary.Known.Values.ToList().FindIndex(v => v.Label == rec.SteelLabel);
        _steel.SelectedIndex = idx >= 0 ? idx : SteelLibrary.Known.Count - 1;
        _modulus.Value = (decimal)rec.ElasticModulusGPa;
        _density.Value = (decimal)rec.DensityKgM3;
        _bore.Value = (decimal)Math.Clamp(ToDisplay(rec.BoreDiameterMm), (double)_bore.Minimum, (double)_bore.Maximum);
        _length.Value = (decimal)Math.Clamp(ToDisplay(rec.LengthMm), (double)_length.Minimum, (double)_length.Maximum);
        _suppress = false;
        RefreshGrid();
        RecomputeChart();
        _status.Text = $"Caricato '{rec.Name}' — {rec.Points.Count} punto/i profilo.";
    }

    private void NewProfile()
    {
        _rec = new BarrelProfileRecord
        {
            Name = "Nuova canna",
            SteelLabel = SteelLibrary.Known[SteelType.ChromeMoly].Label,
            ElasticModulusGPa = 205, DensityKgM3 = 7850, BoreDiameterMm = 6.71, LengthMm = 660,
        };
        // seed a straight cylinder (breech + muzzle at the same OD) so a fresh profile shows
        // something immediately instead of an empty chart — edit/add points to taper it.
        _rec.Points.Add(new BarrelProfilePoint { PositionMm = 0, OuterDiameterMm = _rec.BoreDiameterMm + 10 });
        _rec.Points.Add(new BarrelProfilePoint { PositionMm = _rec.LengthMm, OuterDiameterMm = _rec.BoreDiameterMm + 10 });
        _suppress = true;
        _name.Text = _rec.Name;
        _steel.SelectedIndex = 0;
        _modulus.Value = (decimal)_rec.ElasticModulusGPa;
        _density.Value = (decimal)_rec.DensityKgM3;
        _bore.Value = (decimal)ToDisplay(_rec.BoreDiameterMm);
        _length.Value = (decimal)ToDisplay(_rec.LengthMm);
        _suppress = false;
        RefreshGrid();
        RecomputeChart();
        _status.Text = "Nuovo profilo — aggiungi i punti, poi Salva.";
    }

    private void DeleteSelected()
    {
        if (_profiles.SelectedItem is not ProfileItem pi) return;
        if (MessageBox.Show(this, $"Eliminare il profilo '{pi.Name}'?", "Elimina", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.Delete(pi.Id);
        RefreshProfileList();
        NewProfile();
    }

    // ---- points grid -----------------------------------------------------
    private void RefreshGrid()
    {
        _suppress = true;
        _grid.Rows.Clear();
        string unit = _inches ? "in" : "mm";
        _grid.Columns["pos"].HeaderText = $"Posizione dalla culatta ({unit})";
        _grid.Columns["od"].HeaderText = $"Diametro esterno ({unit})";
        foreach (var p in _rec.Points.OrderBy(p => p.PositionMm))
            _grid.Rows.Add(
                ToDisplay(p.PositionMm).ToString(_inches ? "0.00" : "0.0", CultureInfo.InvariantCulture),
                ToDisplay(p.OuterDiameterMm).ToString(_inches ? "0.000" : "0.00", CultureInfo.InvariantCulture));
        _suppress = false;
    }

    private void SyncFromGrid()
    {
        var pts = new List<BarrelProfilePoint>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            double? pos = Str.ParseNumber(Convert.ToString(row.Cells["pos"].Value, CultureInfo.InvariantCulture));
            double? od = Str.ParseNumber(Convert.ToString(row.Cells["od"].Value, CultureInfo.InvariantCulture));
            if (pos is { } p && od is { } o) pts.Add(new BarrelProfilePoint { PositionMm = ToMm(p), OuterDiameterMm = ToMm(o) });
        }
        _rec.Points.Clear();
        _rec.Points.AddRange(pts.OrderBy(p => p.PositionMm));
    }

    private void AddPoint()
    {
        double lastPos = _rec.Points.Count > 0 ? _rec.Points.Max(p => p.PositionMm) : 0;
        double step = ToMm(_inches ? 2 : 50);
        _rec.Points.Add(new BarrelProfilePoint { PositionMm = Math.Min(_rec.LengthMm, lastPos + step), OuterDiameterMm = _rec.BoreDiameterMm + 10 });
        RefreshGrid();
        RecomputeChart();
    }

    private void RemovePoint()
    {
        if (_grid.CurrentRow?.Index is not { } i || i < 0) return;
        var ordered = _rec.Points.OrderBy(p => p.PositionMm).ToList();
        if (i >= ordered.Count) return;
        _rec.Points.Remove(ordered[i]);
        RefreshGrid();
        RecomputeChart();
    }

    private void RecomputeChart()
    {
        _rec.BoreDiameterMm = ToMm((double)_bore.Value);
        _rec.LengthMm = ToMm((double)_length.Value);
        _rec.ElasticModulusGPa = (double)_modulus.Value;
        _rec.DensityKgM3 = (double)_density.Value;
        _chart.Show(_rec, _inches);
    }

    private void SaveProfile()
    {
        SyncFromGrid();
        _rec.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Canna senza nome" : _name.Text.Trim();
        _rec.SteelLabel = (string)(_steel.SelectedItem ?? SteelLibrary.Known[SteelType.ChromeMoly].Label);
        _rec.ElasticModulusGPa = (double)_modulus.Value;
        _rec.DensityKgM3 = (double)_density.Value;
        _rec.BoreDiameterMm = ToMm((double)_bore.Value);
        _rec.LengthMm = ToMm((double)_length.Value);
        if (_rec.Points.Count < 2)
        {
            MessageBox.Show(this, "Aggiungi almeno 2 punti al profilo prima di salvare.", "Profilo canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _db.Save(_rec);
        RefreshProfileList();
        _status.Text = $"Salvato '{_rec.Name}'.";
    }
}
