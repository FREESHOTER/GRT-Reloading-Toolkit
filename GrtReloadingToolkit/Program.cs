using System.Windows.Forms;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Cal;
using GrtReloadingToolkit.TempCoeff;
using GrtReloadingToolkit.Ocw;
using GrtReloadingToolkit.Ui;

namespace GrtReloadingToolkit;

internal static class Program
{
    internal const string PluginId = "com.grt.plugin.reloadingtoolkit";

    private static Ui.BudgetChart BudgetPreview()
    {
        // 6 series of 8 shots: a velocity that drifts from one series to the next, a bullet-weight effect and a first shot that is slow
        var rnd = new Random(5);
        var shots = new List<Cartridges.ShotSample>();
        for (int sr = 0; sr < 6; sr++)
            for (int k = 0; k < 8; k++)
            {
                double w = 140.0 + (rnd.NextDouble() - 0.5) * 1.6;
                double v = 825 + sr * 1.8 + (w - 140) * 3.1 + (k == 0 ? -2.5 : 0) + (rnd.NextDouble() - 0.5) * 3.2;
                shots.Add(new Cartridges.ShotSample(sr + 1, v, new Dictionary<string, double> { ["bullet_weight_gr"] = w }));
            }
        var c = new Ui.BudgetChart { Dock = DockStyle.Fill };
        c.SetData(Cartridges.VarianceBudget.Analyze(shots, 0.8, 300));
        return c;
    }

    private static Ui.ProgressChart ProgressPreview()
    {
        var c = new Ui.ProgressChart { Dock = DockStyle.Fill };
        var before = new Cartridges.ModelSnapshot(48, 6, 3.4, 3.1, 0.31, new[] { new Cartridges.ModelProof("bullet_weight_gr", 3.0, 0.9, 0.046) });
        var after = new Cartridges.ModelSnapshot(72, 9, 3.3, 2.6, 0.48, new[] { new Cartridges.ModelProof("bullet_weight_gr", 3.1, 0.6, 0.002), new Cartridges.ModelProof("case_volume", -0.4, 0.1, 0.01) });
        c.SetData(new Cartridges.ProgressReport(3, "2026-11-12", 24, before, after, new Cartridges.LotExpectation(30, 2.4, 3.9), new Cartridges.LotExpectation(30, 2.1, 3.3)));
        return c;
    }

