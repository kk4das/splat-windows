# Stage G6 report: help and getting started (2026-11-24)

## What this stage was meant to accomplish
Your request, added as G6 on 2026-11-23:
- a Help menu with indexed, searchable help;
- step-by-step getting-started instructions;
- a decision on a first-run wizard (decision G9).

Started on your words "Start G6". **Decision G9, made at the start: a short first-run setup**
("Short wizard (Recommended)").

## Outcome: built and checked; the review is yours
The app is version 0.7.0 in `gui\publish`, open on your screen.

- **Help** (new, top right) is a menu: Contents, Index, Search, Getting started, Setup…,
  Troubleshooting, and About.
  - **F1** opens help for what you're looking at: the stations list, the station editor, path
    analysis, coverage maps, Settings, QRZ sign-in, or the setup.
  - **"What does this mean?"** buttons sit next to the coverage colour key, the path summary, the
    radio settings and the antenna pattern.
- **The help window:** Contents, Index and Search on the left, the page on the right.
  - It works without internet.
  - Links to web sites open in your browser.
  - The pages are plain HTML in the app's `help` folder, so they open in any browser too.
  - **The contents, the index (231 entries) and the search are all built from the pages
    themselves**, not kept by hand.
  - Search finds pages holding every word you type, best match first, opening at the right section.
    "dbuv" finds dBµV/m.
- **13 pages:**
  - Getting started, with four pictures of the app;
  - Stations;
  - Radio settings;
  - Path analysis, including what the white circles are;
  - Coverage maps, including **reading the colour key** (rules of thumb labelled as such);
  - Terrain;
  - Google Earth;
  - Job folders;
  - Settings and QRZ.com;
  - **Troubleshooting: every message the app can show (187), with what it means and what to do**;
  - Glossary (33 terms);
  - About: version, GPLv2, and credit to John Magliacane KD2BD (SPLAT!), Sid Shumate (ITWOM),
    Leaflet, OpenStreetMap, OpenTopoMap, and the terrain, call-sign and address sources.
- **The first-run setup:** it opens once, on a first start with no stations.
  - Four steps: welcome; terrain folder (the default is fine); call-sign lookups (QRZ.com sign-in,
    where you type the password, or the free callook.info); your first station.
  - "Add my first station" opens the station editor. Once the station is saved, the app offers to
    make a first coverage map for it.
  - It can be skipped, and opened again from Help → Setup….
  - **Your copy won't show it:** you already have stations, so on its next start the app just records
    the setup as not needed.

## How it was checked
- **319 unit tests pass** (15 new for help). They check that:
  - no help page has a broken link, a missing picture or a repeated anchor;
  - every place F1 and the buttons open exists;
  - every index entry goes somewhere;
  - search puts the right page first for six real questions, and needs every word;
  - every glossary term can be found, including a fixed list of the terms the app's own screens use;
  - **troubleshooting covers every message in the code.** A finder reads the app's source (messages
    passed to exceptions, error boxes, failure status lines, and Core's problem sentences, with
    `{…}` parts read properly) and fails the test if any message has no entry. It found the help
    window's own start-up message, added during this stage, before I'd written its entry.
- **The new window test, `scripts\gui_help_uitest.ps1`: 30/30**, run three times. It starts the app as
  on a first run (empty library, its own settings file) and checks:
  - the setup's four steps, Back, and that it's recorded as done;
  - the editor opening;
  - "What does this mean?";
  - **choosing an antenna pattern and an obstacles file with the real Windows file picker** (the G5
    gap: see below);
  - the coverage-map offer after the first station, and the pattern saved and used;
  - **F1** in the coverage panel;
  - Help → Index (typing narrows it), Search (best page first; no answer says so), Contents in
    order, and About with the version filled in;
  - Help → Setup… again;
  - closing the main window closes help too;
  - a second start doesn't show the setup.
- **Earlier window tests, on the final build:**
  - coverage 52/52;
  - path 41/41;
  - stations 31/31;
  - QRZ 24/24.

  They now start with the setup marked done in their own settings files.
- **Gates against Linux, still EQUIVALENT:** path (10 cases) and coverage (4 maps). The engine is
  unchanged.
