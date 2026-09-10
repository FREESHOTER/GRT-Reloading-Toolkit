using System.Globalization;
using GrtReloadingToolkit.Brass;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>Brass-prep calculators: case volume (→ GRT casevol), seating depth from comparator
/// measurements (→ GRT gdepth), and bushing / mandrel sizing for a target neck grip.</summary>
internal sealed class BrassForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Label _status = new();

    // case volume
    private readonly DataGridView _cv = new();
    private readonly ComboBox _cvUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Items = { "grain", "gram" } };
    private readonly Label _cvOut = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    private readonly Button _cvWrite = new() { Text = "Write casevol to GRT load", AutoSize = true, Enabled = false };
    private BrassCalc.CaseVolumeResult? _cvResult;

    // neck — inch by default (bushing dies are sold in inch); toggle to mm if wanted
    private const double MmPerIn = 25.4;
    private readonly ComboBox _neckUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Items = { "inch", "mm" } };
    private readonly NumericUpDown _bulletDia = new() { DecimalPlaces = 4, Increment = 0.0005M, Maximum = 2, Value = 0.2640M, Width = 90 };
    private readonly NumericUpDown _wall = new() { DecimalPlaces = 4, Increment = 0.0005M, Maximum = 0.1M, Value = 0.0140M, Width = 90 };
    private readonly NumericUpDown _interf = new() { DecimalPlaces = 4, Increment = 0.0005M, Maximum = 0.02M, Value = 0.0020M, Width = 90 };
    private readonly NumericUpDown _loadedOd = new() { DecimalPlaces = 4, Increment = 0.0005M, Maximum = 2, Value = 0M, Width = 90 };
    private readonly Label _neckOut = new() { AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private bool _neckInInch = true;
    private bool _suppressNeckUnit;

    // seating depth from comparator measurements -> GRT gdepth
    private readonly ComboBox _sdUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, Items = { "mm", "inch" } };
    private readonly NumericUpDown _sdCbto = new() { DecimalPlaces = 3, Increment = 0.01M, Maximum = 200, Value = 0M, Width = 100 };
    private readonly NumericUpDown _sdCase = new() { DecimalPlaces = 3, Increment = 0.01M, Maximum = 200, Value = 0M, Width = 100 };
    private readonly NumericUpDown _sdBbto = new() { DecimalPlaces = 3, Increment = 0.01M, Maximum = 100, Value = 0M, Width = 100 };
    private readonly Label _sdOut = new() { AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly Button _sdWrite = new() { Text = "Write seating depth to GRT load", AutoSize = true, Enabled = false };
    private BrassCalc.SeatingResult? _sdResult;
    private bool _sdInInch;
    private bool _suppressSdUnit;

    public BrassForm(GrtClient? grt)
    {
        _grt = grt;
        Text = AppVersion.Title("GRT Brass Prep");
        Width = 640; Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 400);
        Build();
        NudFix.ApplyTo(this);
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT)";
        RecalcNeck();
        RecalcSeating();
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCaseVolTab());
        tabs.TabPages.Add(BuildSeatingTab());
        tabs.TabPages.Add(BuildNeckTab());
        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);
        Controls.Add(tabs);
        Controls.Add(_status);
    }

    // ---- case volume -----------------------------------------------------

    private TabPage BuildCaseVolTab()
    {
        var page = new TabPage("Case volume");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 0, 0) };
        var addRow = new Button { Text = "Add 5 rows", AutoSize = true };
        addRow.Click += (_, _) => { for (int i = 0; i < 5; i++) _cv.Rows.Add(); };
        var clr = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        clr.Click += (_, _) => { _cv.Rows.Clear(); Recalc(); };
        _cvUnit.SelectedIndex = 0;
        _cvUnit.SelectedIndexChanged += (_, _) => { SetCvHeaders(); Recalc(); };
        bar.Controls.AddRange(new Control[] { addRow, clr, new Label { Text = "  water weighed in:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) }, _cvUnit });

        _cv.Dock = DockStyle.Fill;
        _cv.AllowUserToAddRows = true;
        _cv.RowHeadersVisible = false;
        _cv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _cv.Columns.Add("empty", "Empty case");
        _cv.Columns.Add("full", "Full w/ water");
        _cv.Columns.Add("water", "→ water");
        _cv.Columns["water"].ReadOnly = true;
        _cv.CellEndEdit += (_, _) => Recalc();
        _cv.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != _cv.Columns["water"].Index) BeginInvoke(new Action(Recalc)); };
        SetCvHeaders();
        for (int i = 0; i < 5; i++) _cv.Rows.Add();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(8, 6, 8, 6) };
        _cvOut.Location = new Point(8, 6);
        _cvWrite.Location = new Point(8, 40);
        _cvWrite.Click += async (_, _) => await WriteCaseVolAsync();
        bottom.Controls.Add(_cvOut);
        bottom.Controls.Add(_cvWrite);

        page.Controls.Add(_cv);
        page.Controls.Add(bottom);
        page.Controls.Add(bar);
        return page;
    }

    private bool CvInGrains => _cvUnit.SelectedIndex == 0;

    private void SetCvHeaders()
    {
        string u = CvInGrains ? "gr" : "g";
        _cv.Columns["empty"].HeaderText = $"Empty case ({u})";
        _cv.Columns["full"].HeaderText = $"Full w/ water ({u})";
        _cv.Columns["water"].HeaderText = $"→ water ({u})";
    }

    private void Recalc()
    {
        var waterGrains = new List<double>();
        foreach (DataGridViewRow row in _cv.Rows)
        {
            if (row.IsNewRow) continue;
            double? e = D(row.Cells["empty"].Value), f = D(row.Cells["full"].Value), w = D(row.Cells["water"].Value);
            double? wIn = (e is { } ev && f is { } fv && fv > ev) ? fv - ev : w;
            row.Cells["water"].Value = wIn is { } x ? x.ToString("0.000", CultureInfo.InvariantCulture) : "";
            if (wIn is { } y && y > 0)
                waterGrains.Add(CvInGrains ? y : BrassCalc.GramsToGrains(y));
        }
        _cvResult = BrassCalc.CaseVolume(waterGrains);
        var r = _cvResult;
        _cvOut.Text = r.N == 0
            ? "enter empty + full weights (or the water weight directly) for 3+ cases"
            : string.Format(CultureInfo.InvariantCulture,
                "n={0}   mean case volume = {1:0.00} grain H2O   ({2:0.000} cm3)   SD {3:0.00} gr ({4:0.0}%)   range {5:0.00}-{6:0.00} gr",
                r.N, r.MeanGr, r.MeanCm3, r.SdGr, r.SdPct, r.MinGr, r.MaxGr);
        _cvWrite.Enabled = r.N >= 1 && _grt is { Connected: true };
    }

    private async Task WriteCaseVolAsync()
    {
        try
        {
            if (_cvResult is null || _grt is not { Connected: true }) return;
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file — switch to your real load.", "Case volume", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix("Case Volume");
            doc.AddNote("Case Volume", CaseVolReport());

            // GRT stores casevol internally in cm3 (see the input's unit attr); write cm3 and pin the unit.
            string existingUnit = doc.InputUnit("caliber", "casevol");
            bool asGrains = existingUnit.StartsWith("gr", StringComparison.OrdinalIgnoreCase);
            double val = asGrains ? _cvResult.MeanGr : _cvResult.MeanCm3;
            string unit = asGrains ? existingUnit : "cm3";
            if (!doc.SetInput("caliber", "casevol", val.ToString("0.#########", CultureInfo.InvariantCulture), unit))
                _status.Text = "note written, but no 'casevol' input in the load";
            string outPath = doc.SaveSibling("casevol");
            await _grt.LoadFileAsync(outPath);
            _status.Text = string.Format(CultureInfo.InvariantCulture, "casevol = mean {0:0.00} gr H2O ({1:0.000} cm3, unit={2}) written and opened in GRT.",
                _cvResult.MeanGr, _cvResult.MeanCm3, unit);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Case volume", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string CaseVolReport()
    {
        var r = _cvResult!;
        return string.Format(CultureInfo.InvariantCulture,
            "Case Volume {0:yyyy-MM-dd}\n" + new string('-', 24) + "\n\n" +
            "cases measured : {1}\n" +
            "mean volume    : {2:0.00} grain H2O   ({3:0.000} cm3)   <- written to casevol\n" +
            "SD             : {4:0.00} gr   ({5:0.0} %)\n" +
            "range          : {6:0.00} - {7:0.00} grain H2O\n\n" +
            "Water density 0.99821 g/cm3 (20 C). GRT stores casevol in cm3 internally.\n" +
            "Trim + uniform prep, fired & de-primed brass, before measuring.\n",
            DateTime.Now, r.N, r.MeanGr, r.MeanCm3, r.SdGr, r.SdPct, r.MinGr, r.MaxGr);
    }

    // ---- seating depth from comparator measurements --------------------

    private TabPage BuildSeatingTab()
    {
        var page = new TabPage("Seating depth");
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };
        void Row(string l, Control c) { t.Controls.Add(new Label { Text = l, AutoSize = true, Padding = new Padding(0, 5, 8, 0) }); t.Controls.Add(c); }
        _sdUnit.SelectedIndex = 0;
        _sdUnit.SelectedIndexChanged += (_, _) => SwitchSdUnit();
        Row("Working units", _sdUnit);
        Row("CBTO  (loaded round, base -> ogive)", _sdCbto);
        Row("Case length  (L3 / CL, trimmed)", _sdCase);
        Row("BBTO  (bare bullet, base -> ogive)", _sdBbto);
        foreach (var n in new[] { _sdCbto, _sdCase, _sdBbto }) n.ValueChanged += (_, _) => RecalcSeating();

        var help = new Label
        {
            Dock = DockStyle.Bottom, Height = 66, ForeColor = SystemColors.GrayText, Padding = new Padding(10, 4, 10, 4),
            Text = "GRT seating depth = distance from the bullet base to the case mouth.\n" +
                   "DIFF = CBTO - case length ;  seating depth = BBTO - DIFF.\n" +
                   "Measure CBTO and BBTO to the SAME comparator / ogive insert.",
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(8, 6, 8, 6) };
        _sdOut.Location = new Point(10, 4);
        _sdWrite.Location = new Point(10, 62);
        _sdWrite.Click += async (_, _) => await WriteSeatingAsync();
        bottom.Controls.Add(_sdOut);
        bottom.Controls.Add(_sdWrite);

        var box = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(box);
        page.Controls.Add(bottom);
        page.Controls.Add(help);
        page.Controls.Add(t);
        return page;
    }

    private bool SdInInch => _sdUnit.SelectedIndex == 1;

    private void SwitchSdUnit()
    {
        if (SdInInch == _sdInInch) return;
        _suppressSdUnit = true;
        bool toInch = SdInInch;
        decimal f = toInch ? 1m / (decimal)BrassCalc.MmPerInch : (decimal)BrassCalc.MmPerInch;
        foreach (var n in new[] { _sdCbto, _sdCase, _sdBbto })
        {
            // read first: lowering Maximum clamps Value on the spot, so converting
            // afterwards would convert the clamped number, not what the user typed.
            decimal v = n.Value;
            n.Maximum = toInch ? 8m : 200m;
            n.DecimalPlaces = toInch ? 4 : 3;
            n.Increment = toInch ? 0.0005M : 0.01M;
            n.Value = Math.Round(Math.Clamp(v * f, n.Minimum, n.Maximum), n.DecimalPlaces);
        }
        _sdInInch = toInch;
        _suppressSdUnit = false;
        RecalcSeating();
    }

    private void RecalcSeating()
    {
        if (_suppressSdUnit) return;
        double toMm = _sdInInch ? BrassCalc.MmPerInch : 1.0;
        double cbto = (double)_sdCbto.Value * toMm, cl = (double)_sdCase.Value * toMm, bbto = (double)_sdBbto.Value * toMm;
        if (cbto <= 0 || cl <= 0 || bbto <= 0)
        {
            _sdResult = null;
            _sdOut.Text = "enter CBTO, case length and BBTO";
            _sdWrite.Enabled = false;
            return;
        }
        _sdResult = BrassCalc.Seating(cbto, cl, bbto);
        var r = _sdResult;
        string U(double mm) => _sdInInch
            ? (mm / BrassCalc.MmPerInch).ToString("0.0000", CultureInfo.InvariantCulture) + " in  (" + mm.ToString("0.000", CultureInfo.InvariantCulture) + " mm)"
            : mm.ToString("0.000", CultureInfo.InvariantCulture) + " mm  (" + (mm / BrassCalc.MmPerInch).ToString("0.0000", CultureInfo.InvariantCulture) + " in)";
        var sb = new System.Text.StringBuilder();
        sb.Append("DIFF (ogive above case mouth) : ").Append(U(r.DiffMm)).Append('\n');
        sb.Append("SEATING DEPTH (GRT gdepth)    : ").Append(U(r.SeatingDepthMm));
        foreach (var note in r.Notes) sb.Append("\n  ! ").Append(note);
        _sdOut.Text = sb.ToString();
        _sdWrite.Enabled = r.SeatingDepthMm > 0 && _grt is { Connected: true };
    }

    private async Task WriteSeatingAsync()
    {
        try
        {
            if (_sdResult is null || _grt is not { Connected: true }) return;
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file - switch to your real load.", "Seating depth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix("Seating Depth (geometry)");
            doc.AddNote("Seating Depth (geometry)", SeatingReport());
            if (!doc.SetInput("projectile", "gdepth", _sdResult.SeatingDepthMm.ToString("0.####", CultureInfo.InvariantCulture), "mm"))
                _status.Text = "note written, but no 'gdepth' input in the load";
            string outPath = doc.SaveSibling("seatdepth");
            await _grt.LoadFileAsync(outPath);
            _status.Text = string.Format(CultureInfo.InvariantCulture, "gdepth {0:0.###} mm written and opened in GRT.", _sdResult.SeatingDepthMm);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Seating depth", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string SeatingReport()
    {
        var r = _sdResult!;
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Format(CultureInfo.InvariantCulture,
            "Seating Depth (geometry) {0:yyyy-MM-dd}\n" + new string('-', 24) + "\n\n" +
            "CBTO - case length = DIFF        : {1:0.###} mm ({2:0.0000} in)\n" +
            "seating depth = BBTO - DIFF      : {3:0.###} mm ({4:0.0000} in)   <- written to gdepth\n\n" +
            "GRT seating depth = bullet base to case mouth.\n",
            DateTime.Now, r.DiffMm, r.DiffIn, r.SeatingDepthMm, r.SeatingDepthIn));
        foreach (var n in r.Notes) sb.Append("! ").Append(n).Append('\n');
        return sb.ToString();
    }

    // ---- neck tension ---------------------------------------------------

    private TabPage BuildNeckTab()
    {
        var page = new TabPage("Neck / bushing");
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Padding = new Padding(10) };
        void Row(string l, Control c, Control? c3 = null)
        {
            t.Controls.Add(new Label { Text = l, AutoSize = true, Padding = new Padding(0, 5, 8, 0) });
            t.Controls.Add(c);
            t.Controls.Add(c3 ?? new Label { Text = "", AutoSize = true });
        }
        _neckUnit.SelectedIndex = 0;
        _neckUnit.SelectedIndexChanged += (_, _) => SwitchNeckUnit();
        Row("Working units", _neckUnit);
        Row("Bullet diameter", _bulletDia);
        Row("Neck wall thickness", _wall);
        Row("Desired interference (grip)", _interf);
        Row("Measured loaded neck OD  (0 = estimate)", _loadedOd);
        foreach (var n in new[] { _bulletDia, _wall, _interf, _loadedOd }) n.ValueChanged += (_, _) => RecalcNeck();

        var help = new Label
        {
            Dock = DockStyle.Bottom, Height = 60, ForeColor = SystemColors.GrayText, Padding = new Padding(10, 4, 10, 4),
            Text = "0.002\" (~0.05 mm) is a common target grip. Bushing dies: order 2-3 bushings around the value. " +
                   "A mandrel as the last step sets the ID directly and evens out wall runout.",
        };
        _neckOut.Location = new Point(12, 4);
        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        box.Controls.Add(_neckOut);

        page.Controls.Add(box);
        page.Controls.Add(help);
        page.Controls.Add(t);
        return page;
    }

    private void SwitchNeckUnit()
    {
        bool toInch = _neckUnit.SelectedIndex == 0;
        if (toInch == _neckInInch) return;
        _suppressNeckUnit = true;
        decimal f = toInch ? 1m / (decimal)MmPerIn : (decimal)MmPerIn;
        foreach (var n in new[] { _bulletDia, _wall, _interf, _loadedOd })
        {
            // read first: lowering Maximum clamps Value on the spot, so converting
            // afterwards would convert the clamped number, not what the user typed.
            decimal v = n.Value;
            n.Maximum = toInch ? 2m : 60m;                    // generous; exact value not important
            n.DecimalPlaces = toInch ? 4 : 3;
            n.Increment = toInch ? 0.0005M : 0.001M;
            n.Value = Math.Round(Math.Clamp(v * f, n.Minimum, n.Maximum), n.DecimalPlaces);
        }
        _neckInInch = toInch;
        _suppressNeckUnit = false;
        RecalcNeck();
    }

    private void RecalcNeck()
    {
        if (_suppressNeckUnit) return;
        double toMm = _neckInInch ? MmPerIn : 1.0;
        double dia = (double)_bulletDia.Value * toMm, wall = (double)_wall.Value * toMm, interf = (double)_interf.Value * toMm;
        double? loaded = _loadedOd.Value > 0 ? (double)_loadedOd.Value * toMm : null;
        var r = BrassCalc.Neck(dia, wall, interf, loaded);

        string U(double mm) => _neckInInch
            ? (mm / MmPerIn).ToString("0.0000", CultureInfo.InvariantCulture) + " in  (" + mm.ToString("0.000", CultureInfo.InvariantCulture) + " mm)"
            : mm.ToString("0.000", CultureInfo.InvariantCulture) + " mm  (" + (mm / MmPerIn).ToString("0.0000", CultureInfo.InvariantCulture) + " in)";
        string step = _neckInInch ? "0.001 in" : "0.025 mm";
        double d = _neckInInch ? MmPerIn * 0.001 : 0.025;

        _neckOut.Text = string.Format(CultureInfo.InvariantCulture,
            "loaded neck OD    : {0}{1}\n\n" +
            "BUSHING die OD    : {2}\n" +
            "  also try        : {3}  and  {4}   (+/- {5})\n\n" +
            "MANDREL / exp. OD : {6}\n" +
            "  final neck ID   = bullet dia - interference\n",
            U(r.LoadedNeckOdMm), r.Notes.Length > 0 ? "   - " + r.Notes : "",
            U(r.BushingOdMm),
            U(r.BushingOdMm - d), U(r.BushingOdMm + d), step,
            U(r.MandrelOdMm));
    }

    private static double? D(object? v) => GrtPluginKit.Util.Str.ParseNumber(Convert.ToString(v, CultureInfo.InvariantCulture));
}
