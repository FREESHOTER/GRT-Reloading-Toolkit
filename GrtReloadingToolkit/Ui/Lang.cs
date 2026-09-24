using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The toolkit's UI text is English in code (this is a community plugin published on GitHub, used
/// by people who don't read Italian) with an Italian overlay for whoever runs it on an
/// Italian-locale Windows -- so the source of truth stays one string per label, not two parallel
/// copies of every form that would drift apart the first time either one gets edited.
///
/// <see cref="T"/> looks a literal up in the table below and returns the Italian text if the
/// current UI culture is Italian and a translation exists, else the English literal unchanged --
/// so a form ships correctly in English even before every one of its strings has a translation
/// entry. Set <c>TOOLKIT_LANG=en</c> to force English on an Italian machine (screenshots, testing).
/// </summary>
internal static class Lang
{
    public static readonly bool IsItalian =
        (Environment.GetEnvironmentVariable("TOOLKIT_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            .Equals("it", StringComparison.OrdinalIgnoreCase);

    public static string T(string en) => IsItalian && Map.TryGetValue(en, out string? it) ? it : en;

    // One table for the whole plugin, grouped by form (a comment header per section) so a form's
    // strings are easy to find and extend without hunting through an alphabetised wall of text.
    private static readonly Dictionary<string, string> Map = new()
    {
        // LauncherForm
        ["BEFORE THE RANGE"] = "PRIMA DEL POLIGONO",
        ["AFTER THE RANGE"] = "DOPO IL POLIGONO",
        ["🔧  Brass prep (case vol / seating / neck)"] = "🔧  Preparazione bossoli (volume / seating / colletto)",
        ["⚖️  Seating force estimate (QC)"] = "⚖️  Stima forza di seduta (QC)",
        ["🎯  Chronograph import (Athlon / Garmin)"] = "🎯  Importa cronografo (Athlon / Garmin)",
        ["📐  Chronograph statistics"] = "📐  Statistiche cronografo",
        ["📈  Ladder / OCW analyzer"] = "📈  Analizzatore Ladder / OCW",
        ["📏  Seating-depth analyzer"] = "📏  Analizzatore profondità di seduta",
        ["🎚  Barrel calibration"] = "🎚  Calibrazione canna",
        ["🌡  Powder temp coefficients"] = "🌡  Coefficienti temperatura polvere",
        ["🏆  Load leaderboard"] = "🏆  Classifica delle cariche",
        ["🏷  Load card / label"] = "🏷  Cartellino / etichetta carico",
        ["📒  Inventory"] = "📒  Inventario",
        ["📓  Load journal"] = "📓  Diario di carico",
        ["🔎  Find best Ba"] = "🔎  Trova miglior Ba",
        ["📄  Install GRT report templates"] = "📄  Installa i template report GRT",
        ["🪄  Guided new load (wizard)"] = "🪄  Nuovo carico guidato (wizard)",
        ["📉  Velocity model (charge + temperature)"] = "📉  Modello velocità (carica + temperatura)",

        // SeatingForceForm
        ["GRT Seating Force Estimate (QC)"] = "GRT Stima Forza di Seduta (QC)",
        ["Boat-tail"] = "Boat-tail (base rastremata)",
        ["From this bore (bullet + prepped neck)"] = "Da questa canna (proiettile + colletto preparato)",
        ["Bullet diameter"] = "Diametro proiettile",
        ["Neck ID after sizing"] = "ID colletto dopo il sizing",
        ["Actual seating depth"] = "Profondità di seduta reale",
        ["Baseline for a \"typical\" setup in this bore"] = "Baseline per un setup \"tipico\" in questa canna",
        ["-- custom / type your own --"] = "-- personalizzato / inserisci il tuo --",
        ["Quick-fill reference (optional)"] = "Riferimento rapido (opzionale)",
        ["Baseline force, typical prep (kg)"] = "Forza baseline, prep tipica (kg)",
        ["Baseline std dev (kg)"] = "Deviazione standard baseline (kg)",
        ["...at this typical interference"] = "...con questa interferenza tipica",
        ["...and this typical seating depth"] = "...e questa profondità di seduta tipica",
        ["Actual prep for this batch"] = "Preparazione reale per questo lotto",
        ["Annealing"] = "Ricottura",
        ["Neck sizing"] = "Sizing colletto",
        ["Neck lube"] = "Lubrificante colletto",
        ["Bullet coating"] = "Rivestimento proiettile",
        ["Empirical estimate, not a measurement -- a QC baseline for THIS bore. The bar/psi " +
            "figure is just the same force expressed over the bullet's cross-section, not a GRT input."] =
            "Stima empirica, non una misura -- una baseline QC per QUESTA canna. Il valore bar/psi " +
            "è solo la stessa forza espressa sulla sezione del proiettile, non un input per GRT.",
        ["fill in bullet diameter, neck ID, seating depth and the baseline fields"] =
            "compila diametro proiettile, ID colletto, profondità di seduta e i campi baseline",
        ["interference           : {0:0.000} mm\n"] = "interferenza            : {0:0.000} mm\n",
        ["estimated mean force   : {0:0.0} kg  (+/- {1:0.0} kg SD)\n"] = "forza media stimata     : {0:0.0} kg  (+/- {1:0.0} kg SD)\n",
        ["  68% band (1-sigma)    : {0:0.0} - {1:0.0} kg\n"] = "  banda 68% (1-sigma)   : {0:0.0} - {1:0.0} kg\n",
        ["  95% band (2-sigma)    : {0:0.0} - {1:0.0} kg\n"] = "  banda 95% (2-sigma)   : {0:0.0} - {1:0.0} kg\n",
        ["  suggested max (QC)    : {0:0.0} kg\n\n"] = "  massimo suggerito (QC): {0:0.0} kg\n\n",
        ["equivalent pressure     : {0:0} bar  ({1:0} psi)\n"] = "pressione equivalente   : {0:0} bar  ({1:0} psi)\n",

        // SeatingForceCalc option labels
        ["Freshly annealed (torch/AMP, 1-2 firings)"] = "Appena ricotto (torcia/AMP, 1-2 sparate)",
        ["Annealed, 5+ firings since"] = "Ricotto, 5+ sparate da allora",
        ["Never annealed, new brass"] = "Mai ricotto, bossolo nuovo",
        ["Never annealed, 5+ firings (hardened neck)"] = "Mai ricotto, 5+ sparate (colletto indurito)",
        ["Full-length die (non-bushing)"] = "Filiera full-length (non a bushing)",
        ["Neck-sizing die (non-bushing)"] = "Filiera neck-sizing (non a bushing)",
        ["Bushing die (Redding/Forster)"] = "Filiera a bushing (Redding/Forster)",
        ["Bushing + final mandrel (best)"] = "Bushing + mandrino finale (ottimale)",
        ["Dry (no lubrication)"] = "A secco (nessuna lubrificazione)",
        ["Graphite (Imperial/Redding dry)"] = "Grafite (Imperial/Redding a secco)",
        ["hBN (hexagonal boron nitride)"] = "hBN (nitruro di boro esagonale)",
        ["Imperial Dry Neck Lube"] = "Imperial Dry Neck Lube",
        ["Lapua Neck Wax (or similar)"] = "Lapua Neck Wax (o simile)",
        ["Moly (molybdenum disulfide)"] = "Moly (disolfuro di molibdeno)",
        ["Bare copper (standard gilding metal)"] = "Rame nudo (gilding metal standard)",
        ["Plated copper (tombac / FMJ)"] = "Rame placcato (tombac / FMJ)",
        ["Moly coated"] = "Rivestito Moly",
        ["hBN coated"] = "Rivestito hBN",
        ["Hybrid / H-series / PDX (Berger)"] = "Hybrid / serie H / PDX (Berger)",

        // LadderAnalyzerForm (shared by OcwForm/SeatingForm) + generic status phrases reused elsewhere
        ["GRT Ladder / OCW Analyzer"] = "GRT Analizzatore Ladder / OCW",
        ["GRT Seating-Depth Analyzer"] = "GRT Analizzatore profondità di seduta",
        ["Charge"] = "Carica",
        ["Seat/jump"] = "Seduta/salto",
        ["Write note to GRT load"] = "Scrivi nota sul load GRT",
        ["drop flyers"] = "escludi flyer",
        ["Pick ladder folder…"] = "Scegli cartella ladder…",
        ["w POI"] = "peso POI",
        ["w group"] = "peso gruppo",
        ["w MV"] = "peso MV",
        ["window"] = "finestra",
        ["Re-analyze"] = "Rianalizza",
        ["Groups from GRT load"] = "Gruppi dal load GRT",
        ["ref dist"] = "dist rif",
        ["shoot dist"] = "dist tiro",
        ["Dist"] = "Dist",
        ["Grp MR MOA"] = "Raggio medio gruppo MOA",
        ["Vert MOA"] = "Disp. verticale MOA",
        ["Folder with chrono *.xlsx (Athlon/Garmin) and OnTarget *.csv for one ladder"] =
            "Cartella con file cronografo *.xlsx (Athlon/Garmin) e *.csv OnTarget per una scalare",
        ["Not connected to GRT."] = "Non connesso a GRT.",
        ["No saved load is open in GRT."] = "Nessun load salvato è aperto in GRT.",
        ["Groups from GRT"] = "Gruppi da GRT",
        ["No usable shot groups in that load."] = "Nessun gruppo di colpi utilizzabile in quel load.",
        ["GRT shot groups"] = "Gruppi di colpi GRT",
        ["Recommended node {0}–{1} {2}"] = "Nodo consigliato {0}–{1} {2}",
        ["analysis done"] = "analisi completata",
        ["Analyze failed"] = "Analisi non riuscita",
        ["Write note"] = "Scrivi nota",
        ["The active tab is a generated file — switch to your real load in GRT first."] =
            "Il tab attivo è un file generato — passa prima al tuo load reale in GRT.",
        ["Note written — opened in GRT as a new tab."] = "Nota scritta — aperta in GRT come nuovo tab.",
        ["Write failed"] = "Scrittura non riuscita",
        ["stand-alone (no GRT)"] = "autonomo (senza GRT)",
        ["connected to GRT :{0}"] = "connesso a GRT :{0}",
        ["GRT connection lost"] = "connessione a GRT persa",
        ["Pick a ladder folder"] = "Scegli una cartella ladder",

        // TempCoeffForm
        ["GRT Powder Temp-Coefficient Fitter"] = "GRT Coefficienti Temperatura Polvere",
        ["Write tcc/tch to GRT load"] = "Scrivi tcc/tch sul load GRT",
        ["Add Athlon strings…"] = "Aggiungi stringhe Athlon…",
        ["Load Ba from GRT load"] = "Carica Ba dal load GRT",
        ["Fit"] = "Calcola",
        ["Use"] = "Usa",
        ["File"] = "File",
        ["Temp"] = "Temp",
        ["Athlon strings — same charge, different temperatures"] = "Stringhe Athlon — stessa carica, temperature diverse",
        ["No saved load open in GRT."] = "Nessun load salvato aperto in GRT.",
        ["The active tab is a generated file — switch to your real load."] = "Il tab attivo è un file generato — passa al tuo load reale.",
        ["Temp coefficients"] = "Coefficienti temperatura",
        ["WARNING: {0} different charges selected — temp fit needs ONE charge"] =
            "ATTENZIONE: {0} cariche diverse selezionate — il fit richiede UNA sola carica",
        ["add strings at 2+ temperatures (ideally cold / {0} / hot)"] =
            "aggiungi stringhe ad almeno 2 temperature (idealmente fredda / {0} / calda)",
        ["tcc/tch written and opened in GRT."] = "tcc/tch scritti e aperti in GRT.",

        // BrassForm
        ["GRT Brass Prep"] = "GRT Preparazione Bossoli",
        ["Write casevol to GRT load"] = "Scrivi casevol sul load GRT",
        ["Write seating depth to GRT load"] = "Scrivi profondità di seduta sul load GRT",
        ["Case volume"] = "Volume bossolo",
        ["Add 5 rows"] = "Aggiungi 5 righe",
        ["Clear"] = "Svuota",
        ["water weighed in:"] = "acqua pesata in:",
        ["Empty case"] = "Bossolo vuoto",
        ["Full w/ water"] = "Pieno d'acqua",
        ["→ water"] = "→ acqua",
        ["enter empty + full weights (or the water weight directly) for 3+ cases"] =
            "inserisci i pesi vuoto + pieno (o il peso dell'acqua direttamente) per 3+ bossoli",
        ["n={0}   mean case volume = {1:0.00} grain H2O   ({2:0.000} cm3)   SD {3:0.00} gr ({4:0.0}%)   range {5:0.00}-{6:0.00} gr"] =
            "n={0}   volume medio bossolo = {1:0.00} grani H2O   ({2:0.000} cm3)   SD {3:0.00} gr ({4:0.0}%)   intervallo {5:0.00}-{6:0.00} gr",
        ["note written, but no 'casevol' input in the load"] = "nota scritta, ma nessun input 'casevol' nel load",
        ["casevol = mean {0:0.00} gr H2O ({1:0.000} cm3, unit={2}) written and opened in GRT."] =
            "casevol = media {0:0.00} gr H2O ({1:0.000} cm3, unità={2}) scritto e aperto in GRT.",
        ["Seating depth"] = "Profondità di seduta",
        ["Working units"] = "Unità di lavoro",
        ["CBTO  (loaded round, base -> ogive)"] = "CBTO  (cartuccia carica, base -> ogiva)",
        ["Case length  (L3 / CL, trimmed)"] = "Lunghezza bossolo  (L3 / CL, spianato)",
        ["BBTO  (bare bullet, base -> ogive)"] = "BBTO  (proiettile nudo, base -> ogiva)",
        ["GRT seating depth = distance from the bullet base to the case mouth.\n" +
            "DIFF = CBTO - case length ;  seating depth = BBTO - DIFF.\n" +
            "Measure CBTO and BBTO to the SAME comparator / ogive insert."] =
            "La profondità di seduta di GRT = distanza dalla base del proiettile alla bocca del bossolo.\n" +
            "DIFF = CBTO - lunghezza bossolo ;  profondità di seduta = BBTO - DIFF.\n" +
            "Misura CBTO e BBTO con lo STESSO inserto comparatore/ogiva.",
        ["enter CBTO, case length and BBTO"] = "inserisci CBTO, lunghezza bossolo e BBTO",
        ["DIFF (ogive above case mouth) : "] = "DIFF (ogiva sopra la bocca del bossolo) : ",
        ["SEATING DEPTH (GRT gdepth)    : "] = "PROFONDITÀ DI SEDUTA (GRT gdepth) : ",
        ["The active tab is a generated file - switch to your real load."] = "Il tab attivo è un file generato - passa al tuo load reale.",
        ["note written, but no 'gdepth' input in the load"] = "nota scritta, ma nessun input 'gdepth' nel load",
        [" (COAL not updated - the load has no case length or bullet length)"] =
            " (COAL non aggiornato - il load non ha lunghezza bossolo o lunghezza proiettile)",
        [" (COAL not updated - the load has no 'oal' input)"] = " (COAL non aggiornato - il load non ha un input 'oal')",
        ["written and opened in GRT."] = "scritto e aperto in GRT.",
        ["Neck / bushing"] = "Colletto / bushing",
        ["Neck wall thickness"] = "Spessore parete colletto",
        ["Desired interference (grip)"] = "Interferenza desiderata (grip)",
        ["Measured loaded neck OD  (0 = estimate)"] = "OD colletto caricato misurato  (0 = stima)",
        ["0.002\" (~0.05 mm) is a common target grip. Bushing dies: order 2-3 bushings around the value. " +
            "A mandrel as the last step sets the ID directly and evens out wall runout."] =
            "0,002\" (~0,05 mm) è un grip target comune. Filiere a bushing: ordina 2-3 bushing intorno al valore. " +
            "Un mandrino come ultimo passaggio imposta l'ID direttamente e uniforma il runout della parete.",
        ["loaded neck OD    : {0}{1}\n\n" +
            "BUSHING die OD    : {2}\n" +
            "  also try        : {3}  and  {4}   (+/- {5})\n\n" +
            "MANDREL / exp. OD : {6}\n" +
            "  final neck ID   = bullet dia - interference\n"] =
            "OD colletto caricato : {0}{1}\n\n" +
            "OD filiera BUSHING   : {2}\n" +
            "  prova anche       : {3}  e  {4}   (+/- {5})\n\n" +
            "OD MANDRINO / exp.   : {6}\n" +
            "  ID colletto finale = diametro proiettile - interferenza\n",

        // AthlonForm
        ["Chronograph import (Athlon / Garmin)"] = "Importa cronografo (Athlon / Garmin)",
        ["Add chrono files…"] = "Aggiungi file cronografo…",
        ["Add folder…"] = "Aggiungi cartella…",
        ["Remove selected"] = "Rimuovi selezionati",
        ["TEMP= on every shot"] = "TEMP= su ogni colpo",
        ["replace previous chrono import in this load"] = "sostituisci il precedente import cronografo in questo load",
        ["Editable — type the charge here when the session note and file name don't carry it."] =
            "Modificabile — digita la carica qui quando né gli appunti né il nome file la riportano.",
        ["Shots"] = "Colpi",
        ["Import into GRT"] = "Importa in GRT",
        ["Select one chrono file (Athlon or Garmin) per charge"] = "Seleziona un file cronografo (Athlon o Garmin) per carica",
        ["Folder with one chrono file (Athlon / Garmin, .xlsx / .csv) per charge"] =
            "Cartella con un file cronografo (Athlon / Garmin, .xlsx / .csv) per carica",
        ["No .xlsx / .csv files in that folder."] = "Nessun file .xlsx / .csv in quella cartella.",
        ["Add folder"] = "Aggiungi cartella",
        ["Could not read file"] = "Impossibile leggere il file",
        ["Nothing selected."] = "Nessuna selezione.",
        ["Import"] = "Importa",
        ["The tab active in GRT is a generated file, not your load.\n\n" +
            "Switch to your real load tab in GRT, then import again."] =
            "Il tab attivo in GRT è un file generato, non il tuo load.\n\n" +
            "Passa al tuo tab load reale in GRT, poi importa di nuovo.",
        ["No load open in GRT — choose where to save the imported strings"] =
            "Nessun load aperto in GRT — scegli dove salvare le stringhe importate",
        ["Imported {0} charge(s) into GRT."] = "Importate {0} carica/che in GRT.",
        ["Wrote"] = "Scritto",
        ["(not connected to GRT)."] = "(non connesso a GRT).",
        ["Saved:"] = "Salvato:",
        ["Done"] = "Fatto",
        ["Import failed"] = "Importazione non riuscita",
        ["{0} file(s) loaded"] = "{0} file caricati",

        // ChronoStatsForm
        ["Chronograph Statistics"] = "Statistiche Cronografo",
        ["From GRT load's Measurement"] = "Dalla Measurement del load GRT",
        ["confidence"] = "confidenza",
        ["target ±"] = "target ±",
        ["Compare"] = "Confronta",
        ["vs"] = "contro",
        ["String"] = "Stringa",
        ["Mean"] = "Media",
        ["CI ± (mean)"] = "CI ± (media)",
        ["shots for target"] = "colpi per target",
        ["outliers"] = "outlier",
        ["One chrono file per string"] = "Un file cronografo per stringa",
        ["Folder with chrono files (Athlon / Garmin, .xlsx / .csv), one per string"] =
            "Cartella con file cronografo (Athlon / Garmin, .xlsx / .csv), uno per stringa",
        ["{0} flagged"] = "{0} segnalati",
        ["Pick two strings first."] = "Scegli prima due stringhe.",
        ["Pick two different strings."] = "Scegli due stringhe diverse.",
        ["Chrono Statistics note written and opened in GRT."] = "Nota Chrono Statistics scritta e aperta in GRT.",

        // CalibrationForm
        ["GRT Barrel Calibration"] = "GRT Calibrazione Canna",
        ["⚠ Ba differs from the last calibration: this file uses Ba={0}, the last calibration logged in the Journal for {1} in {2} was Ba={3} ({4}% difference)."] =
            "⚠ Ba diverso dall'ultima calibrazione: questo file usa Ba={0}, l'ultima calibrazione registrata nel Diario per {1} in {2} era Ba={3} (differenza {4}%).",
        ["Write calibration note"] = "Scrivi nota di calibrazione",
        ["Write Ba-corrected .grtload"] = "Scrivi .grtload corretto in Ba",
        ["Write Ba+a0-corrected .grtload"] = "Scrivi .grtload corretto in Ba+a0",
        ["Load measured from GRT load"] = "Carica misurati dal load GRT",
        ["Capture ALL sim MV (sweeps GRT)"] = "Cattura TUTTE le MV sim (spazza GRT)",
        ["Capture current charge only"] = "Cattura solo carica attuale",
        ["Capture shape-fit sweep (a0)"] = "Cattura sweep shape-fit (a0)",
        ["Remove row"] = "Rimuovi riga",
        ["Meas MV"] = "MV misurata",
        ["Sim MV"] = "MV simulata",
        ["Could not read the current charge (mc) from the load."] = "Impossibile leggere la carica attuale (mc) dal load.",
        ["Load the measured charges first."] = "Carica prima le cariche misurate.",
        ["This will briefly open {0} tabs in GRT (one per charge) to read each simulated MV, then reopen your load.\n\nContinue?"] =
            "Questo aprirà brevemente {0} tab in GRT (uno per carica) per leggere ogni MV simulata, poi riaprirà il tuo load.\n\nContinuare?",
        ["Capture all"] = "Cattura tutto",
        ["swept {0} charges — close the extra GRT tabs when done."] = "spazzate {0} cariche — chiudi i tab GRT extra quando hai finito.",
        ["Capture the baseline sim MV first ('Capture ALL sim MV')."] = "Cattura prima la MV sim di baseline ('Cattura TUTTE le MV sim').",
        ["No 'a0' input found in this load."] = "Nessun input 'a0' trovato in questo load.",
        ["This will briefly open {0} more tabs in GRT (a0 nudged by {1:0%} at each already-captured charge), then reopen your load.\n\nContinue?"] =
            "Questo aprirà brevemente altri {0} tab in GRT (a0 spostato di {1:0%} a ogni carica già catturata), poi riaprirà il tuo load.\n\nContinuare?",
        ["Capture shape-fit sweep"] = "Cattura sweep shape-fit",
        ["swept {0} charges at a0+{1:0%} — close the extra GRT tabs when done."] =
            "spazzate {0} cariche con a0+{1:0%} — chiudi i tab GRT extra quando hai finito.",
        ["{0} point(s), mean offset {1:+0.0;-0.0} {2} ({3:+0.0;-0.0} %)"] = "{0} punto/i, scarto medio {1:+0.0;-0.0} {2} ({3:+0.0;-0.0} %)",
        ["capture at least one charge"] = "cattura almeno una carica",
        ["Ba-corrected load written and opened in GRT."] = "Load corretto in Ba scritto e aperto in GRT.",
        ["Calibration note written."] = "Nota di calibrazione scritta.",
        ["Ba+a0-corrected load written and opened in GRT."] = "Load corretto in Ba+a0 scritto e aperto in GRT.",
        ["Calibration"] = "Calibrazione",

        // LabelForm
        ["GRT Load Card / Label"] = "GRT Cartellino / Etichetta",
        ["Recipe card (A6)"] = "Cartellino ricetta (A6)",
        ["Box labels"] = "Etichette scatola",
        ["count"] = "quantità",
        ["Powder lot"] = "Lotto polvere",
        ["Primer lot"] = "Lotto innesco",
        ["Brass lot"] = "Lotto bossolo",
        ["Bullet lot"] = "Lotto proiettile",
        ["Barrel"] = "Canna",
        ["Print…"] = "Stampa…",
        ["Save PNG…"] = "Salva PNG…",
        ["Write load-sheet note to GRT"] = "Scrivi nota load-sheet su GRT",
        ["— none —"] = "— nessuno —",
        ["Loaded: {0}, {1} {2} — from '{3}'"] = "Caricato: {0}, {1} {2} — da '{3}'",
        ["Load sheet"] = "Load sheet",
        ["load-sheet note written — opened in GRT."] = "nota load-sheet scritta — aperta in GRT.",
        ["render error:"] = "errore rendering:",
        ["saved"] = "salvato",

        // LogForm
        ["GRT Inventory"] = "GRT Inventario",
        ["stand-alone"] = "autonomo",
        ["GRT lost"] = "GRT perso",
        ["Firearms"] = "Armi",
        ["Add"] = "Aggiungi",
        ["Edit"] = "Modifica",
        ["Retire"] = "Ritira",
        ["Total rounds"] = "Colpi totali",
        ["MV entries"] = "Voci MV",
        ["Last used"] = "Ultimo uso",
        // Brass Life tab
        ["Brass Life"] = "Vita Bossoli",
        ["Mark annealed now"] = "Segna come ricotto ora",
        ["Pieces"] = "Pezzi",
        ["Rounds fired"] = "Colpi sparati",
        ["Avg uses/case"] = "Usi medi/pezzo",
        ["Since last anneal"] = "Da ultima ricottura",
        ["Anneal due"] = "Ricottura dovuta",
        ["OK"] = "OK",
        ["Status"] = "Stato",
        ["Add firearm"] = "Aggiungi arma",
        ["Edit firearm"] = "Modifica arma",
        ["Name"] = "Nome",
        ["Rounds before"] = "Colpi precedenti",
        // Pressure Signs tab
        ["Pressure Signs"] = "Segni di Pressione",
        ["Head Ø (0.200\" line)"] = "Ø testa (linea 0.200\")",
        ["Delete this reading?"] = "Eliminare questa lettura?",
        ["Add a firearm in the Firearms tab first."] = "Aggiungi prima un'arma nella scheda Armi.",
        ["No readings logged yet for this firearm. Click Add to log the first one."] =
            "Nessuna lettura registrata per quest'arma. Clicca Aggiungi per registrare la prima.",
        ["Not enough distinct charges yet for a trend ({0} of {1} needed)."] =
            "Non ci sono ancora abbastanza cariche distinte per un trend ({0} di {1} necessarie).",
        ["No accelerating expansion detected across this ladder."] =
            "Nessuna accelerazione dell'espansione rilevata in questa scaletta.",
        ["⚠ {0}→{1}: expansion rate {2}× the ladder's own prior average."] =
            "⚠ {0}→{1}: velocità di espansione {2}× la media precedente della scaletta.",
        ["Add reading"] = "Aggiungi lettura",
        ["Edit reading"] = "Modifica lettura",
        ["Add a powder in the Inventory tab first."] = "Aggiungi prima una polvere nella scheda Inventario.",
        ["Save"] = "Salva",
        ["Cancel"] = "Annulla",
        ["Inventory"] = "Inventario",
        ["Restock"] = "Rifornisci",
        ["Archive"] = "Archivia",
        ["Kind"] = "Tipo",
        ["Component"] = "Componente",
        ["Left"] = "Rimasto",
        ["Cost/unit"] = "Costo/unità",
        ["Notes"] = "Note",
        ["Brass"] = "Bossolo",
        ["Firearm"] = "Arma",
        ["Primer"] = "Innesco",
        ["Load from GRT"] = "Carica da GRT",
        ["Chrono Statistics"] = "Chrono Statistics",
        ["The active tab is a generated file — switch to your real load in GRT."] =
            "Il tab attivo è un file generato — passa al tuo load reale in GRT.",
        ["Add how many {0} to '{1}'? (negative to correct down)"] = "Aggiungi quanti {0} a '{1}'? (negativo per correggere in basso)",
        ["Journal"] = "Diario",
        ["New"] = "Nuovo",
        ["Log from GRT"] = "Registra da GRT",
        ["Delete"] = "Elimina",
        ["Date"] = "Data",
        ["Load"] = "Carico",
        ["Rounds"] = "Colpi",
        ["Cost/rd"] = "Costo/colpo",
        ["Total"] = "Totale",
        ["Delete '{0}' ({1})? Deducted stock will be restored."] = "Eliminare '{0}' ({1})? Le scorte scalate saranno ripristinate.",
        ["Find best Ba"] = "Trova miglior Ba",
        ["No journal entry carries a Ba yet, so there is no caliber to pick. In the Journal window, use "
        + "'Log from GRT' (it reads Ba from the load open in GRT), or 'Fill Ba from loads' for entries "
        + "logged before Ba was recorded, or open an entry and type it into 'Ba (calibrated)'."] =
            "Nessuna voce del diario porta ancora un Ba, quindi non c'è alcun calibro da scegliere. Nella finestra Diario, usa "
            + "'Registra da GRT' (legge il Ba dal load aperto in GRT), oppure 'Recupera Ba dai load' per le voci "
            + "registrate prima che il Ba venisse memorizzato, oppure apri una voce e scrivilo in 'Ba (calibrato)'.",
        // Backfill of Ba/a0 from the .grtload an old journal entry still points at.
        ["Fill Ba from loads"] = "Recupera Ba dai load",
        ["Read Ba back into these journal entries, from the load each one points at?"] =
            "Rileggere il Ba in queste voci del diario, dal load a cui ognuna punta?",
        ["No entry could be filled in from its load file."] =
            "Nessuna voce ha potuto essere completata dal suo file load.",
        ["Left alone:"] = "Lasciate invariate:",
        ["the load carries no Ba"] = "il load non contiene alcun Ba",
        ["the load file is no longer there"] = "il file load non c'è più",
        ["the load file would not open"] = "il file load non si è aperto",
        ["Filled in {0}. To undo one, edit that entry and set its Ba back to 0."] =
            "Completate {0}. Per annullarne una, modifica quella voce e riporta il suo Ba a 0.",
        ["Tightest group (MOA)"] = "Gruppo più stretto (MOA)",
        ["Closest velocity to target"] = "Velocità più vicina al target",
        ["Closest temperature to target"] = "Temperatura più vicina al target",
        ["Caliber"] = "Calibro",
        ["Powder"] = "Polvere",
        ["Bullet"] = "Proiettile",
        ["Rank by"] = "Ordina per",
        ["Target MV"] = "MV target",
        ["Target temp"] = "Temp target",
        ["Search"] = "Cerca",
        ["Group MOA"] = "Gruppo MOA",
        ["-- any --"] = "-- qualsiasi --",
        ["No calibrated entries match this caliber/powder/bullet combo yet."] =
            "Nessuna voce calibrata corrisponde ancora a questa combinazione calibro/polvere/proiettile.",
        ["{0} matching calibration(s), best first."] = "{0} calibrazione/i corrispondente/i, la migliore per prima.",
        ["Plot vs temperature"] = "Grafico vs temperatura",
        ["No entries with both temperature and this value logged yet."] =
            "Nessuna voce con sia la temperatura sia questo valore registrati.",

        // LoadLeaderboardForm
        ["GRT Load Leaderboard"] = "GRT Classifica delle Cariche",
        ["Rank"] = "Classifica",
        ["Sessions"] = "Sessioni",
        ["Score"] = "Punteggio",
        ["Rating"] = "Valutazione",
        // LoadScoring.ScoreLabel's own values -- kept language-agnostic in Log/, translated here
        // at the display site (same split as BaBackfill.Result's reasons).
        ["N/A"] = "N/D",
        ["Excellent"] = "Eccellente",
        ["Very good"] = "Molto buono",
        ["Good"] = "Buono",
        ["Fair"] = "Sufficiente",
        ["Needs work"] = "Da migliorare",
        ["No journal entries yet -- log a session first."] = "Nessuna voce di diario ancora -- registra prima una sessione.",
        ["{0} load(s) ranked, best first."] = "{0} carica/che classificata/e, la migliore per prima.",
        ["Nothing to rank yet -- a load needs SD, ES or a group size logged, not just a round count."] = "Niente da classificare -- una carica ha bisogno di SD, ES o dimensione del gruppo registrati, non solo del numero di colpi.",
        ["⚙ Customize thresholds"] = "⚙ Personalizza soglie",
        ["Write leaderboard note to GRT load"] = "Scrivi nota classifica sul load GRT",
        ["This load hasn't been logged in the Journal yet, so it has no leaderboard entry to write. Log it (Journal -> New / Log from GRT) first."] =
            "Questa carica non è ancora stata registrata nel Diario, quindi non ha una voce in classifica da scrivere. Registrala prima (Diario -> Nuovo / Registra da GRT).",
        ["Rank #{0} of {1} loads tested for {2}."] = "Posizione #{0} di {1} cariche testate per {2}.",
        ["Not enough data to score yet ({0} known loads for {1})."] = "Dati insufficienti per un punteggio ({0} cariche note per {1}).",
        ["Score: {0} ({1})"] = "Punteggio: {0} ({1})",
        ["Sample size"] = "Numerosità campione",
        ["Consistency"] = "Coerenza",
        ["session"] = "sessione",
        ["Leaderboard note written and opened in GRT."] = "Nota classifica scritta e aperta in GRT.",

        // LoadScoringSettingsForm
        ["GRT Load Scoring Thresholds"] = "GRT Soglie di Punteggio Cariche",
        ["Reset to defaults"] = "Ripristina i default",
        ["Up to"] = "Fino a",
        ["(worse than all above)"] = "(peggio di tutte le righe sopra)",
        ["Each row means \"at or below this value, this score\". The last row is the floor for anything worse than every other row."] =
            "Ogni riga significa \"a questo valore o meglio, questo punteggio\". L'ultima riga è il minimo per tutto ciò che è peggio delle altre righe.",

        // WorkflowForm
        ["🧭  Distance workflow"] = "🧭  Workflow distanze",
        ["GRT Distance Workflow"] = "GRT Workflow Distanze",
        ["Write workflow note to GRT load"] = "Scrivi nota workflow sul load GRT",
        ["No charges with at least {0} rounds logged at this distance yet."] =
            "Nessuna carica con almeno {0} colpi registrati a questa distanza.",
        ["{0} charge(s) ranked at this distance, best first."] = "{0} carica/che classificata/e a questa distanza, la migliore per prima.",
        ["{0} charge(s) ranked, best first ({1} excluded, fewer than {2} rounds)."] =
            "{0} carica/che classificata/e, la migliore per prima ({1} escluse, meno di {2} colpi).",
        ["Pick a caliber and distance above first."] = "Scegli prima calibro e distanza qui sopra.",
        ["This load hasn't been logged at {0} yet, so it has no workflow entry to write. Log it (Journal -> New / Log from GRT) first."] =
            "Questa carica non è ancora stata registrata a {0}, quindi non ha una voce workflow da scrivere. Registrala prima (Diario -> Nuovo / Registra da GRT).",
        ["Only {0} round(s) logged at {1} so far -- {2} are needed before this load is ranked."] =
            "Solo {0} colpo/i registrati a {1} finora -- ne servono {2} prima che questa carica venga classificata.",
        ["Distance: {0}"] = "Distanza: {0}",
        ["Rank #{0} of {1} charges tested for {2} at this distance."] = "Posizione #{0} di {1} cariche testate per {2} a questa distanza.",
        ["Not enough data to score yet ({0} known charges for {1} at this distance)."] =
            "Dati insufficienti per un punteggio ({0} cariche note per {1} a questa distanza).",
        ["Top candidate to carry forward to the next distance."] = "Miglior candidata da portare alla distanza successiva.",
        ["Workflow note written and opened in GRT."] = "Nota workflow scritta e aperta in GRT.",

        // PowderCompareForm
        ["🧪  Powder compare"] = "🧪  Confronto polveri",
        ["GRT Powder Compare"] = "GRT Confronto Polveri",
        ["Recipes"] = "Ricette",
        ["Write comparison note to GRT load"] = "Scrivi nota confronto sul load GRT",
        ["Nothing to compare yet -- a powder needs SD, ES or a group size logged, not just a round count."] =
            "Niente da confrontare -- una polvere ha bisogno di SD, ES o dimensione del gruppo registrati, non solo del numero di colpi.",
        ["{0} powder(s) compared, best first."] = "{0} polvere/i confrontata/e, la migliore per prima.",
        ["This load hasn't been logged in the Journal yet, so its powder has no comparison entry to write. Log it (Journal -> New / Log from GRT) first."] =
            "Questa carica non è ancora stata registrata nel Diario, quindi la sua polvere non ha una voce di confronto da scrivere. Registrala prima (Diario -> Nuovo / Registra da GRT).",
        ["Rank #{0} of {1} powders tried for {2}."] = "Posizione #{0} di {1} polveri provate per {2}.",
        ["Not enough data to score yet ({0} known powders for {1})."] = "Dati insufficienti per un punteggio ({0} polveri note per {1}).",
        ["Recipes tried: {0}   Sessions: {1}"] = "Ricette provate: {0}   Sessioni: {1}",
        ["This load's Ba: {0}   Average across {1} calibrated session(s): {2} ({3}{4:0.0}%)"] =
            "Ba di questa carica: {0}   Media su {1} sessione/i calibrata/e: {2} ({3}{4:0.0}%)",
        ["Comparison note written and opened in GRT."] = "Nota confronto scritta e aperta in GRT.",

        // AdvancedDiagnosticsForm
        ["🔬  Advanced diagnostics"] = "🔬  Diagnostica avanzata",
        ["GRT Advanced Diagnostics"] = "GRT Diagnostica Avanzata",
        ["Load strings from GRT load…"] = "Carica stringhe dal load GRT…",
        ["{0} string(s) loaded."] = "{0} stringa/e caricata/e.",
        ["Write diagnostics note to GRT load"] = "Scrivi nota diagnostica sul load GRT",
        ["Load strings from GRT load first."] = "Carica prima le stringhe dal load GRT.",
        ["Diagnostics note written and opened in GRT."] = "Nota diagnostica scritta e aperta in GRT.",
        ["Run at least one analysis first."] = "Esegui prima almeno un'analisi.",
        ["Analyze"] = "Analizza",
        ["Severity"] = "Gravità",
        ["Window"] = "Finestra",
        ["Shot"] = "Colpo",
        ["Jump"] = "Salto",
        ["Mismatch"] = "Incoerenza",
        ["Declining"] = "In calo",
        ["yes"] = "sì",
        ["no"] = "no",
        // Session Trend
        ["Session Trend"] = "Trend Sessioni",
        ["No charge has been logged more than once yet -- a trend needs at least 2 sessions of the same charge."] =
            "Nessuna carica è stata registrata più di una volta -- un trend richiede almeno 2 sessioni della stessa carica.",
        ["{0} charge(s) with a trend across sessions."] = "{0} carica/che con un trend tra le sessioni.",
        ["No concerning trend across any charge."] = "Nessun trend preoccupante su nessuna carica.",
        // Session Trend / severity labels (Log/SessionTrend.cs's own language-agnostic values)
        ["ok"] = "ok",
        ["warn"] = "attenzione",
        ["critical"] = "critico",
        // Pressure Trend
        ["Pressure Trend"] = "Trend di Pressione",
        ["Pressure estimate: {0}"] = "Stima pressione: {0}",
        ["The last step(s) show a flattening velocity/charge curve -- a classic sign of approaching a pressure plateau."] =
            "L'ultimo/i passo/i mostra/no un appiattimento della curva velocità/carica -- segno classico di avvicinamento a un plateau di pressione.",
        ["{0} abnormal velocity jump(s) -- check for a data entry error or real ignition instability."] =
            "{0} salto/i di velocità anomalo/i -- controlla un errore di inserimento dati o una vera instabilità di accensione.",
        ["Need at least {0} charges with a chrono string loaded. Click \"Load strings from GRT load…\" first."] =
            "Servono almeno {0} cariche con una stringa cronografo caricata. Clicca prima \"Carica stringhe dal load GRT…\".",
        // Pressure Trend / jump severity + pressure-level labels (Log/PressureTrend.cs's own values)
        ["High"] = "Alta",
        ["Medium"] = "Media",
        ["Normal"] = "Normale",
        ["Low — ample margin"] = "Bassa — margine abbondante",
        ["High — near the limit"] = "Alta — vicino al limite",
        ["Maximum — possible overpressure signal"] = "Massima — possibile segnale di sovrapressione",
        // Fouling Tracker
        ["Fouling Tracker"] = "Tracciamento Sporcizia",
        ["Fouling suspected: SD is rising as the string progresses."] =
            "Sospetto accumulo di sporcizia: la SD cresce con il progredire della stringa.",
        ["No fouling trend detected."] = "Nessun trend di accumulo sporcizia rilevato.",
        ["Velocity is also declining across the string."] = "Anche la velocità è in calo lungo la stringa.",
        ["Fouling suspected."] = "Sospetto accumulo di sporcizia.",
        // Cold Bore
        ["Cold Bore"] = "Canna Fredda",
        ["Cold-bore shots"] = "Colpi a canna fredda",
        ["Cold bore: {0} {1}, warm: {2} {1} avg (SD {3} {1}).\nDelta: {4} {1} (z={5}) -> {6}"] =
            "Canna fredda: {0} {1}, calda: {2} {1} media (SD {3} {1}).\nDifferenza: {4} {1} (z={5}) -> {6}",
        ["the cold-bore shot(s) print notably FASTER than the rest."] =
            "il/i colpo/i a canna fredda risulta/no notevolmente PIÙ VELOCE/I del resto.",
        ["the cold-bore shot(s) print notably SLOWER than the rest."] =
            "il/i colpo/i a canna fredda risulta/no notevolmente PIÙ LENTO/I del resto.",
        ["no notable cold-bore effect."] = "nessun effetto canna fredda rilevante.",
        ["outlier"] = "anomalo",
        ["normal"] = "normale",
        // Primer Sensitivity
        ["Primer Sensitivity"] = "Sensibilità Inneschi",
        ["Each string's own name is used as its primer lot."] = "Il nome di ogni stringa viene usato come suo lotto di inneschi.",
        ["Best lot: {0}."] = "Lotto migliore: {0}.",
        ["A real spread between lots was detected."] = "È stata rilevata una differenza reale tra i lotti.",
        ["No meaningful spread between lots."] = "Nessuna differenza significativa tra i lotti.",
        // Neck Tension Correlation
        ["Neck Tension"] = "Tensione al Collo",
        ["Tension (mm)"] = "Tensione (mm)",
        ["Name each string with its tested tension, e.g. \"0.05\"."] = "Assegna a ogni stringa un nome con la tensione testata, es. \"0.05\".",
        ["Correlation tension<->ES: {0}, optimal tension: {1} mm"] = "Correlazione tensione<->ES: {0}, tensione ottimale: {1} mm",
        ["Correlation tension<->SD: {0}, optimal tension: {1} mm"] = "Correlazione tensione<->SD: {0}, tensione ottimale: {1} mm",
        ["No ES/SD data on the parsed strings."] = "Nessun dato ES/SD sulle stringhe interpretate.",

        // ComponentDialog
        ["Add component"] = "Aggiungi componente",
        ["Edit component"] = "Modifica componente",
        ["Brand"] = "Marca",
        ["Lot"] = "Lotto",
        ["Qty initial"] = "Qtà iniziale",
        ["Qty current"] = "Qtà attuale",
        ["Lot cost"] = "Costo lotto",
        ["Expected uses"] = "Usi previsti",
        ["(brass: firings before retirement)"] = "(bossolo: sparate prima del ritiro)",
        ["Anneal every"] = "Ricuoci ogni",
        ["uses (0 = no reminder)"] = "usi (0 = nessun promemoria)",
        // The dialog appends GRT's own weight unit (mp, which can be grams while powder is in
        // grains), so the label is the bare words.
        ["Bullet weight"] = "Peso proiettile",
        ["Twist 1:"] = "Torsione 1:",
        ["Barrel length"] = "Lunghezza canna",
        ["Name is required."] = "Il nome è obbligatorio.",

        // JournalDialog
        ["deduct components from inventory on save"] = "scala i componenti dall'inventario al salvataggio",
        ["environment recorded"] = "ambiente registrato",
        ["New journal entry"] = "Nuova voce diario",
        ["Edit journal entry"] = "Modifica voce diario",
        ["Load name"] = "Nome carico",
        // The dialog names the unit itself now (GRT's own, not always metres), so the label is
        // the bare word with the unit appended after translation.
        ["Distance"] = "Distanza",
        ["Temperature"] = "Temperatura",
        ["Pressure"] = "Pressione",
        ["Humidity %"] = "Umidità %",
        ["Ba (calibrated)"] = "Ba (calibrato)",
        ["a0 (calibrated)"] = "a0 (calibrato)",
        ["Cost"] = "Costo",
        ["left"] = "rimasti",
        ["(powder {0:0.000}  primer {1:0.000}  bullet {2:0.000}  brass {3:0.000})"] =
            "(polvere {0:0.000}  innesco {1:0.000}  proiettile {2:0.000}  bossolo {3:0.000})",
        ["— no total until the lots match"] = "— nessun totale finché i lotti non coincidono",
        ["/ round"] = "/ colpo",
        ["   →  {0} for {1}"] = "   →  {0} per {1}",

        // WizardForm
        ["Guided New Load"] = "Nuovo carico guidato",
        ["Pick what you're taking to the range: one known charge, or a ladder of several to find the right one."] =
            "Scegli cosa porti al poligono: una carica già nota, oppure una scaletta (ladder) di più cariche per trovare quella giusta.",
        ["Single load"] = "Carica singola",
        ["Ladder test"] = "Test a scaletta (ladder)",
        ["Open"] = "Apri",
        ["1. Brass prep"] = "1. Preparazione bossoli",
        ["Case volume, seating depth, neck sizing -- done at the bench before you leave."] =
            "Volume del bossolo, profondità di seduta, dimensionamento del colletto -- fatto al banco prima di partire.",
        ["2. Load card / label"] = "2. Cartellino / etichetta carico",
        ["Print the recipe card or box label to take with you."] = "Stampa il cartellino della ricetta o l'etichetta della scatola da portare con te.",
        ["3. Barrel calibration"] = "3. Calibrazione canna",
        ["Back from the range with a real measured velocity: match GRT's model to your barrel."] =
            "Tornato dal poligono con una velocità misurata reale: allinea il modello di GRT alla tua canna.",
        ["2. Chronograph import"] = "2. Importa cronografo",
        ["Back from the range: bring in the ladder's velocities from your chronograph."] =
            "Tornato dal poligono: importa le velocità della scaletta dal tuo cronografo.",
        ["3. Ladder / OCW analyzer"] = "3. Analizzatore Ladder / OCW",
        ["Find the flat spot / node -- the one charge the rest of this load gets built around."] =
            "Trova il flat-spot / nodo -- l'unica carica attorno a cui costruire il resto del carico.",
        ["4. Barrel calibration"] = "4. Calibrazione canna",
        ["Calibrate on that one charge's own measured velocity."] = "Calibra sulla velocità misurata di quella singola carica.",
        ["5. Load card / label"] = "5. Cartellino / etichetta carico",
        ["Print the recipe card for the charge the ladder pointed at."] =
            "Stampa il cartellino della ricetta per la carica indicata dalla scaletta.",

        // VelocityModelForm
        ["GRT Velocity Model"] = "GRT Modello Velocità",
        ["Write model note to GRT load"] = "Scrivi nota modello su load GRT",
        ["Predict MV for charge"] = "Prevedi MV per carica",
        ["temperature"] = "temperatura",
        ["Predict"] = "Prevedi",
        ["Charge needed for MV"] = "Carica necessaria per MV",
        ["Solve"] = "Risolvi",
        ["Predicted"] = "Prevista",
        ["Residual"] = "Residuo",
        ["No journal entry yet carries charge, temperature AND a measured velocity together -- log a session with all three (Journal -> New, or Log from GRT + fill in temperature) to use this model."] =
            "Nessuna voce del diario porta ancora carica, temperatura E una velocità misurata insieme -- registra una sessione con tutte e tre (Diario -> Nuovo, oppure Registra da GRT + compila la temperatura) per usare questo modello.",
        ["Only {0} logged session(s) with charge+temperature+velocity for this combo -- need at least {1} to fit a 2-variable model."] =
            "Solo {0} sessione/i registrata/e con carica+temperatura+velocità per questa combinazione -- ne servono almeno {1} per adattare un modello a 2 variabili.",
        ["Every logged session used the same charge -- nothing to fit a charge effect from. Log sessions at different charges."] =
            "Ogni sessione registrata usa la stessa carica -- niente da cui stimare un effetto della carica. Registra sessioni con cariche diverse.",
        ["Every logged session was at the same temperature -- nothing to fit a temperature effect from. Log sessions at different temperatures."] =
            "Ogni sessione registrata è alla stessa temperatura -- niente da cui stimare un effetto della temperatura. Registra sessioni a temperature diverse.",
        ["Charge and temperature moved together across every logged session (e.g. always tested colder at lower charges) -- there's no way to tell the two effects apart from this data."] =
            "Carica e temperatura si sono mosse insieme in ogni sessione registrata (es. sempre testato più freddo con cariche più basse) -- non c'è modo di distinguere i due effetti da questi dati.",
        ["MV ≈ {0} + {1}·charge(gr) + {2}·temp(°C)   ·   n={3}   ·   R²={4}"] =
            "MV ≈ {0} + {1}·carica(gr) + {2}·temp(°C)   ·   n={3}   ·   R²={4}",
        ["Fitted on {0} session(s) for {1}."] = "Adattato su {0} sessione/i per {1}.",
        ["Fit the model first."] = "Calcola prima il modello.",
        ["≈ {0} m/s"] = "≈ {0} m/s",
        ["Charge has no measurable effect in this fit -- can't solve for it."] =
            "La carica non ha un effetto misurabile in questo modello -- impossibile risolvere.",
        ["≈ {0} gr"] = "≈ {0} gr",
        ["Empirical fit for {0}, from {1} logged session(s):"] = "Modello empirico per {0}, da {1} sessione/i registrata/e:",
        ["Independent of GRT's own model (no Ba/a0/tcc/tch) -- a measured-data cross-check, not a replacement for calibration."] =
            "Indipendente dal modello di GRT (nessun Ba/a0/tcc/tch) -- un controllo incrociato sui dati misurati, non un sostituto della calibrazione.",
        ["Model note written and opened in GRT."] = "Nota del modello scritta e aperta in GRT.",
        // SdRootCauseForm
        ["🌡🎯  SD root-cause"] = "🌡🎯  Diagnosi SD",
        ["SD Root-Cause"] = "Diagnosi SD",
        ["Write root-cause note to GRT load"] = "Scrivi nota diagnosi sul load GRT",
        ["{0} string(s), {1} shot-group tab(s) loaded."] = "{0} stringa/e, {1} tab gruppo caricate.",
        ["Cold-bore shots (0 = none marked)"] = "Colpi a canna fredda (0 = nessuno)",
        ["Shot-group tab (optional, for group dispersion)"] = "Tab gruppo (opzionale, per la dispersione)",
        ["hits"] = "colpi",
        ["Per-shot barrel temperature (optional — probe reading at each shot)"] = "Temperatura canna per colpo (opzionale — lettura sonda ad ogni colpo)",
        ["without"] = "senza",
        ["Reduction"] = "Riduzione",
        ["Outlier?"] = "Anomalo?",
        ["Ranking"] = "Classifica",
        ["SD contribution"] = "Contributo alla SD",
        ["Temp → velocity"] = "Temp → velocità",
        ["Temp → group"] = "Temp → gruppo",
        ["Run the analysis first."] = "Esegui prima l'analisi.",
        ["No per-shot temperature entered for this string."] = "Nessuna temperatura per colpo inserita per questa stringa.",
        ["No matching shot-group data for this string."] = "Nessun dato di gruppo corrispondente per questa stringa.",
        ["one shot statistically stands out (Chauvenet)"] = "un colpo si distingue statisticamente (Chauvenet)",
        ["a few shots statistically stand out (Chauvenet)"] = "alcuni colpi si distinguono statisticamente (Chauvenet)",
        ["no single shot statistically stands out -- dispersion is spread across the string"] =
            "nessun colpo si distingue statisticamente -- la dispersione è distribuita su tutta la stringa",
        ["SD = {0} {1}: {2}."] = "SD = {0} {1}: {2}.",
        ["Shot #{0} contributes the most: removing it alone would take the SD to {1} {2} (a {3:0}% reduction)."] =
            "Il colpo #{0} contribuisce di più: rimuovendolo solo, la SD scenderebbe a {1} {2} (una riduzione del {3:0}%).",
        ["Shot #{0} contributes the most: removing it alone would take the SD to {1} {2} ({3:0}% HIGHER, not lower)."] =
            "Il colpo #{0} contribuisce di più: rimuovendolo solo, la SD salirebbe a {1} {2} ({3:0}% PIÙ ALTA, non più bassa).",
        ["Cold-bore shot(s) print {0} than the rest by {1} {2} (z={3})."] =
            "Il/i colpo/i a canna fredda escono {0} rispetto al resto di {1} {2} (z={3}).",
        ["faster"] = "più veloce/i",
        ["slower"] = "più lento/i",
        ["No notable cold-bore effect."] = "Nessun effetto canna fredda rilevante.",
        ["Temperature <-> velocity correlation: r={0}."] = "Correlazione temperatura <-> velocità: r={0}.",
        ["Temperature <-> group-dispersion correlation: r={0}{1}"] = "Correlazione temperatura <-> dispersione gruppo: r={0}{1}",
        ["-- this barrel may be opening groups up as it warms."] = "-- questa canna potrebbe allargare i gruppi scaldandosi.",
        ["Probable unmeasured thermal/fouling cause: SD rises as the string progresses (no per-shot temperature was entered for this string)."] =
            "Probabile causa termica/incrostazione non misurata: la SD sale mentre la stringa procede (nessuna temperatura per colpo inserita per questa stringa).",
        ["No progressive drift detected across the string."] = "Nessuna deriva progressiva rilevata nella stringa.",
        ["Note (informal convention, not a reloading-specific benchmark): the velocity distribution is notably skewed ({0})."] =
            "Nota (convenzione informale, non uno standard specifico per la ricarica): la distribuzione delle velocità è notevolmente asimmetrica ({0}).",
        ["Root-cause note written and opened in GRT."] = "Nota diagnosi scritta e aperta in GRT.",
        ["Assumes the shot-group tab's hit order matches this Measurement's firing order -- GRT does not link them itself."] =
            "Presuppone che l'ordine dei colpi nella tab gruppo corrisponda all'ordine di sparo di questa misurazione -- GRT non le collega da solo.",
        ["{0} chronographed shot(s) but {1} hit(s) in the shot-group tab -- cannot align."] =
            "{0} colpo/i cronografato/i ma {1} colpo/i nella tab gruppo -- impossibile allineare.",

        // GroupAnalysisForm
        ["🎯📐  Group analysis"] = "🎯📐  Analisi gruppo",
        ["Group Analysis"] = "Analisi Gruppo",
        ["Load GRT's shot-group tabs…"] = "Carica tab gruppo da GRT…",
        ["Load Athlon velocities…"] = "Carica velocità Athlon…",
        ["Folder with Athlon *.xlsx velocity files (one per charge) — can be a different folder than the OnTarget one"] =
            "Cartella con i file *.xlsx Athlon (uno per carica) — può essere diversa da quella OnTarget",
        ["Matched Athlon velocities to {0} of {1} loaded group(s)."] = "Velocità Athlon abbinate a {0} gruppi su {1} caricati.",
        ["Load OnTarget CSV…"] = "Carica CSV OnTarget…",
        ["⚙ Thresholds…"] = "⚙ Soglie…",
        ["Write group analysis note to GRT load"] = "Scrivi nota analisi gruppo sul load GRT",
        ["Group"] = "Gruppo",
        ["Distance range"] = "Fascia di distanza",
        ["Auto (from the group's own distance)"] = "Automatica (dalla distanza del gruppo)",
        ["Short range"] = "Corta distanza",
        ["Long range"] = "Lunga distanza",
        ["or enter a group manually"] = "oppure inserisci un gruppo a mano",
        ["Shooting distance"] = "Distanza di tiro",
        ["Per-shot offsets from centre"] = "Scostamenti per colpo dal centro",
        ["Aim X/Y"] = "Mira X/Y",
        ["Build group from manual entry"] = "Costruisci gruppo dai dati inseriti",
        ["{0} shot-group tab(s) loaded."] = "{0} tab gruppo caricate.",
        ["Loaded {0} impacts from {1}."] = "Caricati {0} colpi da {1}.",
        ["Enter at least one shot's X/Y first."] = "Inserisci prima almeno un colpo X/Y.",
        ["Load or build a group first."] = "Carica o costruisci prima un gruppo.",
        ["Score {0}/10, Confidence: {1} ({2}, n={3}, {4})."] = "Punteggio {0}/10, Confidenza: {1} ({2}, n={3}, {4}).",
        // "High"/"Medium" reuse Log/PressureTrend.cs's own labels above; only "Low" is new here.
        ["Low"] = "Bassa",
        ["Mean radius {0} MOA · Extreme spread {1} MOA · CEP50 {2} MOA"] =
            "Raggio medio {0} MOA · Extreme spread {1} MOA · CEP50 {2} MOA",
        ["Horizontal spread {0} MOA · Vertical spread {1} MOA"] = "Disp. orizzontale {0} MOA · Disp. verticale {1} MOA",
        ["No problems found."] = "Nessun problema rilevato.",
        ["Problems found:"] = "Problemi rilevati:",
        ["Group analysis note written and opened in GRT."] = "Nota analisi gruppo scritta e aperta in GRT.",
        ["Load a group first."] = "Carica prima un gruppo.",
        ["Load OnTarget folder…"] = "Carica cartella OnTarget…",
        ["Folder with one OnTarget *.csv per charge (Athlon *.xlsx alongside is used to label it, not required)"] =
            "Cartella con un *.csv OnTarget per carica (gli *.xlsx Athlon accanto servono solo per l'etichetta, non obbligatori)",
        ["{0} charge group(s) loaded from folder."] = "{0} gruppi per carica caricati dalla cartella.",
        ["Combine all loaded groups"] = "Unisci tutti i gruppi caricati",
        ["Load at least two groups first."] = "Carica prima almeno due gruppi.",
        ["combined ({0} groups)"] = "combinato ({0} gruppi)",
        ["Charge (gr)"] = "Carica (gr)",
        ["Hits"] = "Colpi",
        ["Chrono shots"] = "Colpi cronografati",
        ["Source"] = "Fonte",
        ["manual entry"] = "inserimento manuale",
        ["paste supported"] = "incolla supportato",
        ["Per-shot velocity <-> distance-from-centre correlation: r={0}."] = "Correlazione velocità per colpo <-> distanza dal centro: r={0}.",

        // GroupAnalysisSettingsForm
        ["Group Analysis Thresholds"] = "Soglie Analisi Gruppo",
        ["Short range score"] = "Punteggio corta distanza",
        ["Long range score"] = "Punteggio lunga distanza",
        ["Confidence = Medium at (short range) shots"] = "Confidenza = Media a (corta distanza) colpi",
        ["Confidence = High at (short range) shots"] = "Confidenza = Alta a (corta distanza) colpi",
        ["Confidence = Medium at (long range) shots"] = "Confidenza = Media a (lunga distanza) colpi",
        ["Confidence = High at (long range) shots"] = "Confidenza = Alta a (lunga distanza) colpi",
        ["Minimum shots for outlier detection"] = "Colpi minimi per il rilevamento anomalie",
        ["Off-centre flag threshold (MOA)"] = "Soglia segnalazione fuori centro (MOA)",
        ["Spread-ratio skew flag threshold"] = "Soglia segnalazione asimmetria dispersione",
        ["App-centre mismatch flag threshold (MOA)"] = "Soglia segnalazione scarto centro applicazione (MOA)",
        ["Velocity <-> dispersion correlation flag threshold (|r|)"] = "Soglia segnalazione correlazione velocità <-> dispersione (|r|)",
        ["Each score row means \"at or below this mean-radius MOA, this score\". The last row is the floor for anything worse."] =
            "Ogni riga significa \"fino a questo raggio medio in MOA, questo punteggio\". L'ultima riga è il minimo per tutto ciò che è peggiore.",

        // TargetForm
        ["🎯🖨  Print ladder/OCW target"] = "🎯🖨  Stampa bersaglio ladder/OCW",
        ["Print Ladder/OCW Target"] = "Stampa Bersaglio Ladder/OCW",
        ["OCW (round-robin group per charge)"] = "OCW (gruppo round-robin per carica)",
        ["Ladder / Audette (repeated shot per charge, ~200-300 m)"] = "Ladder / Audette (colpo/i ripetuti per carica, ~200-300 m)",
        ["Shots per aim point (1-5)"] = "Colpi per mira (1-5)",
        ["Target type"] = "Tipo di bersaglio",
        ["Number of charges"] = "Numero di cariche",
        ["Charge weights (gr, comma-separated, optional)"] = "Pesi delle cariche (gr, separati da virgola, opzionale)",
        ["per page (0 = all)"] = "per pagina (0 = tutte)",
        ["Distance (m, optional)"] = "Distanza (m, opzionale)",
        ["Firearm (optional)"] = "Arma (opzionale)",
        ["Powder (optional)"] = "Polvere (opzionale)",
        ["Bullet (optional)"] = "Palla (opzionale)",
        ["Preview page"] = "Pagina anteprima",
        ["Page {0} of {1}."] = "Pagina {0} di {1}.",
        ["Needs A3 paper. Print at 100% / \"actual size\", never \"fit to page\". After printing, measure the 100 mm bar at the bottom with a ruler before shooting."] =
            "Serve carta A3. Stampa al 100% / \"dimensioni reali\", mai \"adatta alla pagina\". Dopo la stampa, misura con un righello la barra da 100 mm in basso prima di sparare.",
        ["Save PDF…"] = "Salva PDF…",
        ["\"Microsoft Print to PDF\" is not installed on this PC. Use Print… instead and choose a printer that supports A3 paper."] =
            "\"Microsoft Print to PDF\" non è installato su questo PC. Usa Stampa… scegliendo una stampante che supporti la carta A3.",
        ["The selected printer has no A3 paper size, so printing now would come out at the wrong scale with overlapping text. Use \"Save PDF…\" instead (always true A3), or pick a printer that supports A3. Continue anyway?"] =
            "La stampante selezionata non ha il formato A3, quindi la stampa uscirebbe in scala sbagliata con testo sovrapposto. Usa \"Salva PDF…\" (sempre in vero A3), oppure scegli una stampante che supporti l'A3. Continuare comunque?",

        // LoadBookForm
        ["📚  Load book export"] = "📚  Esporta libro carichi",
        ["Load Book Export"] = "Esporta Libro Carichi",
        ["Pick folder…"] = "Scegli cartella…",
        ["Export HTML…"] = "Esporta HTML…",
        ["Folder with .grtload files to include (searched recursively)"] =
            "Cartella con i file .grtload da includere (cercati anche nelle sottocartelle)",
        ["Velocity"] = "Velocità",
        ["file only"] = "solo file",
        ["(error)"] = "(errore)",
        ["{0} recipes found."] = "{0} ricette trovate.",
        ["Nothing to export yet — pick a folder first."] = "Niente da esportare — scegli prima una cartella.",
        ["Load Book"] = "Libro Carichi",
    };
}
