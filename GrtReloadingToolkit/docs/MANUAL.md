# GRT Reloading Toolkit — Manual

A community plugin for **Gordon's Reloading Tool (GRT)**. It bundles eight reloading tools plus a
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
4. Optional: in the launcher, click **"Install GRT report templates"** once (see §11).

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

## 4. Ladder / OCW Analyzer  📈

Finds the accuracy node in a **charge ladder** from chronograph velocities and target groups.

**Two data sources for the groups:**

- **Ballistic-X `.csv` exports** — put them in the same folder as the chrono files, one per charge.
- **GRT's own "Shot group" tabs** — see §10.

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
| **Groups from GRT load** | Read the group data from GRT's Shot-group tabs instead of `.csv` (with the two unit boxes + *drop flyers*, see §10). |
| **Write note to GRT load** | Write the "OCW Analysis" note **and the chart** into the load. |

**Grid:** charge · n · MV · SD · ES · distance · POI-Y (MOA) · group mean-radius (MOA) · vertical
spread (MOA). The recommended node is shaded green.

**What it computes**

- **Velocity flat-spot (Satterlee):** the window of steps where average MV changes least.
- **Vertical-POI node (OCW / Audette):** the window where the vertical point of impact is most
  stable.
- **Recommended node:** a weighted blend of MV flatness + POI stability + group size + SD.

The report and chart are written as `~~result.Note("OCW Analysis")~~` and
`~~result.picture.ocw_chart.png~~` — see §11.

> Always confirm a node with a fresh group before committing. SD here is population SD (n).

---

## 5. Seating-Depth Analyzer  📏

Same window and workflow as the OCW analyzer, but the X axis is **seating depth / jump (mm)** and
the primary signal is the **group-size plateau** rather than the MV flat-spot. The step value is
read from a number in the session note or file name ("salto 0.02", "jump .015", "cbto 2.850").

Writes `~~result.Note("Seating Depth Analysis")~~` + `~~result.picture.seating_chart.png~~`.

---

## 6. Barrel Calibration  🎚

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
  it ("shape error" — check fired case volume, barrel length, bullet weight first).
