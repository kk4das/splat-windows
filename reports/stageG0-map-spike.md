# Stage G0 report: GUI map spike (2026-11-22)

## What this stage was meant to accomplish
Prove the riskiest part of the GUI first: show a SPLAT! coverage map **inside a Windows app**,
over a street or terrain map, **in exactly the right place**, with a button that opens it in
Google Earth Pro. This is not yet the full app: no stations, no running SPLAT!, no QRZ.

## Outcome: works; the alignment check is yours
- **The app opens:**
  `gui\publish\SplatForWindows.exe "D:\ClaudeAI\projects\SPLATWIN\work\g0-demo\k4hta-30mi.kml"`.
  It's already open on your screen with a correct 30-mile K4HTA map.
- **What it has:**
  - an **Open coverage map…** button (any SPLAT! coverage `.kml`)
  - **Streets** and **Terrain** background maps (layers button, top right)
  - a **map opacity** slider
  - a scale bar, and a lat/lon readout under the mouse
  - **Open in Google Earth Pro**
  - It follows Windows light/dark mode with the Windows 11 look.
- **Getting the map in the right place needed real work.**
  - SPLAT!'s image is a plain latitude/longitude grid. Web maps stretch rows further from the
    equator (the Mercator projection), so laying the image down unchanged puts the middle of a
    2-degree map **up to half a mile** out here.
  - The app re-samples the image's rows to match. Columns need no change, and every pixel stays an
    exact SPLAT! colour.
  - Google Earth uses the latitude/longitude grid itself, so the `.kmz` keeps the original image.
- **Checked end to end in the rendered page.** At 1,600 points spread over the map, the colour
  shown on screen at each place was compared with SPLAT!'s colour for that place.
  - **1,600 of 1,600 match within one map pixel** (about 90 m, the map's own resolution).
  - The same test without the re-sampling: **942 of 1,600**, so the test does catch
    misalignment.
- **18 unit tests pass:**
  - KML reading, using the real Linux golden KML
  - PPM reading, including truncated files and other formats
  - the re-sampling, at the K4HTA latitude, the map edges, the Southern hemisphere and the
    equator
  - the KMZ writer
  - **With the re-sampling removed on purpose, 4 alignment tests fail**, as they should.

## Findings worth noting
1. **Your first coverage map had no coverage in it.**
   - Your command was `splat -t k4hta -L -R 30 ...`, with no number after `-L`.
   - SPLAT! then silently skips the signal calculation and draws only grey terrain and blue
     water. I reproduced your `coverage.ppm` **byte for byte** with that command.
   - With `-L 6` the map has the coloured signal levels (the demo map).
   - This is upstream behaviour. The README now warns about it, and the GUI will never build a
     command like that.
2. **`KK4DAS.qth` gives 9.14 as the antenna height**, which SPLAT! reads as **9.14 feet**. Your
   path report says "Antenna height: 9.14 feet AGL" and says it must be raised to 36 ft.
   - If you meant 30 ft, change it to `30` (or `9.14 meters`) and re-run.
   - The README now says heights are feet unless marked `meters`.
3. **The .NET SDK went on D:, not C:.** C: has only about 23 GB free. It sits in
   `D:\ClaudeAI\tools\dotnet10` (775 MB), with the package cache at
   `D:\ClaudeAI\tools\nuget-packages` (439 MB).
   - I used Microsoft's signed install script, and checked the signature before running it.
   - No admin rights were needed, and nothing was added to PATH.
   - A .NET 6 runtime that was already installed is untouched.
   - .NET's usage telemetry is turned off in the build script.
4. **The standalone app folder is 143 MB**, because it includes .NET, so other PCs need nothing
   installed. G6 (packaging) can trim this.
5. **Two page bugs were found and fixed while checking in a browser:**
   - The map zoomed out to the whole world when it loaded before the window had a size.
   - The "open a map" hint stayed visible.
6. **The backslash trap struck again** (in a runbook edit; caught by the control-character check).
   The file was restored from git and redone with the Edit tool.

## Declared coverage gaps
- **The check above ran in the built-in browser,** not inside the WPF window. It's the same page
  and the same images, but **inside the app it's your visual check**.
- **The "Open in Google Earth Pro" button wasn't clicked by me.** It would open a window on your
  screen; it's part of your check.
- **Map tiles come from OpenStreetMap and OpenTopoMap over the internet.** The app identifies
  itself as `SPLATforWindows/0.1`, as OSM's tile policy asks. Offline, the coverage image still
  shows on a blank background (not tested).

## Your check (the G0 gate)
1. In the open app, zoom in on a few places you know, such as the Potomac shoreline, the bay
   bridge, or your house. Do the edges of SPLAT!'s blue water and its terrain line up with the
   street map? The opacity slider helps.
2. Click **Open in Google Earth Pro**, and compare the same spots there.
3. Try **Open coverage map…** with one of your own `.kml` files.

## Files added or changed
- `gui\`: `SplatWin.slnx`, `build-gui.ps1`, and three projects:
  - `SplatWin.Core`: KML reading, PPM reading, re-sampling, KMZ writing.
  - `SplatWin.Tests`.
  - `SplatWin.App`: the window, the map page, and Leaflet in `web\vendor\leaflet-1.9.4`, BSD-2
    licence, kept with it.
- `.gitignore` (GUI build output), the README (two warnings), the runbook, and this report.

## Next (needs your go-ahead): Stage G1, stations
A stations library, with places found by grid square, coordinates, a map click, `.qth` import and
callook.info, plus confirm-on-map.

## Halted for owner review.
