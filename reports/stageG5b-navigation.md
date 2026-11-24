# Stage G5b report: switching between panels (2026-11-23)

## What this was meant to accomplish
Your request after the G5 review, before G6:
> "make it easier to switch between coverage map and analyze a path - it wasn't intuitive that I
> needed to click close, close was scrolled off the bottom of the panel, and even when I saw the
> close button it wasn't apparent what it did. maybe make all 4 button in the left hand panel fixed
> at the top of the panel"

## Outcome: done; the check is yours
The app in `gui\publish` is open on your screen.

- **The four buttons are fixed at the top of the left panel** in every view: Add station…, Import
  .qth…, Analyse a path…, Coverage map….
  - The button for what you're looking at is highlighted.
  - Clicking another switches straight to it, so Coverage map → Analyse a path needs no Close
    in between.
  - What you typed in a panel is still there when you come back to it.
- **Back to stations** replaces Close. It sits just under the four buttons and never scrolls away.
  - It's shown in path analysis and coverage maps.
  - It's also shown in the station editor, where it reads **Back to stations (nothing saved)**.
  - It isn't shown on the stations list itself.
- **Cancel is its own button now**, next to Run or Analyse, shown only while a job runs.
  - While a job runs, the switching buttons wait. Clicking one says why: wait, or Cancel.
  - Path analysis can now also be cancelled while it is still fetching terrain.
- Leaving path analysis clears its results and the path from the map, as Close did. Leaving a
  coverage map keeps the map on the map.

## How it was checked
- **Window tests on `gui\publish`, all passing:**
  - coverage 52/52 (4 new);
  - path 41/41 (5 new);
  - stations 31/31;
  - QRZ 24/24.
- **The new checks:**
  - the four buttons and Back are shown;
  - coverage → path → coverage switches directly, with the radius still as typed;
  - path → coverage clears the path's results;
  - while a map runs, Cancel is shown and the switching buttons are off;
  - Back from the editor saves nothing and says so;
  - Back returns to the list.
- **Shown to fire:** a build that leaves the switching buttons on while a map runs fails the
  coverage window test. The code was restored and checked by hash.
- Unit tests 304/304. Toolchain preflight OK at both ends. The engine is unchanged.
- **Pictures of the app's own window (PrintWindow) were checked by eye.** They caught two things
  that the tests can't see, fixed before the final build:
  - after switching, some buttons dropped the Windows 11 look;
  - the first Back button scrolled away with the panel.

## Declared gaps
- **How the buttons look** (the highlight, the spacing) is your judgement. The tests check what the
  buttons do, not how they look.

## Your check
In the open app:
1. Click **Coverage map…**, then **Analyse a path…**, then **Coverage map…** again: no Close needed.
   What you typed is still there.
2. Scroll down in either panel: the four buttons and **Back to stations** stay put.
3. Optionally, start a coverage map and try **Cancel**.

## Halted for owner review.