    private static Control PrintPreview()
    {
        // the charts as they go into a PDF (white ground), one under the other
        var box = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, BackColor = Color.White };
        foreach (var png in new[] { BudgetPreview().RenderPng(1000, 640, print: true), ProgressPreview().RenderPng(760, 330, print: true) })
            if (png != null) box.Controls.Add(new PictureBox { Image = Image.FromStream(new MemoryStream(png)), SizeMode = PictureBoxSizeMode.AutoSize });
        return box;
    }

    private static Ui.RankChart RankPreview()
    {
        var c = new Ui.RankChart { Dock = DockStyle.Fill };
        c.SetData(new[]
        {
            new Ui.RankBar("41.2 gr  Varget", 8.6, "SD 3.1 · 0.42 MOA · 30 colpi"), new Ui.RankBar("41.0 gr  Varget", 8.1, "SD 3.8 · 0.51 MOA · 20 colpi"),
            new Ui.RankBar("41.4 gr  Varget", 6.9, "SD 5.2 · 0.60 MOA · 20 colpi"), new Ui.RankBar("40.8 gr  Varget", 5.2, "SD 7.4 · 0.88 MOA · 10 colpi"),
            new Ui.RankBar("41.6 gr  Varget", null, "SD 4.0 · 0.70 MOA · 5 colpi"),
        });
        return c;
    }

    [STAThread]       // the file and folder dialogs and the clipboard need a single-threaded apartment: without it they freeze the program
    private static void Main(string[] args)
    {
        // The debug command lines stay in English for scripts and logs; TOOLKIT_LANG=it asks for the Italian texts too (to read them).
        if (string.Equals(Environment.GetEnvironmentVariable("TOOLKIT_LANG"), "it", StringComparison.OrdinalIgnoreCase)) Tr.Translate = Ui.Lang.T;
        if (args.Length >= 1 && args[0] == "--dbtest") { DbSelfTest.Run(args); return; }
        if (args.Length >= 2 && args[0] == "--archivio") { Environment.ExitCode = ArchiveCli(args); return; }
        if (args.Length >= 3 && args[0] == "--recupera")
        {
            // Reads every .grtload under args[1] (read only) and writes what it holds into args[2].
            string fotoDir = Path.Combine(args[2], "foto");
            var result = Recovery.HistoryRecovery.Run(args[1], (name, data) =>
            {
                Directory.CreateDirectory(fotoDir);
                File.WriteAllBytes(Path.Combine(fotoDir, name), data);
            });
            Recovery.RecoveryWriter.Write(result, args[2]);
            Console.WriteLine(Recovery.RecoveryWriter.Summary(result));
            return;
        }
        if (args.Length >= 3 && args[0] == "--ladder") { LadderCli.Run(args[1], args[2], args.Length >= 4 ? args[3] : null); return; }
        if (args.Length >= 2 && args[0] == "--chronostats")
        {
            double margin = args.Length >= 4 && double.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out double m) ? m : 3.0;
            ChronoStats.ChronoStatsCli.Run(args[1], args.Length >= 3 ? args[2] : null, margin);
            return;
        }
        if (args.Length >= 4 && args[0] == "--cal") { Cal.CalCli.RunAsync(args).GetAwaiter().GetResult(); return; }
        if (args.Length >= 4 && args[0] == "--tcoeff") { TempCoeffCli.Run(args); return; }
        if (args.Length >= 2 && args[0] == "--card") { Cards.CardCli.Run(args[1], args.Length >= 3 ? args[2] : null); return; }
        if (args.Length >= 2 && args[0] == "--brass") { Brass.BrassCli.Run(args); return; }
        if (args.Length >= 2 && args[0] == "--groups") { Ocw.GroupsCli.Run(args); return; }
        if (args.Length >= 1 && args[0] == "--reports")
        {
            string? root = args.Length >= 2 ? args[1] : Reports.ReportTemplates.InferGrtRoot();
            if (root == null) { Console.WriteLine("could not find GRT root (pass it as an argument)"); return; }
            foreach (var l in Reports.ReportTemplates.Install(root)) Console.WriteLine(l);
            return;
        }
        if (args.Length >= 3 && args[0] == "--athlon")
        {
            Athlon.AthlonCli.Run(args[1], args[2], args.Length >= 4 && double.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : null);
            return;
        }

        ApplicationConfiguration.Initialize();
        Tr.Translate = Ui.Lang.T;     // the engines' report texts follow the UI language
        // On its own the program is Reloading Toolkit Desktop and its windows take the colours of its home page; launched by GRT
        // (with an IPC port or socket file) they keep GRT's look.
        var launch = LaunchArgs.Parse(args);
        bool standalone = !launch.ShouldConnect;
        if (standalone) Ui.GrtTheme.UseBancoPalette();
        Ui.GrtTheme.Install();
        Measuring.ProgramUnits.Prefs = Ui.AppUnits.Prefs;     // the printed texts follow the units chosen in "Units of measure"

        // --collaudo <folder>: opens every tool through the real path (Open), off the screen, and reports what came out: no window, an exception, a window
        // that collapsed or is not on a sane size; a picture of each goes in the folder. Run it on COPIES of the data (RICARICA_ARCHIVIO, RELOADING_LOG_DB, SEGUI_CARICO_DB).
        if (args.Length >= 2 && args[0] == "--collaudo")
        {
            Directory.CreateDirectory(args[1]);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            ToolkitContext.Offscreen = true;
            using var testCtx = new ToolkitContext(Ui.Product.IsDesktop ? null : new GrtClient(0));
            File.WriteAllLines(Path.Combine(args[1], "collaudo.txt"), testCtx.Collaudo(args[1]), new System.Text.UTF8Encoding(true));
            return;
        }

        // --anteprima athlon <file.png>: draws a tool window into a picture without GRT and without showing it, to check
        // its layout. The archive is opened read only for the lists (a copy, so not even its schema is touched).
        if (args.Length >= 3 && args[0] == "--anteprima")
        {
            string copy = Path.Combine(Path.GetTempPath(), "anteprima_archivio.db");
            Archive.ArchiveDb? arc = null;
            if (File.Exists(Archive.ArchivePaths.Database)) { File.Copy(Archive.ArchivePaths.Database, copy, true); arc = new Archive.ArchiveDb(copy); }
            Ui.ArchiveLink? link = arc != null ? new Ui.ArchiveLink(arc, () => 1) : null;
            using Form f = args[1] switch
            {
                "athlon" => new Ui.AthlonForm(null, null, arc),
                "cronostat" => new Ui.ChronoStatsForm(null, link), "cronostat2" => Ui.ChronoStatsForm.Preview(),
                "rosate" => new Ui.GroupAnalysisForm(null, link), "rosate2" => Ui.GroupAnalysisForm.Preview(),
                "sd" => new Ui.SdRootCauseForm(null, new Log.Db(Path.Combine(Path.GetTempPath(), "anteprima_log.db")), link),
                "ocw" => new Ui.OcwForm(null, link), "scala" => Ui.OcwForm.Preview(), "seduta" => Ui.SeatingForm.Preview(),
                "scegli" => new Ui.ArchivePickForm(link!, Ui.ArchivePick.Both),
                "shotmarker" => new Ui.ShotMarkerForm(null, null, link),
                "libreria" => new Ui.LibraryForm(link!),
                "cartucce1" => Ui.CartridgesForm.Preview(link!, 0), "cartucce2" => Ui.CartridgesForm.Preview(link!, 1),
                "cartucce3" => Ui.CartridgesForm.Preview(link!, 2), "cartucce4" => Ui.CartridgesForm.Preview(link!, 3),
                "cartucce5" => Ui.CartridgesForm.Preview(link!, 4), "cartucce6" => Ui.CartridgesForm.Preview(link!, 5), "cartucce7" => Ui.CartridgesForm.Preview(link!, 6),
                "lettura" => Ui.GroupReadingForm.Preview(), "gara" => Ui.GroupReadingForm.PreviewMatch(), "unita" => new Ui.UnitsForm(), "bossoli" => Ui.BrassForm.Preview(),
                "testolungo" => new Ui.LongTextDialog("Installa i template report GRT", "Cartella di GRT: C:\\GRT" + Environment.NewLine + "fatto — 9 cartelle di lingua",
                    string.Join(Environment.NewLine, Enumerable.Range(1, 300).Select(i => $"de: scritto toolkit-pagina-{i}.txt"))),
                "home" => new Ui.HomeForm(_ => { }, new Log.Db(Path.Combine(Path.GetTempPath(), "anteprima_log.db")), _ => { }, arc),
                "abbinamenti" => Ui.LibraryForm.OnMatches(link!),
                // the three kinds of button side by side: main action on, main action off, an ordinary one
                "diagnostica" => new Ui.AdvancedDiagnosticsForm(null, new Log.Db(Path.Combine(Path.GetTempPath(), "anteprima_log.db"))),
                "workflow" => new Ui.WorkflowForm(null, new Log.Db(Path.Combine(Path.GetTempPath(), "anteprima_log.db"))),
                "uscita" => new Ui.OutingDialog(new Log.Outing { GroupMoa = 0.62, DistanceM = 300, SeatingMm = 0.508, ShotsMs = "821.4 823.0 820.2 822.8 821.9", WindMps = 2.5, TempC = 18 }, "Prova"),
                "bilancio" => new Form { Size = new Size(1000, 640), Text = "Bilancio", Controls = { BudgetPreview() } },
                "progresso" => new Form { Size = new Size(760, 330), Text = "Progresso", Controls = { ProgressPreview() } },
                "stampa" => new Form { Size = new Size(1100, 1000), Text = "Stampa", Controls = { PrintPreview() } },
                "lanciatore" => new Ui.LauncherForm(_ => { }, new GrtClient(0), new Log.Db(Path.Combine(Path.GetTempPath(), "anteprima_log.db")), _ => { }),
                "guidato" => new Ui.WizardForm(_ => { }),
                "classifica" => new Form { Size = new Size(900, 360), Text = "Classifica", Controls = { RankPreview() } },
                "pulsanti" => new Form { Size = new Size(560, 120), Text = "Pulsanti", Controls = { new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), Controls = {
                    new Button { Text = Ui.Lang.T("PDF report"), AutoSize = true },
                    new Button { Text = Ui.Lang.T("Save in the archive"), AutoSize = true, Enabled = false },
                    new Button { Text = Ui.Lang.T("From the archive…"), AutoSize = true } } } } },
                _ => throw new ArgumentException("finestra sconosciuta: " + args[1]),
            };
            Tool? asTool = args[1] switch
            {
                "athlon" => Tool.Athlon, "cronostat" or "cronostat2" => Tool.ChronoStats, "rosate" or "rosate2" => Tool.GroupAnalysis, "diagnostica" => Tool.Diagnostics, "guidato" => Tool.Wizard, "uscita" => Tool.FollowLoad, "workflow" => Tool.Workflow, "sd" => Tool.SdRootCause,
                "ocw" or "scala" => Tool.Ocw, "seduta" => Tool.Seating, "shotmarker" => Tool.ShotMarker, "libreria" => Tool.Library,
                "cartucce1" or "cartucce2" or "cartucce6" or "cartucce7" => Tool.Cartridges,
                "cartucce3" => Tool.TrainingCartridges, "cartucce4" or "cartucce5" => Tool.RaceCartridges,
                "lettura" => Tool.GroupReading, "gara" => Tool.MatchReading, "unita" => Tool.Units, "bossoli" => Tool.Brass, _ => null,
            };
            if (asTool is { } at) Ui.ToolBanner.Attach(f, at);
            Ui.GrtTheme.Apply(f);
            // RICARICA_ANTEPRIMA_DIM=WxH: the window at another size (a small screen, the bottom of a long page)
            if (Environment.GetEnvironmentVariable("RICARICA_ANTEPRIMA_DIM")?.Split('x') is [var dw, var dh]
                && int.TryParse(dw, out int pw) && int.TryParse(dh, out int ph))
                f.Size = new Size(pw, ph);
            // RICARICA_ANTEPRIMA_MOSTRA=1: the window stays open on screen (to look at it as it really draws, list views included)
            if (Environment.GetEnvironmentVariable("RICARICA_ANTEPRIMA_MOSTRA") == "1")
            {
                f.StartPosition = FormStartPosition.CenterScreen;
                Application.Run(f);
                arc?.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(copy);
                return;
            }
            // shown for a moment far off-screen: a window never shown draws its children blank
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-5000, -5000);
            f.ShowInTaskbar = false;
            f.Show();
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(30); }
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            // Off-screen a list view draws only its first row into the picture, so what every list really holds is written
            // beside it (<file>.txt): the count and the first rows.
            var lines = new List<string>();
            void Dump(Control c)
            {
                switch (c)
                {
                    case ListView lv:
                        lines.Add($"elenco: {lv.Items.Count} righe");
                        lines.AddRange(lv.Items.Cast<ListViewItem>().Take(8).Select(i => "  " + string.Join(" | ", i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text))));
                        break;
                    case CheckedListBox cl:
                        lines.Add($"lista da spuntare: {cl.Items.Count} voci, {cl.CheckedItems.Count} spuntate");
                        lines.AddRange(cl.Items.Cast<object>().Take(8).Select(o => "  " + o));
                        break;
                    case ComboBox cb:
                        lines.Add($"tendina: {cb.Items.Count} voci, scelta \"{cb.SelectedItem}\"");
                        break;
                    case DataGridView dg:
                        lines.Add($"tabella: {dg.Rows.Count} righe");
                        break;
                    case Button bt when f is Ui.LauncherForm:
                        lines.Add("pulsante: " + bt.Text);       // the whole column of the launcher, also what the screen shows only after scrolling
                        break;
                    case Label lb when f is Ui.LauncherForm && lb.Text.Length > 0:
                        lines.Add("voce: " + lb.Text);
                        break;
                }
                foreach (Control k in c.Controls) Dump(k);
            }
            Dump(f);
            File.WriteAllLines(args[2] + ".txt", lines, new System.Text.UTF8Encoding(true));
            bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
            arc?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(copy);
            return;
        }

        // --icona <file.ico>: the emblem of the home page as the program's icon (the source of app.ico)
        if (args.Length >= 2 && args[0] == "--icona")
        {
            File.WriteAllBytes(args[1], Ui.HomeArt.EmblemIco());
            return;
        }

        // --rapporto-seduta [id]: the chronograph statistics of one session of the real archive (the latest one when no id is
        // given) as the "PDF report" button makes them: the PDF in Documenti\Ricarica\rapporti and, with its analysis, in the
        // archive, after a dated copy of the archive.
        if (args.Length >= 1 && args[0] == "--rapporto-seduta")
        {
            string backup = Archive.ArchiveBackup.Copy(Archive.ArchivePaths.Database, Archive.ArchivePaths.Backups);
            using var arc = new Archive.ArchiveDb(Archive.ArchivePaths.Database);
            long shooter = Archive.ArchiveImports.ShooterFor(arc, null);
            var all = Archive.ArchiveQueries.Sessions(arc, shooter).Where(x => x.Strings > 0).ToList();
            var s = args.Length >= 2 && long.TryParse(args[1], out long sid) ? all.First(x => x.Id == sid) : all.First();
            var strings = Archive.ArchiveQueries.Strings(arc, s.Id).Where(st => st.Velocities.Count(v => v > 50) >= 2).ToList();
            var rows = strings.Select(st => ($"{st.Label} ({s.Date})", ChronoStats.ChronoStatsCalc.Analyze(st.Velocities.Where(v => v > 50).ToList()))).ToList();
            string tool = Ui.Lang.T("Chronograph Statistics");
            var text = new System.Text.StringBuilder();
            text.AppendLine($"{tool} {DateTime.Now:yyyy-MM-dd}");
            text.AppendLine(new string('-', 20));
            text.AppendLine();
            text.Append(ChronoStats.ChronoStatsCalc.BuildTableReport(rows, 3.0, 0.95));
            string title = $"{Ui.HomeForm.ShortLoadName(s.LoadName)} — {s.Date}";
            var doc = new Reports.ReportDoc(title, tool, Ui.AppVersion.Short, DateTime.Now,
                Archive.ArchiveQueries.ReportFacts(arc, s.LoadId, s.Id), text.ToString(), Array.Empty<Reports.ReportImage>());
            string pdfPath = Reports.PdfReport.DefaultPath(doc, Path.Combine(Archive.ArchivePaths.Folder, "rapporti"));
            byte[] pdf = Reports.PdfReport.Write(doc, pdfPath);
            string inputs = System.Text.Json.JsonSerializer.Serialize(new
            {
                serie_archivio = strings.Select(st => st.Id).ToList(), serie = rows.Select(r => r.Item1).ToList(), confidenza = 0.95,
            });
            long id = Archive.ArchiveQueries.SaveAnalysis(arc, tool, Ui.AppVersion.Short, s.LoadId, s.Id, inputs, text.ToString(),
                (pdf, Path.GetFileName(pdfPath)));
            File.WriteAllLines(Path.Combine(Archive.ArchivePaths.Folder, "ultimo_rapporto.txt"), new[]
            {
                "copia prima: " + backup, "seduta: " + s.Id + " " + title, "rapporto: " + pdfPath, "analisi n. " + id,
                "archivio principale: " + arc.IsMaster,
            }, new System.Text.UTF8Encoding(true));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            return;
        }

        // --rapporto-prova <file.pdf>: a PDF report from the shooter's real archive (a copy of it), to check the page the
        // tools print: the session with the most strings and groups, its velocity table and its first measurable group.
        if (args.Length >= 2 && args[0] == "--rapporto-prova")
        {
            string copy = Path.Combine(Path.GetTempPath(), "rapporto_prova_archivio.db");
            File.Copy(Archive.ArchivePaths.Database, copy, true);
            try
            {
                using var arc = new Archive.ArchiveDb(copy);
                long shooter = Archive.ArchiveImports.ShooterFor(arc, null);
                var s = Archive.ArchiveQueries.Sessions(arc, shooter).Where(x => x.Strings > 0)
                    .OrderByDescending(x => x.Groups > 0).ThenByDescending(x => x.Strings).First();
                var rows = Archive.ArchiveQueries.Strings(arc, s.Id).Where(st => st.Velocities.Count(v => v > 50) >= 2)
                    .Select(st => ($"{st.Label} ({s.Date})", ChronoStats.ChronoStatsCalc.Analyze(st.Velocities.Where(v => v > 50).ToList())))
                    .ToList();
                string body = ChronoStats.ChronoStatsCalc.BuildTableReport(rows, 3.0, 0.95);
                var images = new List<Reports.ReportImage>();
                foreach (var g in Archive.ArchiveQueries.Groups(arc, s.Id))
                    if (Archive.ArchiveQueries.ToTargetGroup(g) is { } t)
                    {
                        using var chart = new Ui.GroupTargetChart();
                        chart.SetData(t, Array.Empty<GrtReloadingToolkit.Log.MahalanobisOutlier>());
                        images.AddRange(Ui.ReportPdfAction.Chart($"{g.Label} — {t.Impacts.Count} colpi", chart.RenderPng(900, 900, print: true)));
                        break;
                    }
                // the other charts of the tools, so every one is seen in its print colours
                var strings = Archive.ArchiveQueries.Strings(arc, s.Id);
                if (strings.FirstOrDefault(st => st.Velocities.Count(v => v > 50) >= 3) is { } first)
                {
                    var rep = Log.SdRootCause.Diagnose(first.Velocities.Where(v => v > 50).ToList());
                    using var sd = new Ui.SdRootCauseChart();
                    sd.SetData(rep.Ranking);
                    images.AddRange(Ui.ReportPdfAction.Chart(Ui.Lang.T("SD contribution") + " — " + first.Label, sd.RenderPng(print: true)));
                }
                var vels = strings.Select(st => (st, vs: st.Velocities.Where(v => v > 50).ToList()))
                    .Where(x => x.vs.Count > 0 && Ocw.LadderLoader.MeasurementStep(Ocw.LadderMode.Charge, x.st.ChargeGr, x.st.Label, x.st.Notes) != null)
                    .Select(x => new Ocw.LadderVelocity(Ocw.LadderLoader.MeasurementStep(Ocw.LadderMode.Charge, x.st.ChargeGr, x.st.Label, x.st.Notes)!.Value, x.vs))
                    .ToList();
                if (vels.Count >= 2)
                {
                    var loaded = Ocw.LadderLoader.FromParts(Array.Empty<Ocw.TargetGroup>(), vels, Ocw.LadderMode.Charge);
                    var res = Ocw.LadderAnalyzer.Analyze(loaded.Rows, Ocw.LadderMode.Charge, Ocw.LadderMode.Charge.XUnitName());
                    using var lc = new Ocw.LadderChart();
                    lc.SetData(res);
                    images.AddRange(Ui.ReportPdfAction.Chart("Ladder / OCW", lc.RenderPng(1400, 900, print: true)));
                    using var fb = new Ui.FindBaChart();
                    fb.SetData(res.Rows.Where(r => r.HasVel).Select(r => (r.X, r.MeanMps)), "gr", "m/s", "");
                    images.AddRange(Ui.ReportPdfAction.Chart("Carica → velocità", fb.RenderPng(print: true)));
                }
                var doc = new Reports.ReportDoc($"{s.LoadName} — {s.Date}", Ui.Lang.T("Chronograph Statistics"), Ui.AppVersion.Short, DateTime.Now,
                    Archive.ArchiveQueries.ReportFacts(arc, s.LoadId, s.Id), body, images);
                Reports.PdfReport.Write(doc, args[1]);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(copy);
            }
            return;
        }

        // The plugin is started by GRT. The program that runs on its own is Reloading Toolkit Desktop, a separate build.
        if (!launch.ShouldConnect && !Ui.Product.IsDesktop)
        {
            MessageBox.Show(Ui.Lang.T("This is the plugin of GRT: it is started from GRT's Plugins menu. The program that runs on its own is Reloading Toolkit Desktop, a separate download."),
                Ui.Product.PluginName);
            return;
        }

        GrtClient? grt = null;
        if (launch.ShouldConnect)
        {
            // GRT on Linux passes a Unix socket file (--ipcfile), elsewhere a TCP port (--ipcport).
            grt = launch.IpcFile is { } file ? new GrtClient(file) : new GrtClient(launch.Port);
            try { grt.Connect(); } catch { }
            // Give GRT a moment to deliver the toolbar/menu click that launched us (onDemand),
            // so we can open that tool straight away instead of the launcher.
            for (int i = 0; i < 25 && grt.PendingActivation is null; i++) Thread.Sleep(20);
        }

        // Held in a using rather than passed inline: Application.Run disposes the context itself,
        // but only along paths that end the loop normally, and the context owns the SQLite handle.
        // Disposing twice is harmless; not disposing at all leaves a -wal file behind.
        using (var ctx = new ToolkitContext(grt))
            Application.Run(ctx);
        grt?.Dispose();
    }

    /// <summary>
    /// <c>--archivio crea [--sostituisci] &lt;recupero&gt;…</c> builds Documenti\Ricarica\archivio.db from the old databases and
    /// the folders written by <c>--recupera</c>; <c>--archivio copia</c> makes a dated copy of it.
    /// </summary>
    private static int ArchiveCli(string[] args)
    {
        // A Windows program has no console of its own when started from a shell: then the encoding cannot be set, and
        // the report file below is what counts anyway.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
        string db = Archive.ArchivePaths.Database;
        if (args[1] == "copia")
        {
            Console.WriteLine("Copia fatta: " + Archive.ArchiveBackup.Copy(db, Archive.ArchivePaths.Backups));
            return 0;
        }
        if (args[1] == "aggiorna")
        {
            if (!File.Exists(db)) { Console.WriteLine($"Archivio non trovato: {db}"); return 1; }
            Archive.ArchiveBuildReport r;
            using (var a = new Archive.ArchiveDb(db))
                r = Archive.ArchiveBuilder.Refresh(a, Archive.ArchiveSources.Defaults(Array.Empty<string>()));
            Console.WriteLine(r.Text());
            return r.Ok ? 0 : 1;
        }
        if (args[1] != "crea") { Console.WriteLine("uso: --archivio crea [--sostituisci] <cartella recupero>…  |  --archivio aggiorna  |  --archivio copia"); return 2; }

        bool replace = args.Contains("--sostituisci");
        var folders = args.Skip(2).Where(s => s != "--sostituisci").ToList();
        if (File.Exists(db))
        {
            using (var old = new Archive.ArchiveDb(db))
                if (old.IsMaster)
                {
                    Console.WriteLine("L'archivio è già quello principale (i programmi ci scrivono): non si ricostruisce dai vecchi archivi.");
                    return 1;
                }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (!replace) { Console.WriteLine($"Esiste già {db}. Usa --sostituisci per ricostruirlo (prima ne viene fatta una copia)."); return 1; }
            Console.WriteLine("Copia del vecchio archivio: " + Archive.ArchiveBackup.Copy(db, Archive.ArchivePaths.Backups));
        }

        string tmp = db + ".nuovo";
        if (File.Exists(tmp)) File.Delete(tmp);
        Archive.ArchiveBuildReport rep;
        using (var a = new Archive.ArchiveDb(tmp))
            rep = Archive.ArchiveBuilder.Build(a, Archive.ArchiveSources.Defaults(folders));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        string text = "COSTRUZIONE DELL'ARCHIVIO " + db + "\n" + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\n\n" + rep.Text();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(db)!, "verifica_archivio.txt"), text, new System.Text.UTF8Encoding(true));
        Console.WriteLine(text);
        if (!rep.Ok)
        {
            Console.WriteLine($"L'archivio NON è stato messo al suo posto: il file provvisorio è {tmp}.");
            return 1;
        }
        File.Move(tmp, db, overwrite: true);
        Console.WriteLine("Archivio pronto: " + db);
        return 0;
    }
}

