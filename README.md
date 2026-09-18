# GRT Reloading Toolkit

[![Latest release](https://img.shields.io/github/v/release/FREESHOTER/GRT-Reloading-Toolkit?label=latest%20release&color=brightgreen)](https://github.com/FREESHOTER/GRT-Reloading-Toolkit/releases/latest)

A community plugin for [Gordon's Reloading Tool](https://grtools.de) that bundles twelve small
reloading tools into one window and writes their results back into your load — as notes, charts,
and corrected inputs (`Ba`, `a0`, `casevol`, `gdepth`, `tcc`/`tch`) — plus a set of installable GRT
report templates.

**⬇️ [Download the latest release](https://github.com/FREESHOTER/GRT-Reloading-Toolkit/releases/latest)**
— grab `ReloadingToolkit.zip` (or the smaller `-lite.zip`, see [Install](#install-users) below) from
the Assets list on that page.

> **Status: v0.2.6.** Tested against my own loads (6.5 Creedmoor / VV N550). Not every path with
> every export format has been exercised. Bug reports and Garmin sample exports very welcome —
> please open an issue.

## Tools

| | |
|---|---|
| **Chronograph Import** | Athlon Rangecraft and Garmin Xero C1 Pro / ShotView exports (`.xlsx` / `.csv`), one file or a whole folder → a GRT Imported Measurement. Auto-detects fps vs m/s. |
| **Chronograph Statistics** | Confidence interval on the true mean, shots needed for a target margin, Chauvenet-flagged outliers, Welch t-test / F-test comparing any two strings. |
| **Ladder / OCW Analyzer** | Velocity flat-spot (Satterlee) + vertical-POI node (OCW / Audette) + a weighted best node → an *OCW Analysis* note and chart. Groups from Ballistic-X CSV or GRT's own shot-group tabs. |
| **Seating-Depth Analyzer** | Group-size plateau + vertical-POI node for a seating / jump test. |
| **Barrel Calibration** | Sweeps GRT for the simulated MV at every measured charge, compares to your chrono, suggests a `Ba` tweak (`Ba × (meas ÷ sim)²`); when the offset varies with charge, an optional `a0` shape-fit sweep fits `Ba` and `a0` together. |
| **Powder Temp Coefficients** | Fits `tcc` / `tch` from one charge shot cold, normal and hot. |
| **Brass Prep** | Case volume (grain H₂O) → `casevol`; CBTO / case length / BBTO → `gdepth`; bushing & mandrel sizing for a target neck grip. |
| **Seating Force Estimate (QC)** | Standalone estimator for expected bullet-seating force, for a QC press's own max-pressure threshold — not a GRT input. |
| **Load Leaderboard** | Ranks every load in the journal — grouped by caliber + powder + bullet + charge — on a composite 0-10 score built from SD, ES, group size, round count and cross-session consistency. Thresholds are citable defaults and editable. |
| **Distance Workflow** | The same scoring engine asked a different question: of the charges logged at *one* distance, which earn a test at the next one. No history term, and groups under 3 rounds are left out. |
| **Load Card / Label** | Printable A6 recipe card or box labels with a QR of the recipe; cost per round from component lots. |
| **Inventory & Load Journal** | Component stock with lot tracking and cost/round, a range journal (with environment + calibrated `Ba`/`a0` per entry) that deducts stock, a **Find best Ba** search, and per-barrel round count + MV-drift chart. SQLite. |
| **Install GRT report templates** | Writes eleven DokuWiki report pages into `GRT\doku\<lang>\report\`. |

Full details: **[MANUAL.md](GrtReloadingToolkit/MANUAL.md)**, or as HTML
([EN](GrtReloadingToolkit/docs/Reloading-Toolkit-Manual.html) /
[IT](GrtReloadingToolkit/docs/Reloading-Toolkit-Manual-IT.html)) — both are also attached to the
[latest release](https://github.com/FREESHOTER/GRT-Reloading-Toolkit/releases/latest) as PDFs, for
reading offline.

## Install (users)

[Download the latest release](https://github.com/FREESHOTER/GRT-Reloading-Toolkit/releases/latest),
unzip the `ReloadingToolkit` folder into `…\GRT-2021.2030…\plugins\`, restart GRT. One toolbar
button opens a launcher with every tool.

| Build | Size | Requires |
|---|---|---|
| `ReloadingToolkit` (self-contained) | ~64 MB | nothing |
| `ReloadingToolkit-lite` (framework-dependent) | ~1.5 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows x64) |

### How it writes back

The GRT plugin interface can't edit the open load, so each "Write to GRT load" saves a timestamped
snapshot `MyLoad_toolkit_<date>.grtload` next to your file (**your original is never touched**) and
opens it. Every tool accumulates into the newest snapshot; only the three newest are kept. When
you're done, open the newest one and *File → Save As* under a real name.

## Build (contributors)

Needs the .NET 8 SDK.

```powershell
dotnet build                    # the whole solution (Windows only — the plugin is WinForms)
dotnet build GrtReloadingToolkit/GrtReloadingToolkit.csproj -c Release

# assemble the drop-in plugin folder(s) under dist\ (and optionally .zip them):
pwsh GrtReloadingToolkit/build-plugin.ps1 -Zip
#   -> dist\ReloadingToolkit\        self-contained
#   -> dist\ReloadingToolkit-lite\   framework-dependent
# add -GrtDir "C:\path\to\GRT…" to also copy it straight into that GRT's plugins\
```

The version lives in `Directory.Build.props` and nowhere else — both assemblies get it from the
compiler, the window titles read it back off the assembly, and `build-plugin.ps1` stamps it into
the `com.grt.plugin.xml` it puts in `dist\`. Bump it there and tag the release to match.

Tests:

```
dotnet test GrtReloadingToolkit.Tests/GrtReloadingToolkit.Tests.csproj
```

They run anywhere — no Windows Desktop SDK needed, so on macOS or Linux name the two
portable projects rather than the solution (`dotnet build` at the root pulls in the WinForms
project and fails). The ladder analyser and the log store are
plain arithmetic and SQLite with no WinForms dependency, so the test project links their source
files rather than referencing the `net8.0-windows` plugin project.

Layout:

```
GrtReloadingToolkit/        the plugin (WinForms, net8.0-windows)
  plugin/                   com.grt.plugin.xml + media icons
  build-plugin.ps1
  app.manifest              DPI-unaware (windows use fixed pixel layout — Windows bitmap-scales them)
GrtReloadingToolkit.Tests/  xunit; ladder / OCW node analysis, stock ledger, .xlsx reader
grt-plugins-shared/
  GrtPluginKit/             shared library: GRT IPC client, .grtload reader/writer, chrono parsers
  make-icons.ps1
```

Debug CLIs (headless, on the built exe) are listed in [MANUAL.md §14](GrtReloadingToolkit/MANUAL.md).

## Credits

A couple of these overlap with existing standalone plugins — the chronograph importer and a
seating-depth calculator by **XquiziT Arms** in particular, which were used as a reference for the
export formats.

## Licence

MIT — see [LICENSE](LICENSE).
