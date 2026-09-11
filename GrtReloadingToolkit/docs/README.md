# GRT Reloading Toolkit

Community plugin for [Gordon's Reloading Tool](https://grtools.de). One plugin, eight tools.
GRT shows a **single toolbar / menu entry** ("Reloading Toolkit") that opens a launcher window;
every tool is a button there. One background process serves all of them.

| Tool | What it does |
|---|---|
| 🎯 **Chronograph Import** | **Athlon Rangecraft** and **Garmin Xero C1 Pro / ShotView** exports (`.xlsx` / `.csv`) → GRT Measurements tab (a batch = a ladder). Pick files or **a whole folder** (one file per charge). Auto-detects fps vs m/s (from the header, or the velocity magnitude), tolerates EU/US number formats and the summary footer. Per-string °C (Athlon) is carried into the charge note for GRT's temp-coefficient assistant. |
| 📈 **Ladder / OCW Analyzer** | Charge ladder (chrono velocities + target groups) → velocity flat-spot (Satterlee) + vertical-POI node (OCW/Audette) + weighted best node → an *OCW Analysis* note **and a chart** in the load. Groups come from Ballistic-X `.csv` **or straight from GRT's own "Shot group" tabs** in the load ("Groups from GRT load", with reference/shooting-distance units and an optional drop-flyers toggle). |
| 📏 **Seating-Depth Analyzer** | Seating / jump test → group-size plateau + vertical-POI node → *Seating Depth Analysis* note + chart. Same group sources as the OCW analyzer. |
| 🎚 **Barrel Calibration** | Compares GRT's simulated MV (captured live via IPC) to your measured MV over one or more charges → mean offset, scale-vs-shape verdict, suggested `Ba` tweak. Can write a `Ba`-corrected `.grtload`. |
| 🌡 **Powder Temp Coefficients** | Fits `tcc` / `tch` from Athlon strings shot at different temperatures (GRT's `Ba(T) ≈ Ba·(MV(T)/MV₂₁)²` formalism). Writes `tcc`/`tch` into the propellant. |
| 🔧 **Brass Prep** | *Case volume* — N case water weights (grain H₂O **or** grams) → mean / SD volume, written into the caliber's `casevol`. *Seating depth* — CBTO + case length + BBTO (comparator measurements) → GRT `gdepth`, written into the load. *Neck / bushing* — bullet dia + neck wall + desired interference → bushing & mandrel size, **in inch** by default (toggle to mm). |
| 🏷 **Load Card / Label** | Printable A6 recipe card / ammo-box labels with a QR of the full recipe. Component-lot dropdowns compute **cost per round**. "Write load-sheet note" puts the recipe + cost breakdown into the load. |
| 📒 **Inventory & Journal** | Component inventory (powder / primer / brass / bullet) with lot tracking and cost per round, a load/range journal that deducts stock, and a **Firearms** tab: round-count per barrel + MV-drift chart. SQLite in `%AppData%\GRTPlugins\`. |

Plus **📄 Install GRT report templates** (launcher button / Plugins menu): writes five DokuWiki
report pages into `GRT\doku\<lang>\report\` and links them from the Reports index —

- *Toolkit — Ladder / OCW report* (pulls the OCW note + chart)
- *Toolkit — Seating-depth report*
- *Toolkit — Barrel calibration report* (calibration + temp-coefficient notes)
- *Toolkit — Load sheet* (recipe + predicted results + cost)

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
GRT_Reloading_Toolkit.exe --dbtest [db-path]
GRT_Reloading_Toolkit.exe --ladder charge|seating <folder> [base.grtload]
GRT_Reloading_Toolkit.exe --ladder charge|seating --synthetic [out.png|base.grtload]
GRT_Reloading_Toolkit.exe --groups <load.grtload> [mm|cm|in] [m|yd] [--drop-flyers]
GRT_Reloading_Toolkit.exe --cal <port> <base> <charge:simMv> ...
GRT_Reloading_Toolkit.exe --tcoeff <Ba> <file.xlsx:tempC> ...
GRT_Reloading_Toolkit.exe --card <base.grtload> [outDir]
GRT_Reloading_Toolkit.exe --brass vol gr|g <w> ...  |  --brass neck <dia> <wall> <interf> [loadedOd]  |  --brass seat <cbto> <caselen> <bbto> [mm|in]
GRT_Reloading_Toolkit.exe --athlon <folder> <base> [tempC]
GRT_Reloading_Toolkit.exe --reports [grtRoot]
```

Env: `OCW_FOLDER` / `SEATING_FOLDER` auto-load a ladder; `RELOADING_LOG_DB` overrides the DB path.

## Status

Every tool verified headless (parsers, analysis, DB + cost model, chart rendering) and each window
opens and routes correctly in one process. The merged build has **not yet been clicked through a
running GRT** — toolbar id routing and the `Load_File` round-trip for this build are unverified
live (the earlier standalone Athlon build's manifest + IPC round-trip did work).
