# Stage G2 report: QRZ.com sign-in and lookup (2026-11-22)

## What this stage was meant to accomplish
Call sign lookups worldwide through the owner's own QRZ.com XML Logbook Data subscription. The
owner types the password into the app. It is kept in Windows Credential Manager, never in a file,
log, git or report. Every position comes with a plain warning about where QRZ got it (its
`geoloc`). Started on the owner's words "Start G2".

## Outcome: built and checked against a stand-in; the live check is yours
The app is open on your screen (`gui\publish\SplatForWindows.exe`, version 0.3.0).

- **Sign in:** click **QRZ.com: sign in…** at the top right and type your QRZ user name and
  password.
  - The app tries them on QRZ first, and **saves them only if QRZ accepts them**. A wrong password
    shows QRZ's own reason and replaces nothing.
  - On success, the status line shows your subscription's expiry date. If the account has no XML
    subscription, it says that lookups won't give positions.
  - The button then reads **QRZ.com: <your user name>**.
- **Look up:** while you're signed in, the **Call sign** tab uses QRZ for any call worldwide. When
  you're signed out it uses callook.info (US only), as in G1. The hint under the box says which one
  is in use.
- **Where the position came from**, shown in the yellow note:

  | QRZ `geoloc` | What the note says |
  |---|---|
  | `user` | The ham set it themselves. It's usually their home station; for a repeater or club call it may not be the antenna. |
  | `geocode` | The licence mailing address: often not the antenna, possibly a PO box. Drag the pin. |
  | `grid` | Only a grid square; the pin is at its centre. |
  | `zip`, `state`, `dxcc` | **WARNING:** only the centre of the zip code, state or country. Useless for a VHF path; use the map or the Address tab. |
  | missing or unknown | **WARNING:** QRZ doesn't say where the position came from. |

  As in G1, Save still needs the pin confirmed.
- **Sign out** (in the same window) deletes the saved password from Credential Manager.
- **QRZ's rules, as its spec (v1.34) asks:**
  - The app signs in once and reuses the session key, signing in again only when QRZ says the
    session has ended.
  - It stops at once on "Connection refused", which QRZ says means no sign-in for 24 hours.
  - It identifies itself as `SPLATWin0.3`.
  - Requests are sent as POST, so **the password never appears in a URL**.

**Where the password lives:** Control Panel → Credential Manager → Windows Credentials, entry
`SPLATforWindows/QRZ.com`. Windows encrypts it for your Windows account, on this PC only; it
doesn't roam.

## How it was checked
- **207 unit tests pass** (54 new):
  - **Replies**, from QRZ's own example XML in its spec: sign-in, non-subscriber, wrong password,
    session timeout, connection refused, not found, and the full AA7BQ record.
    - Also with and without QRZ's namespace, and with unknown fields in any order (the spec
      requires both).
    - A DTD is refused, which guards against XML tricks.
  - **Positions:** every `geoloc` value and its note. When lat/lon are missing or impossible, the
    app falls back to the grid square. With no position at all, the message says why (for
    example, no subscription).
  - **Records:** a record for the wrong call is refused; aliases (KJ6RK, DL/AA7BQ) are accepted.
  - **The client:**
    - Sign-in is sent by POST, with the agent name, and the password goes nowhere else.
    - The session is reused, including the one from the sign-in window.
    - After an expired session it signs in again once and then stops.
    - A refused account is not retried. A bad call or a missing sign-in sends nothing.
    - HTTP errors and no internet are explained, and the password never appears in error text.
  - **Credential Manager:** save, read, replace and delete, on a throwaway entry, with a unicode
    password and `;&=` characters.
- **Shown to fire:** I broke the QRZ code on purpose in seven places. **All seven were caught:**
  - zip-code positions not flagged;
  - no second sign-in after expiry;
  - a refused account retried;
  - namespace-sensitive parsing (35 failures);
  - a record for another call accepted;
  - DTDs allowed;
  - the password put in the URL.

  The code was restored from a backup and checked identical (sha1).
