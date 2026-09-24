using System.Drawing.Printing;
using System.Globalization;
using GrtReloadingToolkit.Targets;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Printable OCW / Ladder-Audette targets — the "before the range" counterpart to the Ladder/OCW
/// Analyzer, which only reads a group back afterward. No GRT connection needed: this is pure prep,
/// same standalone shape as <see cref="SeatingForceForm"/>. See <see cref="TargetRenderer"/> for the
/// actual drawing and for why its printed text is Italian regardless of OS locale.
/// </summary>
internal sealed class TargetForm : Form
{
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly RadioButton _rOcw = new() { Text = Lang.T("OCW (round-robin group per charge)"), Checked = true, AutoSize = true };
    private readonly RadioButton _rLadder = new() { Text = Lang.T("Ladder / Audette (repeated shot per charge, ~200-300 m)"), AutoSize = true };
    private readonly NumericUpDown _nCharges = new() { Minimum = 3, Maximum = 20, Value = 5, Width = 55 };
    private readonly NumericUpDown _perPage = new() { Minimum = 0, Maximum = 20, Value = 0, Width = 55 };
    private readonly NumericUpDown _shotsPerPoint = new() { Minimum = 1, Maximum = 5, Value = 3, Width = 55 };
    private readonly TextBox _charges = new() { Width = 260, PlaceholderText = "39.0, 39.5, 40.0, 40.5, 41.0" };
    private readonly TextBox _distance = new() { Width = 80, PlaceholderText = "100" };
    private readonly TextBox _firearm = new() { Width = 260 };
    private readonly TextBox _powder = new() { Width = 260 };
    private readonly TextBox _bullet = new() { Width = 260 };
    private readonly NumericUpDown _previewPage = new() { Minimum = 1, Maximum = 1, Value = 1, Width = 55 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public TargetForm()
    {
        Text = AppVersion.Title(Lang.T("Print Ladder/OCW Target"));
        Width = 980; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 520);
        Build();
        NudFix.ApplyTo(this);
        Render();
    }

