# GRT Reloading Toolkit — Manual

A community plugin for **Gordon's Reloading Tool (GRT)**. It bundles nineteen reloading tools plus a
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
4. Optional: in the launcher, click **"Install GRT report templates"** once (see §26).

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

## 3. GRT basics for beginners

Four things every new GRT user runs into that are **native GRT behaviour, not this toolkit** — the
plugin API has no hook to change GRT's own dialogs, so these are explained here rather than fixed.

- **"Shot Start (Initiation) Pressure" defaults to 250 bar and does not auto-calculate.** GRT does
  not derive it from your primer/neck-tension setup — if you leave the default in place for every
  load regardless of case prep, your predicted `Pmax` and burn timing are quietly off. Set it
  per-load, or at least per bore/prep combination.
- **"There is an input value error"** with no field named is GRT's own generic validation message.
  It almost always means one of: a caliber/bullet/powder dropdown left on its placeholder, a length
  field at exactly 0, or a unit mismatch (e.g. a case length typed in inches while GRT expects mm).
  Check those three before assuming the load itself is wrong.
- **The primer dropdown lists a magnum/standard/small/large split that isn't about brand.** It's
  GRT's own primer *burn-energy class*, used in the internal ballistics model — picking the wrong
  class changes the simulated pressure curve even with the correct primer brand typed elsewhere.
- **Grains vs. grams, and reading a pressure curve:** GRT's charge weight is always **grains**
  internally (1 gr = 0.0648 g) regardless of which unit your install displays; the Inventory (§14)
  and Journal (§15) both follow whichever unit GRT shows. On the pressure/velocity curve GRT plots
  after a simulation, the pressure line should rise quickly to a single peak (`Pmax`, early in the
  barrel) then fall off smoothly as the bullet accelerates — a curve with a second bump, a plateau
  at the peak, or a peak very late in the barrel usually means an implausible input (burn-rate
  mismatch, an unrealistic `Ba`/`a0`) rather than a real powder behaviour.

---

## 4. Guided New Load (wizard)  🪄

A checklist for building a load from scratch, front to back — opens the real tool for each step
(never reimplements one) and ticks the step once you've launched it from here. A ticked box means
"opened from here this session", not "verified done": the wizard doesn't read GRT or the database to
check whether a step's own work actually finished. Nothing here persists between sessions; reopen
the wizard and every box starts unchecked again.

Two paths, chosen with a radio button at the top, because a card and a calibration don't make sense
in the same order for both:

| | **Single load** | **Ladder test** |
|---|---|---|
| You already know | the one charge you're loading | nothing yet — that's the point of the ladder |
| 1 | Brass prep (§11) | Brass prep (§11) |
| 2 | Load card / label (§13) — print it **before** you leave | Chronograph import (§5) — **after** the range |
| 3 | Barrel calibration (§9) — **after** the range, on the real MV | Ladder / OCW analyzer (§7) — finds the node |
| 4 | — | Barrel calibration (§9), on that one charge's real MV |
| 5 | — | Load card / label (§13) — for the charge the ladder pointed at |

The ladder path's card comes **last**, after Ladder/OCW has picked the one charge the rest of the
load gets built around — printing a card before the range makes no sense when you don't yet know
which of several charges you're keeping.

---

## 5. Chronograph Import  🎯

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

## 6. Chronograph Statistics  📐

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
| **target ±** | Feeds the "shots for target" column: how many shots (of this string's spread) it would take to pin the mean down to ± that margin. Typed in whichever velocity unit GRT is configured for (the label says which). |
| **Compare A vs B** | Welch's t-test on the means and an F-test on the spreads between any two strings. |
| **Write note to GRT load** | Writes every string's stats — and the last comparison, if any — as a "Chrono Statistics" note. |

**Grid:** string · n · mean · SD · ES · CI ± on the mean · shots needed for the target margin ·
outliers. Mean, SD, ES and the CI are shown in GRT's own velocity unit (m/s or ft/s — each column
header says which), and the "Chrono Statistics" note follows the same unit. SD here is the same
population SD (÷n) shown everywhere else in the toolkit; the
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

## 7. Ladder / OCW Analyzer  📈

Finds the accuracy node in a **charge ladder** from chronograph velocities and target groups.

**Two data sources for the groups:**

- **OnTarget `.csv` exports** — put them in the same folder as the chrono files, one per charge.
- **GRT's own "Shot group" tabs** — see §14.