- **The real window, against a stand-in QRZ on localhost** (`scripts\gui_qrz_uitest.ps1` with
  `scripts\fake_qrz.ps1`): **24 of 24 checks pass.**
  - The test uses its own throwaway Credential Manager entry and the test password
    `test-password`. It never touches the real QRZ or your real entry.
  - **What it does:** starts signed out (callook hint), then signs in with a wrong password
    (refused, nothing saved) and with the right one (saved, subscription shown). Then it looks up a
    not-found call, a zip-code-only call (loud warning) and AA7BQ (pin, name and note), saves
    AA7BQ, and signs out (entry removed).
  - **What it checks on the wire:** every request was a POST, the password never appeared in a
    URL, the password is in no file, and exactly two sign-ins happened.
  - **The G1 build fails this script**, so it can tell.
  - **The stations script still passes, 31 of 31**, on the new build.
- **Toolchain preflight OK at both ends** (from PowerShell). The SPLAT! engine (`src`, `dist`) is
  unchanged.

## Findings worth noting
1. **The window test caught a real bug.** After you signed in, the app threw away the new QRZ
   session and signed in a second time on the first lookup. QRZ asks clients not to do that. It's
   fixed, with a unit test.
2. **The test replies are QRZ's published examples, not recordings of real sessions.** Code never
   signs in with your password, so I couldn't record any. The parser follows the spec's rules
   (namespace-free, unknown fields ignored, any order) to be safe, but **your live lookups are the
   first real QRZ traffic**.
3. **QRZ's own example record is inconsistent.** Its `DM32af` grid doesn't match its own lat/lon
   (that would be `DM34tf`). The app uses QRZ's lat/lon, uses QRZ's grid only when there is no
   lat/lon, and computes its own grid for the list.
4. **QRZ's spec page refuses automated downloads (HTTP 403),** so I read it in the app's built-in
   browser. It's version 1.34, dated July 2020.
5. **I closed your open app window again** (the normal way) so the new version could be published.
   Your four stations are untouched.
6. **When you're signed in to QRZ, the Call sign tab uses QRZ only.** If a QRZ record has no
   usable position, it says so. You can still sign out to use callook.info, or use the Address tab.

## Declared coverage gaps
- **No live QRZ lookups have been made.** That's your check, below.
- **Session expiry and "Connection refused"** were tested only with the spec's sample replies.
- **The look** of the sign-in window is your judgement.

## Your check (the G2 gate, from the plan)
In the open app:
1. Click **QRZ.com: sign in…** (top right), type your QRZ user name and password, and click
   **Sign in**. The status line should show your subscription's expiry date.
2. Click **Add station…** → **Call sign**, and look up **three calls**, for example KK4DAS, K4HTA,
   and a non-US call you know. For each, read the yellow note (where QRZ got the position) and
   check the pin. You don't have to save them; **Cancel** is fine.
3. Optional: open Credential Manager to see the `SPLATforWindows/QRZ.com` entry, or try
   **Sign out**.

If a reply looks wrong or an error appears, tell me what it said. Please don't paste your
password or session key.

## Files added or changed
- `gui\SplatWin.Core`: `Qrz.cs` (parser and client) and `WinCredentials.cs` (Credential Manager).
- `gui\SplatWin.App`:
  - `QrzSignInWindow.xaml(.cs)`, the toolbar button and the QRZ path in the Call sign tab;
  - `App.xaml.cs`, with the `--qrz-url` and `--qrz-credential` test options;
  - version 0.3.0.
- `gui\SplatWin.Tests`: `QrzTests.cs` and `samples\qrz-*.xml` (from the spec; see
  `qrz-README.txt`).
- `scripts\gui_qrz_uitest.ps1`, `scripts\fake_qrz.ps1`, the runbook, the plan, and this report.

## Next (needs your go-ahead): Stage G3, path analysis
Pick a transmitter and a receiver from the list and click **Analyse**. The app runs SPLAT! and
shows the summary, the graphs (drawn in the app) and the path on the map. The gate is the same
numbers as the command-line reports, with the equivalence test passing.

## Owner's check: passed (2026-11-22)
Owner: "Lookups work". These were the first live QRZ.com sign-in and lookups. Stage G3 waits for
an explicit go-ahead.

## Halted for owner review.
