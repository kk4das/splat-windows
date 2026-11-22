# Stage G1 report: stations (2026-11-22)

## What this stage was meant to accomplish
Let a ham put stations into SPLAT! for Windows **without typing coordinates or writing `.qth`
files**: by call sign, grid square, typed coordinates or a click on the map, always finishing with a
pin on the map that they check, because a call sign gives a mailing address and a grid square only
an area. Plus a stations library with `.qth` import and export.
Started on the owner's words "Lines up, start G1", which also approved G0.

## Outcome: works; the hands-on check is yours
The app is open on your screen (`gui\publish\SplatForWindows.exe`). The **Stations** panel is on
the left.

**Adding a station** (Add station…):
1. **Find the place**, from one of four tabs:
   - **Call sign:** a US call, looked up on callook.info (free FCC data). It fills in the name too.
   - **Grid:** 4 to 10 characters, such as `FM18jw`. The square is outlined on the map and the
     pin goes at its centre.
   - **Coordinates:** as Google Maps shows them (`38.9242, -77.2058`), or
     degrees-minutes-seconds (`38°55'27"N 77°12'21"W`).
   - **Map:** just click where the antenna is.
2. **Put the pin on the antenna.** Drag it, or click the map. A yellow note says how far to trust
   the starting point, e.g. "FM18jw is a square about 2.9 × 4.5 mi… too coarse for a VHF path", or
   that a call sign gives the licence's mailing address. **Save won't work until you tick "The pin
   is on the antenna site"**, and moving the pin clears the tick.
3. **Name and antenna height**, in ft or m.

**The list** shows each station's 8-character grid, its antenna height and how it was found (e.g.
"Grid FM18jw, moved on map"). Clicking a station centres the map on it, and clicking a station's
dot on the map selects it. Also: **Edit…**, **Delete** (asks first), **Import .qth…** (several at
once; asks before replacing a station of the same name) and **Export .qth…** (for the command
line).

