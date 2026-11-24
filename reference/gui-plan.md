# SPLAT! for Windows: GUI front end, plan for owner review (2026-11-22)

**Status: in progress.** All decisions G1–G8 made 2026-11-22. G0 approved; G1 (stations,
plus street addresses) passed the owner's check; G2 (QRZ) passed the owner's check 2026-11-22; G3 (path) passed the owner's check 2026-11-22; G4 (coverage) passed the owner's check 2026-11-23; G5 (Google Earth and polish) passed the owner's review 2026-11-23; G6 (help and getting started) passed the owner's review 2026-11-24 (with American spelling throughout). Progress is in the runbook and `reports\`.

## The owner's request
> "plan a GUI front end to eliminate the need for command line and make it a modern windows
> program. It needs multiple options for locating stations - a call sign lookup on QRZ.com is the
> most common. hams are familiar with maidenhead square designation so we should provide that
> option as well. We should view the coverage map in the application itself and provide a link out
> to google earth pro if possible."

## What it should feel like
A normal Windows program: start it from the Start menu, with no PowerShell, PATH, `.qth` files or
`-d` options.
1. **Stations:** add K4HTA by typing a call sign, a grid square, or coordinates, or by clicking the
   map. Confirm the spot on the map, then enter the antenna height.
2. **Path:** pick a transmitter and a receiver, then click **Analyse**. The result shows the path
   loss, whether there is line of sight, the obstructions, the Fresnel clearance, and the terrain
   profile graph, all in the window.
3. **Coverage:** pick a transmitter, a radius and a receiver height, then click **Run**. The map
   appears in the window over a street or terrain map, with the colour key, and the receivers are
   shown on it.
4. **Open in Google Earth Pro** with one button. A web Google Earth button opens the `.kmz`.
5. **Terrain is fetched automatically** for whatever area a job needs, the first time it's needed.

## Architecture (recommended)
- **The GUI drives the existing, verified `splat.exe` / `splat-hd.exe` as programs.** It writes
  the `.qth`/`.lrp` files into a job folder, runs SPLAT!, and reads what it produces.
  - **The RF engine is not changed at all**, so the Stage 3 equivalence with Linux still holds.
  - Every GUI job can be re-run from the command line from its job folder: the exact command is
    saved alongside.
- **Job folders:** `Documents\SPLAT\Jobs\<date> <name>\`, holding inputs, outputs and the command
  line. **Stations library:** one file, editable in the app, with import and export of `.qth`
  files.
- **The `get_terrain.ps1` and `ppm2png.ps1` logic moves into the app** (C#), with the same sources
  (D7), the same cache, and the same checks.
- **Graphs:** SPLAT! already writes the graph data (`.gp` files, proven equal to Linux under
  D12). The app can draw the profile, elevation and path-loss graphs itself from that data. Then
  **gnuplot (about 1 GB) is not needed at all** on any PC that runs the GUI. See G4.
- **Progress and cancel:** SPLAT!'s console output is shown live in a collapsible log pane, and a
  long coverage run can be cancelled.

## Locating stations
Every method ends the same way: **a pin on the map that the user confirms or drags**, then the
antenna height (ft or m). The reason is in caveat 1 below.

| Method | Source | Notes |
|---|---|---|
| **Call sign, QRZ.com** | QRZ XML Logbook Data API, `https://xmldata.qrz.com/xml/current/` | The owner's own QRZ login. **Full data, including lat/lon/grid, needs a paid QRZ XML Logbook Data subscription.** Without one, QRZ returns limited fields and says that access is for testing (G3). Worldwide. |
| **Call sign, free (US only)** | callook.info, `https://callook.info/<call>/json` | No account, no key, free. FCC data. The coordinates are a geocode of the licence **mailing address** (Bing), and the grid is computed from them. A fallback when there's no QRZ subscription. |
| **Street address** (added at the owner's G1 check) | US Census geocoder and OpenStreetMap Nominatim, both free with no key | For a site whose licence address is a PO box (K4HTA). US Census estimates the spot along the street; OSM often has the building. PO boxes are refused. |
| **Maidenhead grid** | Computed in the app (no network) | 4, 6, 8 or 10 characters. The pin goes at the centre of the square, **and the square is drawn on the map** so its size is obvious (see the precision table). |
| **Coordinates** | Typed or pasted | Decimal degrees or deg-min-sec, and **ordinary signs (west negative)**. The app converts to SPLAT!'s west-positive. Accepts a pasted Google Maps "38.92, -77.21". |
| **Click the map** | In-app map | Also used to refine any of the above. |
| **Import `.qth`** | Existing files | e.g. your `D:\splat-work` sites. |

**QRZ details from the spec (version 1.34):**
- **Login:** `username`, `password` and `agent` (the client's name, e.g. `SPLATWin1.0`). This
  returns a session key, which the client caches and reuses, logging in again only when a reply
  has no key.
- **Lookup:** `s=<key>;callsign=<call>` returns `lat`, `lon`, `grid` and `geoloc`.
- **`geoloc` says where the position came from:** `user` (entered by the ham), `geocode` (US
  address), `grid`, `zip`, `state`, `dxcc` (country centre) or `none`. **The app shows this and
  warns loudly for anything but `user`/`geocode`**, because a zip-code or state-centre position is
  useless for a VHF path. Errors like "Not found" and "Session Timeout" are shown to the user.
  "Connection refused" means no login for 24 hours.

**Credentials:**
- The QRZ password is typed by the owner in the app's own sign-in box, never by Code.
- It is stored in **Windows Credential Manager**: encrypted per Windows user, never in a file, a
  log, git or a report. A "sign out" button deletes it.
- Tests use recorded sample replies, not live logins.

**Maidenhead precision** (at this latitude, about 39° N):

| Characters | Example | Square size |
|---|---|---|
| 4 | `FM18` | about 69 × 108 mi |
| 6 | `FM18jw` | about 2.9 × 4.5 mi |
| 8 | `FM18jw40` | about 0.29 × 0.45 mi (460 × 720 m) |
| 10 | `FM18jw40xx` | about 20 × 30 m |

(`FM18jw` is the square that the K4HTA site falls in.)

So a 6-character grid is fine for "roughly where", but **too coarse for a VHF path**: hilltops and
valleys change inside 3 miles. The app will say so and ask for the pin to be dragged to the real
site.

**Caveat 1, which matters a lot: a call sign lookup gives the licensee's *address*, not the
antenna.**
- For a repeater like K4HTA, the trustee's mailing address can be miles from the tower.
- Even for a home station, an address geocode can land on the street, not the mast.
- Hence the confirm-on-map step for every method.

## Viewing maps in the app
- **The map control is Microsoft Edge WebView2** (built into Windows 11; version 157 is on this
  PC), running the **Leaflet** map library.
  - SPLAT!'s coverage image is laid over the map, using the corner coordinates SPLAT! already
    writes in its `.kml`.
  - The view has an opacity slider, the stations and paths, and a click-for-signal readout. The
    readout reads the pixel's colour against the colour key, so it's approximate; this is labelled.
- **Background map:** OpenStreetMap streets, plus a terrain or satellite option. The tile-source
  terms differ (G2):
  - OpenStreetMap's public tiles allow light use with attribution and an identifying app name.
  - Satellite imagery usually needs an account or key.
- **Offline:** the coverage image and stations still show without internet, just without the
  background.

## Google Earth Pro link
- **Google Earth Pro is installed here, at
  `C:\Program Files\Google\Google Earth Pro\client\googleearth.exe`.**
  - The app writes a `.kmz` (as `ppm2png` does now) and opens it with that program.
  - If Pro is missing, the app opens the file with whatever Windows associates with `.kmz`, or
    offers the web steps.
- **Path jobs get a cleaned-up KML too.** SPLAT!'s point-to-point KML uses the same Pro-only icon,
  so the app writes a web-safe `.kmz` with both stations and the path line. This also closes the
  gap noted earlier: the point-to-point KML was never compared with Linux, and that comparison
  will be added as a test case.

## Technology (recommended, G1)
- **C# on .NET 10 (the current long-term-support release) with WPF and its built-in Windows 11
  "Fluent" theme.** It looks like a modern Windows 11 app, is mature, is easy to test, and hosts
  WebView2 well.
  - The alternative, WinUI 3, is Microsoft's newest UI framework. Its tooling and packaging are
    rougher, and it gains little here.
- **One self-contained `.exe` plus the SPLAT! programs**, so nothing has to be installed on
  another PC except the WebView2 runtime, which Windows 11 already has.
- **Build tools:**
  - **The .NET 10 SDK is not installed on this PC**, and needs the owner's OK (a new global
    install, about 1 GB).
  - VS Code (already installed) plus the C# Dev Kit extension is enough. Visual Studio isn't
    required.

## How it will be checked (same guardrails as the port)
- **Engine equivalence:** the GUI writes the `.qth`/`.lrp` files for the existing test cases, runs
  them, and `compare_outputs.py` must still say EQUIVALENT. That proves the GUI doesn't change the
  answers.
- **Unit tests:**
  - Maidenhead conversion, both ways, including the edges of squares and south/east hemispheres.
  - Coordinate parsing.
  - The QRZ and callook reply parsers, from recorded samples, including errors, expiry and every
    `geoloc` value.
  - KML/KMZ writing.
  - Overlay placement, checked against a known pixel.
- **Map alignment:** the overlay is placed in the app and in Google Earth Pro, and a known
  landmark must line up in both. The owner eyeballs it.
- **Each check is shown to fire** (a wrong grid, a bad QRZ reply, a truncated tile).

## Stages (each ends with a report and a HALT for owner review)
| Stage | What | Gate |
|---|---|---|
| **G0 Spike** | Install the .NET SDK (with approval). A bare window showing the map with your existing `coverage.png` overlaid, positioned from its KML. | The owner confirms it lines up with Google Earth Pro. |
| **G1 Stations** | Stations library; grid, coordinates, map click and `.qth` import; callook.info lookup; confirm-on-map. | The owner adds K4HTA, KK4DAS and Post 180 without typing coordinates. |
| **G2 QRZ** | QRZ sign-in (the owner types the password), lookup, `geoloc` warnings, Credential Manager. | The owner looks up three calls. |
| **G3 Path** | Path analysis: run SPLAT!, a report summary panel, graphs drawn in the app, path on the map. | Same numbers as the CLI reports, and the equivalence test passes. |
| **G4 Coverage** | Coverage jobs, automatic terrain, overlay, opacity, cancel, job history. | The owner runs the 30-mile K4HTA map from the GUI alone. |
| **G5 Google Earth + polish** | Pro and web KMZ, the point-to-point KML test, settings, icons, the click readout. Owner added (2026-11-23): stations in Google Earth, terrain shading off, antenna patterns and -udt. (Help moved to G6.) | The owner's review. |
| **G6 Help + getting started** | A Help menu with indexed, searchable help (offline); step-by-step getting started; a first-run wizard if decision G9 says so. See below. | A new user gets from a fresh start to a first coverage map using only the help (and wizard); the owner's review. |
| **G7 Packaging** | Merges with Stage 5: a zip or installer, a clean-machine test, D10. Was G6 until 2026-11-23. | A clean-PC report. |
| **G8 3D path view** | Added 2026-11-24 at the owner's request: a 3D view of a path's terrain, line of sight and Fresnel zone, seen from either antenna, and better Google Earth path files. See below. | Same blocking points and heights as SPLAT!'s report and height graph; the owner's review. |

## Stage G6: help and getting started (added 2026-11-23 at the owner's request)
The owner: add a stage after G5 for "a help menu with an indexed searchable help documents",
"step-by-step getting started instructions", and to "consider whether a 'first run' wizard that
prompts the user for the necessary information would be helpful". Club members on their own PCs
(decision G5) are the readers, so the help assumes no SPLAT! or command-line knowledge.

- **Help menu:** Contents, Index, Search, Getting started, About (version, GPLv2 licence, credits to
  SPLAT!'s authors). **F1** opens the topic for the panel in use.
- **Help documents:** plain HTML pages shipped with the app and shown in its own window (WebView2,
  already used for the map), so they work offline and can be read in a browser too. A keyword
  index and full-text search, both built from the pages, not kept by hand.
- **Topics (first list):**
  - getting started (below);
  - stations: call-sign lookup (QRZ, callook.info), grid, coordinates, map click, `.qth` import;
  - radio settings, each value in plain words, with SPLAT!'s documented defaults;
  - path analysis: reading the report summary and each graph;
  - **coverage maps: reading the colour key** (dBµV/m and dBm bands, a rough guide to what each
    band means on the air, the 50 %/90 % assumptions, receiver height, "you hear the repeater" vs
    "it hears you", grey terrain shading, opacity). Rough guides are labelled as rules of thumb,
    not SPLAT! output;
  - terrain: where it comes from, the folder, downloads, open water;
  - Google Earth; job folders and `run-again.cmd`;
  - troubleshooting (each error message the app can show, and what to do);
  - glossary (ERP, dBµV/m, dBm, ITWOM, Longley-Rice, Fresnel zone, line of sight).
- **"What does this mean?" links** beside the colour key, the report summary and the radio settings.
- **Getting started, step by step:** install → (optional) QRZ sign-in → add your first station →
  run a path → run a coverage map → open it in Google Earth. Each step with a picture of the
  app's own window (captured with PrintWindow).
- **First-run wizard:** decision G9 below.
- **Checks:** every link and index entry resolves; search finds each glossary term; F1 opens the
  right topic on every panel; each error message in the code has a troubleshooting entry (a test
  fails if one is added without); screen-reader names on the help window; the wizard (if built)
  driven end to end by a UI test with a throwaway QRZ credential and the stand-in terrain server.
  Shown to fire, as always.

## Stage G8: 3D path view (added 2026-11-24 at the owner's request)
The owner: "is it possible to produce a 3D view from transmitter to receiver or reverse to make it
easier to see what might be blocking the signal". A club member's path to K4HTA is "blocked by
terrain, but its difficult to visualize"; Google Earth Pro got "close ... but still not very
satisfying". On that path SPLAT! puts the blockage on a rise about half a mile from the receiver,
which neither the 2D graphs nor the map make obvious.

**What the user gets**
- After a path analysis, **3D view...** opens a window (decision G10) showing a strip of terrain
  along the path: both antennas as masts at their real heights, the **line of sight** between
  them, the **60 % Fresnel zone** as a see-through tube (the full first zone as an option), and
  **SPLAT!'s blocking points** marked in red where the terrain cuts into it, labeled with distance
  and height.
- **Viewpoints:** *From <receiver>'s antenna, looking at <transmitter>*; the reverse; *side view*
  (the 3D equivalent of the height graph); *overview*. Mouse to rotate, zoom and pan; **Reset view**.
- **Height exaggeration** slider, 1x to 10x (default 3x). At true scale a 50 ft rise ten miles away
  can't be seen; with exaggeration the line of sight and Fresnel tube scale with the terrain, so
  what's blocked stays blocked.
- **Ground:** topographic map, street map, or colored by height (works offline); decision G11.
  **Strip width** about 1 mile either side of the path by default, adjustable (decision G12).
- A short status line in plain words: "Blocked: terrain 0.4-0.6 mi from the receiver rises up to 49 ft
  above the line of sight", or "Clear: the 60 % Fresnel zone is clear of terrain".
- A reminder, in the window and the help: SPLAT!'s terrain is bare ground. Trees and buildings
  aren't in it, and Google Earth (which has 3D buildings) is the place to look for those.

**How it stays faithful to SPLAT!**
- **Along the path, SPLAT!'s own numbers are used, not recomputed.** The height graph run
  (`graph-height`, kept with `-gpsav`) already gives the terrain relative to the line of sight with
  Earth's curvature applied (`profile.gp`), and the Fresnel zones (`fresnel.gp`,
  `fresnel_pt_6.gp`). The blocking points come from the report (already parsed).
- **Either side of the path** the app reads the same terrain (.sdf) files SPLAT! used, with a new
  reader in SplatWin.Core that follows SPLAT!'s own file layout and elevation lookup, and applies
  the same Earth-curvature correction so the strip joins the center line exactly.
- **The scene is drawn with curvature already taken out** (heights relative to a flat reference,
  as SPLAT!'s height graph does), so the line of sight is a straight line and exaggeration keeps it
  straight.

**Technology:** WebView2 (already used for the map and help) with **three.js**, a widely used 3D
library (MIT license), vendored and pinned like Leaflet, so it works offline and installs nothing.
Map tiles for the ground come from the same servers and caching as the 2D map; only the strip is
fetched.

**Google Earth path files (the quick improvement)**
- The path KMZ/KML gets: the line of sight at its true height (each point's altitude computed with
  SPLAT!'s curvature, so it is the real ray, not a line hugging the globe), mast lines at both
  sites, red placemarks on the blocking points with their heights, and a starting **camera at the
  receiver's antenna looking at the transmitter**. Google Earth's own terrain differs slightly from
  SPLAT!'s, so the line can sit a few feet off; the help says so.

**Checks (shown to fire, as always)**
- **Terrain reader:** heights it reads along the path equal SPLAT!'s profile graph data
  (`graph-profile\profile.gp`) at every point, on the 20 test cases' paths and the owner's
  stations; standard and HD terrain.
- **Curvature and line of sight:** the app's height of terrain above or below the line of sight
  equals SPLAT!'s `profile.gp` in `graph-height`, and its Fresnel radii equal `fresnel.gp` and
  `fresnel_pt_6.gp`, to 0.01 ft.
- **Blocking points:** the points the view marks red are exactly SPLAT!'s list in the report, and
  "Clear" appears only when SPLAT!'s report has no obstructions.
- **Exaggeration:** at every setting, a point is above or below the line of sight exactly as at 1x.
- **KML:** the line-of-sight altitudes at both ends equal the antenna heights AMSL in the report;
  the file passes the existing KML checks; the owner opens it in Google Earth Pro and on the web.
- **Window test:** opens from a finished path, each viewpoint and the slider work, no script
  errors, a picture of each viewpoint kept with the results; it refuses politely when the path
  hasn't been run or its terrain is gone. A 50-mile path stays smooth.
- The SPLAT! equivalence gates are run again (nothing in SPLAT! or its runs changes).
- Help: a "3D path view" page, troubleshooting entries for its messages, getting started mentions
  it; README.

**Not in this stage:** a 3D coverage map; trees and buildings (SPLAT!'s `-gc` clutter height could be
shown later as a raised layer).

## Decisions for the owner
| # | Decision | Recommendation |
|---|---|---|
| **G1** | **UI technology** | **DECIDED 2026-11-22:** C#/.NET 10 + WPF (Fluent theme) + WebView2/Leaflet; install the .NET 10 SDK. |
| **G2** | **Background maps** | **DECIDED 2026-11-22:** streets + terrain (OpenStreetMap, plus a free topographic layer); no satellite. Was: OpenStreetMap streets (free, with attribution) plus a free terrain layer; satellite only if you want it, which may need a free account or key. |
| **G3** | **Do you have a QRZ XML Logbook Data subscription?** Without one, QRZ lookups won't give usable positions. | **DECIDED 2026-11-22: the owner subscribes** ("Yes, I subscribe"). QRZ is the primary lookup. callook.info stays as the free US fallback for users without a subscription. |
| **G4** | **Draw graphs in the app instead of gnuplot** (removes the ~1 GB dependency) | **DECIDED 2026-11-22:** the app draws the graphs. gnuplot stays available for the command line. |
| **G5** | **Who uses it (this is D10)**: just you, or club members too? This decides the installer, per-user QRZ sign-in and help text. | **DECIDED 2026-11-22:** club members too. Build for "club members on their own PCs" from the start; it costs little. |
| **G6** | **Order: GUI before Stage 5 packaging**, so one package holds both | **DECIDED 2026-11-22:** GUI first. Stage 5 becomes G6 (renumbered G7 on 2026-11-23, when the help stage was added as G6). |
| **G7** | **Licence for the GUI.** It ships with GPLv2 SPLAT!. | **DECIDED 2026-11-22:** GPLv2 as well (simplest, matches SPLAT!). |
| **G8** | **App name** shown in the window and Start menu | **DECIDED 2026-11-22:** "SPLAT! for Windows". |
| **G9** | **First-run wizard?** (stage G6) | **DECIDED 2026-11-24: short wizard** (owner: "Short wizard (Recommended)"). Was recommended: a short wizard that opens once on first run, can be skipped, and can be reopened from Help: (1) welcome and what the app does; (2) terrain folder (the default is fine for most); (3) call-sign lookup: QRZ sign-in (the user types the password) or the free callook.info; (4) add your first station; (5) offer to run a first coverage map. Alternative: no wizard; the Getting started page opens on first run, and empty panels show what to do next. |
| **G10** | **Where the 3D view shows** | **DECIDED 2026-11-24 ("Yes, start G8"): its own window.** Recommended was, opened by a 3D view... button after a path is analyzed: big, resizable, can sit beside the main window. The alternative is a sixth tab beside the graphs, which is small. |
| **G11** | **What covers the ground** | **DECIDED 2026-11-24: topographic map by default.** Recommended was, with street map and "colored by height" (offline) as choices. |
| **G12** | **Strip width** | **DECIDED 2026-11-24: about 1 mile either side.** Recommended was, adjustable from 1/4 to 5 miles. |