internal enum Tool { Launcher, Athlon, ShotMarker, ChronoStats, Ocw, Seating, Cal, Temp, Brass, SeatingForce, Label, Log, Journal, FindBa, Leaderboard, Workflow, PowderCompare, Diagnostics, SdRootCause, GroupAnalysis, TargetPrint, Wizard, VelocityModel, LoadBook, Reports, Diagnosis, FollowLoad, ModelProbe, Stability, Library, GrtLoad, Cartridges, GroupReading, TrainingCartridges, RaceCartridges, MatchReading, TrainingStats, TrainingBudget, Units }

/// <summary>
/// One process, three tools. GRT (launch-type onDemand) starts us on the first
/// toolbar/menu click and sends the item id; each further click on another button
/// reaches the running process and opens that tool.
/// </summary>
internal sealed class ToolkitContext : ApplicationContext
{
    private readonly GrtClient? _grt;
    private readonly Db _db;
    private TrackerDb? _tracker;
    /// <summary>The shooter's own archive (Documenti\Ricarica\archivio.db); null when it could not be opened, and then
    /// the tools work as they did before it existed.</summary>
    private Archive.ArchiveDb? _archive;
    private long _archiveBaseline;
    /// <summary>Set by --collaudo: the windows open off the screen and stay where they are.</summary>
    internal static bool Offscreen;
    private readonly Dictionary<Tool, Form> _open = new();
    private readonly WindowsFormsSynchronizationContext _ui = new();

