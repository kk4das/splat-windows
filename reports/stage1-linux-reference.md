# Stage 1 report: Linux reference baseline (2026-11-21)

## What this stage was meant to accomplish
Build the **original, unmodified** SPLAT! on Linux and record exactly what it outputs for a set of
real K4HTA test cases, so the Windows port has an objective answer to match. No Windows code was
written or changed (`git diff upstream-1.4.2 -- src` is still empty).

## Outcome: gate criteria met (pending your review of the case list)
- **New WSL distro `splat-ref`** (Debian 13.5) created with `wsl --install Debian --name splat-ref`.
  The existing `Debian` is untouched, still stopped, and still the default. Location:
  `%LOCALAPPDATA%\wsl\{ebb27f84-…}\ext4.vhdx` (2.86 GB). Remove with `wsl --unregister splat-ref`.
  Toolchain inside: g++ 14.2.0, libbz2-dev 1.0.8, gnuplot 6.0.2, glibc 2.41.
- **Upstream copied into the distro byte-exact** (42/42 files by sha256, plus the `README2` symlink),
  then built with **upstream's own `configure`/`build`/`install`**. Answers: standard **8×8°**
  (MAXPAGES 64) and HD **4×4°** (MAXPAGES 16), per the D5 recommendation. All 7 programs built. The
  only compiler warning is for the obsolete `register` keyword (`splat.cpp:2730`).
- **Terrain (owner-approved download, about 36 MB, no login):** 4 tiles, N38–N39 × W077–W078.
  - 1″ (HD) tiles from the AWS open terrain bucket (`elevation-tiles-prod/skadi`).
  - 3″ (standard) tiles from viewfinderpanoramas `dem3/J18.zip`.
  - All 8 tiles have the correct size (1201² / 3601² samples) and no voids. Elevations run from 0
    to 749 m (Blue Ridge in N39W078). Checksums are in `data/raw/SHA256SUMS`.
- **18 test cases + 2 terrain conversions ran**, all exit 0, in 3 min 18 s (the HD 30-mile map
  takes 147 s of that).
- **Linux is deterministic.** A second full run reproduced **103/103 output files bit-for-bit**,
  including the PNG graphs and PPM maps. Any Windows difference in Stage 3 is therefore real, not
  run-to-run noise.
- **Golden outputs saved and verified.** All 103 files were copied to `data/golden-linux/` (434 MB,
  git-ignored) and re-hashed on the Windows side: 103/103 match the Linux manifest, and a 1-bit
  change is detected (negative fired). The 77 small text outputs (reports, gnuplot data, KML, GEO,
  colour files, logs) and the manifest are tracked in `tests/golden/linux/` (76 KB).

## The test cases (please confirm they cover how you'll use SPLAT!)
Sites: **K4HTA** TX (240 ft, 146.685 MHz, 93 W ERP, vertical), **KK4DAS** RX (30 ft, rounded
location), **Legion Hall** RX (6 ft mobile). Defined in `tests/cases/cases.json`.

| ID | What it exercises | Linux time |
|---|---|---|
| C01 / C02 | `srtm2sdf` / `srtm2sdf-hd` on all 4 tiles (P1 colon names, P3 binary read, P6 HD by name) | 2.9 s / 24.3 s |
| P01 | Point-to-point report K4HTA→KK4DAS, ITWOM | 0.1 s |
| P02 | Same with old ITM model, plus `-erp 50` | 0.1 s |
| P03 | K4HTA→Legion: profile, elevation, height, and path-loss graphs (`-gpsav` keeps the data) | 0.2 s |
| P04 | Normalized height graph, 30 ft ground clutter, 80% Fresnel clearance | 0.1 s |
| P05 | Metric units | 0.1 s |
| P06 | User-defined terrain: a 200 ft block mid-path (P5 `/tmp` temp file) | 0.1 s |
| P07 | Antenna pattern files (.az/.el borrowed from upstream sample data) | 0.1 s |
| P08 | HD point-to-point + profile | 0.4 s |
| M01 | 30 mi line-of-sight coverage map, city labels, KML, `-log` | 1.8 s |
| M02 | 30 mi field-strength map (dBµV/m), city labels, Xastir GEO | 8.5 s |
| M03 | 30 mi received-power map (dBm), smooth contours | 8.4 s |
| M04 / M05 | Alphanumeric output (`-ano`), then re-plotted from it (`-ani`) | 1.9 s / 0.5 s |
| M06 | Plain topographic map | 0.1 s |
| M07 | **HD** 30 mi field-strength map + KML | 147.3 s |
| M08 | Upstream quirk: `-kml` and `-geo` together (see finding 2) | 1.0 s |
| U01 | `bearing` utility | 0.0 s |

**Real-world sanity check (your judgement):** Linux SPLAT! predicts these signals from K4HTA:

