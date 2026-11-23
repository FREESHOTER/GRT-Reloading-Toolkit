# Reload Lab

The program that works on **your own archive of reloading and shooting**, on its own, without GRT. It is built from the same source as the
[GRT Reloading Toolkit](https://github.com/FREESHOTER/GRT-Reloading-Toolkit) (the plugin for Gordon's Reloading Tool), but it is a separate download with
its own `Reload_Lab.exe`.

|  | **GRT Reloading Toolkit** (plugin) | **Reload Lab** (program) |
|---|---|---|
| Started by | GRT, from its Plugins menu | you, by double click |
| Works on | the load open in GRT (notes, charts, corrected inputs written back) | your own archive (`Documents\Ricarica\archivio.db`) |
| Has | chronograph and ShotMarker import, ladder / OCW, seating depth, calibration, journal, inventory, follow a load, stability… | all of those, **plus** cartridges, training and race, the reading of the shots, the component library, the home page with the archive at a glance |
| Needs GRT | yes | no |

## Start

Unzip the folder anywhere and double-click `Reload_Lab.exe`. No .NET install is needed.

- Your data lives in `Documents\Ricarica\`: the archive, a dated copy of it when you close the program (the last 30 are kept), the PDF reports and the component library.
- GRT is optional. A file `cartella_grt.txt` beside the exe (first line: the folder of GRT) lets Reload Lab read GRT's own units, the folder of powders and where to put report pages. Alone, the program cannot talk to an open GRT: the chronograph import writes to the archive and, if you tick "send to GRT too", saves a `.grtload` file that you open in GRT.
- **Keep a copy of `archivio.db` before updating to a new version**: the first start brings an older archive to the new format.

## Manual

`MANUAL.md` and the manuals in `docs\` (English, Italian, German, French) describe every tool; sections 32 to 36 are about what belongs to Reload Lab (the archive, the cartridges, training and race, units, charts).

Free and open source (MIT): https://github.com/FREESHOTER/GRT-Reloading-Toolkit — bugs and feedback in the issues.
