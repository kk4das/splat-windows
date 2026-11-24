SPLAT! for Windows {VERSION} (preview)
=====================================

SPLAT! for Windows predicts how a radio signal travels over real terrain:
between two stations (path analysis), or from one transmitter over the area
around it (coverage maps). It runs SPLAT!, the RF signal propagation, loss and
terrain analysis tool by John A. Magliacane, KD2BD, and shows its answers on a
map, with files for Google Earth.

This is a preview for testing. Please tell KK4DAS what works, what doesn't,
and what's confusing.


What you need
-------------
- Windows 11, or Windows 10 (64-bit). Nothing else to install: the app brings
  everything it needs.
- On Windows 10, the Microsoft Edge WebView2 Runtime, which draws the map and
  the help. Most Windows 10 PCs already have it; if the map stays blank, get it
  free from Microsoft (search for "WebView2 Runtime").
- An internet connection for the background map, for downloading terrain the
  first time an area is used, and for looking up call signs. Paths and maps in
  areas already downloaded work offline.
- Optional: Google Earth Pro, to open maps and paths in it.
- Optional: a QRZ.com XML Logbook Data subscription, to look up call signs
  worldwide. Without one, US call signs are looked up free on callook.info.


Installing
----------
1. Unzip the whole zip to a folder of your own, for example
   C:\Users\<you>\SPLATforWindows or a folder on another drive. (Not inside
   "Program Files", and don't run it from inside the zip.)
2. Double-click SplatForWindows.exe in that folder.
3. Optional: double-click "Add Start menu shortcut.cmd" to put SPLAT! for
   Windows in your Start menu.

The first time it starts, a short setup asks where terrain goes, how to look
up call signs, and helps you add your first station. Help > Getting started
walks you from there to your first coverage map.

"Windows protected your PC"
  The app isn't signed with a paid code-signing certificate, so Windows may
  warn the first time you run it. Click "More info", check the name is
  SplatForWindows.exe, then "Run anyway". To avoid the warning for every file:
  before unzipping, right-click the zip, choose Properties, tick "Unblock" and
  click OK.


Where things are kept
---------------------
- Your stations:        Documents\SPLAT\stations.json
- Each path and map:    Documents\SPLAT\Jobs\ (one folder per run)
- Downloaded terrain:   %LOCALAPPDATA%\SPLATforWindows\Terrain
- Settings:             %LOCALAPPDATA%\SPLATforWindows\settings.json
- Your QRZ.com password, if you sign in: Windows Credential Manager (never in a
  file). Help > Settings and QRZ.com explains.


Removing it
-----------
1. If you added the Start menu shortcut, double-click
   "Remove Start menu shortcut.cmd".
2. Delete the folder you unzipped. Nothing else was installed.
3. If you also want your data gone: delete Documents\SPLAT and
   %LOCALAPPDATA%\SPLATforWindows, and sign out of QRZ.com in the app first
   (Settings > Sign in or out) to remove the saved password.


Help
----
Everything is in the app: press F1, or use the Help menu. Help > Troubleshooting
explains every message the app can show. The help pages are also in the "help"
folder and open in any web browser.


For command-line users
----------------------
The SPLAT! programs themselves are in the "engine" folder (splat.exe,
splat-hd.exe, srtm2sdf.exe, srtm2sdf-hd.exe), built for Windows and checked
against SPLAT! on Linux. SPLAT!'s own documentation is in docs\SPLAT. Every job
folder the app makes includes run-again.cmd, which repeats the same SPLAT!
commands from the command line.


License and source code
-----------------------
SPLAT! for Windows is free software under the GNU General Public License,
version 2 (see LICENSE.txt), as is SPLAT!. It comes with NO WARRANTY.
The complete source code, for SPLAT! as built here and for the app, is at:

    {SOURCE_URL}

Credits: SPLAT! by John A. Magliacane, KD2BD, and its contributors; the ITWOM
propagation model by Sid Shumate; map display by Leaflet (BSD license); map
data (c) OpenStreetMap contributors and OpenTopoMap (CC-BY-SA); terrain from
SRTM data via viewfinderpanoramas.org and the AWS open terrain tiles. Help >
About has the full list.