**Velocities** come from the chrono `*.xlsx` in the picked folder. Any charge that has no chrono
file is filled in automatically from the **chronograph Measurement already imported into the open
load** (§5), matched by charge weight — so if you ran the chrono import first, the MV flat-spot and
the blue MV line appear even when the folder holds only target `.csv` files. A chrono file in the
folder always wins over the load's own numbers for that charge.

**Top bar**

| Control | Effect |
|---|---|
| **Pick ladder folder…** | Folder with chrono `*.xlsx` + OnTarget `Carica *.csv`, one pair per charge. |
| **w POI**, **w MV**, **window** | Analysis weights and the node width (3–5 steps). |
| **Re-analyze** | Recompute with the current weights. |
| **Groups from GRT load** | Read the group data from GRT's Shot-group tabs instead of `.csv` (with the two unit boxes + *drop flyers*, see §14). |
| **Write note to GRT load** | Write the "OCW Analysis" note **and the chart** into the load. |

**Grid:** charge · n · MV · SD · ES · distance · POI-Y (MOA) · group mean-radius (MOA) · vertical
spread (MOA). The recommended node is shaded green.

**What it computes**

- **Velocity flat-spot (Satterlee):** the window of steps where average MV changes least.
- **Vertical-POI node (OCW / Audette):** the window where the vertical point of impact is most
  stable.
- **Recommended node:** a weighted blend of MV flatness + POI stability + group size + SD.

The report and chart are written as `~~result.Note("OCW Analysis")~~` and
`~~result.picture.ocw_chart.png~~` — see §26.

> Always confirm a node with a fresh group before committing. SD here is population SD (n).

---

## 8. Seating-Depth Analyzer  📏

Same window and workflow as the OCW analyzer, but the X axis is **seating depth / jump (mm)** and
the primary signal is the **group-size plateau** rather than the MV flat-spot. The step value is
read from a number in the session note or file name ("salto 0.02", "jump .015", "cbto 2.850").

Writes `~~result.Note("Seating Depth Analysis")~~` + `~~result.picture.seating_chart.png~~`.

---

## 9. Barrel Calibration  🎚

Compares GRT's **simulated** muzzle velocity to your **measured** MV over several charges and
suggests a `Ba` (combustion coefficient) tweak so GRT matches your barrel.

**Workflow**

1. Import your chrono strings into the load first (§5).
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

**"Ba differs from the last calibration" warning:** if the load you opened carries a `Ba` that
differs by more than 5% from the most recent CALIBRATED `Ba` the Journal (§15) has on file for the
same caliber + powder, a banner says so before you run anything. It compares only against your own
past calibrations — GRT's factory default for a powder can't be read via the plugin API, so there is
nothing to compare against for a powder you've never calibrated, and the banner stays silent rather
than guessing. Silence here means "nothing to compare yet", not "confirmed fine".

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

## 10. Powder Temp Coefficients  🌡

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

## 11. Brass Prep  🔧

Three calculators, one per tab.

### 11a. Case volume → GRT `casevol`

Enter weights for **3+ fired, de-primed** cases: empty and full-of-water (or the water weight
directly). Water is weighed in **grain** or **gram** (toggle). Output is **mean case volume in
grain H₂O** and **cm³**.

- GRT stores `casevol` in **cm³** internally, even though its UI usually shows grain H₂O — the tool
  writes cm³ and pins the unit. Water density 0.99821 g/cm³ (20 °C).
- **Write casevol to GRT load** writes the **mean** into the caliber; SD / range go in the note.

### 11b. Seating depth (from comparator measurements) → GRT `gdepth`

Enter **CBTO** (cartridge base-to-ogive, loaded round), **case length**, **BBTO** (bullet
base-to-ogive, bare bullet) — CBTO and BBTO to the **same comparator insert**. Units mm or inch.

```
DIFF          = CBTO − case length      (how far the ogive sits above the case mouth)
seating depth = BBTO − DIFF             (bullet base to case mouth = GRT's "seating depth")
```

Warnings flag impossible geometry and unusually shallow / deep results. **Write seating depth to
GRT load** sets `gdepth`; GRT recomputes COAL from it.

### 11c. Neck / bushing sizing

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

## 12. Seating Force Estimate (QC)  ⚖️

A standalone tool (its own entry under "BEFORE THE RANGE", not a Brass Prep tab) — estimates expected
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

## 13. Load Card / Label  🏷