    private void Build()
    {
        var right = new TableLayoutPanel { Dock = DockStyle.Right, Width = 300, ColumnCount = 1, Padding = new Padding(8), AutoScroll = true };
        void Add(Control c) { right.Controls.Add(c); right.RowCount++; right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }

        Add(new Label { Text = Lang.T("Target type"), AutoSize = true, Margin = new Padding(0, 4, 0, 2) });
        _rOcw.CheckedChanged += (_, _) => RenderFromStart();
        Add(_rOcw);
        Add(_rLadder);

        var countsPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        countsPanel.Controls.Add(new Label { Text = Lang.T("Number of charges"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _nCharges.ValueChanged += (_, _) => RenderFromStart();
        countsPanel.Controls.Add(_nCharges);
        countsPanel.Controls.Add(new Label { Text = Lang.T("per page (0 = all)"), AutoSize = true, Margin = new Padding(10, 6, 4, 0) });
        _perPage.ValueChanged += (_, _) => RenderFromStart();
        countsPanel.Controls.Add(_perPage);
        Add(countsPanel);

        var shotsPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        shotsPanel.Controls.Add(new Label { Text = Lang.T("Shots per aim point (1-5)"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _shotsPerPoint.ValueChanged += (_, _) => Render();
        shotsPanel.Controls.Add(_shotsPerPoint);
        Add(shotsPanel);

        Add(new Label { Text = Lang.T("Charge weights (gr, comma-separated, optional)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        _charges.TextChanged += (_, _) => Render();
        Add(_charges);

        Add(new Label { Text = Lang.T("Distance (m, optional)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        _distance.TextChanged += (_, _) => Render();
        Add(_distance);

        Add(new Label { Text = Lang.T("Firearm (optional)"), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        _firearm.TextChanged += (_, _) => Render();
        Add(_firearm);
        Add(new Label { Text = Lang.T("Powder (optional)"), AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        _powder.TextChanged += (_, _) => Render();
        Add(_powder);
        Add(new Label { Text = Lang.T("Bullet (optional)"), AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        _bullet.TextChanged += (_, _) => Render();
        Add(_bullet);

        var pagePanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        pagePanel.Controls.Add(new Label { Text = Lang.T("Preview page"), AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _previewPage.ValueChanged += (_, _) => Render();
        pagePanel.Controls.Add(_previewPage);
        Add(pagePanel);

        var note = new Label
        {
            AutoSize = false, Height = 90, Margin = new Padding(0, 8, 0, 0),
            ForeColor = SystemColors.GrayText,
            Text = Lang.T("Needs A3 paper. Print at 100% / \"actual size\", never \"fit to page\". After printing, measure the 100 mm bar at the bottom with a ruler before shooting."),
        };
        Add(note);

        var printBtn = new Button { Text = Lang.T("Print…"), AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        printBtn.Click += (_, _) => Print();
        Add(printBtn);
        var pdfBtn = new Button { Text = Lang.T("Save PDF…"), AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        pdfBtn.Click += (_, _) => SavePdf();
        Add(pdfBtn);
        var pngBtn = new Button { Text = Lang.T("Save PNG…"), AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        pngBtn.Click += (_, _) => SavePng();
        Add(pngBtn);

        _status.Margin = new Padding(0, 10, 0, 0);
        Add(_status);

        Controls.Add(_preview);
        Controls.Add(right);
    }

    private TargetSpec BuildSpec()
    {
        var charges = _charges.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        double? distance = double.TryParse(_distance.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dm) ? dm : null;
        return new TargetSpec
        {
            Kind = _rOcw.Checked ? TargetKind.Ocw : TargetKind.Ladder,
            NCharges = (int)_nCharges.Value,
            ChargesGr = charges.Count > 0 ? charges : null,
            PerPage = (int)_perPage.Value,
            ShotsPerPoint = (int)_shotsPerPoint.Value,
            DistanceM = distance,
            Firearm = _firearm.Text,
            Powder = _powder.Text,
            Bullet = _bullet.Text,
        };
    }

    private Bitmap RenderBitmap(TargetSpec spec, int pageIndex)
    {
        const float dpi = 150f;
        float unitsPerMm = dpi / 25.4f;
        // A3 landscape, not A4: the OCW grid plus header/instructions need more than A4's 210 mm of
        // height to lay out without the header colliding with the grid -- see TargetRenderer.Draw's
        // own rowY comment. Print() below sets the same expectation via PaperSize.
        int wPx = (int)(420 / 25.4 * dpi);
        int hPx = (int)(297 / 25.4 * dpi);
        var bmp = new Bitmap(wPx, hPx);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        // The full page, not a pre-inset rectangle: TargetRenderer owns its own margins internally
        // (matching the reference tool's own layout exactly), the same way Print() below hands it
        // e.PageBounds rather than e.MarginBounds.
        TargetRenderer.Draw(g, new RectangleF(0, 0, wPx, hPx), spec, unitsPerMm, pageIndex);
        return bmp;
    }

    /// <summary>Charge count or per-page changed: the page count may have shrunk under the current
    /// preview page, so clamp it back before re-rendering rather than asking TargetRenderer to draw a
    /// page index that no longer exists.</summary>
    private void RenderFromStart()
    {
        int pages = TargetRenderer.PageCount(BuildSpec());
        _previewPage.Maximum = pages;
        if (_previewPage.Value > pages) _previewPage.Value = pages;
        Render();
    }

    private void Render()
    {
        try
        {
            var spec = BuildSpec();
            int pages = TargetRenderer.PageCount(spec);
            _previewPage.Maximum = pages;
            _preview.Image?.Dispose();
            _preview.Image = RenderBitmap(spec, (int)_previewPage.Value - 1);
            _status.Text = pages > 1 ? string.Format(Lang.T("Page {0} of {1}."), _previewPage.Value, pages) : "";
        }
        catch (Exception ex) { _status.Text = Lang.T("render error:") + " " + ex.Message; }
    }

    private void SavePng()
    {
        var spec = BuildSpec();
        int pageIndex = (int)_previewPage.Value - 1;
        int pages = TargetRenderer.PageCount(spec);
        string suffix = pages > 1 ? $"_p{pageIndex + 1}of{pages}" : "";
        using var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = (_rOcw.Checked ? "ocw_target" : "ladder_target") + suffix + ".png" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        using var bmp = RenderBitmap(spec, pageIndex);
        bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _status.Text = Lang.T("saved") + " " + Path.GetFileName(d.FileName);
    }

    private void Print()
    {
        var spec = BuildSpec();
        using var doc = new PrintDocument();
        doc.DefaultPageSettings.Landscape = true;
        // Best-effort A3 -- see RenderBitmap's comment on why A4 doesn't have enough height. Not
        // every printer's PaperSizes collection lists one (a printer without A3 loaded, or one
        // that simply doesn't support it); when it doesn't, warn plainly instead of silently
        // printing at whatever page size that printer defaults to -- a live print at the wrong
        // (smaller) size is what produced an overlapping, broken printout that looked nothing like
        // this same TargetRenderer.Draw's always-correct on-screen preview (RenderBitmap always
        // renders a true A3 bitmap, independent of any printer). "Save PDF…" below always gets a
        // true A3 page, since it targets the PDF virtual printer directly rather than whichever
        // printer happens to be selected here.
        PaperSize? a3 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(sz => sz.Kind == PaperKind.A3);
        if (a3 != null)
        {
            doc.DefaultPageSettings.PaperSize = a3;
        }
        else
        {
            var choice = MessageBox.Show(this,
                Lang.T("The selected printer has no A3 paper size, so printing now would come out at the wrong scale with overlapping text. Use \"Save PDF…\" instead (always true A3), or pick a printer that supports A3. Continue anyway?"),
                Lang.T("Print…"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes) return;
        }

        int pageIndex = 0;
        doc.BeginPrint += (_, _) => pageIndex = 0;
        doc.PrintPage += (_, e) =>
        {
            // .NET's printing Graphics is in hundredths of an inch, so 1 mm = 100/25.4 units --
            // drawing directly in this unit is what makes the printed page true 1:1, not a bitmap
            // scaled to fit.
            float unitsPerMm = 100f / 25.4f;
            TargetRenderer.Draw(e.Graphics!, e.PageBounds, spec, unitsPerMm, pageIndex);
            pageIndex++;
            e.HasMorePages = pageIndex < TargetRenderer.PageCount(spec);
        };
        using var pv = new PrintPreviewDialog { Document = doc, Width = 900, Height = 700 };
        pv.ShowDialog(this);
    }

    /// <summary>Renders straight to a real PDF file at a guaranteed true A3 page, the same way
    /// Ballistic Lab v2's own reportlab output was a page-size-independent file rather than
    /// something negotiated with whatever printer happened to be selected. Uses Windows' built-in
    /// "Microsoft Print to PDF" virtual printer -- being virtual, its PaperSizes always includes the
    /// standard ISO sizes, unlike an arbitrary physical printer (see Print's own best-effort A3
    /// lookup above), so this is the reliable path when a live print comes out wrong.</summary>
    private void SavePdf()
    {
        const string pdfPrinterName = "Microsoft Print to PDF";
        if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(pdfPrinterName, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                Lang.T("\"Microsoft Print to PDF\" is not installed on this PC. Use Print… instead and choose a printer that supports A3 paper."),
                Lang.T("Save PDF…"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var spec = BuildSpec();
        using var d = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = (_rOcw.Checked ? "ocw_target" : "ladder_target") + ".pdf" };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = pdfPrinterName;
        doc.PrinterSettings.PrintToFile = true;
        doc.PrinterSettings.PrintFileName = d.FileName;
        doc.DefaultPageSettings.Landscape = true;
        PaperSize? a3 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(sz => sz.Kind == PaperKind.A3);
        if (a3 != null) doc.DefaultPageSettings.PaperSize = a3;

        int pageIndex = 0;
        doc.BeginPrint += (_, _) => pageIndex = 0;
        doc.PrintPage += (_, e) =>
        {
            float unitsPerMm = 100f / 25.4f;
            TargetRenderer.Draw(e.Graphics!, e.PageBounds, spec, unitsPerMm, pageIndex);
            pageIndex++;
            e.HasMorePages = pageIndex < TargetRenderer.PageCount(spec);
        };

        try
        {
            doc.Print();
            _status.Text = Lang.T("saved") + " " + Path.GetFileName(d.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Save PDF…"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