**Where the library lives:** `Documents\SPLAT\stations.json`, which on this PC is
`D:\users\dsoul\OneDrive\Documents\SPLAT\`, so OneDrive backs it up. Each save keeps the previous
version as `stations.json.bak`. A damaged file is never overwritten: the app says so and leaves it
alone.

## How it was checked
- **117 unit tests pass** (99 new):
  - **Grid squares**, against answers from outside this code: callook.info's own `FN31pr` for
    W1AW, plus Munich, Sydney (south and east) and London. Also the world's corners, points exactly
    on edges, and 20,000 random round trips. `FM18` comes out exactly the box of the Linux golden
    map.
  - **Coordinates:** 11 pasted forms read correctly. 13 ambiguous or wrong ones are refused with a
    reason: decimal commas, 61 minutes, a minus sign together with W, or three bare numbers.
  - **`.qth` files:** the project's test sites, feet and meters, and west-positive longitude both
    ways. Nine kinds of file that SPLAT! would silently misread are refused: lines too long for it,
    or forms it reads as 0. 2,000 random write-then-read round trips.
  - **callook.info**, from recorded replies: VALID, INVALID, UPDATING, a reply for the wrong call,
    no position, a garbled reply, HTTP errors and no internet. Badly formed call signs are refused
    before anything is sent.
  - **The library:** saving, the `.bak` copy, refusing damaged or foreign files (left untouched),
    unique names, and SPLAT!'s 48-character name limit.
- **Shown to fire:** I broke the code on purpose in five places, one at a time. **All five were
  caught.**
  - The breakages: dropping SPLAT!'s west-positive longitude, ignoring S/W, accepting an unknown
    call, allowing duplicate names, and removing the grid-edge nudge.
  - The grid-edge breakage **survived at first**. I then found that thousands of real square edges
    land a hair below the line in floating point (e.g. −88.9166…), added a test for them, and it
    then failed as it should.
  - The code was restored from a backup and checked identical.
- **The real `splat.exe` reads what the app exports:** K4HTA in feet and KK4DAS in `9.14 meters`
  came out as 240.00 ft and 29.99 ft AGL, at the right positions.
- **The real window, driven by a script** (`scripts\gui_stations_uitest.ps1`, using Windows UI
  Automation and its own throwaway stations file): **27 of 27 checks pass**.
  - It adds Post 180 by grid, W1AW by a live callook lookup, and K4HTA by deg-min-sec in metres.
  - It checks the refusals: an unconfirmed pin, a non-numeric height, a bad grid, a duplicate
    name, an unknown call (live), and `/P`.
  - It imports three `.qth` files (answering No to replacing K4HTA), then edits, exports and
    deletes, and checks the JSON file and the `.bak` copy at each step.
  - **The same script run against the G0 build fails (exit 1)**, so it can tell.
- **The map page** (in a browser, with a stand-in for the app):
  - A click while adding places the pin within one pixel and tells the app.
  - A drag reports exactly where it ended.
  - Clicking a station dot selects that station and doesn't move the pin.
  - Clicks outside editing do nothing.
  - The grid outline stays while you refine.
- **Toolchain preflight OK at both ends** (from PowerShell, see below). The SPLAT! engine
  (`src`, `dist`) is unchanged, so the Linux equivalence still holds.

## Findings worth noting
1. **callook.info puts a station at its licence's mailing address.** For W1AW that is the ARRL HQ
   street address, which happens to be right. For a club repeater like K4HTA it will usually be the
   trustee's house. The app says so every time, and won't save without the confirm tick. QRZ (G2)
   has the same issue unless the ham has set their own position (QRZ's `geoloc` = `user`).
2. **The importer is stricter than SPLAT!.** SPLAT! reads a malformed latitude, such as "38 55"
   or anything over 18 characters, as 0 without a word. The importer refuses the file and says
   why.
3. **The toolchain preflight must run from PowerShell.** Run from Git Bash it reports PROBLEMS,
   because Git Bash sets `HOME` and one of the port findings needs it unset. That is a property of
   the shell, not of the toolchain; the runbook now says so.
4. **I closed your G0 preview window** (the normal way, as if you clicked ×) so the new version
   could be published into `gui\publish`. You had already signed G0 off.
5. **Live lookups done during testing:** W1AW (the ARRL's station) and the made-up call ZZ9ZZZ
   only. The recorded W1AW reply used in the tests has the trustee's personal name replaced.

## Declared coverage gaps
- **Real mouse clicks and drags inside the app window weren't automated.** The script doesn't move
  your mouse. Those paths were checked in the map page in a browser, and the page-to-app message
  path is the same one the "ready" and "station-click" messages use.
- **The look** (Windows 11 style, light and dark) is your judgement; I didn't take screenshots.
- **callook.info coverage:** US calls only, by design. Non-US hams use a grid, coordinates or the
  map until QRZ (G2).

## Your check (the G1 gate, from the plan)
In the open app, add **K4HTA, KK4DAS and Post 180 without typing coordinates**:
1. **K4HTA:** Call sign tab → `K4HTA` → Find. The pin lands at the trustee's mailing address.
   Drag it to the tower, tick the box, enter 240 ft, then Save.
2. **KK4DAS:** by call sign, or Grid then a click on your house. Antenna 30 ft.
3. **Post 180:** Map tab, or Grid `FM18` and then zoom in and click on the hall.

Or try **Import .qth…** with your `D:\splat-work` files. KK4DAS will show **9.14 ft**, which is the
height question from G0; Edit fixes it.

## Files added or changed
- `gui\SplatWin.Core`: `Maidenhead.cs`, `Coordinates.cs`, `QthFile.cs`, `Stations.cs`, `Callook.cs`.
- `gui\SplatWin.App`: `MainWindow.xaml` (stations panel), `MainWindow.Stations.cs`, `App.xaml.cs`
  (a shared web client that identifies the app, and a `--stations <file>` option for testing), and
  `web\index.html` (station dots, the pin, the grid outline). Version 0.2.0.
- `gui\SplatWin.Tests`: `StationTests.cs`, and `samples\` (recorded callook replies).
- `scripts\gui_stations_uitest.ps1`, the runbook, and this report.

## Next (needs your go-ahead): Stage G2, QRZ.com
You sign in to QRZ in the app (you type the password; it's kept in Windows Credential Manager).
Then lookup by call sign worldwide, with a loud warning when QRZ's position is only a zip code,
state or grid.

## Addendum (same day): street-address lookup
**Why:** at the check, the owner found that callook.info puts K4HTA at its PO box, not the repeater
site ("We need to add a postal address lookup").

**What was added:** a new **Address** tab in Add station, second after Call sign.
- **Two lookups at once:** type the site's street address, and the app asks two free services, with
  no account or key:
  - **The US Census Bureau geocoder** (US addresses). It estimates the spot along the street from
    the block's house-number range.
  - **OpenStreetMap's Nominatim** (worldwide). Where OSM has the building mapped, the pin goes on
    the building itself.
- **The matches are listed, best first**, each saying how exact it is: "the building or house
  itself", "estimated from the street's house-number range", "only the street, not the house" or
  "only the town or area". The best match is selected and the pin goes there. Click another to move
  it. As with every method, you then drag the pin to the antenna and tick the box.
- **PO boxes are refused before anything is sent**, with the reason: a PO box is at the post
  office.
- **If one service fails**, the other's matches are still shown and the status line says which one
  failed.
- **OpenStreetMap's usage policy is followed:** the app names itself, and sends at most one request
  a second. There is no searching as you type.

**How it was checked:**
- **153 unit tests pass** (36 new):
  - recorded replies from both services, for the ARRL HQ address (225 Main St, Newington CT),
    Munich (east longitude), a street-only match, "no match" and a refused request;
  - garbled replies, out-of-range positions, the PO-box forms, the rate limit, and one or both
    services failing.
  - The ARRL building from OSM is within 30 m of the FCC position callook gives for W1AW, as an
    independent check.
- **Five deliberate breakages, all caught:** no PO-box check; the Census longitude and latitude
  swapped; a street counted as a building; the rate limit removed; one failing service losing
  everything. The code was then restored and checked identical.
- **The real window:** 31 of 31 checks pass, including a refused PO box and a live ARRL lookup that
  got 4 matches from both services, with the pin on the building. The G1 build without the tab fails
  the script.
- **An opt-in live test** (`SPLAT_LIVE=1`) checks that both services still answer. It passed.
- **Live lookups in testing:** the ARRL HQ address and an obviously fake address only. No private
  addresses were sent.

**Limits:**
- An address is a building's front door, not the tower. For a repeater on a hill or a shared tower,
  the Map tab may still be the right tool.
- Outside OSM's mapped buildings, the US Census only estimates along the street. Rural routes can
  be off by a lot.

## Owner's check: passed (2026-11-22)
"address lookup worked for post-180 and for K4HTA … new address based with drag and pin is more
accurate." The four stations were added without typing coordinates: KK4DAS and KD4MM by call sign,
Post-180 by address (OpenStreetMap), and K4HTA by address (US Census) and then a drag. K4HTA's new
pin is about 80 m from the old `.qth` position.

## Halted for owner review.
