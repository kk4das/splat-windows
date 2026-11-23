# Stage G4 report: coverage maps in the app (2026-11-23)

## What this stage was meant to accomplish
Coverage maps without the command line:
- pick a transmitter, what to map, a radius and a receiver height, then click **Run**;
- terrain is fetched automatically;
- the map appears in the window over the street or terrain map, with opacity control, cancel and a
  history of earlier maps.

The plan's gate: **you run the 30-mile K4HTA map from the app alone.** Started on your words
"Start G4".

## Outcome: built and checked; the gate is yours
The app is open on your screen (`gui\publish\SplatForWindows.exe`, version 0.5.0).

- **Coverage map…** (next to Analyse a path…) opens the coverage panel:
  - **Transmitter:** with its frequency and ERP. These are the radio settings saved with the station
    in G3, so K4HTA's are already there if you entered them. Changes are saved with it.
  - **What to show:**
    - signal strength (dBµV/m);
    - received power (dBm);
    - path loss (dB);
    - line of sight (green; no radio settings needed).
  - **Radius and receiver antenna height,** in miles and feet, or km and m.
  - **More options:** ITWOM or Longley-Rice, smooth colour bands (`-sc`), metric, and HD terrain.
  - **A radius bigger than SPLAT! can map is refused, with the limit.** About 155 miles at your
    latitude (66 for HD); beyond that SPLAT! quietly cuts the map short.
- **Run:**
  1. **Missing terrain is fetched first, automatically,** from the approved sources (runbook D7):
     viewfinderpanoramas.org for standard terrain and AWS skadi for HD. The app downloads, unpacks,
     size-checks and converts each tile with the engine's `srtm2sdf`, exactly as
     `scripts\get_terrain.ps1` does.
     - Downloads are kept, so nothing is fetched twice.
     - A broken or truncated download is thrown away, never used.
     - Where a source has no tile, that's open water, and it is recorded so (sea level is then the
       right answer).
     - **Path analysis now fetches missing terrain too,** instead of refusing as in G3.
  2. SPLAT! runs once with `-kml` in a new job folder (`Documents\SPLAT\Jobs\<date> K4HTA coverage`),
     with `run-again.cmd` as in G3. Progress shows in the status line, and **Close becomes Cancel**
     while it runs.
  3. **The map** appears over the street or terrain map, with the existing opacity slider and
     **Open in Google Earth Pro**.
     - **SPLAT!'s colour key** is shown in the panel. It's now also inside the Google Earth file;
       before, the key was left out.
     - A line-of-sight map gets a one-line key instead.
- **Earlier maps:** every finished map is listed, newest first. Click one to show it again from its
  job folder; nothing is re-run. Cancelled or failed runs aren't listed.
- **Terrain folder:**
  - Yours is still `data\sdf3`, from G3, and new tiles are added there.
  - If none is chosen, the app uses its own local data folder, `%LOCALAPPDATA%\SPLATforWindows\Terrain`.
    That's not Documents, which OneDrive syncs.
  - Standard and HD tiles can share one folder.