    public ToolkitContext(GrtClient? grt)
    {
        _grt = grt;
        SynchronizationContext.SetSynchronizationContext(_ui);
        _db = new Db(Environment.GetEnvironmentVariable("RELOADING_LOG_DB") ?? Db.DefaultPath);
        _db.CurrentShooterId = Ui.ShooterState.Saved(_db);
        OpenArchive();

        // If GRT started us from a click on a *specific* tool, that tool opens itself (the id is
        // replayed synchronously by the subscribe below). A click on the plain toolbar/menu entry
        // resolves to Launcher, so we still want the launcher.
        bool launchedIntoTool = grt?.PendingActivation is { Length: > 0 } pa && FromId(pa) != Tool.Launcher;
        if (grt != null)
            grt.MenuOrToolbarActivated += id => _ui.Post(_ => Open(FromId(id)), null);

        // The "next step" bars at the foot of the tool windows open the following tool through this.
        Ui.AlertPanel.CountForOpenLoad = (caliber, loadName) =>
        {
            try { return Log.LoadAlerts.ForLoad(Log.LoadAlerts.All(Ui.AlertPanel.ContextFor(_db)), caliber, loadName).Count; }
            catch (Exception) { return 0; }
        };
        Ui.NextStepBar.JournalFacts = caliber =>
        {
            try
            {
                var rows = _db.Journal().Where(e => string.Equals(e.Caliber, caliber, StringComparison.OrdinalIgnoreCase)).ToList();
                return (rows.Where(e => e.PowderId is not null).Select(e => e.PowderId).Distinct().Count(), rows.Count(e => e.Ba is not null));
            }
            catch (Exception) { return (0, 0); }
        };
        Ui.ToolNav.Open = name => { if (Enum.TryParse<Tool>(name, out var next)) _ui.Post(_ => Open(next), null); };

        if (!launchedIntoTool) Open(Tool.Launcher);

        GrtPluginKit.Grt.GrtLoadDoc.SnapshotHasMeasurementsMissingFromNewerFile += (sibling, count) => _ui.Post(_ =>
            MessageBox.Show(
                string.Format(Ui.Lang.T("Your own file is newer than the toolkit snapshot '{0}', so the toolkit now builds on your file. The snapshot holds {1} chronograph measurement(s) that your file does not have; they stay in that snapshot file (re-import them, or open the snapshot in GRT and Save As, if you still need them)."),
                    System.IO.Path.GetFileName(sibling), count),
                Ui.Lang.T("Reloading Toolkit"), MessageBoxButtons.OK, MessageBoxIcon.Information), null);
    }