- Suggested `Ba` = old `Ba` × (mean measured ÷ mean sim)²  (GRT's MV scales roughly as √Ba).

**Reading the result:** a small, consistent offset (≤ ~0.3 %) means your barrel already matches
GRT — the `Ba` change will be a fraction of a percent and the remaining per-charge scatter is
chronograph noise, not something to chase. A corrected `Ba` is specific to **that barrel + that
powder lot**; don't treat it as a universal powder value.

---

## 7. Powder Temp Coefficients  🌡

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

## 8. Brass Prep  🔧

Three calculators, one per tab.

### 8a. Case volume → GRT `casevol`

Enter weights for **3+ fired, de-primed** cases: empty and full-of-water (or the water weight
directly). Water is weighed in **grain** or **gram** (toggle). Output is **mean case volume in
grain H₂O** and **cm³**.

- GRT stores `casevol` in **cm³** internally, even though its UI usually shows grain H₂O — the tool
  writes cm³ and pins the unit. Water density 0.99821 g/cm³ (20 °C).
- **Write casevol to GRT load** writes the **mean** into the caliber; SD / range go in the note.

### 8b. Seating depth (from comparator measurements) → GRT `gdepth`

Enter **CBTO** (cartridge base-to-ogive, loaded round), **case length**, **BBTO** (bullet
base-to-ogive, bare bullet) — CBTO and BBTO to the **same comparator insert**. Units mm or inch.

```
DIFF          = CBTO − case length      (how far the ogive sits above the case mouth)
seating depth = BBTO − DIFF             (bullet base to case mouth = GRT's "seating depth")
```

Warnings flag impossible geometry and unusually shallow / deep results. **Write seating depth to
GRT load** sets `gdepth`; GRT recomputes COAL from it.

### 8c. Neck / bushing sizing

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

## 9. Load Card / Label  🏷

Printable **recipe card (A6)** or **ammo-box labels**, each with a **QR code** of the full recipe.

- **Load from GRT** — fill the card from the open load (caliber, firearm, bullet + weight, powder,
  charge, COAL, seating depth, and MV/SD from the last measurement).
- **Powder / Primer / Brass / Bullet lot** drop-downs — pick a component lot from the Inventory
  (§10) to compute **cost per round** and stamp the lot on the card.
- Edit any field in the property grid. Choose **Recipe card** or **Box labels** + a count.
- **Save PNG…**, **Print…** (print-preview), or **Write load-sheet note to GRT** — puts the recipe
  + per-round cost breakdown into the load as a "Load Sheet" note.

---

## 10. Inventory & Load Journal  📒

A small SQLite database at `%AppData%\GRTPlugins\reloading_log.db`. Three tabs.

- **Inventory** — powder / primer / brass / bullet stock with lot id, price and (for brass) an
  *expected uses* count that amortises the case cost. **Add / Edit / Restock / Archive**.
  Stock can go **negative** — that just means the lot was entered short, or rounds were logged
  against the wrong lot. It is never silently absorbed, so editing or deleting the entry always
  gives back exactly what it took. Fix it with **Restock** (or **Edit** the lot's quantity).
- **Journal** — one row per range session or batch: date, load, caliber, firearm, charge, rounds,
  MV, SD, group MOA, distance, notes, and **cost per round** from the referenced lots. **New**,
  **Log from GRT** (prefills from the open load), **Edit**, **Delete**. Editing or deleting an
  entry reconciles the inventory. Tick *deduct components from inventory on save* to draw stock
  down.
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

## 11. GRT report templates

**Install GRT report templates** (launcher / Plugin menu) writes five DokuWiki report pages into
`GRT\doku\<language>\report\` and links them in the report index. Run it once per GRT install
(and tell anyone you share the plugin with to do the same). It is idempotent and non-destructive;
delete a page by saving it empty in GRT.

| Page | Pulls |
|---|---|
| **Toolkit — Full load workup** | recipe + predicted results + GRT curve diagram + every section below |
| **Toolkit — Ladder / OCW report** | the OCW note + chart |
| **Toolkit — Seating-depth report** | the seating note + chart |
| **Toolkit — Barrel calibration report** | the calibration + temp-coefficient notes |
| **Toolkit — Load sheet** | recipe + predicted + cost note |

**To view a report in GRT:** Results panel → **+** (new tab) → **Add report** → pick a "Toolkit —
…" page. Open it **on the `…_toolkit_…grtload` snapshot** (that's where the notes and charts live),
not on your original file. Empty sections just mean you haven't run that tool yet.

---

## 12. Debug command line

All on the exe (`plugins\ReloadingToolkit\GRT_Reloading_Toolkit.exe`):

```
--dbtest [db]                                    inventory / journal self-test
--athlon <folder> <base.grtload> [tempC]         chrono import from a folder
--ladder charge|seating <folder> [base.grtload]  ladder analysis
--ladder charge|seating --synthetic [out.png]    fixture-free chart test
--groups <load.grtload> [mm|cm|in] [m|yd] [--drop-flyers]   GRT shot-group → MOA
--cal <port> <base> <charge:simMv> …             calibration math (sim MVs given, not swept)
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

## 13. Limitations & FAQ

**Why a pile of `_toolkit_…` files?** GRT's plugin API can't edit the open load and can't reload an
already-open tab — so each write is a fresh timestamped snapshot. Each snapshot is complete; keep
the newest (Save As under a real name) and delete the rest. Only the 3 newest are kept
automatically.

**I changed `Ba` but GRT's velocity didn't move.** Either GRT is still showing the old tab (open the
newest snapshot), or the correction is genuinely tiny — a 0.1 % offset moves V0 by ~0.6 m/s, which
is inside chronograph noise.

**"No usable shot groups in that load."** Your load has no GRT Shot-group tabs — you analysed groups
in Ballistic-X instead. Use the `.csv` path, or build the groups in GRT first (§10).

**Temp-coefficient tool won't enable "Write".** It needs one charge at several temperatures, not a
ladder.

**One toolbar icon, not eight.** By design — it opens the launcher; every tool is a button there.
Manifest changes need a GRT restart to show.

**Numbers:** every field accepts `.` or `,` as the decimal separator regardless of your Windows
locale. Report text is always written with a `.` decimal (GRT's requirement).
