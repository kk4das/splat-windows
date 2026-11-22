# SPLAT! for Windows

A native Windows build of **SPLAT!** 1.4.2 (John Magliacane, KD2BD), the RF path and terrain
analysis tool: point-to-point path reports, terrain and path-loss graphs, and coverage maps.
It was ported from the Linux source and **gives the same answers as the Linux build**. Every map and
terrain file is byte-identical on the project's 20 test cases (see `reports/stage3-equivalence.md`).

SPLAT! is a command-line program. You run it from PowerShell in a working folder that holds your
site files, and it writes its reports, graphs, and maps into that folder. The full manual is
`src\docs\english\text\splat.txt` (or the PDF next to it).

---

## 1. One-time setup

You need **`dist\`**, which holds the programs. If it's empty, build it:
```powershell
.\build.ps1
```
(The build needs MSYS2 at `C:\msys64` with GCC, bzip2, and zlib; graphs also need its gnuplot. See
the runbook. The finished `.exe` files themselves need nothing from MSYS2.)

Each time you open a PowerShell window for SPLAT!, put `dist\` on that window's PATH. This lasts
until the window closes, and nothing on the PC is changed permanently:
```powershell
$env:PATH = "D:\path\to\this\project\dist;$env:PATH"
```
SPLAT! draws graphs by running `gnuplot`. The `gnuplot.cmd` in `dist\` points it to MSYS2's
gnuplot, so this PATH line is what makes the graphs work.

## 2. Get terrain for your area

SPLAT! needs elevation data (SDF files) for every square degree it analyses. The helper works out
which tiles you need, downloads them from public sources (no login), and converts them:
```powershell
.\scripts\get_terrain.ps1 -Lat 38.924 -Lon -77.206 -RadiusMiles 30 -Download -Convert
```
- Use ordinary coordinates, as Google Maps shows them (west longitude is negative).
- Add `-Hd` for the 1-arc-second tiles used by `splat-hd` (about 25 MB per tile).
- Output goes to `data\sdf3\` (or `data\sdf1\` for HD). Downloads are kept in `data\raw\`, so
  nothing is downloaded twice.
- **Missing terrain is silent in SPLAT!:** an area with no SDF file is treated as sea level, with
  only a "assumed as sea-level" line on screen. The helper tells you if any tile failed.

## 3. Describe your sites

Make a working folder, for example `C:\splat-work`, and create one **`.qth`** file per site in
Notepad: name, latitude, longitude, and antenna height above ground (feet, or add `meters`).
**SPLAT! uses WEST longitude as a positive number**, so `-77.205793` from Google Maps is written
`77.205793`:

`k4hta.qth`
```
K4HTA
38.924216
77.205793
240.0
```
Tip: avoid `_` in site names. gnuplot treats it as "subscript", so `Legion_Hall` appears as
Legion<sub>H</sub>all in graph titles (on Linux too). Use letters, digits, and `-`.

For path-loss and coverage calculations, add a **`.lrp`** file with the **same name as the
transmitter** (`k4hta.lrp`). Copy `tests\cases\inputs\k4hta.lrp` and change the frequency,
polarization, and ERP lines. The manual explains the other values.

## 4. Run SPLAT!

In the PowerShell window from step 1, go to your working folder, then run `splat` there. SPLAT!
reads the `.qth`/`.lrp` files from the current folder and writes its output there too. The `-d`
option tells it where the terrain (SDF files from step 2) is. A complete example, with the project
in `D:\ClaudeAI\projects\SPLATWIN` and the sites in `D:\splat-work`:
```powershell
$env:PATH = "D:\ClaudeAI\projects\SPLATWIN\dist;$env:PATH"
cd D:\splat-work
splat -t k4hta -r kk4das -d D:\ClaudeAI\projects\SPLATWIN\data\sdf3
```
`-t k4hta` means "the transmitter is in `k4hta.qth`" and `-r kk4das` means "the receiver is in
`kk4das.qth`" (you leave off `.qth`). The report appears as `K4HTA-to-KK4DAS.txt`.

Other jobs use the same pattern; `...\sdf3` below is short for the full `-d` path above:

| What | Command | Output |
|---|---|---|
| Path report between two sites | `splat -t k4hta -r kk4das -d ...\sdf3` | `K4HTA-to-KK4DAS.txt` |
| + terrain profile and path-loss graphs | add `-p profile.png -l loss.png` | PNG graphs |
| Coverage map (signal strength, 30 mi) | `splat -t k4hta -L 6 -R 30 -d ...\sdf3 -o coverage.ppm -kml` | `coverage.ppm`, `coverage.kml` |
| Same in dBm instead of dBµV/m | add `-dbm` | |
| High resolution (1″ terrain) | use `splat-hd` and `-d ...\sdf1` | |

`-L 6` means "receiver antenna 6 ft above ground". Run `splat` with no arguments for every option.

To avoid typing `-d` each time, put the SDF folder's path on the first line of
`%USERPROFILE%\.splat_path` (e.g. `C:\Users\you\.splat_path`).

## 5. View maps

SPLAT! maps are `.ppm` files, which Windows can't open directly. Convert them:
```powershell
D:\path\to\project\scripts\ppm2png.ps1 coverage.ppm
```
This writes `coverage.png` (checked pixel-for-pixel against the `.ppm`). If there is a
`coverage.kml`, it also writes **`coverage-png.kml`**. Open that one in Google Earth to see the
coverage overlaid on the map. SPLAT!'s own `.kml` points at the `.ppm`, which Google Earth can't
show.

---

## Differences from the Linux SPLAT!
- **Terrain file names use `_` instead of `:`** (`38_39_77_78.sdf`), because Windows forbids `:` in
  file names. SDF files made on Linux must be renamed. Their contents are identical.
- **The settings file is `%USERPROFILE%\.splat_path`** (`$HOME/.splat_path` on Linux; `HOME` is
  still used if you set it).
- **Text reports have Windows (CR-LF) line endings.** The content is identical.
- Unchanged upstream behaviour worth knowing: `-kml` and `-geo` together produce **neither** file.
  Use one or the other.

## For developers
- `build.ps1`: builds everything into `dist\` (`-StdPages`/`-HdPages` set the maximum region;
  defaults are standard 8×8° and HD 4×4°).
- `scripts\verify_toolchain.ps1`: toolchain preflight. `scripts\smoke_stage2.ps1`: quick check on
  real data.
- **Acceptance test against Linux:** `scripts\run_cases.py` then
  `scripts\compare_outputs.py --last-digit` (see the runbook, D12). Every change to the port must
  still say **EQUIVALENT**.
- `splat-windows-runbook.md`: how the project is run, decisions, and history. `reports\`: one report
  per stage.

## License
SPLAT! and the ITWOM 3.0 model are GPL version 2 (`src\COPYING`). If you give the Windows programs
to anyone, include `COPYING` and this source (or a written offer of it).
