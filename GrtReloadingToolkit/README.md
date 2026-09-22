# GRT Reloading Toolkit

Community plugin for [Gordon's Reloading Tool](https://grtools.de). One plugin, eighteen tools.
GRT shows a **single toolbar / menu entry** ("Reloading Toolkit") that opens a launcher window;
every tool is a button there. One background process serves all of them.

| Tool | What it does |
|---|---|
| 🪄 **Guided New Load** | A checklist for building a load from scratch — opens the real tool for each step (never reimplements one) and ticks it once opened from here. Two paths: **single load** (brass prep → card → calibration) or **ladder test** (brass prep → chrono import → Ladder/OCW → calibration → card, printed last for whichever charge the ladder pointed at). No persistence, no verified-done tracking — a ticked box just means "opened this session". |
| 🎯 **Chronograph Import** | **Athlon Rangecraft** and **Garmin Xero C1 Pro / ShotView** exports (`.xlsx` / `.csv`) → GRT Measurements tab (a batch = a ladder). Pick files or **a whole folder** (one file per charge). Auto-detects fps vs m/s (from the header, or the velocity magnitude), tolerates EU/US number formats and the summary footer. Per-string °C (Athlon) is carried into the charge note for GRT's temp-coefficient assistant. |
| 📐 **Chronograph Statistics** | Confidence interval on the true mean, shots needed for a target margin, Chauvenet-flagged outliers, and a Welch t-test / F-test comparing any two strings — everything GRT's own AVG/SD/ES line doesn't tell you. Reads the same chrono files, or pulls strings straight from the load's Measurement. |
| 📈 **Ladder / OCW Analyzer** | Charge ladder (chrono velocities + target groups) → velocity flat-spot (Satterlee) + vertical-POI node (OCW/Audette) + weighted best node → an *OCW Analysis* note **and a chart** in the load. Groups come from Ballistic-X `.csv` **or straight from GRT's own "Shot group" tabs** in the load ("Groups from GRT load", with reference/shooting-distance units and an optional drop-flyers toggle). Missing velocities are filled in from the load's own chrono Measurement. |
| 📏 **Seating-Depth Analyzer** | Seating / jump test → group-size plateau + vertical-POI node → *Seating Depth Analysis* note + chart. Same group sources as the OCW analyzer. |
| 🎚 **Barrel Calibration** | Compares GRT's simulated MV (captured live via IPC) to your measured MV over one or more charges → mean offset, scale-vs-shape verdict, suggested `Ba` tweak. Can write a `Ba`-corrected `.grtload`. When the offset varies with charge (a shape error `Ba` alone can't fix), an optional **a0 shape-fit sweep** fits `Ba` **and** `a0` together and can write a `Ba`+`a0`-corrected `.grtload`. |
| 🌡 **Powder Temp Coefficients** | Fits `tcc` / `tch` from Athlon strings shot at different temperatures (GRT's `Ba(T) ≈ Ba·(MV(T)/MV₂₁)²` formalism). Writes `tcc`/`tch` into the propellant. |
| 🔧 **Brass Prep** | *Case volume* — N case water weights (grain H₂O **or** grams) → mean / SD volume, written into the caliber's `casevol`. *Seating depth* — CBTO + case length + BBTO (comparator measurements) → GRT `gdepth`, written into the load. *Neck / bushing* — bullet dia + neck wall + desired interference → bushing & mandrel size, **in inch** by default (toggle to mm). |
| ⚖️ **Seating Force Estimate (QC)** | Standalone estimator for expected bullet-seating (press-fit) force, for a QC press's own max-pressure safety threshold — baseline force for a "typical" setup plus your actual prep (annealing, sizing, lube, coating, boat-tail) → mean force, 1σ/2σ bands, suggested max. Every length field has its own mm/in toggle. The bar/psi figure alongside it is the same force over the bullet's cross-section, **not** a GRT input and not a substitute for GRT's own Initial Pressure. |
| 🏷 **Load Card / Label** | Printable A6 recipe card / ammo-box labels with a QR of the full recipe. Component-lot dropdowns compute **cost per round**, and a barrel pick adds its twist and length to the card. "Write load-sheet note" puts the recipe + cost breakdown into the load. |
| 📒 **Inventory** | Component inventory (powder / primer / brass / bullet / barrel) with lot tracking, brand pick-lists, powder counted in **g, gr, lb or kg**, twist and length on barrels, and cost per round; a **Firearms** tab (round-count per barrel + MV-drift chart); a **Brass Life** tab (avg uses/case, anneal-due and retirement-due status per lot, from Journal-logged round counts). SQLite in `%AppData%\GRTPlugins\`, shared with Load Journal and Find best Ba. |
| 📓 **Load Journal** | A load/range journal (its own window, split out of Inventory) that deducts stock and records the session's environment (temperature/pressure/humidity, own units) plus the calibrated `Ba`/`a0`. "Log from GRT" prefills from the open load. A **Fill Ba from loads** migration backfills `Ba`/`a0` for pre-`Ba` entries from the `.grtload` each one still points at. |
| 🔎 **Find best Ba** | A search (its own window) over the Journal's calibrated entries: filter by caliber+powder+bullet, rank by tightest group / closest velocity / closest temperature, plus a Ba/velocity/group vs temperature chart. Read-only. |
| 🏆 **Load Leaderboard** | Ranks *every* load ever logged (grouped by caliber+powder+bullet+charge) by a composite 0-10 score — SD, ES, group size, sample size, cross-session consistency, simple average of whichever are available. Default thresholds are anchored to real competitive/military references (Bryan Litz/Applied Ballistics, Mk 316 Mod 0 and M118LR sniper-ammo specs, published benchrest/hunting benchmarks) and are fully **editable** (⚙ Customize thresholds), saved to `%AppData%\GRTPlugins\load-scoring.json`. "Write leaderboard note to GRT load" finds the open load's own rank/score among same-caliber loads and writes the full breakdown into a note. |
| 🧭 **Distance Workflow** | Same 0-10 score and grid as Load Leaderboard, but a different question: not "what's the best load overall", but "of what I tested *today at this distance*, which earns a test at the next distance" (100m → 300m, say). Scoped to one caliber + one distance you've logged, no cross-session consistency term (a same-day call shouldn't be discounted for a load's history), and charges under 3 logged rounds are excluded from the ranking rather than scored low. "Write workflow note to GRT load" finds the open load's rank at whichever distance is selected and writes the breakdown into a note. |
| 🧪 **Powder Compare** | Same 0-10 scoring, one level coarser than Leaderboard: groups by caliber+powder only, over powders you've actually calibrated (GRT's own factory propellant data isn't reachable via IPC). "Write comparison note" also flags how the open load's own `Ba` compares to the average of every other calibration of that powder. |
| 📉 **Velocity Model** | An empirical `MV ≈ a + b·charge + c·temperature` fit from the Journal's own logged sessions, independent of GRT's physics model (no `Ba`/`a0`/`tcc`/`tch`). Needs ≥5 sessions with charge, temperature and velocity all logged, and charge/temperature varying independently — refuses to fit (and says why) otherwise. Includes a predictor (charge+temp → MV) and a solver (target MV+temp → needed charge). Always in native grains/°C/m/s. |
| 🔬 **Advanced Diagnostics** | Six diagnostics in one tool, none needing a calibrated `Ba`: Fouling Tracker, Cold Bore, Primer Sensitivity, Neck Tension Correlation (five, reading chrono strings from the open load) and Session Trend (reading the Journal, for drift across repeated sessions of the same charge). |

Plus **📄 Install GRT report templates** (launcher button / Plugins menu): writes fourteen DokuWiki
report pages into `GRT\doku\<lang>\report\` and links them from the Reports index —

- *Toolkit — Full load workup* (recipe + predicted results + every section below, in one page)
- *Toolkit — Chronograph statistics report*
- *Toolkit — Ladder / OCW report* (pulls the OCW note + chart)
- *Toolkit — Seating-depth report*
- *Toolkit — Barrel calibration report* (calibration note — `Ba`, or the `Ba`+`a0` shape fit — + temp-coefficient note)
- *Toolkit — Powder temp-coefficients report*
- *Toolkit — Case volume report*
- *Toolkit — Seating depth (geometry) report*
- *Toolkit — Load sheet* (recipe + predicted results + cost)
- *Toolkit — Load leaderboard report* (rank/score note)
- *Toolkit — Distance workflow report* (rank/score note at one distance)
- *Toolkit — Powder compare report* (rank/score note)
- *Toolkit — Advanced diagnostics report* (all six diagnostics tabs)
- *Toolkit — Velocity model report* (the empirical fit)

Every note-writing tool has its own dedicated page now, except Seating Force Estimate (QC) and the
Guided New Load wizard — the QC estimator has no GRT linkage at all, and the wizard only opens
other tools, it never writes anything of its own.

Run it once after installing the plugin. It is idempotent and non-destructive; delete a page by
saving it empty in GRT.

## Install

Copy the `ReloadingToolkit` folder into `<GRT>\plugins\` and restart GRT. Then open the Plugins
menu → *Install GRT report templates* if you want the report pages.

Two builds are provided:

| Build | Size | Runtime |
|---|---|---|
| **`ReloadingToolkit`** (self-contained) | ~64 MB | none — runs anywhere |
| **`ReloadingToolkit-lite`** (framework-dependent) | ~1.5 MB | needs **.NET 8 Desktop Runtime** ([get it here](https://dotnet.microsoft.com/download/dotnet/8.0) → *.NET Desktop Runtime 8.0.x*, Windows x64) |

Use `lite` if you need to attach the plugin somewhere with a size limit; otherwise the
self-contained one is simplest.

```
dotnet build -c Release
pwsh ./build-plugin.ps1 -GrtDir "C:\path\to\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
```

## Notes / conventions

- The GRT plugin API can't edit the open load, so every "Write … to GRT load" writes a
  **timestamped snapshot** `<load>_toolkit_<date>.grtload` next to your file (your original is
  never touched). Every tool accumulates into the same family — each snapshot is complete (all
  notes/edits so far) — and only the 3 newest are kept. A fresh name each time is needed because
  GRT's `Load_File` won't reload a path it already has open. Ba / tcc-tch / casevol are read from
  the pristine original so a second calibration never compounds. Keep the newest snapshot (Save As
  in GRT under a real name) and delete the rest.
- Numbers in notes and reports are ISO (dot decimal), as GRT's report engine requires.
- Case volume is entered/displayed in **grain H₂O** (reloader convention); GRT stores `casevol`
  internally in cm³ and the plugin converts on write.

## Debug CLIs

```
GRT_Reloading_Toolkit.exe --dbtest [db-path]   # fixture data goes to [db-path], or a temp file if omitted -- never your real DB
GRT_Reloading_Toolkit.exe --ladder charge|seating <folder> [base.grtload]
GRT_Reloading_Toolkit.exe --ladder charge|seating --synthetic [out.png|base.grtload]
GRT_Reloading_Toolkit.exe --groups <load.grtload> [mm|cm|in] [m|yd] [--drop-flyers]
GRT_Reloading_Toolkit.exe --cal <port> <base> <charge:simMv> ...
GRT_Reloading_Toolkit.exe --tcoeff <Ba> <file.xlsx:tempC> ...
GRT_Reloading_Toolkit.exe --card <base.grtload> [outDir]
GRT_Reloading_Toolkit.exe --brass vol gr|g <w> ...  |  --brass neck <dia> <wall> <interf> [loadedOd]  |  --brass seat <cbto> <caselen> <bbto> [mm|in]
GRT_Reloading_Toolkit.exe --athlon <folder> <base> [tempC]
GRT_Reloading_Toolkit.exe --chronostats <folder> [base.grtload] [marginMps]
GRT_Reloading_Toolkit.exe --reports [grtRoot]
```

Env: `OCW_FOLDER` / `SEATING_FOLDER` auto-load a ladder; `RELOADING_LOG_DB` overrides the DB path;
`GRT_OCW_CHART_SIZE=WxH` sets the exported OCW/seating chart pixel size (default `1400x1040`).

## Status

Live in the community — see the repo root [README](../README.md) and
[releases](https://github.com/FREESHOTER/GRT-Reloading-Toolkit/releases) for the current version
and what changed. `dotnet test` covers the ladder/OCW node analysis, the chrono statistics, the
inventory ledger and the xlsx reader; see `GrtReloadingToolkit.Tests/`.
