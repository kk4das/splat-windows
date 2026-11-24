# Stage G5 report: Google Earth and polish (2026-11-23)

## What this stage was meant to accomplish
From the plan:
- Google Earth Pro and web KMZ;
- the point-to-point KML test (the path KML had never been compared with Linux);
- settings and an icon;
- the click-for-signal readout planned for the map view.

You added three items at the start: **your stations in Google Earth, terrain shading off, and
antenna patterns with user terrain (-udt)**. You left out town names after the explanation. Help
moved to the new stage G6. The plan's gate is **your review**. Started on your words "Start G5".

## Outcome: built and checked; the review is yours
The app is version 0.6.0 in `gui\publish`, with its own icon.

- **Click the map to read it.**
  - On a coverage map, a click shows what the map says at that point, for example "58 to 68
    dBµV/m", or "no colour: below 8 dBµV/m".
  - It reads the colour SPLAT! painted there against SPLAT!'s own colour file in the job folder,
    so it's a band, not SPLAT!'s exact number. The popup says so.
  - With smooth colour bands it gives an estimate ("about 63 dBµV/m").
  - A line-of-sight map says "line of sight" or "no line of sight".
- **Google Earth:**
  - **Your stations are in the coverage map's Google Earth file**, as pins in a "Stations" folder.
    Stations outside the map are left out, and so is the transmitter, which SPLAT! already marks.
  - **Path analysis makes a Google Earth file too.** After Analyse, Open in Google Earth Pro shows
    both stations, the path and its line-of-sight part, with SPLAT!'s colours.
  - **Google Earth on the web…** (new button) gives the steps for opening the same file in a web
    browser, with buttons to open earth.google.com, show the file and copy its path. The app never
    uploads anything. If Google Earth Pro isn't installed and no program opens .kmz files, the
    Google Earth Pro button shows these steps instead.
- **Terrain shading off:**
  - Coverage map… → More options → untick **Grey terrain shading**. SPLAT! then leaves the no-signal
    areas white (its `-ngs` option), and the app shows them clear.
  - Only the coverage colours sit over the street map, in the app and in Google Earth.
  - SPLAT!'s own map file in the job folder is unchanged.
- **Antenna patterns:**
  - Edit a station → **4. Antenna pattern (optional)** → Choose .az file…. The app reads the .az
    file and, if there is one beside it, the .el file. It checks them against SPLAT!'s format and
    keeps them with the station.
  - Path analysis and coverage maps then use the pattern whenever that station transmits, and the
    transmitter panel says so.
  - Importing a .qth file now also brings .az/.el files of the same name, as it does a .lrp file.
- **Obstacles (user terrain, .udt):**
  - More options in both panels → Obstacles → Choose…. The obstacles are added for that job (SPLAT!'s
    `-udt`).
  - A file whose obstacles all fall outside the job's area is refused. That almost always means the
    longitudes were written east-negative (-77.2), but SPLAT! reads them as degrees west (77.2), and
    it would otherwise quietly ignore every obstacle.
