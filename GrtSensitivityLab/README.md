# GRT Sensitivity Lab

A GRT plugin for asking "what if I use the value I actually measured?" instead of the value a
`.grtload` file happens to have on file.

## What if

Pick a GRT input (case volume, case length, barrel length, groove diameter, bullet mass, seating
depth, bearing-surface pressure, bullet friction), type in the value you actually measured for it,
and run. GRT Sensitivity Lab re-simulates your active load with that one substitution and reports
how GRT's predicted muzzle velocity and max pressure change versus the file as it stands -- one
input at a time, with results accumulating so you can build up a picture across several
measurements.

This studies how GRT's own model reacts to a real measurement on the components you actually
have, not a hypothetical +/- manufacturing tolerance.

## Sensitivity sweep

The companion question: before you've measured anything, which inputs are even worth measuring
precisely? Pick the inputs to check, and Sensitivity Sweep nudges each one by +/- a delta (or, for
bullet friction, tests 0/50/100 bar) and ranks them by how much GRT's predicted muzzle velocity
moves -- largest first.

## Requirements

- GRT 2021.2030 or later
- Windows 10/11 x64