- **The engine is unchanged.** The app now also ships `srtm2sdf.exe` and `srtm2sdf-hd.exe` from
  `dist\`.

## How it was checked
- **End to end against the Linux golden run, from an empty terrain folder:
  `scripts\gui_coverage_equiv.ps1`. Result: EQUIVALENT.**
  - **How it works:** the published app runs coverage jobs headless (`--coverage-job`), through the
    same code as the window.
    - Terrain comes from a stand-in of the real sources on localhost (`scripts\fake_terrain.ps1`).
      The stand-in serves the project's cached downloads, so no real server is touched.
    - The app fetches and converts the terrain, runs SPLAT!, and `compare_outputs.py` compares the
      result with the golden run.
  - **The cases:**
    - **M01:** line of sight, 30 mi, with town names.
    - **M07:** signal strength, 30 mi, HD. This is the 7200×7200 map and its colour key, and the
      terrain is four HD tiles fetched and converted by the app.
  - **The result:** all 8 files are identical, apart from CR-LF line endings in 3 text files. The
    maps (`.ppm`) and KMLs are byte-identical.
    - Not compared: console output (the app passes an absolute terrain path) and `-log`'s
      `cmd.log` (the app keeps `splat-log.txt` and `run-again.cmd` instead).
  - Saved in `tests\results\stageG4-gui-coverage-equiv.txt`. The **G3 path gate** still says
    EQUIVALENT.
- **273 unit tests pass** (25 new):
  - **Which tiles:** the app picks exactly the tiles SPLAT! itself loaded, in its order, in the golden
    runs M01, M02, M04 and M07.
  - **Fetching:**
    - **Tiles fetched and converted by the app are byte-identical to the Linux golden terrain**
      (standard and HD).
    - Downloaded once and reused. A 404 is recorded as open water.
    - Refused: a truncated download, a wrong-size tile, and a corrupt archive (each removed, so it's
      fetched again).
    - No internet is explained.
  - **Coverage jobs:**
    - **An app line-of-sight job's map is byte-identical to golden M01.**
    - The options become SPLAT!'s flags. A path-loss map writes ERP 0, which is how SPLAT! is told.
    - Radius limits. Cancel kills SPLAT! and records the job as cancelled.
    - History: newest first, with unfinished, damaged and unrelated folders skipped.
    - The KMZ carries the colour key.
- **The real window: `scripts\gui_coverage_uitest.ps1`, 34 of 34 checks pass.** It uses an empty
  terrain folder and the stand-in sources. Covered:
  - refusals (no download, no job folder);
  - a line-of-sight map with the four tiles fetched from one download (`J18.zip`), and the overlay on
    the map at SPLAT!'s box;
  - a signal map with no new download, the colour key shown, and the radio settings saved;
  - the KMZ holding the key;
  - an HD map cancelled while SPLAT! HD computed (process stopped, not in the history);
  - an earlier map shown again without re-running;
  - only the stand-in server was ever asked.
- **One live download through the app,** to prove it works against the real server: a 10-mile map
  at 40.5° N fetched `K18.zip` (27 MB) from viewfinderpanoramas.org, converted the tile and made the
  map in 17 s. It's in `work\g4-live`, not in your terrain folder.
- **Shown to fire:**
  - **Ten deliberate breakages, all caught by the unit tests:**
    - tile order;
    - no size check;
    - a broken download kept;
    - 404 not treated as water;
    - `-dbm` dropped;
    - line of sight run as `-L`;
    - path loss keeping the ERP;
    - unfinished jobs in the history;
    - the key dropped from the KMZ;
    - the radius limit without latitude.

    The third was **missed at first**: no test made an archive that couldn't be opened at all. I
    added one, and it is now caught.
  - **A broken build** (line of sight run as `-L`) fails the gate script (2 DIFF, 2 EXTRA) and the
    window test.
  - The code was restored and checked identical (sha1).
- **Earlier stages, on the new build:** path 33/33, stations 31/31, QRZ 24/24. The path window test
  now points terrain at a closed local port, so it can't reach the internet, and checks that a failed
  download stops the job. **Toolchain preflight OK at both ends** (from PowerShell). The SPLAT!
  engine is unchanged.

## Findings worth noting
1. **The app now names site files in lower case** (`k4hta.qth`), as SPLAT!'s documentation and your
   test files do.
   - SPLAT! writes that name inside its colour file (`k4hta.scf`). With `K4HTA.qth`, the M07
     comparison differed by that one word.
   - I changed the app rather than add an exception to the comparer. Station names in reports and
     maps are unchanged.
2. **The map covers whole terrain tiles, not just the radius.** SPLAT! draws its map over every tile
   it loaded (here 38–40° N, 76–78° W, grey terrain shading), with the coverage inside the radius.
   That's upstream behaviour; the opacity slider helps.
3. **Station labels:** the map no longer repeats a SPLAT! placemark that sits on one of your stations
   (K4HTA was labelled twice).
4. **Test-script slips, each caught by a failing check, not app bugs:**
   - PowerShell's built-in `history` alias hid my `History` function.
   - A `\t` became a tab character in a pattern.
   - The "Earlier maps" list only created rows on screen, so screen readers missed the others. I
     turned that off in the app.
5. **The standard source's blocks are 6° × 4° zips** (J18.zip is 7 MB, K18.zip 27 MB), so the first
   map in a new area can take a minute to download.

## Declared coverage gaps
- **Real downloads were exercised once** (standard, K18.zip). HD downloads were tested only through
  the stand-in, which serves files previously downloaded from the real HD source.
- **Town names (`-s cities.dat`)** work in the job code (used for M01) but aren't in the window yet;
  there's no cities file for your area. I suggest G5.
- **Antenna patterns and `-udt`** are still not in the app (G5 candidate, as noted at G3).
- **The look** (colour key size, panel layout) is your judgement.

## Your check (the G4 gate)
In the open app:
1. Click **Coverage map…**. Choose **K4HTA**. Check the frequency and ERP (146.685 MHz and 93 W, if
   you entered them in G3).
2. **What to show:** Signal strength. **Radius:** 30 miles. **Receiver antenna:** 6 ft, or the height
   you normally use. Click **Run**.
3. The map appears over the street map, with SPLAT!'s colour key in the panel. Try the opacity slider
   and **Open in Google Earth Pro**.
4. Optionally, try **Line of sight**, then click an entry under **Earlier maps**.

Your terrain folder already has the four tiles this map needs, so nothing is downloaded. A map in a
new area would download its terrain first.

## Files added or changed
- `gui\SplatWin.Core`:
  - `TerrainFetcher.cs` (get_terrain in C#) and `CoverageJob.cs` (job, flags, `job.json` history);
  - `Terrain.cs` (coverage tiles, radius limit, open-water markers);
  - `Kmz.cs` (colour key);
  - `PathJob.cs` (lower-case site files, fetch before analysis).
- `gui\SplatWin.App`:
  - `MainWindow.Coverage.cs` and the coverage panel in `MainWindow.xaml`;
  - `MapLoader.cs` (colour key);
  - terrain fetching for paths;
  - `--terrain-std-url`, `--terrain-hd-url` and `--coverage-job`;
  - map labels;
  - the engine's converters;
  - version 0.5.0.
- `gui\SplatWin.Tests\CoverageTests.cs`.
- `scripts\fake_terrain.ps1`, `scripts\gui_coverage_equiv.ps1`, `scripts\gui_coverage_uitest.ps1`,
  and `scripts\gui_path_uitest.ps1` (offline terrain).
- `tests\results\stageG4-gui-coverage-equiv.txt`, the runbook, the plan, and this report.

## Next (needs your go-ahead): Stage G5, Google Earth and polish
- Google Earth Pro and web KMZ.
- The point-to-point KML test (the path KML was never compared with Linux).
- Settings, help, and an icon.
- Candidates from G3/G4: town names, antenna patterns, terrain shading off.

## Halted for owner review.
