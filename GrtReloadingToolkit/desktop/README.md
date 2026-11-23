# Reloading Toolkit Desktop

The program that works on **your own archive of reloading and shooting**, on its own, without GRT. It is built from the same source as the
[GRT Reloading Toolkit](https://github.com/FREESHOTER/GRT-Reloading-Toolkit) (the plugin for Gordon's Reloading Tool), but it is a separate download with
its own `Reloading_Toolkit_Desktop.exe`.

| | **GRT Reloading Toolkit** (plugin) | **Reloading Toolkit Desktop** (program) |
|---|---|---|
| What it is | the Toolkit as it has always been, inside GRT | the same Toolkit as a separate program, for whoever does not want it inside GRT |
| Asset | `ReloadingToolkit.zip` (or `-lite.zip`) | `ReloadingToolkitDesktop.zip` |
| Started by | GRT, from its Plugins menu | you, by double click |
| Needs GRT | yes | no: from GRT it takes only what it needs so that nothing is typed twice (a load's file, GRT's units, the folder of powders) |
| Has | all the tools: chronograph and ShotMarker import, ladder / OCW, seating depth, calibration, journal, inventory, follow a load, stability… | everything the plugin has, **plus** the novelties: your own archive, cartridges (measure, predict, plan), training and race, the reading of the shooter's errors and of a match, the component library and a home page |
| What it lacks | the novelties | barrel calibration and the model probe, which run GRT's own simulation and need GRT open |

## Start

Unzip the folder anywhere and double-click `Reloading_Toolkit_Desktop.exe`. No .NET install is needed.

- Your data lives in `Documents\Ricarica\`: the archive, a dated copy of it when you close the program (the last 30 are kept), the PDF reports and the component library.
- GRT is optional. A file `cartella_grt.txt` beside the exe (first line: the folder of GRT) lets Reloading Toolkit Desktop read GRT's own units, the folder of powders and where to put report pages. Alone, the program cannot talk to an open GRT: the chronograph import writes to the archive and, if you tick "send to GRT too", saves a `.grtload` file that you open in GRT.
- **Keep a copy of `archivio.db` before updating to a new version**: the first start brings an older archive to the new format.

## Manual

`MANUAL.md` and the manuals in `docs\` (English, Italian, German, French) describe every tool; section 32 says what each product has; sections 32 to 36 describe the novelties that only this program has (the archive, the cartridges, training and race, units and charts).

Free and open source (MIT): https://github.com/FREESHOTER/GRT-Reloading-Toolkit — bugs and feedback in the issues.