- **Shown to fire: 11 deliberate breakages, all caught.**
  - **Nine, caught by the unit tests:**
    - a new message without an entry;
    - a broken link;
    - a help topic that doesn't exist;
    - a "What does this mean?" button pointing nowhere;
    - the index without section headings;
    - search without the µ fold;
    - search matching any word instead of all;
    - a glossary term renamed;
    - the message finder not splitting `{…}`.
  - **Two, on published builds, caught by the window test:**
    - the setup not remembered;
    - F1 doing nothing.
  - **Two first attempts missed, both teaching something:**
    - Renaming a glossary term passed, because nothing pinned which terms must exist. I added the
      fixed list.
    - My first "message finder" breakage changed nothing real. I replaced it with one that does.
- **Toolchain preflight OK at both ends.**

## Findings worth noting
1. **A G1 bug found by the getting-started pictures:** while you placed a station's pin, the map
   still said "Open a SPLAT! coverage map (.kml) to show it here" across the middle. It now goes
   away once there are stations or a pin. With no stations it says "Add a station to begin (Add
   station…), or open a SPLAT! coverage map (.kml)".
2. **Correction to the G5 report:** it said the file-choosing steps couldn't be driven by the window
   tests. They can: the stations test already drove the Windows file picker with window messages.
   The new help test now chooses a pattern file and an obstacles file through the real picker.
3. **Closing the main window now closes the app**, even with the help window open. Before, an open
   help window would have kept it running.
4. **One timing flake in the new test:** the Help pop-up menu once didn't appear in time. The test now
   opens it again (up to three tries) and reports when it had to. It didn't need to in the next two
   runs.
5. A wording fix in the setup ("…from Help > Setup…." ended in four dots).

## Declared coverage gaps
- **Google Earth on the web** is still described, not tried (it needs a Google sign-in).
- **The help's accuracy** was checked against the app and SPLAT!'s documentation as I wrote it, and
  the tests check its structure, not its sense. Your reading is the real check, especially the
  rules of thumb in the colour-key table.
- **The pictures** come from the test stations and need refreshing if the window changes:
  `scripts\help_screens.ps1` makes them again in a minute.
- **The F1 check covers one panel** (coverage) in the window test. The others go through the same
  code, and the unit tests check that each panel's help target exists.

## Your check (the G6 review)
In the open app:
1. Press **F1** on the stations list, in a path analysis and in a coverage map. Each opens the right
   page.
2. **Help → Search**: try "colour key", "white circles", or a few words of a message you've seen.
3. **Help → Getting started**: read it as a new club member would.
4. In a coverage map, click **What does this mean?** under the colour key.
5. Optional: **Help → Setup…** shows the first-run setup, with Skip setup to close it.

## Files added or changed
- `gui\SplatWin.Core\HelpLibrary.cs` (pages, index, search, link checks, help topics) and
  `AppSettings.cs` (SetupDone).
- `gui\SplatWin.App`:
  - `help\` (13 pages, `help.css`, 4 pictures);
  - `HelpWindow`, `SetupWindow`, `MainWindow.Help.cs`;
  - the Help menu and buttons in `MainWindow.xaml`;
  - F1 in the Settings and QRZ windows;
  - one shared WebView2 environment;
  - closes with the main window;
  - the map's empty hint;
  - version 0.7.0.
- `gui\SplatWin.Tests\G6Tests.cs` (help tests and the message finder).
- `scripts\gui_help_uitest.ps1`, `scripts\help_screens.ps1`; the four earlier window tests (setup
  marked done).
- `tests\results\stageG6-gui-help-uitest.txt`, the runbook, the plan, and this report.

## Next (needs your go-ahead): Stage G7, packaging
A zip or installer, and a clean-machine test (merges the original Stage 5). Stage 4's formal
approval is still open.

## Owner's review: passed (2026-11-24), with one change
Owner: "change colour to color and any other british english spellings to american english.
Everything else with help looks good. setup wizard seems helpful - I'll have others test it when we
get the app packaged."

- **Done:** 232 words, in the app's text, messages, help and setup, and in the tests that check that
  text: color, analyze, meter, kilometer, license, center, gray, canceled, labeled, normalized,
  recognize.
- **Left alone:** code names, and the stored job files' `ColourKey` field, so earlier maps still open.
  SPLAT!'s own output is untouched.
- **Checks:** the help pictures were retaken, and all checks pass on the rebuilt app.
- **From now on:** American English in everything you see.

Stage G7 (packaging) waits for an explicit go-ahead.

## Halted for owner review.