    public static Tool FromId(string id) =>
        id.EndsWith(".athlon", StringComparison.Ordinal) ? Tool.Athlon :
        id.EndsWith(".shotmarker", StringComparison.Ordinal) ? Tool.ShotMarker :
        id.EndsWith(".chronostats", StringComparison.Ordinal) ? Tool.ChronoStats :
        id.EndsWith(".ocw", StringComparison.Ordinal) ? Tool.Ocw :
        id.EndsWith(".seating", StringComparison.Ordinal) ? Tool.Seating :
        id.EndsWith(".calibration", StringComparison.Ordinal) ? Tool.Cal :
        id.EndsWith(".tempcoeff", StringComparison.Ordinal) ? Tool.Temp :
        id.EndsWith(".brass", StringComparison.Ordinal) ? Tool.Brass :
        id.EndsWith(".seatingforce", StringComparison.Ordinal) ? Tool.SeatingForce :
        id.EndsWith(".label", StringComparison.Ordinal) ? Tool.Label :
        id.EndsWith(".inventory", StringComparison.Ordinal) ? Tool.Log :
        id.EndsWith(".journal", StringComparison.Ordinal) ? Tool.Journal :
        id.EndsWith(".findba", StringComparison.Ordinal) ? Tool.FindBa :
        id.EndsWith(".leaderboard", StringComparison.Ordinal) ? Tool.Leaderboard :
        id.EndsWith(".workflow", StringComparison.Ordinal) ? Tool.Workflow :
        id.EndsWith(".powdercompare", StringComparison.Ordinal) ? Tool.PowderCompare :
        id.EndsWith(".diagnostics", StringComparison.Ordinal) ? Tool.Diagnostics :
        id.EndsWith(".sdrootcause", StringComparison.Ordinal) ? Tool.SdRootCause :
        id.EndsWith(".groupanalysis", StringComparison.Ordinal) ? Tool.GroupAnalysis :
        id.EndsWith(".targetprint", StringComparison.Ordinal) ? Tool.TargetPrint :
        id.EndsWith(".wizard", StringComparison.Ordinal) ? Tool.Wizard :
        id.EndsWith(".followload", StringComparison.Ordinal) ? Tool.FollowLoad :
        id.EndsWith(".velocitymodel", StringComparison.Ordinal) ? Tool.VelocityModel :
        id.EndsWith(".loadbook", StringComparison.Ordinal) ? Tool.LoadBook :
        id.EndsWith(".reports", StringComparison.Ordinal) ? Tool.Reports :
        id.EndsWith(".modelprobe", StringComparison.Ordinal) ? Tool.ModelProbe :
        id.EndsWith(".stability", StringComparison.Ordinal) ? Tool.Stability :
        id.EndsWith(".cartridges", StringComparison.Ordinal) ? Tool.Cartridges :
        id.EndsWith(".groupreading", StringComparison.Ordinal) ? Tool.GroupReading :
        id.EndsWith(".trainingcartridges", StringComparison.Ordinal) ? Tool.TrainingCartridges :
        id.EndsWith(".racecartridges", StringComparison.Ordinal) ? Tool.RaceCartridges :
        id.EndsWith(".matchreading", StringComparison.Ordinal) ? Tool.MatchReading :
        id.EndsWith(".trainingstats", StringComparison.Ordinal) ? Tool.TrainingStats :
        id.EndsWith(".trainingbudget", StringComparison.Ordinal) ? Tool.TrainingBudget :
        id.EndsWith(".units", StringComparison.Ordinal) ? Tool.Units :
        Tool.Launcher;

