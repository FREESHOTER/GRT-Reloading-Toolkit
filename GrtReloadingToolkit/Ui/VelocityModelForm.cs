using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// UI for <see cref="VelocityModel"/> -- an empirical MV(charge, temperature) fit from the journal's
/// own logged data, for one caliber+powder+bullet combo (same combo-scoping as "Find best Ba":
/// mixing bullets or powders would be fitting noise). Deliberately always in GRT's OWN native units
/// -- grains, °C, m/s -- not whatever <see cref="GrtUnits"/> the rest of the toolkit is currently
/// showing: a 2-axis fit's coefficients are only meaningful together with the units they were fitted
/// in, and re-deriving them under a live unit toggle (charge AND temperature both need re-fitting,
/// not just re-labelling) is real added risk for a tool whose whole point is numeric correctness,
/// not a convenience worth it here the way a single converted display value is everywhere else.
///
/// GRT carries no ambient-temperature field on a load (only the Journal does, as manual entry -- see
/// <see cref="Db"/>'s journal schema), so unlike every other "write to GRT load" tool here, this one
/// writes the FIT ITSELF into a note (formula, n, R²), not a prediction for whatever load happens to
/// be open -- there is no load-side temperature to predict from.
/// </summary>
internal sealed class VelocityModelForm : Form
{
    private const string NoteTitle = "Velocity Model";

    private readonly GrtClient? _grt;
    private readonly Db _db;

