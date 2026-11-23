# GRT Ladder / OCW Analyzer

Community plugin for [Gordon's Reloading Tool](https://grtools.de). Analyses a charge-weight
ladder for the **velocity flat-spot** (Satterlee) and the **OCW / Audette vertical-POI node**,
and writes an OCW note into the load.

## Input

Point it at **one folder** holding, for a single ladder:

- **Athlon Rangecraft** `*.xlsx` exports — chronograph velocities per charge.
- **Ballistic-X** `Carica <n>.csv` exports — target impact coordinates (MOA):
  `Project Title,Group,Ammunition,Distance,Aim X,Aim Y,Center X,Center Y,Point X,Point Y,Velocity`

Files are paired by charge weight (from the file name / Athlon session note, ±0.05 gr). A charge
with only velocity or only a target group is still used for the parts of the analysis it can feed.

## Analysis

Per charge: MV mean / SD / ES (SD is population, ÷n), group centre POI-Y (MOA), group mean-radius,
group ES, vertical spread, shooting distance.

- **Velocity flat-spot** — the 3-charge window with the smallest MV spread.
- **OCW / Audette node** — the 3-charge window with the smallest vertical-POI spread (+ tightest groups).
- **Recommended node** — weighted combination (POI 1.4, MV 1.0, group 1.0, SD 0.8; adjustable, window 3–5).

Distances may differ between groups (100/200/300/500 m) — POI is compared in MOA so it stays
meaningful, but a warning is shown because conditions differ between sessions.

## Output

**Write OCW note to GRT load** reads the load open in GRT (`Get_TabOnTop`), appends an
`OCW Analysis <date>` note to its appendix, writes `<load>_ocw_<yyyymmdd_hhmm>.grtload` next to
the original (base preserved), and opens it via `Load_File`. Earlier `OCW …` notes are replaced;
if the active tab is itself a generated file it refuses and asks you to switch to your real load.

## Build

```
dotnet build -c Release
pwsh ./build-plugin.ps1 -GrtDir "C:\path\to\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
```

## Debug

```
GRT_OCW_Analyzer.exe --analyze <folder> [base.grtload]
```

Prints the analysis (and, with a base file, writes the note copy) to the console.
`OCW_FOLDER=<folder>` auto-loads on startup.

## Status

v0.1 — analysis engine and note writer verified on a real 10-charge N550 ladder; UI renders.
Not yet clicked through a running GRT.
