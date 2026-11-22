# SPLAT! 1.4.2 — Windows port assessment (2026-11-21)

Source examined: `https://github.com/jmcmellen/splat`, commit `08f06f56470a8361a5609503a6b1cb02f07f48dc`
(2015-08-18, "Merge pull request #1 from jsr38/master"). The content is KD2BD's SPLAT! 1.4.2
(release dated 08-Dec-14). Line numbers below refer to that commit. Shallow clone used for reading
only; nothing was built.

## Inventory

| File | Lines | Role | Libraries |
|---|---|---|---|
| `splat.cpp` | 8,838 | Everything except propagation: terrain loading, path geometry, reports, PPM/KML/GEO writers, gnuplot driver, CLI (`main` at 7770) | libc, libm, **libbz2**, `unistd.h` |
| `itwom3.0.cpp` | 2,863 | ITM (Longley-Rice) and ITWOM 3.0 models | libm, `<complex>` only |
| `fontdata.h` | 343 | Bitmap font for map labels | — |
| `utils/srtm2sdf.c` | ~700 | SRTM `.hgt`/`.bil` → SDF; HD when invoked as `srtm2sdf-hd` | libc, **libbz2**, `fcntl.h`, `unistd.h` |
| `utils/usgs2sdf.c` | ~330 | Old USGS 250k DEM → SDF | libc |
| `utils/citydecoder.c` | — | US Census city files → SPLAT! city list | libc |
| `utils/bearing.c` | — | Distance/bearing between two QTH files | libc, libm, `unistd.h` (unused) |
| `utils/fontdata.c` | — | Regenerates `fontdata.h` from `s.fnt.gz` (developer-only) | **zlib** |
| `configure`, `build`, `install`, `utils/build`, `utils/install`, `utils/postdownload` | — | bash | — |

`foobar.txt` is a stray two-line test file from the fork ("This is a test"); ignore it.

## Build model (Linux)
- `configure` asks for the maximum analysis region (standard: 2–8°, HD: 0–8°) and writes
  `std-parms.h` / `hd-parms.h` with `#define HD_MODE` and `#define MAXPAGES`.
- `build` copies each into `splat.h`, then:
  `g++ -O2 -s -fomit-frame-pointer -ffast-math -pipe -march=x86-64 -mcmodel=medium itwom3.0.cpp splat.cpp -lm -lbz2`
- `splat.cpp:37–101` maps `MAXPAGES` to `ARRAYSIZE` (path samples) and `IPPD` (1200 standard, 3600 HD).

## Portability findings

### P1 — Colons in SDF filenames (blocker)
- Written: `utils/srtm2sdf.c:180,182` (`"%d:%d:%d:%d.sdf"`, `"…-hd.sdf"`), and `usgs2sdf.c`.
- Opened: `splat.cpp:7410–7441` builds the names; `1659, 1888, 2069` parse them with `sscanf("%d:%d:%d:%d")`.
- Also `srtm2sdf.c:431` builds the USGS replacement filename the same way.
- Windows rejects `:` in filenames (NTFS treats it as an alternate-data-stream separator: `fopen`
  either fails or silently writes into a hidden stream of a file named `40`). **Either outcome is
  wrong and must be tested against.** *Confirmed in Stage 0 (MinGW UCRT64): `fopen(…, "wb")` fails,
  and no file or stream is created.* (P2 link failure and P3's 2-of-8-bytes text-mode read were also
  confirmed. See `reports/stage0-scaffold-toolchain.md`.)
- Fix: one separator constant used by all writers and readers; `sscanf` accepts either separator.

### P2 — 2 GB static-data limit
- `splat.cpp:148–157`: `struct dem { …; short data[IPPD][IPPD]; unsigned char mask[IPPD][IPPD]; unsigned char signal[IPPD][IPPD]; } dem[MAXPAGES];`
  Per page: IPPD² × 4 bytes = 5.76 MB (standard) / 51.8 MB (HD). Totals: standard 8×8° ≈ 369 MB;
  HD 4×4° ≈ 830 MB; HD 8×8° ≈ 3.3 GB.
- `splat.cpp:141–144`: `struct path` holds four `double[ARRAYSIZE]` arrays (HD 64 pages: ~7 MB), plus
  `elev[ARRAYSIZE+10]` at 176.
- Linux relies on `-mcmodel=medium`. PE/COFF images have a hard ~2 GB limit, and MinGW/MSVC have no
  equivalent. Any build over ~2 GB fails to link or load.
- Fix: `dem` becomes a pointer allocated once in `main` (`calloc`, so it's zeroed like static
  storage); `dem[indx].data[x][y]` syntax is unchanged. Same for `path` if needed. Fail cleanly with a
  message if allocation fails.

### P3 — Text-mode read of binary SRTM data (silent corruption)
- `utils/srtm2sdf.c:160`: `infile=open(filename, O_RDONLY);` then `read(infile,&buffer,2)` at 168/193.
- On Windows the CRT opens in text mode by default: `0x0D 0x0A` → `0x0A`, and `0x1A` is end-of-file.
  Big-endian 16-bit elevations hit these bytes routinely (e.g. 3338 m = `0x0D0A`, 26 m = `0x001A`).
