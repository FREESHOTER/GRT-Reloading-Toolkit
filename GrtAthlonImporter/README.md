# GRT Athlon Rangecraft Importer

A [Gordon's Reloading Tool](https://grtools.de) plugin (community, unofficial) that imports
**Athlon Rangecraft Velocity Pro** `.xlsx` chronograph exports into GRT's Measurements tab.

- **Batch = ladder.** Select one Athlon file per charge; each becomes a `<charge>` in one
  `<Measurement>`, ordered by charge weight.
- **Charge weight** is read from the Athlon *session note* (`carica 39.2`) or, failing that,
  from the file name (`39.20.xlsx`).
- **Per-string temperature** (°C) — editable column; written into the charge `name` and `note`
  as `@ 21 °C` / `T=21°C` so GRT's powder temperature-coefficient assistant can read it.
  (Per-*shot* temperature is optional and cosmetic — GRT has no per-shot temperature field.)
- Velocities are stored in **m/s** (converted from ft/s if the file uses FPS).

### File handling

GRT's plugin IPC has no "write into the open tab" — `Load_File` only opens a **new tab**. So one
**import** produces **one file** (never one per charge):

- The open load is copied to `"<load>_athlon_<yyyymmdd_hhmm>.grtload"` next to the original, with a
  single new `<Measurement title="Athlon Ladder|Measurement <date>">` appended. Everything else in
  the load is preserved byte-for-byte (only the XML declaration case may change). GRT opens the copy;
  you review and **Save As** over your original when happy.
- **replace previous Athlon import in this load** (checkbox, on by default): before appending, any
  earlier `Athlon …` Measurement is removed, so re-importing the same ladder doesn't pile up.
- **Anti-chaining**: if the tab active in GRT is itself a previous Athlon import (or a placeholder),
  the plugin refuses and asks you to switch to your real load first.
- No saved load open → the plugin asks where to save a stand-alone file.

## Build

Requires the .NET 8 SDK.

```
dotnet build -c Release
```

### Package as a GRT plugin

```
pwsh ./build-plugin.ps1 -GrtDir "C:\path\to\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
```

This produces `dist/AthlonImporter/` (self-contained, no runtime needed) and, if `-GrtDir`
is given, copies it to `<GrtDir>\plugins\AthlonImporter\`. Restart GRT; the toolbar/menu
entry "Athlon Importer" appears.

## Run stand-alone (no GRT)

```
GRT_Athlon_Importer.exe
```

The UI works without GRT; "Import" then just writes a minimal `.grtload` next to your temp
folder instead of merging into the open load.

## Debug

```
GRT_Athlon_Importer.exe --parse path\to\export.xlsx [path\to\base.grtload]
```

Dumps the parsed string and the generated `.grtload` to the console.

## Protocol notes

The GRT plugin IPC (TCP `127.0.0.1:<--ipcport>`, concatenated compact-JSON messages, no
framing) is implemented in `Ipc/`. Commands used: `Get_TabOnTop`, `Load_File`. See
`Ipc/GrtClient.cs` and `Ipc/JsonFrameSplitter.cs`.

## Status

v0.1 — verified against real data and a running GRT:

- parser: 10 real Rangecraft exports (`39.20`…`41.00`), incl. `1.0`-style shot indices and
  bare-number session notes; SD is population (÷n) like the device.
- `.grtload` writer: merges into `noname.grtload` / real loads, base preserved, replace-previous works.
- live GRT (2021.2030 on Win11): manifest accepted, toolbar entry created, click launches the
  plugin, IPC connects, `Get_TabOnTop` + `Load_File` round-trip, GRT parses the imported Measurement
  (charge, shots, m/s velocities, note).

Not yet done:

- multi-select in the file dialog when driven by automation (manual use is fine).
- optional in-place write (rewrite the open load and rely on `Load_File` to re-read it) —
  needs a check of whether GRT actually re-reads an already-open path.
