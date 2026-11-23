# GRT Inventory & Load Journal

Community plugin for [Gordon's Reloading Tool](https://grtools.de). Tracks reloading component
inventory with **cost per round**, and a **load / range journal** that deducts stock as you shoot.

## Storage

SQLite at `%AppData%\GRTPlugins\reloading_log.db` (override with the `RELOADING_LOG_DB` env var).
Tables: `components`, `journal`, `ledger` (an audit trail of every stock movement).

## Inventory

Powder / Primer / Brass / Bullet, each with brand, name, lot, quantity (initial + current),
lot cost + currency, and — for brass — *expected uses* (firings before retirement, for cost
amortisation). Rows go amber at ≤25 % remaining, red at ≤10 %.

## Journal

An entry records date, load, caliber, firearm, the four component lots, charge, COAL/CBTO,
round count, MV / SD / ES, group MOA, distance and notes.

- **deduct components from inventory on save** (default on): powder −(charge_gr × 0.0648 × rounds) g,
  primer / brass / bullet −rounds. Editing or deleting an entry reconciles the stock automatically.
- **Log from GRT** reads the load open in GRT (`Get_TabOnTop`), pulls caliber / firearm / charge and
  the MV stats of the last populated Measurement charge, and pre-fills a new entry.

## Cost per round

`powder = charge_g × lotCost / qtyInitial_g` · `primer = bullet = lotCost / qtyInitial` ·
`brass = lotCost / qtyInitial / expectedUses`. Shown live while editing an entry and as a column
(plus the total for the round count).

## Build

```
dotnet build -c Release
pwsh ./build-plugin.ps1 -GrtDir "C:\path\to\GRT-2021.2030 (W11) V1.0\GRT-2021.2030 (W11) V1.0"
```

## Debug

```
GRT_Reloading_Log.exe --dbtest [db-path]
```

Creates a throwaway DB, logs a sample entry, prints the cost breakdown and the stock before/after,
then rolls it back.

## Status

v0.1 — DB, cost model and stock reconciliation verified headless; UI renders. Not yet clicked
through a running GRT.
