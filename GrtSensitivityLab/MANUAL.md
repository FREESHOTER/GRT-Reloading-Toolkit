# GRT Sensitivity Lab -- manual

## What if

1. Open a load in GRT and compute a result for it (What If needs a baseline result to compare
   against).
2. Open "GRT Sensitivity Lab" from GRT's toolbar/menu.
3. Click "Read current values from GRT" to pull the active load's own values into the form.
4. Check the input(s) you want to test, and type in the value you actually measured for each
   (a burette-measured case volume, a scale-verified bullet weight, a caliper seating depth...).
5. Click "Run what-if". The tool briefly opens one extra GRT tab per checked input (plus one for
   the file as-is), reads each simulated result, then reopens your original load.
6. The grid fills in one row per substitution: the file's own value, your measured value, GRT's
   simulated muzzle velocity and max pressure, and how much each moved versus the file as-is.
   Run again with different inputs to keep adding rows.
7. "Write what-if note to GRT load" saves the accumulated table as a note on a copy of your load
   (suffixed `_whatif`), and opens that copy in GRT.

## Sensitivity sweep

1. Open a load in GRT and compute a result for it.
2. Open "GRT Sensitivity Lab" from GRT's toolbar/menu, and pick "Sensitivity sweep".
3. Check the inputs to test (all are checked by default), adjusting each one's +/- delta if the
   default doesn't match a realistic tolerance for that component.
4. Click "Run sweep". The tool briefly opens two extra GRT tabs per checked input (one at +delta,
   one at -delta; bullet friction instead tests fixed values of 0/50/100 bar), plus one for the
   baseline, then reopens your original load.
5. The grid fills in, ranked by impact -- the input whose +/- delta moved GRT's predicted muzzle
   velocity the most appears first.
6. "Write sensitivity note to GRT load" saves the ranked table as a note on a copy of your load
   (suffixed `_sweep`), and opens that copy in GRT.

## A note on bullet friction (gfriction)

Unlike every other input here, bullet friction/resistance is **relative to GRT's own computed
default**, not an absolute physical value -- GRT's own documentation (Projectile window, "Bullet
resistance") is explicit that 0 means "use the default" (part of GRT's internal energy-loss model,
based on bullet length and cross-section), not "zero resistance". A positive value adds resistance
on top of that default; a negative one reduces it. So it's expected, not a bug, that a *higher*
value here (e.g. 100 bar vs. 50 bar) predicts a *lower* muzzle velocity -- more resistance means
more of the powder's energy is lost to friction instead of accelerating the bullet.

## GRT report templates

"Install GRT report templates" writes a printable report page into GRT's own doku system (one per
language folder). It pulls in both tools' notes by exact title, so it stays blank until you've run
a tool and used its "Write ... note to GRT load" button at least once on that file. Reach it from
GRT's "+" -> "Add report" once installed. Safe to run again any time (e.g. after an update) --
it just rewrites the page and skips the link if `start.txt` already has it.

## Units

Every length (mm/in) and volume (cm3/gr H2O) field has its own metric/imperial dropdown right next
to it -- there's no single form-wide unit switch, so you can type a case volume in cm3 and a
seating depth in inches in the same run. GRT's own file is always written in its native metric
units regardless of what you have selected.
