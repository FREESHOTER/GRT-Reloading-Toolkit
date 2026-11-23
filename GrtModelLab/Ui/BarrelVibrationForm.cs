using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtModelLab.BarrelVibration;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtModelLab.Ui;

/// <summary>
/// Barrel Vibration Model: pick a saved barrel profile and see its natural bending frequencies and
/// mode shapes. Deliberately separate from Barrel Physical Profile — that form is pure data capture
/// (steel, taper) with nothing computed in it; this one reads a saved profile read-only and is the
/// only place V1/V2 actually get run and shown.
///
/// Shows V2 (the real variable-profile FEM) only. V1 (BeamModel's closed-form uniform-cylinder
/// approximation) still exists and is still exercised — it's V2's own correctness check, verified by
/// mesh-convergence in --femtest — but it is never displayed here: two different numbers for "the
/// barrel's frequency" side by side reads as the tool disagreeing with itself, and V2 already
/// supersedes V1 for any barrel with a real taper.
/// </summary>
internal sealed class BarrelVibrationForm : Form
{
    private const double MmPerInch = 25.4;
    private const double GrainsPerGram = 1.0 / 0.06479891;

    // A one-shot compute triggered by picking a profile, not a per-keystroke live one, so a much
    // bigger mesh than the data-capture form's live estimate costs nothing here.
    private const int FemElements = 120;
    // 5, not 3: a rifle's fundamental bending period (tens of ms) is far longer than a real barrel
    // time (~1-2 ms) - the bullet exits long before mode 1 ever reaches its first velocity-zero
    // point, for any realistic charge. Higher modes (4th/5th) have half-periods on the same order as
    // the barrel time itself, which is where a reachable node can actually exist.
    private const int ModeCount = 5;
    private const string NoteTitle = "Modello di Vibrazione Canna";

    private readonly BarrelProfileDb _db;
    private readonly GrtClient? _grt;
    private BarrelProfileRecord? _rec;
    private BeamFem.Mode[]? _modes;
    private NodeTimelineChart.SweepPoint[]? _lastSweep;
    private ObtTimelineChart.ObtPoint[]? _lastObtSweep;
    private (double ChargeGr, double BarrelTimeMs, double ExitVelMmPerMs, double NearestCrossingMs, double? GrtObtMs, string GrtObtNode)[]? _lastSweepRows;
    private double? _lastGenericObtMs;
    private string _lastGenericObtNode = "";

