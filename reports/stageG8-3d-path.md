# Stage G8 report: 3D path view (2026-11-24)

## What this stage was meant to accomplish
Your request: "is it possible to produce a 3D view from transmitter to receiver or reverse to make it
easier to see what might be blocking the signal". A club member's path showed "blocked by terrain, but
its difficult to visualize", and Google Earth Pro was "still not very satisfying". Planned in
reference/gui-plan.md; started on your words "Yes, start G8", accepting decisions G10–G12 as recommended:
- **G10:** the 3D view gets its own window;
- **G11:** a topographic map on the ground by default;
- **G12:** a strip about 1 mile either side of the path.

## Outcome: built and checked; the review is yours
The app in `gui\publish` has it. Analyze a path, then click **3D view…** in the summary, beside
"Open the job folder".

- **What you see.** A strip of terrain along the path with the topo map on it, and both antennas as
  masts. The line of sight runs between the antenna tops (light blue). The 60 % Fresnel zone is drawn
  see-through around it, with its lower edge as a dark blue line. The ground right under the path is
  yellow, turning **red where it rises above the line of sight**. SPLAT!'s obstruction points are red
  dots, and one sentence states the verdict, for example: *"Blocked: SPLAT! finds terrain in the way
  0.42 mi to 0.64 mi from <receiver>. Along the path the ground rises up to 42 ft above the line of sight."*
- **Viewpoints:** From <receiver> (1), From <transmitter> (2), Side (3), Overview (4). Drag to turn,
  scroll to zoom. **Click the ground** to read its distance and how far it is above or below the line
  of sight.
- **The antenna view answers the question directly.** The far antenna top is a yellow dot when it can
  be seen. When a hill hides it, it shows only as a **white ring** through the hill. On the club
  member's path, from his antenna, K4HTA's mast is a white ring just behind the rise half a mile away,
  with SPLAT!'s red points on the crest.
- **Height exaggeration** from 1× to 20× (keys + and −), starting at 3×.
- **Ground:** topographic map, street map, or colored by height (works offline).
- **Strip:** ¼ to 5 miles each side. Extra terrain for the sides is downloaded as for a path, and if it
  can't be, that part is left out and the view says so.
- **Fresnel:** the zone your analysis used, the full first zone, or off.
- **Google Earth path file:** SPLAT!'s own "Line-of-Sight Path" was drawn 5 m above the ground, so it
  never showed the radio path. The file now adds, in a folder "3D path (SPLAT! for Windows)":
  - the **line of sight at its true height** above sea level, with SPLAT!'s allowance for the
    atmosphere's bending;
  - both masts;
  - red pins on SPLAT!'s obstruction points;
  - a **view from each antenna**. Google Earth opens at the receiver's antenna, looking at the
    transmitter.

  SPLAT!'s own `.kml` in the job folder is unchanged, and its view is kept as "Overview (SPLAT!'s view)".
- **Help:** a new page, "The 3D path view", with links from Getting started, Path analysis, Google Earth
  and the index. Troubleshooting has a new section covering all 17 new messages. About credits
  three.js. The README mentions the view.

## How it stays faithful to SPLAT!
- **Along the path, the view uses SPLAT!'s own arithmetic.** New core code copies SPLAT!'s functions
  operation for operation:
  - `SplatGeo`: ReadPath, Distance, Azimuth, ElevationAngle, ReadBearing and LoadQTH;
  - `SdfTerrain`: LoadSDF, GetElevation, and LoadUDT/AddElevation for obstacle files.

  `PathScene` then computes what GraphHeight writes (height above the line of sight with Earth's
  curvature, the Fresnel zones) and what ObstructionAnalysis lists. That includes SPLAT!'s way of
  raising the receiver after each point it finds, which is why its list isn't simply "everything above
  the line".
- **It's checked against the files SPLAT! wrote** in each job folder (`--path3d-check`, and the unit
  tests):
  - the terrain profile;
  - the five height-graph data files;
  - the obstruction list, character for character;
  - the antenna heights above sea level in the report.