    private void Open(Tool t)
    {
        if (!Ui.Product.Offers(t)) return;                    // the plugin does not offer what belongs to Reloading Toolkit Desktop
        if (t == Tool.Reports) { InstallReports(); return; }
        if (t == Tool.GrtLoad) { ImportGrtLoad(); return; }
        if ((t == Tool.Library || t == Tool.GrtLoad || t == Tool.Cartridges || t == Tool.TrainingCartridges || t == Tool.RaceCartridges || t == Tool.TrainingBudget) && _archive == null)
        {
            MessageBox.Show(Ui.Lang.T("The archive could not be opened; the tools work as before and the chronograph import writes to GRT only."),
                Ui.Lang.T("Shooter's archive"));
            return;
        }

        if (_open.TryGetValue(t, out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            existing.BringToFront();
            return;
        }

        // Every tool window is born here, so this is the one place that has to dress them.
        Form f = AppIcon.Apply<Form>(t switch
        {
            Tool.Athlon => new AthlonForm(_grt, _db, _archive),
            Tool.ShotMarker => new ShotMarkerForm(_grt, _db, Link()),
            Tool.ChronoStats => new ChronoStatsForm(_grt, Link()),
            Tool.Ocw => new OcwForm(_grt, Link()),
            Tool.Seating => new SeatingForm(_grt, Link()),
            Tool.Cal => new CalibrationForm(_grt, _db),
            Tool.Temp => new TempCoeffForm(_grt),
            Tool.Brass => new BrassForm(_grt),
            Tool.SeatingForce => new SeatingForceForm(),
            Tool.Label => new LabelForm(_grt, _db),
            Tool.Log => new LogForm(_grt, _db),
            Tool.Journal => new JournalForm(_grt, _db),
            Tool.FindBa => new FindBaForm(_db),
            Tool.Leaderboard => new LoadLeaderboardForm(_grt, _db),
            Tool.Workflow => new WorkflowForm(_grt, _db),
            Tool.PowderCompare => new PowderCompareForm(_grt, _db),
            Tool.Diagnostics => new AdvancedDiagnosticsForm(_grt, _db),
            Tool.SdRootCause => new SdRootCauseForm(_grt, _db, Link()),
            Tool.GroupAnalysis => new GroupAnalysisForm(_grt, Link()),
            Tool.TargetPrint => new TargetForm(_grt),
            Tool.Diagnosis => new DiagnosisForm(_grt),
            Tool.FollowLoad => new FollowLoadForm(_grt, Tracker()),
            Tool.Wizard => new WizardForm(Open),
            Tool.VelocityModel => new VelocityModelForm(_grt, _db),
            Tool.LoadBook => new LoadBookForm(_db),
            Tool.ModelProbe => new ModelProbeForm(_grt),
            Tool.Stability => new StabilityForm(_grt),
            Tool.Library => new LibraryForm(Link()!),
            Tool.Cartridges => new CartridgesForm(Link()!),
            Tool.GroupReading => new GroupReadingForm(Link()),
            // the training and the race open the cartridges window already on their own tab, the match reading on its own mode
            Tool.TrainingCartridges => new CartridgesForm(Link()!, startTab: 2),
            Tool.RaceCartridges => new CartridgesForm(Link()!, startTab: 3),
            Tool.MatchReading => new GroupReadingForm(Link(), match: true),
            // the statistics of the chronograph and the budget of the SD are the same windows, in the phase where the training teaches the model
            Tool.TrainingStats => new ChronoStatsForm(_grt, Link()),
            Tool.TrainingBudget => new CartridgesForm(Link()!, startTab: 6),
            Tool.Units => new UnitsForm(),
            // on its own it opens on the home page; beside GRT it keeps the compact column
            _ => _grt == null ? new HomeForm(Open, _db, SetShooter, _archive) : new LauncherForm(Open, _grt, _db, SetShooter),
        });
        // on its own, each tool window wears the band of Reloading Toolkit Desktop (its phase, step and colour)
        if (_grt == null && t != Tool.Launcher && Ui.GrtTheme.Banco && Ui.GrtTheme.Enabled) Ui.ToolBanner.Attach(f, t);
        Ui.GrtTheme.Apply(f);
        f.FormClosed += (_, _) =>
        {
            _open.Remove(t);

            // A form that went away without raising FormClosed — disposed directly, or its handle
            // torn down under us — would otherwise sit in _open for ever and, because the count
            // never reaches zero, leave a windowless process alive holding the exe locked.
            foreach (Tool stale in _open.Where(kv => kv.Value.IsDisposed).Select(kv => kv.Key).ToList())
                _open.Remove(stale);

            // Ending the message loop comes first and on its own. Anything that can throw or block
            // — closing SQLite above all — runs in Dispose below, where failing it costs a tidy
            // shutdown rather than the process's only way out.
            if (_open.Count == 0) ExitThread();
        };
        _open[t] = f;
        // The launcher places itself; every other window is kept inside the screen it opens on (see WindowFit).
        if (Offscreen) { f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-5000, -5000); f.ShowInTaskbar = false; }
        else if (t != Tool.Launcher && f.WindowState == FormWindowState.Normal)
            f.Shown += (_, _) =>
            {
                var wa = Screen.FromControl(f).WorkingArea;
                var fit = WindowFit.Fit((f.Left, f.Top, f.Width, f.Height), (wa.Left, wa.Top, wa.Width, wa.Height));
                if (fit != (f.Left, f.Top, f.Width, f.Height)) f.SetBounds(fit.X, fit.Y, fit.Width, fit.Height);
            };
        f.Show();
        f.Activate();
    }