    private bool _inches;

    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _units = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Items = { "metrico (mm)", "imperiale (in)" } };
    private readonly Label _freq = new() { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
    private readonly ComboBox _modeShape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Items = { "1", "2", "3", "4", "5" } };
    private readonly BarrelProfileChart _chart = new();
    private readonly NodeTimelineChart _timeline = new();
    private readonly ObtTimelineChart _obtTimeline = new();
    private readonly Button _sweepBtn = new() { Text = "Scansiona la scala di cariche GRT", AutoSize = true };
    private readonly Label _btLabel = new() { AutoSize = true, Padding = new Padding(10, 4, 0, 0) };
    private readonly DataGridView _corrGrid = new();
    private readonly Button _writeBtn = new() { Text = "Scrivi nota sul caricamento GRT", AutoSize = true };
    private readonly Label _status = new();

    public BarrelVibrationForm(BarrelProfileDb db, GrtClient? grt)
    {
        _db = db;
        _grt = grt;
        Text = "Modello di Vibrazione Canna  (v0.1)";
        Width = 980; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);
        Build();
        _units.SelectedIndex = 0;
        RefreshProfileList();
    }

    private void Build()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 0, 0) };
        top.Controls.Add(new Label { Text = "Profilo", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        top.Controls.Add(_profiles);
        _profiles.SelectedIndexChanged += (_, _) => LoadSelected();
        top.Controls.Add(new Label { Text = "   Forma del modo", AutoSize = true, Padding = new Padding(10, 6, 4, 0) });
        top.Controls.Add(_modeShape);
        _modeShape.SelectedIndex = 0;
        _modeShape.SelectedIndexChanged += (_, _) => UpdateChart();
        top.Controls.Add(new Label { Text = "   Unità", AutoSize = true, Padding = new Padding(10, 6, 4, 0) });
        top.Controls.Add(_units);
        _units.SelectedIndexChanged += (_, _) => { _inches = _units.SelectedIndex == 1; UpdateChart(); UpdateStatusForProfile(); };

        var freqPanel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(8, 6, 0, 0) };
        freqPanel.Controls.Add(_freq);

        // GRT barrel-time correlation: sweeps GRT's own charge ladder (reading it straight from the
        // open load - base charge, step, count - no re-typing) and, for each charge, computes the
        // ACTUAL predicted muzzle transverse velocity at the moment the bullet exits - via modal
        // superposition (Duhamel's integral) driven by GRT's own real P(t)/v(t) curve, not an assumed
        // excitation shape. See ForcedResponse's own class doc for the mechanism (the bullet reacting
        // against the barrel's self-weight sag as it travels down the bore) and its own honestly
        // stated limits (one candidate mechanism among several plausible ones; undamped; no
        // bedding/action stiffness).
        var corrPanel = new Panel { Dock = DockStyle.Top, Height = 220, Padding = new Padding(8, 4, 8, 4) };
        var corrTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, WrapContents = false };
        _sweepBtn.Click += async (_, _) => await SweepGrtAsync();
        corrTop.Controls.Add(_sweepBtn);
        corrTop.Controls.Add(_btLabel);
        var assumption = new Label
        {
            Text = "Scansiona la scala di cariche di GRT e calcola per ogni carica la velocità trasversale reale della volata all'uscita del proiettile, dalla curva P(t)/v(t) reale di GRT (nessuna forma di eccitazione presunta) tramite il cedimento per gravità della canna.\n" +
                   "Questo è un possibile meccanismo di eccitazione candidato, non una misura. Un test OCW/ladder misura quale CARICA raggruppa meglio — se sono in disaccordo, carica quello che mostra il tuo test, non questa tabella.",
            Dock = DockStyle.Top, Height = 52, ForeColor = SystemColors.GrayText, Padding = new Padding(8, 4, 0, 0),
        };
        _corrGrid.Dock = DockStyle.Fill;
        _corrGrid.AllowUserToAddRows = false;
        _corrGrid.ReadOnly = true;
        _corrGrid.RowHeadersVisible = false;
        _corrGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _corrGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "charge", HeaderText = "Carica (gr)" });
        _corrGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "bt", HeaderText = "Tempo di canna (ms)" });
        _corrGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "exitvel", HeaderText = "Velocità alla volata all'uscita (mm/ms)" });
        _corrGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "nearest", HeaderText = "Istante più quieto vicino (ms)" });
        _corrGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "grtobt", HeaderText = "OBT nativo di GRT (ms, nodo)" });
        corrPanel.Controls.Add(_corrGrid);
        corrPanel.Controls.Add(assumption);
        corrPanel.Controls.Add(corrTop);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var chartTab = new TabPage("Canna e forma del modo");
        _chart.Dock = DockStyle.Fill;
        chartTab.Controls.Add(_chart);
        var timelineTab = new TabPage("Carica vs. moto della volata all'uscita");
        _timeline.Dock = DockStyle.Fill;
        timelineTab.Controls.Add(_timeline);
        var obtTab = new TabPage("Tempo di canna vs. OBT nativo di GRT");
        _obtTimeline.Dock = DockStyle.Fill;
        obtTab.Controls.Add(_obtTimeline);
        tabs.TabPages.Add(chartTab);
        tabs.TabPages.Add(timelineTab);
        tabs.TabPages.Add(obtTab);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        _writeBtn.Click += async (_, _) => await WriteNoteAsync();
        bottom.Controls.Add(_writeBtn);

        _status.Dock = DockStyle.Bottom; _status.Height = 22; _status.ForeColor = SystemColors.GrayText; _status.Padding = new Padding(8, 4, 0, 0);

        Controls.Add(tabs);
        Controls.Add(_status);
        Controls.Add(bottom);
        Controls.Add(corrPanel);
        Controls.Add(freqPanel);
        Controls.Add(top);
    }

    // ---- unit helpers --------------------------------------------------
    private double ToDisplay(double mm) => _inches ? mm / MmPerInch : mm;
    private string UnitLabel => _inches ? "in" : "mm";
    private string FormatMm(double mm) => _inches
        ? string.Format(CultureInfo.InvariantCulture, "{0:0.000} in", ToDisplay(mm))
        : string.Format(CultureInfo.InvariantCulture, "{0:0.0} mm", mm);

    /// <summary>Sets a GRT propellant-charge input (grains in, whatever unit the file itself uses
    /// out) - same read-unit/write-same-unit pattern <c>SweepEngine.ApplyVariant</c> uses for bullet
    /// mass, applied here to charge mass ("mc") instead.</summary>
    private static void SetChargeGr(GrtLoadDoc doc, double grains)
    {
        double grams = grains / GrainsPerGram;
        string unit = doc.InputUnit("propellant", "mc");
        bool kg = string.Equals(unit, "kg", StringComparison.OrdinalIgnoreCase);
        double raw = kg ? grams / 1000.0 : grams;
        doc.SetInput("propellant", "mc", raw.ToString("0.######", CultureInfo.InvariantCulture), kg ? "kg" : "g");
    }

    /// <summary>
    /// Sweeps GRT's own charge ladder (base charge/step/count read straight from the currently open
    /// load - see <see cref="GrtLoadDoc.PropellantChargeGr"/>/<c>LadderStepGr</c>/<c>LadderCount</c>)
    /// so the correlation reflects the WHOLE range of charges you could actually load, not just
    /// whichever single charge happened to be on top in GRT. Mirrors
    /// <c>SensitivitySweepForm.RunOneAsync</c>'s temp-file-per-run pattern: each charge gets its own
    /// deterministic (ladder-off) temp file, loaded, read, and discarded; GRT's original tab is
    /// reloaded at the end. For each charge, reads GRT's own real P(t)/v(t) curve (not just the
    /// scalar summary) and runs it through <see cref="ForcedResponse.MuzzleResponse"/> to get this
    /// barrel's own predicted muzzle transverse velocity at the moment the bullet actually exits.
    /// </summary>
    private async Task SweepGrtAsync()
    {
        try
        {
            if (_grt is not { Connected: true })
            {
                MessageBox.Show(this, "Non connesso a GRT.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_rec is null || _modes is null)
            {
                MessageBox.Show(this, "Seleziona prima un profilo con un modello di vibrazione calcolato.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, "Nessun caricamento salvato aperto in GRT.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string basePath = GrtLoadDoc.PristineBasePath(top.file);
            if (!File.Exists(basePath)) basePath = top.file;

            var pristine = GrtLoadDoc.Load(basePath);
            double? bulletMassGr = pristine.BulletMassGr;
            if (bulletMassGr is null)
            {
                MessageBox.Show(this, "Impossibile leggere la massa del proiettile dal caricamento aperto.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            double? mc = pristine.PropellantChargeGr;
            double? step = pristine.LadderStepGr;
            int? count = pristine.LadderCount;
            double[] charges = mc is { } baseGr && step is { } stepGr && stepGr > 0 && count is > 1
                ? Enumerable.Range(0, count.Value).Select(i => baseGr - i * stepGr).OrderBy(c => c).ToArray()
                : mc is { } single ? new[] { single } : throw new InvalidOperationException("Impossibile leggere il peso della carica dal caricamento aperto.");

            if (MessageBox.Show(this,
                    $"Questo aprirà {charges.Length} nuova/e tab in GRT (una per carica da {charges.Min():0.00} a {charges.Max():0.00} grani, chiamate \"vib_...\") per leggere la curva completa di pressione/velocità di ogni sparo, poi riaprirà il tuo caricamento.\n\n" +
                    "L'interfaccia plugin di GRT non permette a un plugin di chiudere una tab, quindi resteranno tutte aperte — chiudile tu (oppure lasciale; sono file temporanei usa-e-getta, sicuri da chiudere senza salvare). Leggere la curva completa per ogni carica è più lento di una lettura rapida di un solo valore — potrebbe richiedere un po' più tempo del solito.\n\nContinuare?",
                    "Scansiona la scala di cariche", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            _sweepBtn.Enabled = false;
            string tmpDir = Path.Combine(Path.GetTempPath(), "grt_vibration_sweep_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            double[] sag = BeamFem.StaticSagMm(_rec, FemElements);
            var points = new List<NodeTimelineChart.SweepPoint>();
            var rows = new List<(double ChargeGr, double BarrelTimeMs, double ExitVelMmPerMs, double NearestCrossingMs, double? GrtObtMs, string GrtObtNode)>();
            double? genericObtMs = null;
            string genericObtNode = "";

            for (int i = 0; i < charges.Length; i++)
            {
                _status.Text = $"Scansione {i + 1}/{charges.Length}: {charges[i]:0.00} gr (lettura curva completa)...";
                var doc = GrtLoadDoc.Load(basePath);
                doc.SetInput("propellant", "laddercnt", "0"); // ladder off - one deterministic charge per run
                SetChargeGr(doc, charges[i]);
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "vib_{0:000}.grtload", i));
                doc.Save(tmp);

                await _grt.LoadFileAsync(tmp);
                await Task.Delay(1400);
                var t2 = await _grt.GetTabOnTopAsync();
                var (res, curve) = await _grt.GetTabResultsWithCurveAsync(t2.handle);
                if (genericObtMs is null && res.OptimalBarrelTimeMs is { } obt) { genericObtMs = obt; genericObtNode = res.OptimalBarrelTimeNode; }
                if (res.BarrelTimeMs is not { } bt || curve.Length < 3) continue;

                var series = ForcedResponse.MuzzleResponse(_modes, sag, _rec.LengthMm, bulletMassGr.Value,
                    curve.Select(c => c.TimeMs).ToArray(), curve.Select(c => c.PositionMm).ToArray(), curve.Select(c => c.VelocityMps).ToArray());
                double exitVelMmPerMs = series.VelocityMmPerMs[^1];
                double[] crossings = ForcedResponse.VelocityZeroCrossingsMs(series);
                double nearestCrossing = crossings.Length > 0
                    ? crossings.OrderBy(t => Math.Abs(t - bt)).First()
                    : double.NaN;

                points.Add(new NodeTimelineChart.SweepPoint(charges[i], exitVelMmPerMs));
                rows.Add((charges[i], bt, exitVelMmPerMs, nearestCrossing, res.OptimalBarrelTimeMs, res.OptimalBarrelTimeNode));
            }

            await _grt.LoadFileAsync(top.file);
            try { Directory.Delete(tmpDir, true); } catch { }

            if (points.Count == 0)
            {
                _status.Text = "Nessun risultato utilizzabile per le cariche scansionate - calcola prima un risultato in GRT, poi riprova.";
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            _lastSweep = points.ToArray();
            _lastSweepRows = rows.ToArray();
            _lastObtSweep = rows.Select(r => new ObtTimelineChart.ObtPoint(r.ChargeGr, r.BarrelTimeMs, r.GrtObtMs, r.GrtObtNode)).ToArray();
            _lastGenericObtMs = genericObtMs;
            _lastGenericObtNode = genericObtNode;

            _corrGrid.Rows.Clear();
            foreach (var r in rows)
                _corrGrid.Rows.Add(
                    r.ChargeGr.ToString("0.00", ci), r.BarrelTimeMs.ToString("0.000", ci),
                    r.ExitVelMmPerMs.ToString("0.000000", ci),
                    double.IsNaN(r.NearestCrossingMs) ? "-" : r.NearestCrossingMs.ToString("0.000", ci),
                    r.GrtObtMs is { } rowObtMs ? string.Format(ci, "{0:0.000} ({1})", rowObtMs, string.IsNullOrEmpty(r.GrtObtNode) ? "?" : r.GrtObtNode) : "-");

            _timeline.Show(_lastSweep);
            _obtTimeline.Show(_lastObtSweep);

            var (best, edgeOfRange) = NodeTimelineChart.FindQuietest(_lastSweep);
            string obtText = genericObtMs is { } obtMs
                ? string.Format(ci, "  |  OBT generico nativo di GRT: {0:0.000} ms (nodo {1})", obtMs, string.IsNullOrEmpty(genericObtNode) ? "?" : genericObtNode)
                : "";
            _btLabel.Text = string.Format(ci, "Scansionate {0} carica/che, {1:0.00}-{2:0.00} gr{3}", points.Count, charges.Min(), charges.Max(), obtText);
            _status.Text = edgeOfRange
                ? string.Format(ci, "{0:0.00} gr è il bordo meno peggio di questo intervallo ({1:0.000000} mm/ms) - la curva non si avvicina mai allo zero tra {2:0.00}-{3:0.00} gr, quindi un vero nodo (se esiste) probabilmente si trova fuori; allarga la scala prima di fidarti di questo. Chiudi le tab extra \"vib_...\" in GRT quando hai finito.",
                    rows[best].ChargeGr, Math.Abs(rows[best].ExitVelMmPerMs), charges.Min(), charges.Max())
                : string.Format(ci, "Più quieta all'uscita: {0:0.00} gr -> velocità trasversale alla volata {1:0.000000} mm/ms al momento dell'uscita. Chiudi le tab extra \"vib_...\" in GRT quando hai finito.",
                    rows[best].ChargeGr, Math.Abs(rows[best].ExitVelMmPerMs));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _sweepBtn.Enabled = true; }
    }

    /// <summary>
    /// Writes V2's frequencies for the currently selected profile (plus the last charge-ladder sweep,
    /// if one has been run) as a note on whatever load GRT currently has on top — same sibling-file
    /// family (<see cref="GrtLoadDoc.SaveSibling"/>) the Reloading Toolkit's Calibration/Chrono notes
    /// use, so all of them accumulate on the same load regardless of which tool wrote them.
    /// </summary>
    private async Task WriteNoteAsync()
    {
        try
        {
            if (_grt is not { Connected: true })
            {
                MessageBox.Show(this, "Non connesso a GRT.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_rec is null || _modes is null)
            {
                MessageBox.Show(this, "Seleziona prima un profilo con un modello di vibrazione calcolato.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, "Nessun caricamento salvato aperto in GRT.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, "La tab attiva è un file generato — passa al tuo caricamento reale in GRT.", "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix(NoteTitle);
            var ci = CultureInfo.InvariantCulture;
            // Title kept stable (no date) so a report template can pull this note in reliably via
            // ~~result.Note("...")~~ - GRT matches that pragma by exact title, and a date suffix
            // would break the match every day. The date lives in the body's own first line instead.
            string headline = NoteTitle;
            string body = string.Format(ci,
                "{0:yyyy-MM-dd}\r\n\r\nCanna: '{1}' ({2}, foro {3}, lunghezza {4})\r\nFrequenze naturali di flessione (FEM a profilo variabile, N={5} elementi):\r\n{6}",
                DateTime.Now, _rec.Name, _rec.SteelLabel, FormatMm(_rec.BoreDiameterMm), FormatMm(_rec.LengthMm), FemElements,
                string.Join("\r\n", _modes.Select((m, i) => string.Format(ci, "  modo {0}: {1:0.0} Hz", i + 1, m.FrequencyHz))));
            if (_lastSweepRows is { } rows && rows.Length > 0)
            {
                body += string.Format(ci,
                    "\r\n\r\nScansione scala cariche - velocità alla volata all'uscita e OBT nativo di GRT per carica:\r\n{0}",
                    string.Join("\r\n", rows.Select(r => string.Format(ci,
                        "  {0:0.00} gr: canna {1:0.000} ms, volata {2:0.000000} mm/ms, OBT GRT {3}",
                        r.ChargeGr, r.BarrelTimeMs, r.ExitVelMmPerMs,
                        r.GrtObtMs is { } obtMsRow ? string.Format(ci, "{0:0.000} ms (nodo {1})", obtMsRow, string.IsNullOrEmpty(r.GrtObtNode) ? "?" : r.GrtObtNode) : "n/d"))));
                var (quietestIdx, edgeOfRange) = NodeTimelineChart.FindQuietest(_lastSweep!);
                body += edgeOfRange
                    ? string.Format(ci,
                        "\r\n\r\nATTENZIONE: {0:0.00} gr è solo il bordo di questo intervallo, la curva non attraversa" +
                        " mai lo zero - un vero nodo (se esiste) è probabilmente fuori da {1:0.00}-{2:0.00} gr." +
                        " Allarga la scala prima di fidarti di un valore qui.",
                        rows[quietestIdx].ChargeGr, rows.Min(r => r.ChargeGr), rows.Max(r => r.ChargeGr))
                    : string.Format(ci,
                        "\r\n\r\nPiù quieta in questo intervallo: {0:0.00} gr - un vero candidato nodo, non solo il" +
                        " bordo della scala.",
                        rows[quietestIdx].ChargeGr);
                body += "\r\n\r\nNote:\r\n" +
                    "- \"Volata all'uscita\": zero = canna ferma quando il proiettile esce; calcolato dalla curva" +
                    " P(t)/v(t) reale di GRT, nessun fattore inventato.\r\n" +
                    "- Meccanismo assunto: il proiettile segue il cedimento per gravità della canna - un'ipotesi" +
                    " fisica tra diverse possibili, non una misura; niente smorzamento né bedding/azione modellati.\r\n" +
                    "- L'OBT nativo di GRT è un fenomeno longitudinale diverso da questa flessione trasversale, non" +
                    " un concorrente - vedi la tab \"Tempo di canna vs. OBT nativo di GRT\" per il confronto diretto.\r\n" +
                    "- Solo un vero test OCW/ladder decide la carica giusta: se non è d'accordo con questa tabella," +
                    " vince il test.";
            }
            doc.AddNote(headline, body);
            if (_lastSweep is { Length: > 0 } sweepPoints)
            {
                _timeline.Show(sweepPoints);
                if (_timeline.RenderPng() is { } png)
                    doc.AddGalleryPicture($"{NoteTitle} - Carica vs Moto della Volata all'Uscita", "vibration_timeline", png);
            }
            if (_lastObtSweep is { Length: > 0 } obtPoints)
            {
                _obtTimeline.Show(obtPoints);
                if (_obtTimeline.RenderPng() is { } obtPng)
                    doc.AddGalleryPicture($"{NoteTitle} - Tempo di Canna vs OBT Nativo di GRT", "vibration_obt_timeline", obtPng);
            }
            string outPath = doc.SaveSibling("vibration");
            await _grt.LoadFileAsync(outPath);
            _status.Text = "Nota di vibrazione scritta e aperta in GRT.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Modello di vibrazione canna", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshProfileList()
    {
        _profiles.Items.Clear();
        foreach (var (id, name) in _db.ListProfiles()) _profiles.Items.Add(new ProfileItem(id, name));
        if (_profiles.Items.Count > 0)
        {
            _profiles.SelectedIndex = 0;
        }
        else
        {
            _rec = null; _modes = null;
            _freq.Text = "";
            _status.Text = "Nessun profilo canna salvato ancora — creane uno prima in \"Profilo fisico canna\".";
        }
    }

    private sealed record ProfileItem(long Id, string Name) { public override string ToString() => Name; }

    private void LoadSelected()
    {
        if (_profiles.SelectedItem is not ProfileItem pi) return;
        var rec = _db.Load(pi.Id);
        if (rec is null) return;
        _rec = rec;
        Recompute();
    }

    private void Recompute()
    {
        if (_rec is null) return;
        if (_rec.Points.Count < 2)
        {
            _modes = null;
            _freq.Text = "Aggiungi prima almeno 2 punti al profilo di questa canna.";
            UpdateStatusForProfile();
            UpdateChart();
            return;
        }
        var ci = CultureInfo.InvariantCulture;
        try
        {
            _modes = BeamFem.Modes(_rec, FemElements, ModeCount);
            var parts = _modes.Select((m, i) => string.Format(ci, "modo {0}: {1:0.0} Hz", i + 1, m.FrequencyHz)).ToList();
            var lines = new List<string>();
            for (int i = 0; i < parts.Count; i += 3) lines.Add(string.Join("     ", parts.Skip(i).Take(3)));
            _freq.Text = string.Join("\n", lines);
        }
        catch (Exception)
        {
            _modes = null;
            _freq.Text = "Impossibile calcolare un modello di vibrazione per questa canna.";
        }
        UpdateStatusForProfile();
        _lastSweep = null;
        _lastObtSweep = null;
        _lastSweepRows = null;
        _lastGenericObtMs = null;
        _lastGenericObtNode = "";
        _btLabel.Text = "";
        _corrGrid.Rows.Clear();
        _timeline.Show(Array.Empty<NodeTimelineChart.SweepPoint>());
        _obtTimeline.Show(Array.Empty<ObtTimelineChart.ObtPoint>());
        UpdateChart();
    }

    private void UpdateStatusForProfile()
    {
        if (_rec is null) return;
        _status.Text = _modes is null
            ? $"'{_rec.Name}' non ha ancora dati di rastremazione utilizzabili."
            : string.Format(CultureInfo.InvariantCulture, "'{0}' — foro {1}, lunghezza {2}, {3} punto/i profilo, N={4} elementi.",
                _rec.Name, FormatMm(_rec.BoreDiameterMm), FormatMm(_rec.LengthMm), _rec.Points.Count, FemElements);
    }

    private void UpdateChart()
    {
        if (_rec is null) return;
        if (_modes is null) { _chart.Show(_rec, _inches); return; }
        int modeNumber = Math.Clamp(_modeShape.SelectedIndex + 1, 1, _modes.Length);
        _chart.Show(_rec, _inches, _modes[modeNumber - 1], modeNumber);
    }
}