- **Either side of the path,** the same terrain lookup and curvature correction are applied (plus the
  small sideways drop of Earth's curvature).
- **The page only draws.** It checks itself on every drawing: at every exaggeration from 1× to 20×, each
  point along the path stays on the same side of the line of sight as at 1×.

## How it was checked
| Check | Result |
|---|---|
| Unit tests (8 new G8 tests: standard, metric, Fresnel 80 % + clutter 30 ft, obstacle file, scene, summary, Google Earth file, SPLAT! readers; plus the P09 test updated) | **326 of 326 pass** |
| "Shown to fire": a terrain point changed by 1 m, and a height by 0.01 ft, in SPLAT!'s files | **both caught** at the right line; untouched files still pass |
| 3D numbers vs SPLAT!'s files, all ten path test cases (P01–P10: Longley-Rice, Fresnel 80 % + clutter, metric, HD, obstacles, antenna pattern, KML) | **90 of 90 checks pass**: obstruction lists identical; graph data identical except one point in the HD case (see below) |
| Your real paths: K4HTA to the club member's station (3 runs) and to another station | all pass; obstruction lists (6 and 4 points) identical to the reports |
| 3D window test (new, 25 checks): opens, draws, self-check, all four viewpoints, 20×, map on the ground, pictures, missing terrain refused politely | **25 of 25** |
| Window tests: help 30, stations 31, QRZ 24, path 41, coverage 52 | **all pass** (after the fix below) |
| GUI path and coverage equivalence gates (Linux reference) | **EQUIVALENT**, both |

Results: `tests\results\stageG8-*.txt`.

**The graph-data rule (new for this check).** Values must agree with SPLAT!'s file to **0.001 ft**
(or m), which is a thousand times finer than anything the view can show. A 0.01 ft error is caught.
The counts are reported:
- Nearly all values are identical.
- In the HD case, one point 0.03 mi from the receiver differs by 0.000003 ft. There SPLAT! measures
  distance with an arc-cosine of a number extremely close to 1, which magnifies the last bits of the
  math library's results.

## Findings worth noting
1. **A map problem from Windows, not from G8, found and fixed.** After the 8:41 restart, coverage maps
   stopped appearing in the app's map ("map image failed to load"). This morning's 0.9.0 build had the
   same problem when retested.
   - **Cause:** the WebView2 runtime that took over at the restart no longer lets the map page load
     images from the app's second internal web address.
   - **Fix:** the app now hands the map images to the page itself (only from its map-cache folder,
     nothing outside it).
   - I also renamed the internal addresses from `.local` to `.example`, as Microsoft recommends for
     WebView2. That alone didn't fix it.
   - **This affects the 0.9.0 copy club members have.** If their PCs have the same WebView2 update,
     coverage maps won't show in the app, though Google Earth still works. **I recommend a 0.9.1 with
     this fix soon** (your call; nothing has been published).
2. **What you'll see on the club member's path:** the blockage is local, a rise 0.4–0.6 mi from his
   station. His station position is his FCC mailing address, with the pin not moved onto his antenna.
   Moving it, and checking trees in Google Earth along the true line of sight, is the next step for him.
3. **Exaggeration goes to 20×, not 10× as planned.** On a 10-mile path, a 40 ft rise is still hard to
   see from the side at 10×. Everything scales together, so nothing is distorted in a misleading way.
4. **The page's controls aren't visible to Windows accessibility** (a WebView2 limit), so the window
   test drives the view with its keyboard shortcuts, as a keyboard user would. Screen readers get a
   text description of what's drawn.

## Declared coverage gaps
- **The Ground and Strip menus aren't clicked by the test.** Topo loading is checked, the height-color
  fallback is coded but only seen on screen when offline, and strip widths are covered by the core
  tests. Please try them.
- **Not tested on a clean PC** (Windows Sandbox) for this stage. The three.js files are inside the app
  folder, so packaging doesn't change.
- **Google Earth additions:** the file's content is checked by tests, but only you can judge how it
  looks in Google Earth Pro and on the web.

## Your check (the G8 review)
1. Analyze K4HTA to the club member's station, click **3D view…**, and try each viewpoint and the + / − keys.
2. Try Ground → Street map and Colored by height, and Strip → 2 mi.
3. Click **Open in Google Earth Pro** on the path, then double-click "View from <his call>'s antenna" and
   look at the yellow line of sight.

## Files added or changed
- New: `gui/SplatWin.Core/SplatGeo.cs`, `SdfTerrain.cs`, `PathScene.cs`;
  `gui/SplatWin.App/View3DWindow.xaml(.cs)`; `web/path3d.html`, `web/path3d.js`;
  `web/vendor/three-0.180.0/` (three.js 0.180.0, MIT, npm integrity checked);
  `help/path-3d.html`; `gui/SplatWin.Tests/G8Tests.cs`; `scripts/gui_view3d_uitest.ps1`.
- Changed:
  - `PathJob.cs`, `Kmz.cs`: the Google Earth additions;
  - `MainWindow.xaml(.cs)`, `MainWindow.Path.cs`: the 3D view button, the map-image fix, and the
    `.example` addresses (also `HelpWindow.xaml.cs`);
  - `App.xaml.cs`: the `--path3d-check` and `--view3d` test hooks;
  - help pages; `G5Tests.cs` (the P09 file test); README.

## Halted for owner review.