    /// <summary>
    /// "Import a load from GRT": a load designed and saved in GRT enters the archive whole (its file, components, charge,
    /// COAL, case capacity, Ba), before any range session, and the shooter sees what came in.
    /// </summary>
    private void ImportGrtLoad()
    {
        if (_archive == null) return;
        var owner = Form.ActiveForm;
        string title = Ui.LauncherCaption.From(Ui.Lang.T("📥  Import a load from GRT"));
        long shooter = Archive.ArchiveImports.ShooterFor(_archive, _db.CurrentShooterId);
        var log = new List<string>();
        if (Ui.ArchiveLoadPicker.PickGrtFile(owner, _archive, shooter, log) is not { } id) return;
        bool isNew = log.Any(l => l.StartsWith("nuovo carico", StringComparison.Ordinal));
        var lines = new List<string>
        {
            isNew ? Ui.Lang.T("The load is now in the archive:") : Ui.Lang.T("This load was already in the archive:"), "",
        };
        using (var cmd = _archive.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name, caliber, firearm_text, bullet, powder, charge_gr, coal_mm, case_capacity_cm3 FROM loads WHERE id=$i";
            cmd.Parameters.AddWithValue("$i", id);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var it = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
                string N(int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                string D(int i, string fmt, string unit) => r.IsDBNull(i) ? "" : r.GetDouble(i).ToString(fmt, it) + unit;
                void Add(string label, string value) { if (value.Trim().Length > 0) lines.Add(label + ": " + value.Trim()); }
                Add(Ui.Lang.T("Load"), N(0));
                Add(Ui.Lang.T("Caliber"), N(1));
                Add(Ui.Lang.T("Firearm"), N(2));
                Add(Ui.Lang.T("Bullet"), N(3));
                Add(Ui.Lang.T("Powder"), (N(4) + " " + D(5, "0.0#", " gr")).Trim());
                Add("COAL", D(6, "0.00", " mm"));
                Add(Ui.Lang.T("Case capacity"), D(7, "0.00", " cm³"));
            }
        }
        if (isNew) { lines.Add(""); lines.Add(Ui.Lang.T("Its components are in the component library; the imports can now pick it from the list.")); }
        MessageBox.Show(owner, string.Join(Environment.NewLine, lines), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void InstallReports()
    {
        string? root = Reports.ReportTemplates.InferGrtRoot();
        if (root == null)
        {
            using var d = new FolderBrowserDialog { Description = Ui.Lang.T("Select your GRT folder (the one containing 'doku')") };
            if (d.ShowDialog() != DialogResult.OK) return;
            root = d.SelectedPath;
        }
        var log = Reports.ReportTemplates.Install(root);
        // one line per page and per language of GRT: hundreds of lines, so a scrolling box, not a MessageBox
        Ui.LongTextDialog.Show(Form.ActiveForm, Ui.Lang.T("Install GRT report templates"),
            string.Format(Ui.Lang.T("GRT folder: {0}"), root) + Environment.NewLine + (log.LastOrDefault() ?? ""), log);
    }

    /// <summary>
    /// Runs once the message loop has ended, so this is where the things the context owns are
    /// released. A throw here is swallowed on purpose: the windows are gone and the loop has
    /// stopped, and letting SQLite teardown take the process down with it would turn tidy-up
    /// into a crash on exit. May run twice (Application.Run and Main's using both dispose);
    /// SqliteConnection.Dispose tolerates that.
    /// </summary>
    /// <summary>The followed loads' own file, opened the first time "Segui un carico" is: nobody else gets an empty one.</summary>
    private TrackerDb Tracker() => _tracker ??= new TrackerDb(TrackerDb.DefaultPath) { CurrentShooterId = _db.CurrentShooterId };

    /// <summary>The launcher's picker changed: every list from now on shows this person (null = everybody). Windows already open keep what they show until they refresh.</summary>
    private void SetShooter(long? id)
    {
        _db.CurrentShooterId = id;
        if (_tracker != null) _tracker.CurrentShooterId = id;
    }

    /// <summary>
    /// Opens the archive and brings it up to date with the old databases the other tools still write to; builds it from
    /// them the first time. A failure is shown once and leaves the tools as they were: nothing depends on the archive yet
    /// except the chronograph import, which then writes to GRT only.
    /// </summary>
    /// <summary>The archive as the analysis tools see it (with the launcher's current shooter), or null without an archive.</summary>
    private Ui.ArchiveLink? Link() => _archive is { } a ? new Ui.ArchiveLink(a, () => _db.CurrentShooterId) : null;

    /// <summary>--collaudo: every tool opened through <see cref="Open"/>, what came out, and a picture of each.</summary>
    internal List<string> Collaudo(string outDir)
    {
        var report = new List<string> { $"COLLAUDO di {Ui.Product.Name}  v{Ui.AppVersion.Short}  ({DateTime.Now:dd/MM/yyyy HH:mm})", "" };
        string progress = Path.Combine(outDir, "avanzamento.txt");
        Application.ThreadException += (_, e) => report.Add("  ECCEZIONE non gestita: " + e.Exception.GetType().Name + ": " + e.Exception.Message
            + " | " + (e.Exception.StackTrace?.Split((char)10).FirstOrDefault()?.Trim() ?? ""));
        foreach (Tool t in Enum.GetValues<Tool>())
        {
            File.AppendAllText(progress, t + Environment.NewLine);
            if (t is Tool.GrtLoad or Tool.Reports) { report.Add($"{t,-22} saltato (apre una finestra di Windows di scelta file)"); continue; }
            if (!Ui.Product.Offers(t)) { report.Add($"{t,-22} non offerto da questa versione"); continue; }
            int before = report.Count;
            try
            {
                Open(t);
                for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(40); }
                if (!_open.TryGetValue(t, out var f) || f.IsDisposed) { report.Add($"{t,-22} PROBLEMA: nessuna finestra creata"); continue; }
                string flag = f.Width < 300 || f.Height < 160 || f.ClientSize.Width < 250 ? "  <-- PROBLEMA: finestra troppo piccola o collassata" : "";
                report.Add($"{t,-22} {f.GetType().Name,-26} {f.Width}x{f.Height}  controlli={f.Controls.Count}  titolo='{f.Text}'{flag}");
                using (var bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height)))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(Path.Combine(outDir, t + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                if (t != Tool.Launcher) { f.Close(); Application.DoEvents(); }
            }
            catch (Exception ex) { report.Add($"{t,-22} ERRORE: {ex.GetType().Name}: {ex.Message} | {ex.StackTrace?.Split((char)10).FirstOrDefault()?.Trim()}"); }
            _ = before;
        }
        report.Add(""); report.Add("fine del collaudo");
        return report;
    }

    private void OpenArchive()
    {
        try
        {
            string path = Archive.ArchivePaths.Database;
            bool existed = File.Exists(path);
            var src = Archive.ArchiveSources.Defaults(Array.Empty<string>());
            src = new Archive.ArchiveSources
            {
                LogDb = Environment.GetEnvironmentVariable("RELOADING_LOG_DB") ?? src.LogDb,
                TrackerDb = Environment.GetEnvironmentVariable("SEGUI_CARICO_DB") ?? src.TrackerDb,
                ModelLabDb = src.ModelLabDb,
                EpLibrary = src.EpLibrary,
            };
            _archive = new Archive.ArchiveDb(path);
            var rep = existed ? Archive.ArchiveBuilder.Refresh(_archive, src) : Archive.ArchiveBuilder.Build(_archive, src);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "ultimo_aggiornamento.txt"),
                (existed ? "AGGIORNAMENTO DELL'ARCHIVIO DAI VECCHI ARCHIVI" : "ARCHIVIO CREATO") + Environment.NewLine
                + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + Environment.NewLine + Environment.NewLine + rep.Text(),
                new System.Text.UTF8Encoding(true));
            if (!rep.Ok)
                MessageBox.Show(Ui.Lang.T("The archive was brought up to date, but a check did not match: see ultimo_aggiornamento.txt in the Ricarica folder."),
                    Ui.Lang.T("Shooter's archive"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _archiveBaseline = _archive.TotalChanges();
        }
        catch (Exception ex)
        {
            try { _archive?.Dispose(); } catch { }
            _archive = null;
            MessageBox.Show(Ui.Lang.T("The archive could not be opened; the tools work as before and the chronograph import writes to GRT only.")
                + Environment.NewLine + Environment.NewLine + ex.Message,
                Ui.Lang.T("Shooter's archive"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _db.Dispose(); } catch { }
            try { _tracker?.Dispose(); } catch { }
            if (_archive != null)
            {
                // a dated copy whenever this run changed something (the newest 30 are kept)
                bool changed = false;
                try { changed = _archive.TotalChanges() > _archiveBaseline; } catch { }
                string path = _archive.Path;
                try { _archive.Dispose(); } catch { }
                _archive = null;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (changed) try { Archive.ArchiveBackup.Copy(path, Archive.ArchivePaths.Backups); } catch { }
            }
        }
        base.Dispose(disposing);
    }
}
