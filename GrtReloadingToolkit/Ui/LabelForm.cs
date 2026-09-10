using System.Drawing.Printing;
using System.Globalization;
using GrtReloadingToolkit.Cards;
using GrtReloadingToolkit.Log;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>Printable recipe card / ammo-box label for the load open in GRT, with a QR of the full recipe.</summary>
internal sealed class LabelForm : Form
{
    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly LoadCard _card = new();
    private List<Component> _components;
    private bool _suppressCombo;

    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly RadioButton _rCard = new() { Text = "Recipe card (A6)", Checked = true, AutoSize = true };
    private readonly RadioButton _rLabel = new() { Text = "Box labels", AutoSize = true };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 60, Value = 12, Width = 50 };
    private readonly PropertyGrid _props = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = false };
    private readonly ComboBox _powder = Combo(), _primer = Combo(), _brass = Combo(), _bullet = Combo();
    private readonly Label _status = new();

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    public LabelForm(GrtClient? grt, Db db)
    {
        _grt = grt;
        _db = db;
        _components = db.Components();
        Text = AppVersion.Title("GRT Load Card / Label");
        Width = 940; Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Build();
        NudFix.ApplyTo(this);
        Activated += (_, _) => RefreshComponents();
        Render();
        _status.Text = _grt is { Connected: true } ? $"connected to GRT :{_grt.Port}" : "stand-alone (no GRT)";
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0), WrapContents = true };
        var load = new Button { Text = "Load from GRT", AutoSize = true };
        load.Click += async (_, _) => await LoadAsync();
        top.Controls.Add(load);
        top.Controls.Add(_rCard); _rCard.Margin = new Padding(14, 6, 0, 0);
        top.Controls.Add(_rLabel); _rLabel.Margin = new Padding(8, 6, 0, 0);
        top.Controls.Add(new Label { Text = "count", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        top.Controls.Add(_count);
        _rCard.CheckedChanged += (_, _) => Render();
        _count.ValueChanged += (_, _) => Render();

        var right = new TableLayoutPanel { Dock = DockStyle.Right, Width = 300, ColumnCount = 2, Padding = new Padding(6), AutoScroll = true };
        void Row(string l, Control c) { right.Controls.Add(new Label { Text = l, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 6, 0) }); right.Controls.Add(c); }
        FillCombo(_powder, ComponentKind.Powder);
        FillCombo(_primer, ComponentKind.Primer);
        FillCombo(_brass, ComponentKind.Brass);
        FillCombo(_bullet, ComponentKind.Bullet);
        foreach (var cb in new[] { _powder, _primer, _brass, _bullet })
            cb.SelectedIndexChanged += (_, _) => { if (!_suppressCombo) { ApplyComponents(); Render(); } };
        Row("Powder lot", _powder);
        Row("Primer lot", _primer);
        Row("Brass lot", _brass);
        Row("Bullet lot", _bullet);
        _props.SelectedObject = _card;
        _props.PropertyValueChanged += (_, _) => Render();
        right.Controls.Add(_props);
        right.SetColumnSpan(_props, 2);
        _props.Height = 320;

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var print = new Button { Text = "Print…", AutoSize = true };
        print.Click += (_, _) => Print();
        var png = new Button { Text = "Save PNG…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        png.Click += (_, _) => SavePng();
        var note = new Button { Text = "Write load-sheet note to GRT", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        note.Click += async (_, _) => await WriteLoadSheetNoteAsync();
        bottom.Controls.Add(print);
        bottom.Controls.Add(png);
        bottom.Controls.Add(note);

        _status.Dock = DockStyle.Bottom; _status.Height = 20; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 2, 0, 0);

        Controls.Add(_preview);
        Controls.Add(right);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private (ComponentKind kind, ComboBox box)[] Slots => new[]
    {
        (ComponentKind.Powder, _powder),
        (ComponentKind.Primer, _primer),
        (ComponentKind.Brass,  _brass),
        (ComponentKind.Bullet, _bullet),
    };

    private void FillCombo(ComboBox cb, ComponentKind k)
    {
        cb.Items.Clear();
        cb.Items.Add("— none —");
        foreach (var c in _components.Where(x => x.Kind == k)) cb.Items.Add(c.Display);
        cb.SelectedIndex = 0;
    }

    /// <summary>
    /// Re-reads the component list, so a lot added or renamed in the Inventory window while this
    /// one is open actually shows up in the drop-downs. The combos hold display strings indexed
    /// into a kind-filtered slice of the list, so they have to be rebuilt wholesale; the current
    /// selection is carried across by component id, not by index.
    /// </summary>
    private void RefreshComponents()
    {
        var fresh = _db.Components();
        if (fresh.Count == _components.Count
            && fresh.Zip(_components).All(p => p.First.Id == p.Second.Id && p.First.Display == p.Second.Display))
            return;

        var slots = Slots;
        var keep = slots.Select(s => Sel(s.box, s.kind)?.Id).ToList();
        _components = fresh;

        _suppressCombo = true;
        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var (kind, cb) = slots[i];
                FillCombo(cb, kind);
                if (keep[i] is not { } id) continue;
                int ix = _components.Where(x => x.Kind == kind).ToList().FindIndex(x => x.Id == id);
                if (ix >= 0) cb.SelectedIndex = ix + 1;
            }
        }
        finally { _suppressCombo = false; }

        ApplyComponents();
        Render();
    }

    private Component? Sel(ComboBox cb, ComponentKind k)
        => cb.SelectedIndex <= 0 ? null : _components.Where(x => x.Kind == k).ElementAtOrDefault(cb.SelectedIndex - 1);

    private void ApplyComponents()
    {
        var pw = Sel(_powder, ComponentKind.Powder);
        var pr = Sel(_primer, ComponentKind.Primer);
        var br = Sel(_brass, ComponentKind.Brass);
        var bu = Sel(_bullet, ComponentKind.Bullet);
        if (pw != null) _card.Powder = $"{pw.Brand} {pw.Name}".Trim();
        if (pr != null) _card.Primer = $"{pr.Brand} {pr.Name}".Trim();
        if (br != null) _card.Brass = $"{br.Brand} {br.Name}".Trim();
        if (bu != null) { _card.Bullet = $"{bu.Brand} {bu.Name}".Trim(); _card.BulletGr = bu.BulletWeightGr; }

        var probe = new JournalEntry { PowderId = pw?.Id, PrimerId = pr?.Id, BrassId = br?.Id, BulletId = bu?.Id, ChargeGr = _card.ChargeGr ?? 0, Rounds = 1 };
        var cb = Costing.PerRound(probe, id => _components.FirstOrDefault(c => c.Id == id));
        _card.CostPerRound = cb.PerRound > 0 ? Costing.Format(cb.PerRound, cb.Currency) + " / rd" : "";
        string lot = string.Join(" · ", new[] { pw?.Lot, pr?.Lot, bu?.Lot }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => "lot " + s));
        if (lot.Length > 0) _card.LotNote = lot;
        _props.Refresh();
    }

    private async Task LoadAsync()
    {
        try
        {
            if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            var loaded = LoadCard.FromGrtload(GrtLoadDoc.EffectiveReadPath(top.file));
            foreach (var p in typeof(LoadCard).GetProperties().Where(p => p.CanWrite))
                p.SetValue(_card, p.GetValue(loaded));
            _props.Refresh();
            ApplyComponents();
            Render();
            _status.Text = $"loaded {_card.Caliber}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task WriteLoadSheetNoteAsync()
    {
        try
        {
            if (_grt is not { Connected: true }) { MessageBox.Show(this, "Not connected to GRT."); return; }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { MessageBox.Show(this, "No saved load open in GRT."); return; }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "The active tab is a generated file — switch to your real load in GRT first.", "Load sheet",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix("Load Sheet");
            doc.AddNote("Load Sheet", BuildLoadSheetNote());
            string outPath = doc.SaveSibling("loadsheet");
            await _grt.LoadFileAsync(outPath);
            _status.Text = "load-sheet note written — opened in GRT.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load sheet", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string BuildLoadSheetNote()
    {
        var ci = CultureInfo.InvariantCulture;
        var pw = Sel(_powder, ComponentKind.Powder);
        var pr = Sel(_primer, ComponentKind.Primer);
        var br = Sel(_brass, ComponentKind.Brass);
        var bu = Sel(_bullet, ComponentKind.Bullet);
        var probe = new JournalEntry { PowderId = pw?.Id, PrimerId = pr?.Id, BrassId = br?.Id, BulletId = bu?.Id, ChargeGr = _card.ChargeGr ?? 0, Rounds = 1 };
        var cb = Costing.PerRound(probe, id => _components.FirstOrDefault(c => c.Id == id));

        var sb = new System.Text.StringBuilder();
        sb.Append("Load Sheet ").Append(DateTime.Now.ToString("yyyy-MM-dd", ci)).Append('\n');
        sb.Append(new string('-', 24)).Append("\n\n");
        void L(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) sb.Append(k.PadRight(16)).Append(": ").Append(v).Append('\n'); }
        L("Caliber", _card.Caliber);
        L("Firearm", _card.Firearm);
        string bw = _card.BulletGr is { } bg && bg > 0 ? " " + bg.ToString("0.#", ci) + " gr" : "";
        L("Bullet", (_card.Bullet + bw).Trim());
        L("Powder", _card.Powder);
        L("Charge", _card.ChargeGr is { } g ? g.ToString("0.0#", ci) + " gr" : null);
        L("Primer", _card.Primer);
        L("Brass", _card.Brass);
        L("COAL", _card.CoalMm is { } o ? o.ToString("0.###", ci) + " mm" : null);
        L("CBTO", _card.CbtoMm is { } cbto ? cbto.ToString("0.###", ci) + " mm" : null);
        string mvStr = "";
        if (_card.MvMs is { } mv)
        {
            mvStr = mv.ToString("0", ci) + " m/s";
            if (_card.SdMs is { } sd) mvStr += "  SD " + sd.ToString("0.0", ci);
        }
        L("MV (measured)", mvStr);
        L("Lot", _card.LotNote);

        if (cb.PerRound > 0)
        {
            sb.Append("\ncost per round\n").Append(new string('-', 14)).Append('\n');
            void C(string k, double v) { if (v > 0) sb.Append(k.PadRight(10)).Append(": ").Append(Costing.Format(v, cb.Currency)).Append('\n'); }
            C("powder", cb.Powder);
            C("primer", cb.Primer);
            C("bullet", cb.Bullet);
            C("brass", cb.Brass);
            sb.Append("TOTAL     : ").Append(Costing.Format(cb.PerRound, cb.Currency)).Append(" / round\n");
        }
        return sb.ToString();
    }

    private Bitmap RenderBitmap()
    {
        using var qr = CardRenderer.QrBitmap(_card.QrText());
        if (_rCard.Checked)
        {
            var bmp = new Bitmap(1050, 1480); // A6 @ ~250 dpi
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            CardRenderer.DrawRecipeCard(g, new RectangleF(20, 20, bmp.Width - 40, bmp.Height - 40), _card, qr);
            return bmp;
        }
        else
        {
            int n = (int)_count.Value, cols = 3, rows = (n + cols - 1) / cols;
            int lw = 640, lh = 300, gap = 16;
            var bmp = new Bitmap(cols * lw + (cols + 1) * gap, rows * lh + (rows + 1) * gap);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            for (int i = 0; i < n; i++)
            {
                int r = i / cols, cc = i % cols;
                CardRenderer.DrawBoxLabel(g, new RectangleF(gap + cc * (lw + gap), gap + r * (lh + gap), lw, lh), _card, qr);
            }
            return bmp;
        }
    }

    private void Render()
    {
        try { _preview.Image?.Dispose(); _preview.Image = RenderBitmap(); }
        catch (Exception ex) { _status.Text = "render error: " + ex.Message; }
    }

    private void SavePng()
    {
        using var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = $"{Safe(_card.Caliber)}_{_card.Date}.png" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        using var bmp = RenderBitmap();
        bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _status.Text = "saved " + Path.GetFileName(d.FileName);
    }

    private void Print()
    {
        using var doc = new PrintDocument();
        doc.PrintPage += (_, e) =>
        {
            using var qr = CardRenderer.QrBitmap(_card.QrText());
            var m = e.MarginBounds;
            if (_rCard.Checked)
            {
                // A6 card centred in the page margins
                float w = Math.Min(m.Width, m.Height * 105f / 148f);
                float h = w * 148f / 105f;
                CardRenderer.DrawRecipeCard(e.Graphics!, new RectangleF(m.X + (m.Width - w) / 2, m.Y, w, h), _card, qr);
            }
            else
            {
                int n = (int)_count.Value, cols = 2;
                float lw = m.Width / (float)cols, lh = lw * 40f / 78f;
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols, cc = i % cols;
                    float y = m.Y + r * lh;
                    if (y + lh > m.Bottom) break;
                    CardRenderer.DrawBoxLabel(e.Graphics!, new RectangleF(m.X + cc * lw + 4, y + 4, lw - 8, lh - 8), _card, qr);
                }
            }
        };
        using var pv = new PrintPreviewDialog { Document = doc, Width = 900, Height = 700 };
        pv.ShowDialog(this);
    }

    private static string Safe(string s) => string.Concat((s.Length == 0 ? "load" : s).Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Replace(' ', '_');
}