Printable **recipe card (A6)** or **ammo-box labels**, each with a **QR code** of the full recipe.

- **Load from GRT** — fill the card from the open load (caliber, firearm, bullet + weight, powder,
  charge, COAL, seating depth, and MV/SD from the last measurement).
- **Powder / Primer / Brass / Bullet lot** drop-downs — pick a component lot from the Inventory
  (§14) to compute **cost per round** and stamp the lot on the card.
- **Barrel** drop-down — pick a barrel from the Inventory (§14) and the card gains a **Barrel**
  row: its name, its twist and its length, in GRT's units. The same recipe out of a different
  tube is a different load, so the card, the QR and the load-sheet note all say which one it
  was. A barrel with no twist or length recorded prints as just its name, and no barrel prints
  no row at all. A barrel is not consumed, so it never enters the cost per round.
- Edit any field in the property grid. Choose **Recipe card** or **Box labels** + a count.
- **Save PNG…**, **Print…** (print-preview), or **Write load-sheet note to GRT** — puts the recipe
  + per-round cost breakdown into the load as a "Load Sheet" note.

---

## 14. Inventory  📒

A small SQLite database at `%AppData%\GRTPlugins\reloading_log.db`, shared with the Load Journal
(§15) and Find best Ba (§16) — three separate windows over the same file, so anything logged in one
shows up in the others the next time each window gets focus. Four tabs.

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
  A brass lot also takes an **anneal-every** count (0 = no reminder).
- **Firearms** — one row per barrel: total round count (`rounds before` + journal rounds), number
  of MV entries, last used. **Add / Edit / Retire**, and a **MV-drift chart** (muzzle velocity vs
  cumulative rounds with a trend line — "+X m/s per 100 rd").
- **Brass Life** — one row per brass lot: pieces, total rounds fired (summed from every Journal
  entry that names this lot), **average uses per case** (rounds ÷ pieces — a lot-level average
  assuming roughly even rotation through the lot, not a per-individual-case count, since no per-shot
  case-id log exists), uses since the last anneal, and a status of **OK**, **Anneal due** (uses
  since last anneal ≥ the lot's anneal-every count) or **Retire** (average uses ≥ the lot's
  *expected uses*). **Mark annealed now** resets the anneal counter to the lot's current average.
- **Pressure Signs** — logs fired-case head diameter at the 0.200"-from-head line, per firearm and
  powder, one reading per charge tested. Flags a charge-to-charge step whose expansion rate
  accelerates well beyond that same ladder's own earlier average (2× by default, editable) — a
  self-referential comparison on purpose, since brass lot, chamber and caliber all affect the
  absolute expansion rate too much for one universal threshold to mean anything. **Add / Edit /
  Delete**, filtered by firearm then by powder, with a diameter-vs-charge chart highlighting the
  flagged step(s) in red.

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

## 15. Load Journal  📓

Its own window (used to be a tab inside Inventory — split out so it isn't easy to miss and isn't
mistaken for a stock-management screen). One row per range session or batch: date, load, caliber,
firearm, charge, rounds, MV, SD, group MOA, distance, notes, **cost per round** from the referenced
lots, and environmental conditions + the calibrated propellant coefficients for that session:

- **Temperature** (C or F, own dropdown) and **pressure** (hPa or inHg, own dropdown) and
  **humidity %** — tick *environment recorded* to save these; GRT itself tracks none of them
  (checked every field in a real `.grtload`: no such tag anywhere), so they're always typed in
  by hand, never read from the load. The Velocity Model (§20) needs this field filled in on at
  least a handful of entries to have anything to fit.
- **Ba** and **a0** — the values Barrel Calibration's fit landed on for this session. **Log from
  GRT** reads these straight from the open load if you ran *Write Ba-corrected* or *Write
  Ba+a0-corrected .grtload* there first and it's the load now open; otherwise type them in.
