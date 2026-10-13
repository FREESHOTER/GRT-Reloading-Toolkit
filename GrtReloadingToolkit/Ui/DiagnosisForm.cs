using System.Diagnostics;
using System.Globalization;
using System.Text;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Guided diagnosis (step 17 of the recommended path), a tool of its own and outside the chain of steps: it reads the load open in GRT
/// and says which hypotheses its strings and groups support (see <see cref="Diagnosis"/>), each with its numbers, its confidence and the
/// test that would confirm it, and which rules it could not apply for lack of data. Experimental, and says so: the banner invites the
/// community to try it, to report where it is wrong and to bring the data that are missing.
/// </summary>
internal sealed class DiagnosisForm : Form
{
    private const string IssuesUrl = "https://github.com/FREESHOTER/GRT-Reloading-Toolkit/issues";
    private const string NoteTitle = "Guided Diagnosis";

    private readonly GrtClient? _grt;
    private readonly TextBox _report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f), BackColor = Color.White };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = SystemColors.GrayText, Padding = new Padding(8, 2, 0, 0) };
    private readonly Button _read = new() { AutoSize = true };
    private readonly Button _write = new() { AutoSize = true, Enabled = false };
    private readonly Button _copy = new() { AutoSize = true, Enabled = false };
    private string _noteText = "";

    public DiagnosisForm(GrtClient? grt)
    {
        _grt = grt;
        Text = AppVersion.Title(Lang.T("Guided diagnosis"));
        Width = 900; Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);

        var banner = new Panel { Dock = DockStyle.Top, BackColor = Color.FromArgb(255, 244, 214), Padding = new Padding(12, 8, 12, 8) };
        var bannerText = new Label
        {
            AutoSize = false, Dock = DockStyle.Fill, UseMnemonic = false,
            Text = Lang.T(ExperimentalText),
        };
        var link = new LinkLabel { Text = Lang.T("Report a case or bring data on GitHub"), AutoSize = true, Dock = DockStyle.Bottom, UseMnemonic = false };
        link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(IssuesUrl) { UseShellExecute = true }); } catch (Exception) { } };
        banner.Controls.Add(bannerText);
        banner.Controls.Add(link);
        banner.Height = 120;

        _read.Text = Lang.T("Read the open load");
        _read.Click += async (_, _) => await RunAsync();
        _write.Text = Lang.T("Write note to GRT load");
        _write.Click += async (_, _) => await WriteNoteAsync();
        _copy.Text = Lang.T("Prepare report for GitHub");
        _copy.Click += (_, _) => CopyReport();
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bottom.Controls.Add(_write);
        bottom.Controls.Add(_copy);
        bottom.Controls.Add(_read);

        Controls.Add(_report);
        Controls.Add(bottom);
        Controls.Add(_status);
        Controls.Add(banner);

        Shown += async (_, _) => await RunAsync();
    }

    /// <summary>The community message, English key (translated at the point of use).</summary>
    internal const string ExperimentalText =
        "EXPERIMENTAL FUNCTION. These hypotheses come from rules tried on little real data, and they can be wrong. Each one gives the numbers it rests on " +
        "and the test that would confirm it: use it as an indication, not as a verdict. Testing the rules is something the community can do, and it helps: if " +
        "you find a case where it is wrong, or one it reads correctly, report it on the project's GitHub (with the load file if you can). The community can also " +
        "strengthen the data this diagnosis lacks (see the end of the report): the more of it arrives, the more reliable the rules become.";

    /// <summary>What the community can bring: each item is the condition that switches a rule on or makes it stronger.</summary>
    private static readonly string[] DataWanted =
    {
        "Long strings (10 shots or more) with the cold-bore shot marked: for the drift and cold-barrel rules.",
        "Groups at distance (300-1000 m) with the wind measured: for the wind / support rule.",
        "The ballistics of the load at distance, the same day: for the bullet / seating rule.",
        "The weights of the single bullets of a lot: for the bullet / seating rule.",
        "The number of reloads of each case and the rounds fired by the barrel: for the barrel and brass alerts.",
        "Strings of the same charge at temperatures 5 C or more apart: for the temperature rule.",
        "The cases where the diagnosis was wrong, with the load file.",
    };

    private async Task RunAsync()
    {
        _write.Enabled = false;
        _copy.Enabled = false;
        if (_grt is not { Connected: true })
        {
            _report.Text = Lang.T("Not connected to GRT. Open the toolkit from GRT with a saved load to read its strings and groups.");
            return;
        }
        try
        {
            _read.Enabled = false;
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                _report.Text = Lang.T("No saved load is open in GRT.");
                return;
            }
            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            var u = GrtUnits.Current;
            var input = BuildInput(doc, u);
            var result = Diagnosis.Run(input);
            _noteText = BuildReport(result, input, Path.GetFileNameWithoutExtension(top.file));
            _report.Text = _noteText.Replace("\n", Environment.NewLine);
            _write.Enabled = true;
            _copy.Enabled = true;
            _status.Text = string.Format(CultureInfo.InvariantCulture, Lang.T("{0} hypothesis(es), {1} observation(s), {2} rule(s) not evaluated."), result.Findings.Count, result.Observations.Count, result.Gaps.Count);
        }
        catch (Exception ex)
        {
            _report.Text = ex.Message;
        }
        finally { _read.Enabled = true; }
    }

    /// <summary>Strings from the measurements, groups from the shot-group tabs, the chosen charge from its note, and the node from the same
    /// ladder analysis the analyzer runs; units as GRT shows them.</summary>
    private static DiagnosisInput BuildInput(GrtLoadDoc doc, GrtUnits u)
    {
        var (groups, _) = GrtShotGroups.FromDoc(doc, new GrtShotGroups.Options(RefUnit.Mm, ShootUnit.Meters, false));
        var strings = new List<DiagnosisString>();
        var ladderVelocities = new List<LadderVelocity>();
        foreach (var meas in doc.Measurements())
            foreach (var ch in meas.Charges)
            {
                var shots = ch.Shots.Where(s => s.VelocityMps > 50).ToList();
                if (shots.Count == 0) continue;
                var v = shots.Select(s => s.VelocityMps).ToList();
                var cold = shots.Select(s => DataTokens.IsCold(s.Note)).ToList();
                double? charge = ch.ChargeGrains;
                double? moa = charge is { } c
                    ? groups.FirstOrDefault(g => g.ChargeGrains is { } gc && Math.Abs(gc - c) < 0.005)?.GroupEsMoa
                    : null;
                strings.Add(new DiagnosisString(ch.Name, charge, v, ToolSuggestions.TemperatureC(ch.Note), moa, cold.Any(c => c) ? cold : null));
                if (LadderLoader.MeasurementStep(LadderMode.Charge, ch.ChargeGrains, ch.Name, ch.Note) is { } step)
                    ladderVelocities.Add(new LadderVelocity(step, v));
            }

        double? chosen = null;
        string? chosenText = ChosenChargeReader.ChargeFrom(doc.FindNoteText(ChosenChargeNote.Title));
        if (chosenText is not null)
        {
            var digits = new string(chosenText.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-').ToArray()).Replace(',', '.');
            if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double cv)) chosen = cv;
        }

        DiagnosisNode? node = null;
        bool searched = false;
        try
        {
            var usable = groups.Where(g => g.ChargeGrains is not null).ToList();
            if (usable.Count >= 3)
            {
                searched = true;
                var loaded = LadderLoader.FromGroups(usable, LadderMode.Charge, null);
                LadderLoader.FillMissingVelocities(loaded, ladderVelocities);
                var res = LadderAnalyzer.Analyze(loaded.Rows, LadderMode.Charge, LadderMode.Charge.XUnitName(), AnalysisWeights.For(LadderMode.Charge));
                if (res.BestNode is { } n) node = new DiagnosisNode(LadderMode.Charge.XValue(n.Low), LadderMode.Charge.XValue(n.High));
            }
        }
        catch (Exception) { /* no node: the rule says so */ }

        var far = groups.Select(g =>
        {
            var wind = DataTokens.WindOf(g.SourceFile);   // "GRT:<tab title>#<group name>": the token may sit in either
            return new DiagnosisGroup(g.SourceFile.Replace("GRT:", ""), g.DistanceM, g.HorizontalSpreadMoa, g.VerticalSpreadMoa, g.Impacts.Count, wind?.Mps, wind?.Degrees);
        }).ToList();

        return new DiagnosisInput(strings, chosen, searched, node, mps => u.VelocitySd(mps) + " " + u.VelocityUnitName, far);
    }

    /// <summary>The report, as plain text: the banner line, the hypotheses, the rules not evaluated, what the community can bring, and where to go next.</summary>
    internal static string BuildReport(DiagnosisResult result, DiagnosisInput input, string loadName)
    {
        string T(string template, object[] args) => string.Format(CultureInfo.InvariantCulture, Lang.T(template), args);
        var sb = new StringBuilder();
        sb.AppendLine($"{NoteTitle} {DateTime.Now:yyyy-MM-dd}  -  {loadName}");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine(Lang.T("EXPERIMENTAL: hypotheses from rules tried on little data. They can be wrong; each one says what would confirm it."));
        sb.AppendLine();

        if (result.Findings.Count == 0)
            sb.AppendLine(Lang.T("Nothing in the data of this load points at a problem."));
        else
        {
            sb.AppendLine(Lang.T("HYPOTHESES, most reliable first"));
            int i = 0;
            foreach (var f in result.Findings)
            {
                sb.AppendLine();
                sb.AppendLine($"{++i}. [{Lang.T(f.Confidence == DiagnosisConfidence.High ? "high confidence" : f.Confidence == DiagnosisConfidence.Medium ? "medium confidence" : "low confidence")}] " + T(f.Template, f.Args));
                sb.AppendLine("   " + Lang.T("To confirm it:") + " " + Lang.T(f.ProofTemplate));
            }
            sb.AppendLine();
            int phase = result.Findings.First().ReturnToPhase;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, Lang.T("If the check at distance is not good, the strongest hypothesis sends you back to phase {0}."), phase));
        }

        if (result.Observations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.T("OBSERVATIONS (rules not validated yet: the number is real, its reading is not decided)"));
            foreach (var o in result.Observations) sb.AppendLine(" - " + T(o.Template, o.Args));
        }

        if (result.Gaps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Lang.T("NOT EVALUATED (a missing measurement, not a clean bill of health)"));
            foreach (var g in result.Gaps) sb.AppendLine(" - " + T(g.Template, g.Args));
        }

        sb.AppendLine();
        sb.AppendLine(Lang.T("HOW TO WRITE DOWN WHAT THE RULES NEED (in text GRT already lets you write)"));
        sb.AppendLine(" - " + Lang.T("COLD=1 in the note of the cold-bore shot (the shot's note in a measurement)."));
        sb.AppendLine(" - " + Lang.T("WIND=3.2m/s (or km/h, mph, ft/s) and, if you can, @90 for its direction in degrees from your twelve o'clock, in the title of the shot-group tab or in the name of the group."));
        sb.AppendLine();
        sb.AppendLine(Lang.T("DATA THAT THE COMMUNITY CAN BRING TO MAKE THE RULES STRONGER"));
        foreach (var d in DataWanted) sb.AppendLine(" - " + Lang.T(d));
        sb.AppendLine();
        sb.AppendLine(Lang.T("Report cases and bring data at: ") + IssuesUrl);
        return sb.ToString();
    }

    /// <summary>A report that can be pasted into a GitHub issue: the version, the numbers and the rules not evaluated.</summary>
    private void CopyReport()
    {
        if (string.IsNullOrEmpty(_noteText)) return;
        string text = $"GRT Reloading Toolkit v{AppVersion.Short} - guided diagnosis report" + Environment.NewLine
            + "Please attach the .grtload file of this load if you can, and say whether the hypotheses fit what you know." + Environment.NewLine + Environment.NewLine
            + _noteText.Replace("\n", Environment.NewLine);
        try { Clipboard.SetText(text); _status.Text = Lang.T("Report copied: paste it into a new issue on GitHub."); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private async Task WriteNoteAsync()
    {
        if (_grt is not { Connected: true } || string.IsNullOrEmpty(_noteText)) return;
        try
        {
            _write.Enabled = false;
            var top = await _grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file))
            {
                MessageBox.Show(this, Lang.T("No saved load is open in GRT."), Lang.T("Write note"));
                return;
            }
            if (GrtLoadDoc.LooksLikeGeneratedSibling(top.file))
            {
                MessageBox.Show(this, Lang.T("The active tab is a generated file — switch to your real load in GRT first."), Lang.T("Write note"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var doc = GrtLoadDoc.OpenForToolkitEdit(top.file);
            doc.RemoveByTitlePrefix(NoteTitle);
            doc.AddNote(NoteTitle, _noteText);
            string outPath = doc.SaveSibling("diagnosis");
            await _grt.LoadFileAsync(outPath);
            _status.Text = Lang.T("Note written and opened in GRT.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Write note"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _write.Enabled = true; }
    }
}