- **Settings…** (new, top right):
  - the terrain folder (Change…, or back to the app's own folder);
  - what new coverage maps start with: units, radius, receiver height, terrain shading;
  - map opacity;
  - QRZ.com sign-in;
  - where Google Earth Pro is;
  - the job folders.
- **The icon:** a radio mast with coverage rings in SPLAT!'s key colours, red (strongest) nearest
  the mast. `scripts\make_icon.ps1` draws it.
- **The engine is unchanged.**

## How it was checked
- **New Linux reference runs.** Four new test cases (`tests\cases\cases-g5.json`) ran on the Stage 1
  Linux build (WSL `splat-ref`, same binaries):
  - P09: a path with KML;
  - P10: a path with a pattern, obstacles, KML and metric units;
  - M09: a signal map with a pattern, obstacles and no shading;
  - M10: a line-of-sight map with no shading.

  The small outputs are in `tests\golden\linux-g5`; the maps are in `data\golden-linux-g5` (not in git).
- **The port on those cases: EQUIVALENT.** The no-shading maps are byte-identical. The path KMLs
  differ from Linux in the last printed digit of one longitude (finding 1). **Your decision:** the
  approved last-digit tolerance now also covers path KML; coverage KML stays exact.
  - The comparer's self-test checks both:
    - a +1 last-digit change in path KML passes, and +2 fails;
    - without `--last-digit`, path KML is exact;
    - a coverage KML stays exact.

    It reports OK (7/7) on the G5 set and still OK (20/20) on the original set. The Stage 4 run is
    still EQUIVALENT.
- **The app against Linux, through the app itself:**
  - **Path gate (`scripts\gui_path_equiv.ps1`): EQUIVALENT, 10 cases.** These are the G3 six, plus:
    - P06, obstacles;
    - P07, the antenna pattern, kept with the station as the window keeps it;
    - P09, path KML;
    - P10, all of them together.
  - **Coverage gate (`scripts\gui_coverage_equiv.ps1`): EQUIVALENT, 4 maps, from empty terrain
    folders.** These are M01 and M07 from G4, plus M09 (pattern, obstacles, no shading) and M10 (no
    shading). The maps are byte-identical.
    - SPLAT!'s colour file names itself in its first lines (`k4hta_pat.scf` in the test, the app's
      `k4hta_pattern.scf`). It is checked as identical apart from that name.
  - Saved in `tests\results\stageG5-gui-path-equiv.txt`, `stageG5-gui-coverage-equiv.txt` and
    `stageG5-port-equiv.txt`.
- **304 unit tests pass** (31 new). Among them:
  - pattern and obstacle files read as SPLAT! reads them, with bad ones refused;
  - P06, P07, P09 and P10 reports, and the M09 and M10 maps, identical to Linux;
  - the path Google Earth file: degree signs right, line colours kept, no Pro-only icon;
  - station pins, with names like "A & B <club>" made safe;
  - **the readout against every pixel of a real SPLAT! map** (M09: every colour understood);
  - smooth-colour estimates within 2 units of the value SPLAT! painted, for every level.
- **The real window, on `gui\publish`:**
  - **coverage 48/48** (14 new);
  - **path 36/36** (3 new);
  - stations 31/31 and QRZ 24/24, unchanged.

  New checks include:
  - **the readout at a clicked point matches what SPLAT!'s map and colour file say there**, worked
    out separately by the test script;
  - the no-shading map shown with clear areas while SPLAT!'s file keeps its white;
  - station pins in the Google Earth file;
  - the Settings window, including a bad value refused;
  - the web steps naming the right file;
  - the path's Google Earth file.

  The window tests now use their own settings file (`--settings`), and **your real settings file
  was checked unchanged**.
- **Shown to fire: 12 deliberate breakages, all caught.** The code was restored after each and
  checked by hash.
  - **Ten, caught by the unit tests:**
    - no-shading flag dropped;
    - obstacles not passed to SPLAT!;
    - pattern not written into the job;
    - path line colours dropped;
    - SPLAT!'s Latin-1 KML read as UTF-8;
    - pin names not made safe;
    - readout band off by one;
    - smooth readout using the wrong colour channel;
    - obstacle longitudes left in SPLAT!'s signs;
    - pattern amplitude not checked.
  - **Two, on published builds:**
    - a window that draws white instead of clear fails the window test (0 clear pixels);
    - a build without `-ngs` fails the coverage gate (M09 and M10 maps differ).
- **Toolchain preflight OK at both ends** (from PowerShell). The SPLAT! engine is unchanged.

## Findings worth noting
1. **The port's path KML differs from Linux in one digit.**
   - The path line's first point is -77.205794 instead of -77.205793 (about 0.1 m).
   - SPLAT! computes it as lon − arccos(a number that should be exactly 1), and the Windows and
     Linux maths libraries round that number differently in its last bit.
   - It's the same cause as the last-digit tolerance you approved at Stage 3; you extended that
     tolerance to path KML.
2. **SPLAT!'s path KML declares UTF-8 but writes the degree sign as a Latin-1 byte**, so Google
   Earth would show "38� 55' 27"". The app's Google Earth file fixes it; SPLAT!'s own .kml is left
   as written.