- **Fill Ba from loads** is for a journal written before `Ba`/`a0` were recorded: for every entry
  that has no `Ba` but still names a `.grtload`, it reads the value back out of that file. It
  lists what it found and what it is leaving alone (load moved, never calibrated, won't open)
  and waits for a yes before writing; it never overwrites a `Ba` you typed, and running it twice
  does nothing the second time. To undo one, edit that entry and set its `Ba` back to 0.

**New**, **Log from GRT** (prefills from the open load — reads whichever `.grtload` is on top in
GRT, so double-check the right GRT tab is frontmost before clicking if you keep several open),
**Edit**, **Delete**, **Fill Ba from loads**. Editing or deleting an entry reconciles the inventory.
Tick *deduct components from inventory on save* to draw stock down.

---

## 16. Find best Ba  🔎

Its own window (split out from Inventory for the same reason as the Journal above). Searches the
Journal for entries that carry a calibrated `Ba`, filtered to one caliber + powder + bullet combo (a
`Ba` from a different combo isn't comparable), ranked by whichever "best" means for you: **tightest
group (MOA)**, **closest velocity to a target**, or **closest temperature to a target** (target
temperature also takes C or F). Read-only — it's a search over what the Journal has already
recorded, not a place to enter new data. The caliber list is built from journal entries that carry a
`Ba`, so an entry logged before that field existed puts nothing in it: fill in its **Ba
(calibrated)**, or run **Fill Ba from loads** (both in the Journal, §15), and its caliber appears.

---

## 17. Load Leaderboard  🏆

A standalone tool in the launcher's "AFTER THE RANGE" section — ranks **every load
you've ever logged** in the Journal, not one caliber/powder/bullet combo at a time like *Find best
Ba*. Loads are grouped by caliber + powder + bullet + charge weight (the same recipe tested across
several range days is still one load, not several) and given a composite **0-10 score**.

The score is a simple average of whichever of these a load's sessions actually recorded — nothing
is weighted more than anything else, so there is nothing to argue about later:

- **SD** and **ES** (m/s or ft/s, whichever GRT is configured for)
- **Group size** (MOA, extreme spread — the same convention "Group MOA" uses everywhere else)
- **Round count** (penalizes small samples — statistical confidence, not a performance figure)
- **Consistency across sessions** — how much SD drifted between repeat range days on the same load
  (neutral if you've only tested it once)

A load needs no calibrated `Ba` to appear here — unlike *Find best Ba*, it only needs whichever of
SD/ES/group a session actually recorded. It does need at least one of them: a load with nothing but
a round count logged is still listed, at the bottom, with `-` for its rank and score. Round count
and cross-session consistency say how far to trust the other three, so they cannot produce a score
on their own — otherwise twenty rounds fired over no chronograph and no target would score 8.5 and
outrank a load you actually measured.

**The default thresholds are not arbitrary.** Each one is a real, citable reference from
competitive or military precision-rifle practice — the standard handloader SD goal and "poor,
mass-produced factory" benchmark (Bryan Litz, *Applied Ballistics*), the Mk 316 Mod 0 and M118LR
sniper-ammunition specification ceilings, and published benchrest/hunting-rifle group-size
benchmarks. See **⚙ Customize thresholds** in the tool itself to open, edit or reset every tier —
your own discipline may reasonably call for a stricter or looser scale than a general-purpose
default, and the numbers are saved to `%AppData%\GRTPlugins\load-scoring.json` so an edit survives
a restart.

**Write leaderboard note to GRT load** reads the caliber/powder/bullet/charge of whichever
`.grtload` is on top in GRT right now, finds that exact load's own placement among every other load
tested at that caliber, and writes its rank, score and full sub-score breakdown into a note —
same timestamped-snapshot pattern every other tool in the Toolkit uses, so your original file is
never touched. If the open load hasn't been logged in the Journal yet, the tool says so instead of
writing anything. See **Toolkit — Load leaderboard report** below for the matching report page.

---

## 18. Distance Workflow  🧭

A second standalone tool next to Load Leaderboard in "AFTER THE RANGE". At a glance it looks
like the same grid and the same 0-10 score again — it is the same scoring engine — but it answers a
different question, and the two are easy to mix up at first sight:

| | Load Leaderboard (§17) | Distance Workflow |
|---|---|---|
| **Question it answers** | "Of everything I've ever tested for this caliber, what's the best overall?" | "Of the charges I tested **today at this distance**, which earns a test at the next distance?" (100 m → 300 m, say) |
| **Scope** | Every load ever logged, any distance, aggregated | Only loads logged at **one selected distance** |
| **Score** | Includes a cross-session **consistency** term (has this load stayed stable over time?) | No consistency term — a same-day decision shouldn't be discounted for a load's *history* |
| **Small samples** | Shown, just scored lower | Groups under **3 rounds** at that distance are left out of the ranking entirely |

Ported from Ballistic Lab v2's own "Workflow Distanze".

Pick a **caliber** and a **distance** (only distances the Journal actually has entries for are
listed, nearest metre), then **Rank**. Loads are grouped the same way as Load Leaderboard (caliber +
powder + bullet + charge), but only among entries logged at that one distance.

**Write workflow note to GRT load** works like Load Leaderboard's write-back, with one difference:
GRT's own `.grtload` has no shooting-distance field at all (only the Journal does, as a manual
entry), so the distance it ranks against is always whichever one is selected in the tool itself, not
something read from the file. It writes the open load's rank, score and breakdown at that distance
into a note — same timestamped-snapshot pattern, original file never touched. See **Toolkit —
Distance workflow report** below for the matching report page.

---

## 19. Powder Compare  🧪

A standalone tool, one caliber-level step coarser than Load Leaderboard: instead of ranking exact
recipes (caliber + powder + bullet + charge), it groups by **caliber + powder only** and asks "of
the powders I've actually calibrated here, which tends to work out for me?" Scored the same 0-10 way
as Load Leaderboard, over the same Journal data.

This can't reach for a powder you've never tried: GRT's own factory propellant database is a single
opaque binary file the plugin API has no call to enumerate, so an untried powder simply isn't
comparable here — what IS comparable, powders you've actually calibrated, is also the more grounded
question, since GRT's predicted numbers for an untried powder are no more trustworthy than its own
factory data (which the GRT community has reported as stale since Gordon's 2022 death).

**Compare** runs on open and again on demand. **Write comparison note to GRT load** compares within
the OPEN load's own caliber (regardless of what the on-screen filter is set to), finds that load's
own powder in the ranking, and adds one line no other tool has: how this file's own `Ba` compares to
the average of every other time this powder was calibrated — the closest thing to a possibly-stale
`Ba` flag this plugin can give without access to GRT's own factory number.

---

## 20. Velocity Model  📉

An empirical `MV ≈ a + b·charge + c·temperature` fit from your own logged Journal sessions, for one
caliber + powder + bullet combo — deliberately **independent of GRT's own physics model** (no
`Ba`/`a0`/`tcc`/`tch` anywhere in it), so it's a measured-data cross-check you can compare against
GRT's simulated/calibrated numbers, not a replacement for Barrel Calibration (§9, fits `Ba`, needs
no temperature spread) or Powder Temp Coefficients (§10, fits GRT's own `tcc`/`tch`, needs no charge
spread).

A 2-variable fit needs **both** charge and temperature to vary across your logged sessions, **and**
to vary somewhat independently of each other — locked at one charge, or every session shot at the
same temperature, or charge and temperature happening to track each other across every session, all
make the fit unrecoverable. The tool says exactly which of these is missing rather than returning a
number it can't stand behind; needs at least 5 logged sessions with charge, temperature AND a
measured velocity all present (§15 — tick *environment recorded* and fill in the temperature).

Once fitted: **Predict MV for charge** (given a charge + temperature) and **Charge needed for MV**
(the inverse — the charge that should land on a target velocity at a given temperature) both work
off the same fit. **Write model note to GRT load** writes the fit itself (formula, sample count,
R²) into a note — not a prediction for the open load, since GRT carries no ambient-temperature
field on a load at all (only the Journal does).

Always in GRT's own native grains / °C / m/s, regardless of what unit GRT is currently displaying
elsewhere: a 2-axis fit's coefficients are only meaningful together with the units they were fitted
in, and this tool's whole point is numeric correctness.

---

## 21. Advanced Diagnostics  🔬

Six diagnostics in one tool, none of which need a calibrated `Ba` to run. Five read chronograph
strings from the load open in GRT (same reader Chronograph Statistics uses); one reads the Journal.

| Tab | Reads | Flags |
|---|---|---|
| **Fouling Tracker** | one string's shots in order | a rising SD / declining velocity trend as the string progresses (barrel fouling) |
| **Cold Bore** | one string's first shot vs. the rest | an honestly-labelled z-score heuristic, not a rigorous significance test |
| **Primer Sensitivity** | several strings, same charge | SD/ES compared across primer lots (uses each string's own GRT name as its lot label) |
| **Neck Tension Correlation** | several strings named by tension (e.g. "0.05", "0.10") | Pearson correlation between neck tension and velocity ES/SD — needs strings actually named by tension to mean anything |
| **Pressure Trend** | one charge ladder | flattening of the velocity-vs-charge slope — diminishing fps-per-grain as charge rises signals approaching a pressure limit, a different signal from both the Ladder flat-spot and the OCW node |
| **Session Trend** | the Journal | SD / ES / velocity drift across REPEATED sessions of the same charge over time — rising SD/ES session over session flags degrading powder, primers or barrel |

**Write diagnostics note to GRT load** writes every tab's result into one note; a tab with nothing
to flag prints "No concerning trend" rather than leaving its section blank.

---

## 22. SD Root-Cause  🌡🎯

Advanced Diagnostics (§21) is six separate tabs you have to already suspect a cause to open. This
tool answers a different question directly: for **one** selected string, which shot(s) — if any —
are dragging an otherwise-good SD down, ranked automatically, without setting anything up first.

**Leave-one-out ranking:** for every shot, the SD the string would have with just that shot removed.
The shot with the biggest drop ranks first. **Pattern**, from how many shots Chauvenet's criterion
(the same test Chronograph Statistics uses, §6) actually flags as statistical outliers — not an
invented percentage cutoff:

| Pattern | Meaning |
|---|---|
| **Single outlier** | one shot is statistically distinct from the rest |
| **Partial outlier** | a few shots are |
| **Systematic** | none are — the dispersion is spread across the whole string |

**Causes**, each shown only when the string's data supports it:

| Cause | Needs | Reused from |
|---|---|---|
| **Cold bore** | you mark the first N shots | the same `Cold Bore` analysis as §21 |
| **Progressive drift** | nothing extra — only fires when no per-shot temperature was entered | the same `Fouling Tracker` trend as §21, honestly relabelled as an *unmeasured probable* thermal cause, since it's really just a firing-order trend standing in for a temperature GRT cannot supply |
| **Temperature ↔ velocity** | per-shot barrel temperature, entered by hand | — |
| **Temperature ↔ group dispersion** | per-shot temperature **and** a matching GRT Shot-group tab | GRT's own native per-shot hit data (§7's Ladder/OCW reader) |
| **Distribution shape** | (Systematic pattern only) an informal skewness note, not a reloading-specific benchmark | — |

**Per-shot barrel temperature is never something GRT supplies** — the plugin API carries only
velocity per shot. If you clip a temperature probe to the barrel and read it at the moment of each
shot, type each reading into the grid next to that shot's velocity; it's saved (keyed to this exact
string, by caliber + name — not to the `.grtload` file, which gets renamed on every write-back) so
it's still there next time you open the same string. Leaving it blank is fully supported: the tool
still runs on cold-bore and progressive-drift alone.

**Temperature ↔ group dispersion** is the one analysis nothing else in the Toolkit does: with a
matching GRT "Shot group analysis" tab for the same string (same shot count, same firing order —
GRT does not link the two itself, and the tool always shows this assumption on screen, not only when
it fails), it correlates your per-shot temperature against each shot's distance from the group
centre, not just its velocity — the direct answer to "does this barrel stop holding tight groups
above some temperature?"

**Write root-cause note to GRT load** writes the ranking, pattern and every triggered cause into one
note, with the leave-one-out bar chart attached as a picture.

---

## 23. Group Analysis  🎯📐

SD Root-Cause (§22) diagnoses one chronograph string; this is its 2D sibling — for one shot
**group**, is the dispersion actually good, how much should you trust that read given the sample
size, and what specific problem (if any) explains a shot or a pattern dragging it down.

**Three ways to load a group**, all producing the same internal group so every metric below works
identically regardless of source:

| Source | Use when |
|---|---|
| **OnTarget CSV** (one file, or a folder of them) | you photograph/scan targets and calibrate in OnTarget |
| **GRT's own Shot-group tabs** | you already build groups inside GRT (see §14's note on that workflow) |
| **Manual entry** | calipers on a printed target, or any other software — type X/Y per shot, in mm or inch |

