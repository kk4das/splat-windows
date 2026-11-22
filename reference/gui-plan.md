# SPLAT! for Windows: GUI front end, plan for owner review (2026-11-22)

**Status: PLAN ONLY.** Nothing is built yet. This document needs the owner's decisions (G1–G8 at
the end) and an explicit "start Stage G0" before any work begins.

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
| **G5 Google Earth + polish** | Pro and web KMZ, the point-to-point KML test, settings, help, icons. | The owner's review. |
| **G6 Packaging** | Merges with Stage 5: a zip or installer, a clean-machine test, D10. | A clean-PC report. |

## Decisions for the owner
| # | Decision | Recommendation |
|---|---|---|
| **G1** | **UI technology** | C#/.NET 10 + WPF (Fluent theme) + WebView2/Leaflet; install the .NET 10 SDK. |
| **G2** | **Background maps** | OpenStreetMap streets (free, with attribution) plus a free terrain layer; satellite only if you want it, which may need a free account or key. |
| **G3** | **Do you have a QRZ XML Logbook Data subscription?** Without one, QRZ lookups won't give usable positions. | If not, build callook.info (US, free) first, and QRZ in G2 anyway for anyone who has one. |
| **G4** | **Draw graphs in the app instead of gnuplot** (removes the ~1 GB dependency) | Yes. gnuplot stays available for the command line. |
| **G5** | **Who uses it (this is D10)**: just you, or club members too? This decides the installer, per-user QRZ sign-in and help text. | Build for "club members on their own PCs" from the start; it costs little. |
| **G6** | **Order: GUI before Stage 5 packaging**, so one package holds both | Yes. Stage 5 becomes G6. |
| **G7** | **Licence for the GUI.** It ships with GPLv2 SPLAT!. | GPLv2 as well (simplest, matches SPLAT!). |
| **G8** | **App name** shown in the window and Start menu | e.g. "SPLAT! for Windows"; your call. |