| To | Distance | ITWOM path loss | Received signal | Mode |
|---|---|---|---|---|
| KK4DAS (30 ft) | 4.69 mi, bearing 301.8° | 116.7 dB | **−64.9 dBm** (55.7 dBµV/m) | double-horizon diffraction; 3 obstructions within 1.1 mi of your QTH; a 39 ft mast would clear them |
| Legion Hall (6 ft mobile) | 3.73 mi | 94.3 dB | **about −42 dBm** | (see report) |

The old ITM model gives 101.3 dB to KK4DAS (15 dB more optimistic than ITWOM). Does −65 dBm at home
and a very strong signal at the Legion Hall match your experience on the repeater?

## Findings worth noting
1. **The GitHub copy of upstream lost the executable bit on all 9 shell scripts** (they're stored as
   mode 100644), so `./configure` fails with "Permission denied". The build script sets
   `chmod +x` on the Linux working copy only. That's a permissions step, not a source change, and
   it's recorded in `scripts/linux-ref/build_ref.sh`.
2. **Upstream quirk: asking for `-kml` and `-geo` together writes neither file.** The code writes the
   KML only `if (kml && geo==0)` and the GEO only `if (geo && kml==0)` (for example
   `splat.cpp:3942/3956`). My first case list asked for both and got neither. I caught it by checking
   the produced-files list against the options. The cases now test each format separately, and M08
   pins the quirk so the port reproduces it. **Not fixed** (port, don't rewrite). It may be worth
   reporting upstream.
3. **The degree sign is written as byte 176 (Latin-1)** in reports and `bearing` output. In a UTF-8
   terminal it shows as `�`. This is upstream behaviour and will look the same on Windows (it shows
   as `°` only in a Latin-1/CP1252 viewer such as Notepad's ANSI mode).
4. **`-log` records `argv[0]`**, i.e. the full path of the executable (`/opt/splat-bin/splat`). On
   Windows that path will differ, so **Stage 3's comparison must treat that one line as an expected
   difference.** This is the only known path-dependent output.
5. **Every real tile contains thousands of `0x1A` bytes** (for example 47,328 in N38W078 at 1″). P3
   isn't a theoretical risk: an unpatched Windows `srtm2sdf` would stop reading at the first one.
6. **The 1″ tiles contain small negative elevations (down to −42 m)**, which upstream `srtm2sdf`
   replaces by averaging (its default `-n 0` threshold). That is upstream behaviour, reproduced as-is.
7. **The user-defined obstacle (P06) *lowered* path loss** (116.7 → 106.9 dB). A single sharp
   knife-edge can diffract better than several rounded ridges in ITWOM. This is physics and model
   behaviour, not a port issue. Noted because it surprises people.
8. **Two tooling lessons** (recorded in the runbook): (a) commands passed through
   `wsl.exe -- bash -c` from Git Bash got their redirections mangled, and one run printed a false
   "BYTE-EXACT" line with an empty count. All WSL work now runs as script files. (b) Copying files
   with colon names to `/mnt/c` would create strange substitute characters, so the export renames
   `:` to `_` explicitly, and the manifest keeps the original names.

## Declared coverage gaps
- **Not exercised:** `citydecoder` and `-b` boundary files (need US Census files), `usgs2sdf` and
  `postdownload` (need old USGS DEM files), `fontdata` (a developer-only tool), and upstream's own
  `sample_data/wnju-dt` case (needs New Jersey terrain). Also not exercised: `-N`, `-nf`, `-ngs`,
  `-db`, `-R` with metric, and more than one transmitter (`-t a b c`). Recommend: acceptable for
  the port; tell me if you use any of these.
- **PNG graphs and PPM maps are compared by hash only for now.** Stage 3 adds pixel-level
  comparison. gnuplot on Windows may be a different version, so graph PNGs may never be identical.
  The `.gp` data files SPLAT! writes are the real check.
- **Golden copies of the large files live only in `data/golden-linux/`** (git-ignored, 434 MB). The
  tracked manifest proves their integrity, and the `splat-ref` distro can regenerate them.

## Files added
`tests/cases/cases.json`, `tests/cases/inputs/*` (QTH, LRP, AZ/EL, cities, UDT),
`tests/golden/linux/*`, `scripts/run_cases.py`, `scripts/linux-ref/*`, `.gitattributes`
(`-text` on `src/`, `tests/`), and this report.

## Handed to Stage 2
A deterministic oracle for 20 cases, the reusable runner (`run_cases.py`, which runs unchanged on
Windows), the terrain tiles, and the confirmed P1/P3 risks on real data.

## Next stage (needs owner go-ahead): Stage 2, the minimum native port
`build.ps1` plus one commit per fix for P1–P7 and P9. **Decisions needed first:**
- **D3**, the SDF filename separator (recommended `_`).
- **D4**, where `.splat_path` lives (recommended `%USERPROFILE%\.splat_path`).

## Halted for owner review.
