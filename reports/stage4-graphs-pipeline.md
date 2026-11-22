# Stage 4 report: Graphs, data pipeline, and helpers (2026-11-22)

## What this stage was meant to accomplish
Make the everyday workflow work end to end on Windows: get terrain, convert it, run SPLAT!, draw the
graphs, and view the maps. Then finish with **you** running one job from the README alone. No
SPLAT! source code changed in this stage.

## Outcome: built and checked; the final gate step is yours
- **gnuplot 6.0.6 installed from MSYS2** (D8). `build.ps1` now writes `dist\gnuplot.cmd`, a one-line
  shim that calls MSYS2's gnuplot by full path, so SPLAT! finds it with only `dist\` on PATH and the
  rest of MSYS2 stays hidden. **All 8 graphs now render on Windows.**
- **Graphs match Linux.** The `.gp` data and the gnuplot commands SPLAT! writes are identical (data
  within the D12 tolerance). The PNGs are the same size (640×480), and 2.7–5.7% of pixels differ:
  **only the lettering** (gnuplot 6.0.2 on Linux vs 6.0.6 on Windows draw fonts slightly
  differently). Every plotted line is pixel-identical. The side-by-side page is
  `work\graph-compare\index.html` (I sent it to you).
- **Full suite with gnuplot (`work\windows-s4`):** the same as Stage 3. 26 byte-identical, 61
  identical apart from ratified differences, 8 within the D12 tolerance, and **8 PNGs that differ
  in bytes only** (above). Nothing is missing, extra, or different.
- **`scripts\ppm2png.ps1`** (D9): `.ppm` → `.png` with .NET (no install), **verified
  pixel-for-pixel** after writing. A 2400×2400 map takes under 1 s (16.9 MB → about 3.2 MB). If a
  SPLAT! `.kml` refers to the map, it also writes a companion `*-png.kml` for Google Earth.
  SPLAT!'s own `.kml` is left byte-identical. Bad and truncated PPMs are refused, with no partial
  PNG left behind.
- **`scripts\get_terrain.ps1`:** from a site and radius (or a box), it lists the tiles, downloads them
  from the D7 sources (cached in `data\raw`, never twice), and converts them with
  `srtm2sdf`/`srtm2sdf-hd`. Tested without any new downloads (from the cached archives): **all 5
  SDFs it produced are byte-identical to the Linux goldens**. Re-runs skip finished work, a
  truncated tile is refused, and tile naming is checked across the Equator and Greenwich.
- **`README.md`** covers setup, terrain, site files, running, viewing maps, the differences from
  Linux, and the developer checks. **Dry run:** I followed it literally in a fresh PowerShell and an
  empty folder, through terrain, a path report with graphs, a 30-mile coverage map, PNG, and Google
  Earth KML. It worked, and its report is identical to the golden P01.
- **USGS `postdownload`: deferred** (your decision). It is recorded in the runbook.

## Findings worth noting
1. **The gnuplot install is about 1 GB, not the ~50 MB I quoted.** pacman pulled in Qt, wxWidgets,
   and other GUI dependencies of the MSYS2 gnuplot package. I quoted without checking the
   dependency list. **Lesson recorded: check `pacman -S --print` sizes before quoting.** It's all
   inside `C:\msys64`, and nothing is on the system PATH. If space matters, a no-GUI gnuplot is a
   Stage 5/6 option.
2. **The post-install "failed" message was harmless.** fontconfig couldn't write a system-wide font
   cache, but a per-user cache exists, and a PNG renders in 0.05 s.
3. **`_` in a site name becomes a subscript in graph titles** (gnuplot "enhanced text"). This is
   upstream behaviour on both systems, now a README tip. (Our own test site `Legion_Hall` shows it.)
4. **A SPLAT! site report appends when re-run in the same folder.** The dry run's
   `K4HTA-site_report.txt` held two runs. That's upstream behaviour, worth knowing.
5. **Script lessons:**
   - Windows PowerShell 5.1 leaves `$PSScriptRoot` empty inside `param()` defaults.
   - `[char]` won't take the double that `Math.Floor` returns.
   - A stray `\r` again came through shell quoting. Each was caught by the scripts' own tests on the
     first run.

## Declared coverage gaps
- **PNG judgement is visual** (yours). The comparer still reports PNGs as byte-DIFF. See D13.
- **`get_terrain.ps1` real downloads were not exercised in this stage** (the cache was used
  deliberately, to avoid new downloads). The download URLs are the ones used successfully in
  Stage 1.
- **A right-sized but wrong-content tile** (e.g. all zeros) can't be detected: zero is a legal
  sea-level elevation.
- **`ppm2png`'s own verification wasn't forced to fail** (it would need a broken PNG encoder). It
  re-reads the written file independently.

## Decisions for the owner
| # | Decision | Recommendation |
|---|---|---|
| **D13** | **How graph PNGs count in the acceptance test.** Bytes can never match across gnuplot versions. | Treat a PNG as passing when it has the same size and its `.gp` data passes D12; report pixel-difference % for information. Visual spot-checks only when gnuplot changes version. |
| *(gate)* | **Run one job yourself from `README.md`**, e.g. a coverage map for K4HTA, and open the `-png.kml` in Google Earth | — |

## Files added or changed
`build.ps1` (gnuplot shim), `README.md` (new), `scripts\ppm2png.ps1`, `scripts\get_terrain.ps1`,
`scripts\compare_graphs.ps1` (new), and this report.

## Next stage (needs owner go-ahead): Stage 5, packaging and hand-off
A zip with all the `.exe` files, the docs, the sample data, `COPYING`, the README, and the port's
source (GPL), tested on a clean account. Decide D10 (who uses it, where it lives, PATH). Decide how
other PCs get gnuplot.

## Halted for owner review, including your end-to-end run.