    private readonly ComboBox _caliber = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _powder = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DisplayMember = "Text", ValueMember = "Id" };
    private readonly ComboBox _bullet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DisplayMember = "Text", ValueMember = "Id" };
    private readonly Label _formula = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly DataGridView _grid = new();

    private readonly NumericUpDown _predChg = new() { DecimalPlaces = 2, Maximum = 200, Width = 70 };
    private readonly NumericUpDown _predTemp = new() { DecimalPlaces = 1, Minimum = -40, Maximum = 140, Width = 70 };
    private readonly Label _predOut = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly NumericUpDown _targetMv = new() { DecimalPlaces = 0, Maximum = 3000, Width = 70 };
    private readonly NumericUpDown _targetTemp = new() { DecimalPlaces = 1, Minimum = -40, Maximum = 140, Width = 70 };
    private readonly Label _targetOut = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly Button _writeBtn = new() { Text = "", AutoSize = true, Margin = new Padding(10, 8, 0, 0), Enabled = false };

    private VelocityModel.Result? _fit;
    private string? _fitCaliber;

    public VelocityModelForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        Text = AppVersion.Title(Lang.T("GRT Velocity Model"));
        Width = 980; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 460);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 6, 0, 0), WrapContents = true };
        Control Group(string label, params Control[] ctls)
        {
            var g = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            g.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            foreach (var c in ctls) { c.Margin = new Padding(0, 0, 4, 0); g.Controls.Add(c); }
            return g;
        }
        bar.Controls.Add(Group(Lang.T("Caliber"), _caliber));
        bar.Controls.Add(Group(Lang.T("Powder"), _powder));
        bar.Controls.Add(Group(Lang.T("Bullet"), _bullet));
        var fitBtn = new Button { Text = Lang.T("Fit"), AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        fitBtn.Click += (_, _) => RunFit();
        bar.Controls.Add(fitBtn);
        _writeBtn.Text = Lang.T("Write model note to GRT load");
        _writeBtn.Click += async (_, _) => await WriteToGrtAsync();
        bar.Controls.Add(_writeBtn);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(6, 0, 6, 6) };
        top.Controls.Add(_formula);

        var predRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
        predRow.Controls.Add(new Label { Text = Lang.T("Predict MV for charge"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        predRow.Controls.Add(_predChg);
        predRow.Controls.Add(new Label { Text = "gr,", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        predRow.Controls.Add(new Label { Text = Lang.T("temperature"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        predRow.Controls.Add(_predTemp);
        predRow.Controls.Add(new Label { Text = "°C", AutoSize = true, Margin = new Padding(4, 6, 8, 0) });
        var predBtn = new Button { Text = Lang.T("Predict"), AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        predBtn.Click += (_, _) => RunPredict();
        predRow.Controls.Add(predBtn);
        predRow.Controls.Add(_predOut);
        top.Controls.Add(predRow);

        var targetRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
        targetRow.Controls.Add(new Label { Text = Lang.T("Charge needed for MV"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        targetRow.Controls.Add(_targetMv);
        targetRow.Controls.Add(new Label { Text = "m/s,", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        targetRow.Controls.Add(new Label { Text = Lang.T("temperature"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        targetRow.Controls.Add(_targetTemp);
        targetRow.Controls.Add(new Label { Text = "°C", AutoSize = true, Margin = new Padding(4, 6, 8, 0) });
        var targetBtn = new Button { Text = Lang.T("Solve"), AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        targetBtn.Click += (_, _) => RunSolveCharge();
        targetRow.Controls.Add(targetBtn);
        targetRow.Controls.Add(_targetOut);
        top.Controls.Add(targetRow);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (n, h) in new[] { ("date", Lang.T("Date")), ("load", Lang.T("Load")),
                     ("chg", Lang.T("Charge") + " gr"), ("temp", Lang.T("Temp") + " °C"),
                     ("mv", "MV m/s"), ("pred", Lang.T("Predicted")), ("resid", Lang.T("Residual")) })
            _grid.Columns.Add(n, h);

        _status.Dock = DockStyle.Bottom; _status.Padding = new Padding(8, 4, 0, 4);

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(top);
        Controls.Add(bar);
        NudFix.ApplyTo(this);

        RefreshFilters();
        RunFit();
    }

    /// <summary>Same convention as "Find best Ba": calibers that have SOMETHING this tool can use --
    /// here, charge+temperature+velocity together, regardless of whether the entry was ever
    /// Ba-calibrated (this model doesn't touch Ba at all).</summary>
    private void RefreshFilters()
    {
        string? keepCal = _caliber.SelectedItem as string;
        _caliber.Items.Clear();
        foreach (var c in _db.VelocityModelCalibers()) _caliber.Items.Add(c);
        if (keepCal != null && _caliber.Items.Contains(keepCal)) _caliber.SelectedItem = keepCal;
        else if (_caliber.Items.Count > 0) _caliber.SelectedIndex = 0;

        var powders = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Powder).ToList();
        var bullets = _db.Components(includeArchived: true).Where(c => c.Kind == ComponentKind.Bullet).ToList();
        FillCombo(_powder, powders);
        FillCombo(_bullet, bullets);
    }

    private static void FillCombo(ComboBox cb, List<Component> items)
    {
        long? keep = (cb.SelectedItem as Item)?.Id;
        cb.Items.Clear();
        cb.Items.Add(new Item(0, Lang.T("-- any --")));
        foreach (var c in items) cb.Items.Add(new Item(c.Id, c.Display));
        cb.SelectedIndex = 0;
        if (keep is { } id)
            for (int i = 0; i < cb.Items.Count; i++)
                if (((Item)cb.Items[i]!).Id == id) { cb.SelectedIndex = i; break; }
    }

    private sealed record Item(long Id, string Text) { public override string ToString() => Text; }

    private void RunFit()
    {
        _fit = null;
        _writeBtn.Enabled = false;
        _grid.Rows.Clear();
        RefreshFilters();

        if (_caliber.SelectedItem is not string caliber)
        {
            _status.Text = Lang.T("No journal entry yet carries charge, temperature AND a measured velocity together -- log a session with all three (Journal -> New, or Log from GRT + fill in temperature) to use this model.");
            _formula.Text = "";
            return;
        }
        _fitCaliber = caliber;
        long? powderId = (_powder.SelectedItem as Item)?.Id is > 0 ? (_powder.SelectedItem as Item)!.Id : null;
        long? bulletId = (_bullet.SelectedItem as Item)?.Id is > 0 ? (_bullet.SelectedItem as Item)!.Id : null;

        var entries = _db.EntriesForVelocityModel(caliber, powderId, bulletId);
        var points = entries.Select(e => (e.ChargeGr, e.TemperatureC!.Value, e.VelocityAvgMs!.Value)).ToList();
        var result = VelocityModel.Fit(points);
        _fit = result;

        foreach (var e in entries)
        {
            double pred = result.Fitted ? VelocityModel.Predict(result, e.ChargeGr, e.TemperatureC!.Value) : double.NaN;
            int i = _grid.Rows.Add(e.Date, e.LoadName,
                e.ChargeGr.ToString("0.00", CultureInfo.InvariantCulture),
                e.TemperatureC!.Value.ToString("0.0", CultureInfo.InvariantCulture),
                e.VelocityAvgMs!.Value.ToString("0.0", CultureInfo.InvariantCulture),
                result.Fitted ? pred.ToString("0.0", CultureInfo.InvariantCulture) : "",
                result.Fitted ? (e.VelocityAvgMs!.Value - pred).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "");
        }

        if (!result.Fitted)
        {
            _formula.Text = "";
            _writeBtn.Enabled = false;
            _status.Text = result.Reason switch
            {
                VelocityModel.UnfitReason.TooFewPoints => string.Format(
                    Lang.T("Only {0} logged session(s) with charge+temperature+velocity for this combo -- need at least {1} to fit a 2-variable model."),
                    result.N, VelocityModel.MinPoints),
                VelocityModel.UnfitReason.NoChargeVariance => Lang.T("Every logged session used the same charge -- nothing to fit a charge effect from. Log sessions at different charges."),
                VelocityModel.UnfitReason.NoTemperatureVariance => Lang.T("Every logged session was at the same temperature -- nothing to fit a temperature effect from. Log sessions at different temperatures."),
                VelocityModel.UnfitReason.Collinear => Lang.T("Charge and temperature moved together across every logged session (e.g. always tested colder at lower charges) -- there's no way to tell the two effects apart from this data."),
                _ => "",
            };
            return;
        }

        _formula.Text = string.Format(Lang.T("MV ≈ {0} + {1}·charge(gr) + {2}·temp(°C)   ·   n={3}   ·   R²={4}"),
            result.Intercept.ToString("0.0", CultureInfo.InvariantCulture),
            result.ChargeCoef.ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
            result.TempCoef.ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
            result.N, result.R2.ToString("0.00", CultureInfo.InvariantCulture));
        _status.Text = string.Format(Lang.T("Fitted on {0} session(s) for {1}."), result.N, caliber);
        _writeBtn.Enabled = _grt is { Connected: true };
    }

    private void RunPredict()
    {
        if (_fit is not { Fitted: true } fit) { _predOut.Text = Lang.T("Fit the model first."); return; }
        double v = VelocityModel.Predict(fit, (double)_predChg.Value, (double)_predTemp.Value);
        _predOut.Text = string.Format(Lang.T("≈ {0} m/s"), v.ToString("0.0", CultureInfo.InvariantCulture));
    }

    private void RunSolveCharge()
    {
        if (_fit is not { Fitted: true } fit) { _targetOut.Text = Lang.T("Fit the model first."); return; }
        double chg = VelocityModel.ChargeForTarget(fit, (double)_targetMv.Value, (double)_targetTemp.Value);
        _targetOut.Text = double.IsNaN(chg)
            ? Lang.T("Charge has no measurable effect in this fit -- can't solve for it.")
            : string.Format(Lang.T("≈ {0} gr"), chg.ToString("0.00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes the fit itself -- not a prediction for the open load, since GRT carries no ambient
    /// temperature to predict from (see the class doc). Same write-back mechanics as every other
    /// tool: timestamped sibling snapshot, original untouched.
    /// </summary>
    private async Task WriteToGrtAsync()
    {
        if (_grt is not { Connected: true } || _fit is not { Fitted: true } fit || _fitCaliber is null) return;
        try
        {
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write model note to GRT load"));
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}");
            report.AppendLine(new string('-', 20));
            report.AppendLine();
            report.AppendLine(string.Format(Lang.T("Empirical fit for {0}, from {1} logged session(s):"), _fitCaliber, fit.N));
            report.AppendLine($"  MV = {fit.Intercept.ToString("0.00", CultureInfo.InvariantCulture)} "
                + $"+ {fit.ChargeCoef.ToString("0.00", CultureInfo.InvariantCulture)} × charge(gr) "
                + $"+ {fit.TempCoef.ToString("0.00", CultureInfo.InvariantCulture)} × temperature(°C)");
            report.AppendLine($"  R² = {fit.R2.ToString("0.000", CultureInfo.InvariantCulture)}");
            report.AppendLine();
            report.AppendLine(Lang.T("Independent of GRT's own model (no Ba/a0/tcc/tch) -- a measured-data cross-check, not a replacement for calibration."));

            var writeDoc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            writeDoc.RemoveByTitlePrefix(NoteTitle);
            writeDoc.AddNote(NoteTitle, report.ToString());
            string outPath = writeDoc.SaveSibling("velocitymodel");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Model note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write model note to GRT load"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
