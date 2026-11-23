# Stage G3 report: path analysis in the app (2026-11-22)

## What this stage was meant to accomplish
Point-to-point path analysis without the command line: pick a transmitter and a receiver, click
**Analyse**, and see SPLAT!'s results, its graphs (drawn by the app, with no gnuplot) and the path
on the map. The plan's gate: **the same numbers as the CLI reports, and the equivalence test
passes.** Started on the owner's words "Start G3".

## Outcome: built, and the equivalence gate passes; your check is below
The app is open on your screen (`gui\publish\SplatForWindows.exe`, version 0.4.0).

- **Analyse a path…** (next to Import .qth) opens the path panel:
  - **Stations:** choose the transmitter and the receiver from your library. **Swap** exchanges them.
  - **Transmitter radio:** frequency, ERP and polarization. These are the `.lrp` values.
    - They are saved with the transmitter station, so you enter them once.
    - **Load from a .lrp file…** fills them from an existing file.
    - Importing a `.qth` now also brings in a `.lrp` with the same name, if one is beside it.
  - **Ground, climate and model** (folded away): ground type (SPLAT!'s documented table), radio
    climate, situations and time percentages, and atmospheric bending.
    - The model is **ITWOM 3.0** or **Longley-Rice** (`-olditm`).
    - Also Fresnel clearance % (`-fz`), ground clutter (`-gc`), metric units (`-metric`), and
      high-definition terrain (`splat-hd`).
  - **Terrain:** the folder with your `.sdf` files, chosen once with **Change…** and remembered.
- **The result:**
  - **A summary:** distance, azimuth, line of sight clear or blocked, signal level, field strength,
    path loss and model, free-space and terrain loss, mode of propagation, and SPLAT!'s "raise the
    antenna to…" sentences.
    - **Every value is SPLAT!'s own text, copied from its report, never recomputed.** That's how
      the panel shows "the same numbers as the CLI" by construction.
    - A model warning (for example Longley-Rice error 3) is shown in red.
  - **Four graphs, drawn by the app from SPLAT!'s graph data:** terrain profile, elevation angle,
    height with Fresnel zone, and path loss. They use SPLAT!'s titles, labels, ranges and lines,
    including the right-hand axis for earth curvature. Hovering shows each line's value.
  - **Report:** SPLAT!'s full report, exactly as written.
  - **The map:** a line between the stations, **green** if SPLAT! finds line of sight and **red
    dashed** if terrain blocks it, with SPLAT!'s obstructions as red rings.
- **Every analysis is a job folder:** `Documents\SPLAT\Jobs\<date> <tx> to <rx>\`.
  - It holds the `.qth` and `.lrp` files, SPLAT!'s outputs, `splat-log.txt` and **`run-again.cmd`**.
  - **`run-again.cmd` repeats the exact SPLAT! commands from the command line.** The window test
    runs it and gets the identical report.
  - **Open the job folder** is a button under the summary.
- **The engine is unchanged.** The app ships `splat.exe` and `splat-hd.exe` byte-identical to
  `dist\` (with GPL `COPYING`) in its `engine` folder, and runs them as programs.
  - Each graph is a separate SPLAT! run in its own subfolder, because SPLAT! reuses the same data
    file names for every graph.
  - Only Windows itself is on the engine's PATH, so gnuplot never runs and results never depend on
    what else is installed.
- **Missing terrain is refused before running.**
  - SPLAT! quietly treats a missing tile as sea level and gives wrong answers.
  - The app works out the tiles exactly as SPLAT! does (its box of whole degrees by west longitude)
    and names the missing ones.
  - Fetching terrain from the app is G4.

## How it was checked
- **The gate: `scripts\gui_path_equiv.ps1`. Result: EQUIVALENT.**
  - **How it works:** the published app runs path jobs headless (`--path-job`), through the same
    code as the window, for six of the port's test cases, using their stations and `.lrp`. The
    outputs are compared with the **Linux golden run** by the Stage 3/4 comparer
    (`compare_outputs.py`, with the ratified last-digit tolerance for `.gp`).
  - **The cases:** P01 (ITWOM), P02 (Longley-Rice, 50 W), P03 (height/Fresnel graph data),
    P04 (Fresnel 80%, clutter 30 ft), P05 (metric, path-loss data) and P08 (HD, profile data).
  - **The result:** 20 files.
    - 17 are identical, or identical apart from line endings.
    - 3 `.gp` files differ in the last printed digit, the same differences Stage 4 accepted.
  - **Not compared, and why** (the script prints this list):
    - console output (the app passes an absolute terrain path);
    - graph pictures (the app draws from the `.gp` data, which is compared);
    - P04's normalized-height data (the app draws the `-h` height graph, compared in P03).

    For P05 and P08, `splat.gp` was checked identical apart from the output file name.
  - Saved in `tests\results\stageG3-gui-path-equiv.txt`.
- **248 unit tests pass** (41 new):
  - **Reports:** report parsing on the real Linux golden reports (P01–P05, P08). Every value shown
    appears verbatim in its report. Clear paths and missing ERP are handled.
  - **Graphs:** graph reading from the golden `.gp` files. Unknown gnuplot commands are refused, and
    a graph may read only files in its own folder.
  - **`.lrp` files:** read exactly as SPLAT! reads them, including "dBm" and long lines that SPLAT!
    would split.
  - **Terrain tile names:** across Greenwich, the southern hemisphere, and exact whole-degree
    longitudes.
  - **The real engine:** an app job on `dist\splat.exe` and `data\sdf3` reproduces golden P01
    character for character, and the P03 height data within the tolerance.
- **The real window: `scripts\gui_path_uitest.ps1`, 33 of 33 checks pass** (7 clean runs; one earlier run
  stopped with a UI Automation error that didn't recur):
  - **Refusals:** a blank frequency, and missing terrain (tiles named, no job folder made).
  - **Golden reports:** with the matching settings, the Report tab is **identical to Linux golden
    P01, P02, P04 and P05**.
  - **What you see:** the summary's numbers, the clearance sentences, the model warning, all four
    graphs drawn, the map path with 3 obstructions and then 6, and the radio settings saved with
    K4HTA.
  - **The job folder:** `run-again.cmd`, run from the command line, gives the same report.
- **Shown to fire:**
  - **Nine deliberate breakages, all caught by the unit tests:**
    - frequency rounded in the `.lrp`;
    - wrong summary field;
    - tiles from east longitude;
    - `-olditm` dropped;
    - `-metric` dropped;
    - gnuplot back on PATH;
    - the right axis lost;
    - the terrain check skipped;
    - obstruction longitude sign.
  - **Broken builds fail the end-to-end checks too:** two broken builds fail the gate script
    (P02 DIFF; 10 DIFFs), and one fails the window test (2 checks).
  - The code was restored and checked identical (sha1).
- **Earlier stages, on the new build:** stations 31/31, QRZ 24/24. **Toolchain preflight OK at both
  ends.** The SPLAT! engine (`src`, `dist`) is unchanged.

## Findings worth noting
1. **The G1 stations test had been using your real QRZ sign-in.** Once you signed in at G2, its
   call sign checks went to QRZ instead of callook.info.
   - When I ran it this stage, it made **three live lookups on your account** (W1AW, ZZ9ZZZ and
     KK4DAS/P) and three checks failed.
   - Lookups only: nothing was changed, and the password stayed in Credential Manager.
   - **Fixed:** every window test now uses a throwaway credential entry, so none can touch your
     real one again.
2. **A screenshot mistake, corrected.** My first screenshot of the test window copied that area of
   the screen, so it captured another window that was on top. I deleted it at once, kept no copy and
   haven't described it anywhere, then switched to a capture that asks only the app's own window to
   draw itself.
3. **`dist\` holds the `gnuplot.cmd` shim.** With the engine folder on PATH, SPLAT! drew PNGs with
   gnuplot. The app now gives SPLAT! only Windows on PATH: graphs come from the data, the same on
   every PC.
4. **Your numbers won't match golden P01 (116.68 dB).** The golden case uses the test files'
   positions: KK4DAS rounded, and K4HTA's old `.qth` site. Your library has the real positions,
   about 80 m different for K4HTA. The gate uses the test positions, so it compares like with like.
5. **The model warning is real SPLAT! output.** Longley-Rice reports "error 3: a combination of
   parameters is out of range" for K4HTA–KK4DAS (golden P02). The app shows it in red rather than
   hiding it.
6. **I ran the preflight from Git Bash again**, which the runbook warns against; it reported
   PROBLEMS (P4, HOME). Run from PowerShell, it is OK.
7. **Line endings:** `sed` in Git Bash had turned a few CRLF files into LF. I restored them before
   committing and re-ran the tests.

## Declared coverage gaps
- **Antenna patterns (`.az`/`.el`, P07) and user terrain (`-udt`, P06) aren't in the app yet.** The
  plan doesn't list them for G3; I suggest G5.
- **One terrain folder at a time.** Your standard and HD tiles are in different folders
  (`data\sdf3`, and the Stage 4 suite's `sdf1`), so HD needs **Change…**. G4's terrain cache will
  hold both.
- **The graphs' look** is your judgement. The data is checked; the drawing is checked only for
  being there, with the right lines.

## Your check (the G3 gate)
In the open app:
1. Click **Analyse a path…**. Under **Terrain**, click **Change…** and choose
   `D:\ClaudeAI\projects\SPLATWIN\data\sdf3` (it covers 38–40° N, 76–78° W).
2. Transmitter **K4HTA**, receiver **KK4DAS**. Enter **146.685** MHz and **93** W, or use **Load from
   a .lrp file…**. Click **Analyse**.
3. Look at the summary, the line on the map, the four graph tabs and the **Report** tab. The
   summary's numbers are the report's.
4. **"Same numbers as the CLI":** click **Open the job folder** and double-click `run-again.cmd`. It
   reruns the same SPLAT! commands from the command line (with gnuplot pictures if your PATH has
   `dist\`). Compare its `K4HTA-to-KK4DAS.txt` with the Report tab.
5. Try a path to another station (Post-180, KD4MM). If a station is outside the terrain, the app
   says which tiles are missing.

## Files added or changed
- `gui\SplatWin.Core`:
  - `PathJob.cs` (job folder, engine runs, `run-again.cmd`) and `PathReport.cs` (summary);
  - `GraphData.cs` (gnuplot data);
  - `Radio.cs` (`.lrp` settings) and `Terrain.cs` (tiles);
  - `AppSettings.cs`;
  - `Station.Radio`.
- `gui\SplatWin.App`:
  - `MainWindow.Path.cs`, `GraphView.cs` and the path panel and result tabs in `MainWindow.xaml`;
  - map path drawing in `web\index.html`;
  - `--engine`, `--terrain`, `--jobs` and `--path-job` options;
  - the engine in `engine\`;
  - version 0.4.0.
- `gui\SplatWin.Tests\PathTests.cs`, which reads the golden reports and graph data.
- `scripts\gui_path_equiv.ps1` (the gate) and `scripts\gui_path_uitest.ps1`.
- `scripts\gui_stations_uitest.ps1`: the throwaway QRZ entry.
- `tests\results\stageG3-gui-path-equiv.txt`, the runbook, the plan, and this report.

## Next (needs your go-ahead): Stage G4, coverage
Coverage jobs from the app: pick a transmitter, a radius and a receiver height, then click **Run**.
- Terrain is fetched automatically into one cache.
- The map is shown over the street or terrain map, with an opacity control, cancel and job history.

The gate is that you run the 30-mile K4HTA map from the app alone.

## Halted for owner review.