**Metrics**: extreme spread and mean radius, plus **CEP50** (the radius containing half of a
Rayleigh-distributed group, σ√ln4) and separate horizontal/vertical spread.

**Score (0–10) and Confidence (Low/Medium/High) are two separate numbers, never collapsed into
one** — a tight group from 3 shots and a tight group from 15 shots are not the same claim. Score
comes from dispersion against editable tier thresholds; Confidence comes from sample size alone,
against a **distance band** picked from the group's own shooting distance — **100, 200, 300, 600,
800 or 1000 m**, not a single short/long cutover, because not every shooter can test at 1000 m and
a load proven at 300 m deserves its own bar, not the same one as a load only ever shot at 100 m.
Only the 100 m and 1000 m ends of that scale are anchored to anything real (this tool's own
original default, and the practical minimum — 10 shots — a competitive F-Class shooter said he
never trusts a read below, whatever the distance); the steps in between are a plain interpolation,
not independently sourced, same as the score tiers themselves (a citable long-range minimum-sample
number does not exist in the literature the way CEP50's formula does). All of it is editable in
**Settings**, one tab per band.

**Outlier detection** uses the group's own 2D covariance (Mahalanobis distance) rather than an
assumed circular spread — the 2D analogue of the Chauvenet's-criterion outlier test Chronograph
Statistics (§6) and SD Root-Cause (§22) use on a 1D string. Skipped, not guessed, below a minimum
shot count.

**Problem list**, each with the number behind it, never a bare claim: shots flagged as statistical
outliers (named by index), sample size too small for a confident read at this distance (states the
threshold used), horizontal/vertical spread ratio notably skewed, group centre offset from point of
aim beyond a threshold, and — always shown on the bands configured for it (600 m and up by
default), never hidden — a caveat that raw dispersion still includes uncorrected wind effects.

**"The load has done its job, it's wind now"**: below a configurable vertical-spread threshold
(default ~0.57 MOA, from 6 inches of vertical at 1000 yards — a competitive F-Class benchmark), the
tool says so as a plain, positive note rather than staying silent: past this point, tightening the
group further is about reading wind, not the load. MOA is distance-independent, so this reads the
same whether the group was actually shot at 1000 yards, 1000 m, or 300 m with a proportionally
smaller vertical spread in inches. Gated on the same sample-size floor as everything else here — a
lucky small group reading tight at this threshold is not evidence of anything yet.

**Cross-check against the Ladder/OCW Analyzer's own node**: when a group is loaded via **"Load GRT's
shot-group tabs…"** and that same `.grtload` already carries an "OCW Analysis" note (§7) with a
found node, Group Analysis reports whether this group's own charge falls inside or outside it — a
candidate explanation worth noting alongside the dispersion numbers, never claimed as a proven
cause. This is deliberately the *empirical* half of connecting a group's shape to a possible
physical reason: the node itself came from the user's own measured ladder, not a simulation, so the
cross-check never asserts more than "this matches a pattern you already found in your own data". A
group loaded from an OnTarget CSV or typed in by hand is never cross-checked this way, since there
is no way to be sure it belongs to the same rifle/load as whatever `.grtload` happens to be open.

**Combine groups** pools several charges' groups into one by **recentring each on its own centroid
first** — this isolates pooled dispersion (are these charges each behaving consistently?) from real
point-of-impact differences between them, which a naive pooling would blur together.

**Velocity ↔ dispersion correlation**: point a second folder at matching Athlon chronograph files
(one per charge, same charges as the OnTarget folder) and the tool aligns each shot's velocity with
its distance from the group centre by **firing order** — the same alignment SD Root-Cause's
temperature correlation uses — and reports whether faster shots in this specific string land
closer to or further from centre. This is a measurement of *your* data, never a general claim.

**Write group-analysis note to GRT load** writes the score, confidence, metrics and every triggered
problem into one note, with the target-plane chart (impacts, centroid, point of aim, outliers
highlighted) attached as a picture.

---

## 24. Print Ladder/OCW Target  🎯🖨

A true-scale printable target for a charge ladder, prepared **before** you leave for the range —
the "before the range" counterpart to the Ladder / OCW analyzer (§7), which only reads a group back
afterward. No GRT connection needed.

**Two target types**, both with a shared aim-point row sized to the number of charges (3–12 for
OCW, 3–20 for Ladder) and both showing mm **and** inch scales:

| Target | Layout | Shots per point |
|---|---|---|
| **OCW** | one aim disc per charge, round-robin style, a dashed line connecting every point at the same height | 1–5, repeated in passes |
| **Ladder / Audette** | one aim disc per charge with a ±50 mm ruler centred on it, sized to read at ~200–300 m | 1–5, fired together |

Charge weights are optional (typed in, comma-separated, printed under each point) or left blank for
you to fill in by hand after the range decides the actual step. **Pages per sheet** (0 = all) splits
a long ladder across several A3 sheets, each repeating the header and instructions.

**Print at 100% ("actual size"), never "fit to page"** — the printed 100 mm bar at the bottom is
there to verify the printer didn't silently rescale anything before you shoot at it. Needs A3
paper: an OCW grid plus the header and instructions need more vertical room than A4 has without the
header colliding with the grid.

**Save PDF…** renders straight to a real PDF file at a guaranteed true A3 page, through Windows'
built-in "Microsoft Print to PDF" — unlike whatever printer happens to be selected under **Print…**,
which may not list A3 at all (best-effort there; a clear warning fires if it can't, rather than
silently printing at the wrong scale). **Save PNG…** exports the current preview page as an image.

---

## 25. Load Book Export  📚

Walks a folder of `.grtload` files — recursively, one row per real recipe even when this toolkit's
own write-back siblings (or GRT's own auto-saved ones) sit right next to the original — and renders
one HTML document, grouped by caliber: powder, charge, bullet, COAL, the propellant-model
coefficients (`Ba`/`a0`) GRT's simulation is calibrated to for that recipe, and whatever was
measured. "Measured" prefers a Load Journal (§15) entry logged against that exact file (richer:
bullet name, group size, distance, notes) and falls back to the `.grtload`'s own embedded shot data
when nothing was ever logged for it — those rows are shown in grey italic in both the on-screen
preview and the exported page, so you can tell at a glance which recipes were only ever chrono'd
inside GRT itself.

**Pick folder…** scans; the grid previews what will be exported before you commit to a file.
**Export HTML…** saves the page and opens it in your default browser, where you can print it (to
paper or to PDF, the same "Save PDF…" idea as §24) at whatever page size you choose — deliberately
HTML rather than a bundled PDF generator, so this plugin never carries a PDF library of its own.

A file renamed by a *later* toolkit save, after already being logged in the Journal, won't be
matched back to that Journal entry — the same accepted limitation every other file-path-keyed
lookup in this toolkit lives with.

---

## 26. GRT report templates

**Install GRT report templates** (launcher / Plugin menu) writes sixteen DokuWiki report pages into
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
| **Toolkit — Load leaderboard report** | the leaderboard rank/score note |
| **Toolkit — Distance workflow report** | the workflow rank/score note |
| **Toolkit — Powder compare report** | the powder-compare rank/score note |
| **Toolkit — Advanced diagnostics report** | all six diagnostics tabs' note |
| **Toolkit — SD root-cause report** | the root-cause note + leave-one-out chart |
| **Toolkit — Group analysis report** | the group-analysis note + target-plane chart |
| **Toolkit — Velocity model report** | the empirical fit note |

Every tool that writes a note has its own dedicated page now, except **Seating Force Estimate
(QC)** (§12), the **Guided New Load wizard** (§4), **Print Ladder/OCW Target** (§24) and **Load
Book Export** (§25) — none of the four write anything back to an open GRT load (the last two need
no GRT connection at all).

**To view a report in GRT:** Results panel → **+** (new tab) → **Add report** → pick a "Toolkit —
…" page. Open it **on the `…_toolkit_…grtload` snapshot** (that's where the notes and charts live),
not on your original file. Empty sections just mean you haven't run that tool yet.

---

## 27. Debug command line

All on the exe (`plugins\ReloadingToolkit\GRT_Reloading_Toolkit.exe`):

```
--dbtest [db]                                    inventory / journal self-test (writes fixture data
                                                  to [db], or to a throwaway temp file if omitted --
                                                  NEVER to your real Journal/Inventory database)
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

## 28. Limitations & FAQ

**Why a pile of `_toolkit_…` files?** GRT's plugin API can't edit the open load and can't reload an
already-open tab — so each write is a fresh timestamped snapshot. Each snapshot is complete; keep
the newest (Save As under a real name) and delete the rest. Only the 3 newest are kept
automatically.

**I changed `Ba` but GRT's velocity didn't move.** Either GRT is still showing the old tab (open the
newest snapshot), or the correction is genuinely tiny — a 0.1 % offset moves V0 by ~0.6 m/s, which
is inside chronograph noise.

**"No usable shot groups in that load."** Your load has no GRT Shot-group tabs — you analysed groups
in OnTarget instead. Use the `.csv` path, or build the groups in GRT first (§14).

**Temp-coefficient tool won't enable "Write".** It needs one charge at several temperatures, not a
ladder.

**One toolbar icon, not twenty-two.** By design — it opens the launcher; every tool is a button there.
Manifest changes need a GRT restart to show.

**Numbers:** every field accepts `.` or `,` as the decimal separator regardless of your Windows
locale. Report text is always written with a `.` decimal (GRT's requirement).