3. **Obstacle files are in degrees west.** A file written with ordinary east-negative longitudes
   would be silently ignored by SPLAT!. The app refuses it and explains.
4. **The .NET prompts you saw were my mistake.** I built the first G5 test copy without its own
   .NET, so each start offered to install the .NET Desktop Runtime. Nothing was installed. It was
   rebuilt self-contained like every other build, and the runbook now says so.
5. **Caught by the new tests before you saw them:**
   - the first smooth-colour readout took its estimate from the least precise colour channel;
   - three text items in the new windows had fixed screen-reader names that hid their text. One
     window check had passed for the wrong reason because of it; it now reads the real text.
6. Two file paths in the runbook (from G4) had been garbled by escape codes; fixed.

## Declared coverage gaps
- **Google Earth on the web wasn't tried in a browser**: it needs a Google sign-in. The menu names
  in the steps (Projects → New project → Import KML file from computer) are from Google's current
  web version and may change.
- **The file-choosing steps aren't driven by the window tests**: Choose .az file…, obstacles
  Choose…, and import with a .az beside the .qth all go through the Windows file picker. Everything
  after the picker is covered: the unit tests, and the gates through the same job code.
- **The readout is approximate by design**: it reads colours, not SPLAT!'s numbers. A point on a
  site label or a map edge says so.
- **Settings' map defaults apply when the coverage panel first opens in a session**; after that the
  panel keeps what you typed.
- **The path's Google Earth file has no obstruction marks**: SPLAT!'s path KML doesn't include them.
  The app's map shows them.
- **The icon and the new windows' look** are your judgement.

## Your check (the G5 review)
The new version (0.6.0) is open.
1. **Coverage map…** → K4HTA → signal strength, 30 mi → Run. Click a few places on the map: the
   popup gives the band there. Compare it with the colour key.
2. More options → untick **Grey terrain shading** → Run. Only the coverage colours should sit over
   the street map.
3. **Open in Google Earth Pro**: your stations are pins in a "Stations" folder alongside K4HTA.
4. **Analyse a path…** K4HTA → KK4DAS → Analyse → **Open in Google Earth Pro**: the stations, the
   path and its line-of-sight part, with readable degree signs in the descriptions.
5. **Settings…** (top right): check that the terrain folder is yours, and try the defaults.
6. Optional:
   - **Google Earth on the web…**;
   - an antenna pattern, if you have .az files for your antennas (Edit a station → 4.).

## Files added or changed
- `gui\SplatWin.Core`:
  - new `AntennaPattern.cs`, `UserTerrain.cs` and `ColourKey.cs` (the readout);
  - `Kmz.cs` (path KMZ, station pins, Latin-1 KML, keeps path styles);
  - `PathJob.cs` (-kml, -udt, pattern);
  - `CoverageJob.cs` (-ngs, -udt, pattern);
  - `Stations.cs` (Pattern);
  - `AppSettings.cs` (defaults).
- `gui\SplatWin.App`:
  - new `SettingsWindow`, `EarthWebWindow` and `splat.ico`;
  - `MapLoader.cs` (clear white, pins, readout);
  - the main window and panels;
  - `web\index.html` (readout popup);
  - `--settings`;
  - version 0.6.0.
- `gui\SplatWin.Tests\G5Tests.cs`; the path flags test now expects `-kml`.
- `tests\cases\cases-g5.json`, `tests\golden\linux-g5\`, `scripts\linux-ref\run_g5.sh`.
- `scripts\compare_outputs.py` (path KML rule, G5 self-tests), both gate scripts, all four window
  tests (`--settings`, G5 checks), and `scripts\make_icon.ps1`.
- `tests\results\stageG5-*.txt`, the runbook, the plan, and this report.

## Next (needs your go-ahead): Stage G6, help and getting started
- the Help menu, with indexed, searchable help (including reading the colour key);
- step-by-step getting started;
- decision G9 (first-run wizard).

Then G7, packaging.

## Halted for owner review.