- Fix: `O_RDONLY | O_BINARY` (with `#ifndef O_BINARY #define O_BINARY 0` for Linux).
- Must-fire test: a tile containing those byte patterns converts identically on Linux and Windows.

### P4 — `HOME` unset
- `splat.cpp:8330–8331` and `utils/srtm2sdf.c:678–680`: `env=getenv("HOME"); snprintf(…"%s/.splat_path",env);`
- NULL `env` with `%s` is undefined behaviour (MSVC crashes; MinGW prints `(null)`).
- Fix: use `HOME`, else `USERPROFILE`, else skip. Path joining at `8349–8357` adds `/`, which Windows
  accepts. Also accept a trailing `\` so the check at 8355 doesn't append `/` after it.

### P5 — `/tmp` and `mkstemp`
- `splat.cpp:2254–2261, 2388` (`LoadUDT`, the `-udt` option): `strcpy(tempname,"/tmp/XXXXXX")`,
  `mkstemp`, then re-`fopen`s by name; `tempname` is only 15 chars.
- Fix: build the name in the Windows temp directory (`GetTempPath`/`_mktemp_s`, larger buffer), or
  use `tmpfile()`. Note the original also leaks the `mkstemp` descriptor; keep behaviour, note it.

### P6 — `srtm2sdf-hd` symlink
- `utils/build` creates `ln -s srtm2sdf srtm2sdf-hd`; `srtm2sdf.c:610` checks `strstr(argv[0],"srtm2sdf-hd")`.
- On Windows `argv[0]` is e.g. `C:\…\srtm2sdf-hd.exe`, which still matches. Copy the exe; no code change.

### P7 — bash scripts
Replace `configure`+`build` with `build.ps1`; `postdownload` (gunzip + `dd conv=unblock` + `usgs2sdf`)
with a PowerShell equivalent if USGS DEM data is wanted. `install` is not ported.

### P8 — gnuplot
- `splat.cpp:5883, 6081, 6455, 7275`: `system("gnuplot splat.gp")`, then `unlink` of `.gp` files unless
  `-gpsav`. The terminal is chosen from the output extension (`png`, `postscript`, …) at `5853` etc.
- Needs `gnuplot.exe` on PATH for the SPLAT! process (or the build sets an absolute path). The PNG
  terminal must exist in the chosen gnuplot build. `cmd.exe` runs `system()`; no quoting issues since
  the command has no arguments with spaces.

### P9 — Text/binary `fopen` modes
- Map/KML/GEO/PPM writers already use `"wb"` (good: PPM P6 data is binary).
- Text-mode writes that will get CRLF on Windows: reports (`report_name "w"`), `splat.lrp` defaults,
  auto-generated `.scf/.lcf/.dcf`, `splat.gp`, `profile.gp` (one of the four), the log file, and
  `usgs2sdf`'s SDF output (`usgs2sdf.c:312`).
- Readers use `"r"`/`"rb"` with `fgets`/`sscanf`; CRLF input should parse, but **this must be
  proven**, because users will move QTH/LRP files between Linux and Windows.
- Recommendation: keep text-mode for human-readable reports (CRLF is right for Notepad), make SDF
  writers binary so SDFs are identical on both OSes, and compare reports ignoring line endings.

### P10 — Floating point
- `-ffast-math` lets GCC reorder maths; glibc vs UCRT/mingw-w64 `libm` give different last-bit
  results for `sin/cos/atan2/exp/log/pow`. Expect small differences in dB values and occasional
  colour-band flips at contour edges. Measure, don't chase (Guardrail 5); the owner ratifies a
  tolerance. Option if needed: build *both* Linux and Windows without `-ffast-math` for the
  comparison only.

### Lesser items
- `#include <unistd.h>` in `splat.cpp` and `bearing.c`: MinGW provides it; MSVC needs a shim.
- `rint`, `rintf`, `snprintf`, `strncasecmp`-style calls: present in MinGW-w64 UCRT.
- `-march=x86-64` is fine; `-s` strip and `-static` recommended.
- Character 176 (`°`) printed in DMS strings (`splat.cpp:305`) shows as `░` in a CP437 console. Cosmetic;
  report files are unaffected. Owner's call whether to touch it (it's a behaviour change).
- No threads, sockets, signals, `fork`, or locale use: nothing else OS-specific found.

### P11, P12 — found in Stage 2 (not predicted here)
- **P11:** `srtm2sdf.c:62` `base=strrchr(filename,'/')` doesn't see `\` separators, so a full
  Windows path is rejected.
- **P12:** `LoadPAT()` (`splat.cpp:1278–1286`) has about 1.5 MB of local `float` arrays, more than
  the Windows 1 MB default stack. It is fixed in the link step.
- Also learned: on Windows `bzlib.h` includes `windows.h` (hence `ARRAYSIZE redefined`), and
  `srtm2sdf` reports "Done!" even when its output file couldn't be created.

## Not changed by the port
ITM/ITWOM maths, CLI options and their meanings, all input and output file formats (except the SDF
filename separator, P1), default parameter values, and report wording.

## Effort estimate
Stage 2 code changes are roughly 50–100 lines in total. Most of the project's effort is in Stage 1
(reference outputs) and Stage 3 (proving equivalence), which is deliberate.
