# GRT Reloading Toolkit — Manual

A community plugin for **Gordon's Reloading Tool (GRT)**. It bundles ten reloading tools plus a
set of printable GRT report templates into one window.

- **Author:** community
- **Plugin id:** `com.grt.plugin.reloadingtoolkit`
- **Build:** self-contained Windows single-file exe (~64 MB) — **no .NET install needed**.

---

## 1. Installing

1. Copy the `ReloadingToolkit` folder into `…\GRT-2021.2030…\plugins\`.
2. Restart GRT.
3. A single toolbar button **"Reloading Toolkit"** (wrench + screwdriver icon) and a matching entry
   in the **Plugin** menu appear. Click either — a small **launcher** window opens with one button
   per tool.
4. Optional: in the launcher, click **"Install GRT report templates"** once (see §13).

The toolkit is *on-demand*: GRT starts it on the first click and it exits when you close its last
window. All tool windows are served by one background process, so opening a second tool while one is
already open just brings up that tool.

---

## 2. How the toolkit writes back to GRT — read this first

The GRT plugin interface **cannot edit the load that is open in GRT**. It can only ask GRT to *open
a file*. So every tool that "writes to the GRT load" actually does this:

1. Reads your load (the `.grtload` XML on disk).
2. Adds its result — a **note** in the result-field appendix, sometimes a **chart** in the gallery,
   sometimes a changed **input** (`Ba`, `casevol`, `gdepth`, `tcc`/`tch`).
3. Saves a **sibling snapshot** next to your file:

   ```
   MyLoad.grtload                       ← your original, never touched
   MyLoad_toolkit_20260612_2014xx.grtload   ← snapshot the toolkit wrote
   ```

4. Tells GRT to open that snapshot.

Key points:

- **Every tool accumulates into the same snapshot family.** Run the Ladder analyzer, then Barrel
  Calibration, then Brass prep — the newest snapshot contains all three results. Only the **3 most
  recent** snapshots are kept; older ones are deleted automatically.
- **Each write makes a new filename** (timestamped to the second). This is deliberate: GRT's
  *Load_File* will not reload a path it already has open, so a fixed name would show stale content
  after the second write.
- `Ba`, `tcc`/`tch` and `casevol` are always read from your **original** file, never from a
  snapshot that already carries a correction — so running a calibration twice never compounds.
- **When you are done:** in GRT open the newest `…_toolkit_…grtload`, use **File → Save As** to give
  it a clean name, then delete the `_toolkit_…` snapshots.
- Do **not** press *Ctrl+S* on an old fixed-name `…_toolkit.grtload` tab that GRT opened before a
  newer snapshot was written — GRT would save its stale in-memory copy over the file.

### Units: the toolkit shows what GRT shows

A `.grtload` file is **always metric** — lengths in mm, velocities in m/s, temperatures in °C —
whatever units GRT is displaying. GRT keeps your choice of units separately, in `ValueUnits` in
`GordonsReloadingTool.cfg`, and converts on the way to the screen.

The toolkit does the same. At startup it reads that line from the GRT beside it and follows seven
of its settings:

| GRT field | What follows it |
|---|---|
| `oal` | every length: COAL, seating depth, neck OD, case length, the card, the label |
| `velocity` | every muzzle velocity, SD and ES, in grids, charts, notes and the QR block |
| `pt` | every temperature |
| `range` | every shooting distance |
| `charge` | every powder charge: the ladder, the journal, Find best Ba, the cost of a round |
| `mp` | every bullet weight |
| `twistlen` | the barrel twist, falling back to `oal` where GRT has no entry for it |

So if GRT is set to inches and ft/s, so is the toolkit — column headers, chart axes, the load
card, the box label and the notes it writes back all say `in` and `ft/s`. If there is no GRT
installed beside it (stand-alone, or a test box) it stays metric, which is what it has always
done. The stored file never changes: it is metric either way, so a load stays readable by anyone.

Three things deliberately do **not** follow GRT:

- **MOA** is an angle. The number is the same in every unit system, so it is never converted.
- **`UNIT=`** in a chronograph note records what unit *your export file* used. That is provenance,
  not a display choice.
- **The ladder X column** (charge or seat/jump). That number is read out of your own file names
  and charge notes, so nothing records which unit you wrote it in — converting it would be a
  guess. The heading follows GRT so you know the convention, but the number is exactly as you
  typed it.

Where a tab has its own unit picker (the brass tabs, the ladder's target units), the picker still
wins — GRT's setting is only what it opens on the first time. After that, your choice is
remembered. *Find best Ba*'s target-temperature picker is the one that isn't: it opens on GRT's
unit every time, because that window keeps nothing between sessions.

### Your typing is kept

Every box you fill in is saved as you go and comes back when you reopen the window — closing a
tool is no longer a lost measurement. That covers the values that used to vanish, including
*measure loaded neck OD* and *seating depth*. The state lives in
`%APPDATA%\GRTPlugins\toolkit-ui.json`; delete that file to start every tab fresh.

---

## 3. Chronograph Import  🎯

Turns chronograph exports into a GRT **Imported Measurement** tab (one `charge` per file, its shots
underneath). A folder of files = a ladder.

**Supported:** Athlon Rangecraft (Velocity Pro) and Garmin Xero C1 Pro / ShotView, as `.xlsx`
or `.csv`.

**Buttons**

| Control | Effect |
|---|---|
| **Add chrono files…** | Pick one or more files (one per charge). |
| **Add folder…** | Pick a folder — every `.xlsx/.csv` in it is read; files that are not chrono exports (target CSVs, databases…) are skipped silently. |
| **Remove selected** | Drop the highlighted grid rows. |
| **TEMP= on every shot** | Also write the session temperature as a per-shot note (for GRT's temp assistant). |
| **replace previous chrono import in this load** | Remove earlier "Athlon …" measurements before adding this one (on by default). |
| **Import into GRT** | Write the measurement into the load and open the snapshot. |

**Grid columns:** Use · File · Charge (gr) · Shots · AVG m/s · SD · ES · Temp °C (editable).

**How the charge weight is found:** from the session note ("carica 39.2", "charge 41.5 gr"), else a
number in the file name ("39.20.xlsx"). Edit the Temp °C cell if the file doesn't carry it.

**Units:** taken from the column header if it says `(m/s)` / `(mps)` / `(fps)` / `ft/s`; otherwise
guessed from the velocity magnitude (≥ 1400 → fps). fps is converted to m/s for GRT. Both `1,234.5`
and `1.234,5` number styles are accepted; the summary footer is ignored.

---

## 4. Chronograph Statistics  📐

What GRT's own AVG/SD/ES line on an imported Measurement doesn't tell you: how much the *true*
average velocity could plausibly differ from what you measured, how many shots you'd need to pin
it down tighter, whether a shot looks like it doesn't belong, and whether two strings are actually
different or that's just chrono noise.

**Data sources:** the same chrono files as Chronograph Import (**Add chrono files…** / **Add
folder…**), or **From GRT load's Measurement** — pulls every charge already imported into the
open load, one row per charge.

| Control | Effect |
|---|---|
| **confidence** | 90 / 95 / 99% — applies to every confidence interval and test below. |
| **target ± m/s** | Feeds the "shots for target" column: how many shots (of this string's spread) it would take to pin the mean down to ± that margin. |
| **Compare A vs B** | Welch's t-test on the means and an F-test on the spreads between any two strings. |
| **Write note to GRT load** | Writes every string's stats — and the last comparison, if any — as a "Chrono Statistics" note. |

**Grid:** string · n · mean · SD · ES · CI ± on the mean · shots needed for the target margin ·
outliers. SD here is the same population SD (÷n) shown everywhere else in the toolkit; the
confidence interval and the two tests use sample SD (÷n−1) internally, since that's what those
formulas are built on — for n this small the difference matters.

**Outliers** are flagged by Chauvenet's criterion (classic normal-distribution formulation): a shot
is rejected if the number of measurements you'd expect to see that far from the mean, over the
whole string, is under ½. Flagged shots are excluded from the string's mean/SD/CI, same as a
careful hand analysis would do — they're still listed, not silently dropped.

**Compare** answers "is charge A actually faster than charge B, or could that be chrono noise?"
without assuming the two strings are the same size or the same spread:

```
Welch t-test (means):  t = (meanA - meanB) / sqrt(sdA²/nA + sdB²/nB)
F-test (spreads):      F = sdA² / sdB²
```

Both report a p-value; under 0.05 the tool calls it statistically significant. A small, consistent
velocity gap across many shots can still be "not significant" if the strings are short — that's the
tool being honest about what a handful of shots can and can't tell you, not a bug.

> This is a read of what you already measured, not a promise about what the *next* string will
> look like — chronographs (and barrels) are noisier than any n=4 confidence interval lets on.

---

## 5. Ladder / OCW Analyzer  📈

Finds the accuracy node in a **charge ladder** from chronograph velocities and target groups.

**Two data sources for the groups:**

- **Ballistic-X `.csv` exports** — put them in the same folder as the chrono files, one per charge.
- **GRT's own "Shot group" tabs** — see §12.

**Velocities** come from the chrono `*.xlsx` in the picked folder. Any charge that has no chrono
file is filled in automatically from the **chronograph Measurement already imported into the open
load** (§3), matched by charge weight — so if you ran the chrono import first, the MV flat-spot and
the blue MV line appear even when the folder holds only target `.csv` files. A chrono file in the
folder always wins over the load's own numbers for that charge.

**Top bar**

| Control | Effect |
|---|---|
| **Pick ladder folder…** | Folder with chrono `*.xlsx` + Ballistic-X `Carica *.csv`, one pair per charge. |
| **w POI**, **w MV**, **window** | Analysis weights and the node width (3–5 steps). |
| **Re-analyze** | Recompute with the current weights. |
| **Groups from GRT load** | Read the group data from GRT's Shot-group tabs instead of `.csv` (with the two unit boxes + *drop flyers*, see §12). |
| **Write note to GRT load** | Write the "OCW Analysis" note **and the chart** into the load. |

**Grid:** charge · n · MV · SD · ES · distance · POI-Y (MOA) · group mean-radius (MOA) · vertical
spread (MOA). The recommended node is shaded green.

**What it computes**

- **Velocity flat-spot (Satterlee):** the window of steps where average MV changes least.
- **Vertical-POI node (OCW / Audette):** the window where the vertical point of impact is most
  stable.
- **Recommended node:** a weighted blend of MV flatness + POI stability + group size + SD.

The report and chart are written as `~~result.Note("OCW Analysis")~~` and
`~~result.picture.ocw_chart.png~~` — see §13.

> Always confirm a node with a fresh group before committing. SD here is population SD (n).

---

## 6. Seating-Depth Analyzer  📏

Same window and workflow as the OCW analyzer, but the X axis is **seating depth / jump (mm)** and
the primary signal is the **group-size plateau** rather than the MV flat-spot. The step value is
read from a number in the session note or file name ("salto 0.02", "jump .015", "cbto 2.850").

Writes `~~result.Note("Seating Depth Analysis")~~` + `~~result.picture.seating_chart.png~~`.

---

## 7. Barrel Calibration  🎚

Compares GRT's **simulated** muzzle velocity to your **measured** MV over several charges and
suggests a `Ba` (combustion coefficient) tweak so GRT matches your barrel.

**Workflow**

1. Import your chrono strings into the load first (§3).
2. **Load measured from GRT load** — fills the grid with one row per charge (charge + mean measured
   MV, from the load's Measurements).
3. Get the simulated MV, either:
   - **Capture ALL sim MV (sweeps GRT)** — for each measured charge the tool briefly opens a
     one-charge copy of the load in GRT, lets it compute, reads the sim MV, then reopens your load.
     It leaves one throwaway tab per charge in GRT; close them afterwards.
   - **Capture current charge only** — reads GRT's *current* sim result and files it under the
     charge currently set in the load (`mc`). Use this if you prefer to step through charges
     yourself in GRT.
4. Read the verdict, then **Write calibration note** (note only) or **Write Ba-corrected .grtload**
   (note **+** the new `Ba`).

**Grid:** Charge gr · Meas MV · Sim MV · Δ m/s · Δ %.

**The maths**

- Δ% is the key number. If it is roughly **constant** across charges → a single `Ba` fixes it
  ("scale error", the tool says *consistent*). If it **varies with charge** → `Ba` alone won't do
  it ("shape error" — check fired case volume, barrel length, bullet weight first, or try the shape
  fit below).
- Suggested `Ba` = old `Ba` × (mean measured ÷ mean sim)²  (GRT's MV scales roughly as √Ba).

**Reading the result:** a small, consistent offset (≤ ~0.3 %) means your barrel already matches
GRT — the `Ba` change will be a fraction of a percent and the remaining per-charge scatter is
chronograph noise, not something to chase. A corrected `Ba` is specific to **that barrel + that
powder lot**; don't treat it as a universal powder value.

**Shape fit (Ba + a0), for when the offset varies with charge**

A single `Ba` can only correct a *scale* error (the whole curve too fast/slow by the same %) — if
the offset itself changes with charge, the propellant's burn *shape* is off, and `Ba` alone can't
fix that no matter how you tune it.

1. Capture the baseline sim MV first (steps above), with **3+ charges**.
2. **Capture shape-fit sweep (a0)** — re-simulates the same charges with the propellant's `a0`
   (its "prog/deg" burn-shape coefficient) nudged by +5%, so the tool can see how sensitive each
   charge's MV is to `a0` versus to `Ba`.
3. The report gains a "Shape fit (Ba + a0)" section with a suggested `Ba` **and** `a0` fitted
   together across every charge. **Write Ba+a0-corrected .grtload** writes both.

Why `a0` and not GRT's `k` (which the OBT tool's own docs mention alongside `Ba`): `k` is the
combustion gases' ratio of specific heats — a thermochemical property, not a shape-fitting knob.
Bending it to force a velocity match would leave `Pmax` and burn-time predictions (which OBT/BLT
directly depend on) wrong in ways this velocity-only fit can't see. `a0` is what GRT's own
`formalism.txt` describes as the coefficient meant to be adjusted to match a measured burn curve.

This needs the charges spread out (not clustered close together) — with too little spread, `Ba`
and `a0` affect the sim too similarly to separate reliably, and the tool will say so instead of
guessing.

---

## 8. Powder Temp Coefficients  🌡

Fits the propellant's **temperature coefficients** `tcc` (below 21 °C) and `tch` (above 21 °C) from
chronograph strings of **one charge** fired at **different temperatures**.

> This is **not** for a charge ladder. You need e.g. 40.0 gr shot cold (~0 °C), normal (~21 °C) and
> hot (~40 °C). If you load several different charges the tool refuses and says so.

**Workflow**

1. **Add Athlon strings…** — the strings for the one charge, at each temperature.
2. Fill the **Temp °C** column with the real temperature of each string; tick **Use**.
3. **Load Ba from GRT load** (or type the `Ba`).
4. **Fit** → tcc / tch, and the raw m/s-per-°C.
5. **Write tcc/tch to GRT load**.

Model: `Ba(T) ≈ Ba₂₁ · (MV(T) / MV₂₁)²`;  `tcc = mean( (Ba₂₁ − Ba(T)) / (21 − T) )` for the cold
strings, `tch` symmetric for the hot ones.

---

## 9. Brass Prep  🔧

Three calculators, one per tab.

### 9a. Case volume → GRT `casevol`

Enter weights for **3+ fired, de-primed** cases: empty and full-of-water (or the water weight
directly). Water is weighed in **grain** or **gram** (toggle). Output is **mean case volume in
grain H₂O** and **cm³**.

- GRT stores `casevol` in **cm³** internally, even though its UI usually shows grain H₂O — the tool
  writes cm³ and pins the unit. Water density 0.99821 g/cm³ (20 °C).
- **Write casevol to GRT load** writes the **mean** into the caliber; SD / range go in the note.

### 9b. Seating depth (from comparator measurements) → GRT `gdepth`

Enter **CBTO** (cartridge base-to-ogive, loaded round), **case length**, **BBTO** (bullet
base-to-ogive, bare bullet) — CBTO and BBTO to the **same comparator insert**. Units mm or inch.

```
DIFF          = CBTO − case length      (how far the ogive sits above the case mouth)
seating depth = BBTO − DIFF             (bullet base to case mouth = GRT's "seating depth")
```

Warnings flag impossible geometry and unusually shallow / deep results. **Write seating depth to
GRT load** sets `gdepth`; GRT recomputes COAL from it.

### 9c. Neck / bushing sizing

Enter bullet diameter, neck-wall thickness, desired **interference (grip)**, and optionally a
**measured loaded neck OD** (0 = estimate as bullet dia + 2×wall). Units **inch** by default
(bushing dies are sold in inch), toggle to mm.

```
bushing die OD = loaded neck OD − interference − springback   (springback ≈ 0.03 mm)
mandrel / expander OD = bullet dia − interference
```

Order 2–3 bushings around the value; a mandrel as the last step sets the ID directly and evens out
wall runout.

---

## 10. Seating Force Estimate (QC)  ⚖️

A standalone tool (its own entry under "PLAN & PREPARE", not a Brass Prep tab) — estimates expected
bullet-seating (press-fit) force for a QC press's own "max pressure" safety threshold, ported from
the Pressa QC press project's own estimator. Every length field (bullet diameter, neck ID, seating
depth, the baseline's typical interference/depth) has its **own** mm/in dropdown, so you can mix
units field by field.

Fill in bullet diameter and neck ID after sizing (read these off the Neck / bushing tab if you've
already sized this bore there) and the actual seating depth, then a **baseline force** for a
"typical" setup in this bore — a "Quick-fill reference" dropdown offers 3 known starting points
(.223, 6.5CM, .308), but any caliber works if you know or estimate its own baseline. Pick the
actual prep for this batch (annealing, neck sizing, lube, bullet coating, boat-tail) and the
estimate updates live: mean force, 1σ/2σ bands, and a suggested max (QC) value.

The bar/psi figure alongside it is just the **same force** expressed as an equivalent pressure over
the bullet's own cross-section (F / area) — a unit some of the press community uses for neck
tension, nothing more. **It is not a GRT input and not a substitute for GRT's own "Initial
Pressure"** (gpressure): shot-start pressure is dominated by bearing-surface engraving into the
rifling, which this estimator (built for a bench press's static neck tension) doesn't model at all
— an earlier version of this tool that converted the force into a gpressure estimate underestimated
GRT's own reference values by roughly 4-5x and was removed.

---

## 11. Load Card / Label  🏷

Printable **recipe card (A6)** or **ammo-box labels**, each with a **QR code** of the full recipe.

- **Load from GRT** — fill the card from the open load (caliber, firearm, bullet + weight, powder,
  charge, COAL, seating depth, and MV/SD from the last measurement).
- **Powder / Primer / Brass / Bullet lot** drop-downs — pick a component lot from the Inventory
  (§12) to compute **cost per round** and stamp the lot on the card.
- **Barrel** drop-down — pick a barrel from the Inventory (§12) and the card gains a **Barrel**
  row: its name, its twist and its length, in GRT's units. The same recipe out of a different
  tube is a different load, so the card, the QR and the load-sheet note all say which one it
  was. A barrel with no twist or length recorded prints as just its name, and no barrel prints
  no row at all. A barrel is not consumed, so it never enters the cost per round.
- Edit any field in the property grid. Choose **Recipe card** or **Box labels** + a count.
- **Save PNG…**, **Print…** (print-preview), or **Write load-sheet note to GRT** — puts the recipe
  + per-round cost breakdown into the load as a "Load Sheet" note.

---

## 12. Inventory & Load Journal  📒

A small SQLite database at `%AppData%\GRTPlugins\reloading_log.db`. Four tabs.

- **Inventory** — powder / primer / brass / bullet / barrel stock with lot id, price and (for
  brass) an *expected uses* count that amortises the case cost. **Add / Edit / Restock / Archive**.
  **Brand** offers the usual makers for the kind and stays typeable for the ones it misses.
  Powder is counted in whichever unit you buy it in — **g, gr, lb or kg**, picked next to *Qty
  initial*; everything else is counted in pieces. Switching the unit re-labels the lot, it does
  not change it: 1 lb and 453.6 g are the same jug, the grid, the restock prompt and the
  cost-per-unit column all follow, and the journal still burns the same grams per round.
  A **barrel** takes a *twist* and a *barrel length*, shown in the units GRT is set to (`twistlen`
  and `oal` in its unit map) and stored metric either way — so a 1:8 tube reads `1:8 in` beside
  `26.0000 in` on an imperial install and `1:203.2 mm` beside `660.4000 mm` on a metric one. Both
  are optional; leave them at zero and the row just shows the barrel's name.
  Stock can go **negative** — that just means the lot was entered short, or rounds were logged
  against the wrong lot. It is never silently absorbed, so editing or deleting the entry always
  gives back exactly what it took. Fix it with **Restock** (or **Edit** the lot's quantity).
- **Journal** — one row per range session or batch: date, load, caliber, firearm, charge, rounds,
  MV, SD, group MOA, distance, notes, **cost per round** from the referenced lots, and (new)
  environmental conditions + the calibrated propellant coefficients for that session:
  - **Temperature** (C or F, own dropdown) and **pressure** (hPa or inHg, own dropdown) and
    **humidity %** — tick *environment recorded* to save these; GRT itself tracks none of them
    (checked every field in a real `.grtload`: no such tag anywhere), so they're always typed in
    by hand, never read from the load.
  - **Ba** and **a0** — the values Barrel Calibration's fit landed on for this session. **Log from
    GRT** reads these straight from the open load if you ran *Write Ba-corrected* or *Write
    Ba+a0-corrected .grtload* there first and it's the load now open; otherwise type them in.
  **New**, **Log from GRT** (prefills from the open load), **Edit**, **Delete**. Editing or
  deleting an entry reconciles the inventory. Tick *deduct components from inventory on save* to
  draw stock down.
- **Find best Ba** — searches the journal for entries that carry a calibrated `Ba`, filtered to one
  caliber + powder + bullet combo (a `Ba` from a different combo isn't comparable), ranked by
  whichever "best" means for you: **tightest group (MOA)**, **closest velocity to a target**, or
  **closest temperature to a target** (target temperature also takes C or F). Read-only — it's a
  search over what the Journal has already recorded, not a place to enter new data.
- **Firearms** — one row per barrel: total round count (`rounds before` + journal rounds), number
  of MV entries, last used. **Add / Edit / Retire**, and a **MV-drift chart** (muzzle velocity vs
  cumulative rounds with a trend line — "+X m/s per 100 rd").

### Using GRT's Shot-group tabs as an analyzer source

If you do your group analysis **inside GRT** (Toolbar → shot-group icon, or Results → **+** → Shot
group — drag a target photo, set a reference distance and the shooting distance, add a `(+) Group`
and `(+) Shot` markers, mark one point as *Point of Aim*, flag flyers):

1. **Name each `<group>` with its charge / seating** — "40.0", "Gruppo 40.2 gr", "salto 0.02". The
   analyzer parses the number. Several groups may share one shot-group tab (same target photo) or
   you can use one tab per charge.
2. **Save the load** so the shot-group tabs are written into the `.grtload`.
3. In the Ladder / Seating analyzer set **ref dist** (mm / cm / inch) and **shoot dist** (m / yd) —
   GRT does not store which unit the reference-distance field used, so tell the tool — optionally
   tick **drop flyers**, then click **Groups from GRT load**.

The tool converts each hit (stored as an image fraction) to MOA using the two reference points and
the image aspect ratio (1 MOA = 29.0888 mm at 100 m).

---

## 13. GRT report templates

**Install GRT report templates** (launcher / Plugin menu) writes nine DokuWiki report pages into
`GRT\doku\<language>\report\` and links them in the report index. Run it once per GRT install
(and tell anyone you share the plugin with to do the same). It is idempotent and non-destructive;
delete a page by saving it empty in GRT.

| Page | Pulls |
|---|---|
| **Toolkit — Full load workup** | recipe + predicted results + GRT curve diagram + every section below |
| **Toolkit — Chronograph statistics report** | the chrono-statistics note |
| **Toolkit — Ladder / OCW report** | the OCW note + chart |
| **Toolkit — Seating-depth report** | the seating note + chart |
| **Toolkit — Barrel calibration report** | the calibration note (Ba, or Ba+a0 shape fit) + temp-coefficient note |
| **Toolkit — Powder temp-coefficients report** | the temp-coefficient note on its own |
| **Toolkit — Case volume report** | the case-volume note |
| **Toolkit — Seating depth (geometry) report** | the seating-depth-from-comparator note |
| **Toolkit — Load sheet** | recipe + predicted + cost note |

Every tool that writes a note has its own dedicated page now, except **Seating Force Estimate
(QC)** — it has no GRT linkage at all (§10), so there is nothing for a report to pull.

**To view a report in GRT:** Results panel → **+** (new tab) → **Add report** → pick a "Toolkit —
…" page. Open it **on the `…_toolkit_…grtload` snapshot** (that's where the notes and charts live),
not on your original file. Empty sections just mean you haven't run that tool yet.

---

## 14. Debug command line

All on the exe (`plugins\ReloadingToolkit\GRT_Reloading_Toolkit.exe`):

```
--dbtest [db]                                    inventory / journal self-test
--athlon <folder> <base.grtload> [tempC]         chrono import from a folder
--chronostats <folder> [base.grtload] [marginMps]  per-string stats + a compare of the first two
--ladder charge|seating <folder> [base.grtload]  ladder analysis
--ladder charge|seating --synthetic [out.png]    fixture-free chart test
--groups <load.grtload> [mm|cm|in] [m|yd] [--drop-flyers]   GRT shot-group → MOA
--cal <port> <base> <charge:simMv> …             calibration math
--tcoeff <Ba> <file.xlsx:tempC> …                temp-coefficient fit
--card <base.grtload> [outDir]                   render a recipe card / labels
--brass vol gr|g <w…>  |  --brass neck <dia> <wall> <interf> [loadedOd]  |  --brass seat <cbto> <caselen> <bbto> [mm|in]
--reports [grtRoot]                              install the report templates
```

Environment: `OCW_FOLDER` / `SEATING_FOLDER` auto-load a ladder folder on open;
`RELOADING_LOG_DB` overrides the database path;
`GRT_OCW_CHART_SIZE=WxH` sets the exported OCW/seating chart pixel size (default `1400x1040`) —
GRT's picture tab fits the image to its width and centres it, so a wide value like `2000x560`
fills a wide results panel, a near-square value a tall one.

---

## 15. Limitations & FAQ

**Why a pile of `_toolkit_…` files?** GRT's plugin API can't edit the open load and can't reload an
already-open tab — so each write is a fresh timestamped snapshot. Each snapshot is complete; keep
the newest (Save As under a real name) and delete the rest. Only the 3 newest are kept
automatically.

**I changed `Ba` but GRT's velocity didn't move.** Either GRT is still showing the old tab (open the
newest snapshot), or the correction is genuinely tiny — a 0.1 % offset moves V0 by ~0.6 m/s, which
is inside chronograph noise.

**"No usable shot groups in that load."** Your load has no GRT Shot-group tabs — you analysed groups
in Ballistic-X instead. Use the `.csv` path, or build the groups in GRT first (§12).

**Temp-coefficient tool won't enable "Write".** It needs one charge at several temperatures, not a
ladder.

**One toolbar icon, not eight.** By design — it opens the launcher; every tool is a button there.
Manifest changes need a GRT restart to show.

**Numbers:** every field accepts `.` or `,` as the decimal separator regardless of your Windows
locale. Report text is always written with a `.` decimal (GRT's requirement).
