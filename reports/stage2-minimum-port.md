# Stage 2 report: Minimum native port (2026-11-22)

## What this stage was meant to accomplish
Make the smallest set of changes that lets every SPLAT! program build and run correctly on Windows,
one commit per problem, with Linux behaviour untouched. The full 20-case equivalence proof is
Stage 3. This stage ends with a smoke test on real K4HTA data.

## Outcome: gate criteria met
- **`build.ps1`** replaces `configure` + `build` + `utils/build` and builds all 8 programs into
  `dist\`. It uses upstream's flags, minus `-mcmodel=medium` (no Windows equivalent), plus `-static`
  and an 8 MB stack. Defaults are standard 8×8° and HD 4×4° (D5); other sizes via
  `-StdPages`/`-HdPages`. **No new compiler warnings** (the same counts as the unmodified source).
- **The whole port is 110 added and 14 changed lines in 3 files** (`git diff --stat upstream-1.4.2
  -- src`), plus the build script. There are 13 `WIN32PORT` markers. Every behavioural change is
  inside `#ifdef _WIN32`. The two exceptions behave identically on Linux: `O_BINARY` is defined as 0
  there, and `"wb"` equals `"w"` on Linux.
- **Linux is provably unchanged:** the *patched* source, built on Linux with upstream's scripts,
  reproduced **102/102 golden files bit-for-bit**. The 103rd (`cmd.log`) differs only in the
  binary's path, by design.
- **Windows smoke test (`scripts/smoke_stage2.ps1`): 27/27 pass.** It runs with a clean PATH and
  HOME unset, like a normal Windows PC. Details are below.

## The commits (one problem each)
| Commit | Fix | Symptom before the fix (unmodified source on Windows, real data) |
|---|---|---|
| `baf8206` | **P6/P7** `build.ps1`; `srtm2sdf-hd.exe` is a copy, not a symlink | bash scripts don't run on Windows |
| `7392ee5` | **P12 (new)** link with an 8 MB stack (`-Wl,--stack,8388608`), Linux's default | `splat.exe` **crashed on every run** with `0xC00000FD` (stack overflow). `LoadPAT()` keeps about 1.5 MB of local arrays; Windows' default stack is 1 MB |
| `b2f21ec` | **P1** SDF names `38_39_77_78.sdf` (D3) | `srtm2sdf` printed "Writing 38:39:77:78.sdf... **Done!**" but **no file was written**; `splat` then treated every region as sea level |
| `cb9b3ab` | **P3** `O_BINARY` when reading `.hgt` | "Premature EOF detected while reading N38W078.hgt" (text mode stops at the first `0x1A`) |
| `237b0e3` | **P11 (new)** tile name found after the last `\` or `/` | a full Windows path was rejected: "doesn't look like a valid .hgt SRTM filename" |
| `6fb9a8b` | **P4** `%USERPROFILE%\.splat_path` when HOME is unset (D4) | NULL passed to `printf("%s")`; the settings file was never found |
| `e4228da` | **P5** `-udt` temp file in `%TEMP%`, descriptor closed so it can be deleted | no `/tmp`, so `fopen` returned NULL and the next write would crash |
| `a478478` | **P9** `usgs2sdf` writes SDF in binary mode | CR-LF SDF files, not byte-identical to Linux |
| `c960803` | **P2** terrain pages `calloc()`ed on Windows | **HD 8×8° (3.3 GB) didn't build:** the assembler said "value of 00000000c663dba0 too large for field of 4 bytes" |

## Smoke-test evidence (verbatim summary, 2026-11-22)
```
== every program starts (usage banner, clean PATH) ==             8/8 PASS (fontdata's exit -1 is upstream's own exit(-1))
== srtm2sdf: full Windows paths (P11), binary read (P3), '_' names (P1) ==
[PASS] srtm2sdf N38W077.hgt -> 38_39_76_77.sdf      identical to Linux (3355 KB)
[PASS] srtm2sdf N38W078.hgt -> 38_39_77_78.sdf      identical to Linux (4541 KB)
[PASS] srtm2sdf N39W077.hgt -> 39_40_76_77.sdf      identical to Linux (4722 KB)
[PASS] srtm2sdf N39W078.hgt -> 39_40_77_78.sdf      identical to Linux (5509 KB)
[PASS] srtm2sdf-hd N38W078.hgt -> 38_39_77_78-hd.sdf (P6 HD by name) identical to Linux (41016 KB)
[PASS] no colon-named or stray files written
== splat point-to-point (P12 stack, P1 SDF loading, P2 heap) ==
[PASS] P01 loaded real terrain (not sea level)      Loading "../sdf3/38_39_77_78.sdf" into page 1... Done!
[PASS] P01 report K4HTA-to-KK4DAS.txt               identical to Linux apart from CR-LF line endings
[PASS] negative: 0.01 dB change still detected      CRLF-tolerant comparison still fires on a real change
== %USERPROFILE%\.splat_path, with HOME unset (P4) ==
[PASS] report via .splat_path                       identical to Linux apart from CR-LF line endings
[PASS] negative: no .splat_path -> sea level (fires)
== user-defined terrain temp file (P5) ==
[PASS] P06 report (with 200 ft obstacle)            identical to Linux apart from CR-LF line endings
[PASS] temp file created in %TEMP% and removed      0 file(s) left in TEMP
== HD point-to-point (heap terrain pages, P2) ==
[PASS] P08 HD report                                identical to Linux apart from CR-LF line endings
-- 27/27 passed
```
P2 extra check: with `-HdPages 64` (8×8°, 3.3 GB), the patched `splat-hd.exe` builds, runs, and gives
the same HD report as Linux. The unmodified source fails to assemble.

## Findings worth noting
1. **Two problems the plan didn't predict, both found by running the real programs:**
   - **P12 (stack overflow).** Without it, *nothing* runs.
   - **P11 (backslash paths).** Without it, `srtm2sdf` rejects any full Windows path.
   The ranked list in the port assessment was right about everything else.
2. **Silent failure modes are upstream behaviour, and this is why Stage 3 matters.** `srtm2sdf`
   reports "Done!" when its write failed, and `splat` quietly treats a missing SDF as sea level. An
   incomplete port would produce plausible but wrong coverage maps without any error.
3. **Every numeric result matches Linux exactly.** Path loss, field strength, and obstruction lists
   are identical to the last digit. This holds despite `-ffast-math` and a different maths library
   (UCRT vs glibc), at least for these cases. P10 (floating-point drift) hasn't shown up yet. The
   maps in Stage 3 are the bigger test.
4. **Reports use CR-LF line endings on Windows.** Upstream writes reports in text mode, so Windows
   adds CR before each LF. That's what the port assessment recommended keeping, because Notepad and
   other Windows tools expect it. **Decision D11 below.**
5. **Why `ARRAYSIZE redefined` appears:** on Windows, `bzlib.h` includes `windows.h`, which defines
   its own `ARRAYSIZE`. SPLAT!'s definition comes later and wins, so the value is correct. The
   warning is left as-is (no change needed). Recorded so nobody "fixes" it by reordering includes.
6. **Not every upstream oddity is worth fixing.** If both `HOME` and `USERPROFILE` are unset (never on
   a normal Windows PC), MinGW prints `(null)/.splat_path`, which simply isn't found. That is
   harmless, and left as is.

## Declared coverage gaps
- **P5 before-state not demonstrated:** the unmodified `-udt` crash was predicted from the code, not
  run (the baseline run crashed earlier on P12).
- **P2's "out of memory" message** was not triggered (that would need a machine without 3.3 GB free).
- **`usgs2sdf` (P1/P9) and `citydecoder`** were only checked to start, because there are no
  USGS/Census files to feed them (the same gap as Stage 1).
- **gnuplot graphs and PPM/KML maps were not run on Windows yet.** That's Stage 3 (maps) and Stage 4
  (gnuplot).

## Decision for the owner
| # | Decision | Recommendation |
|---|---|---|
| **D11** | **Line endings of text outputs on Windows** (reports, site reports, KML, `.lrp`/`.scf` defaults, gnuplot scripts): keep CR-LF (upstream's text-mode behaviour, Notepad-friendly) **or** force LF so they're byte-identical to Linux (a small extra change) | **Keep CR-LF.** It's what a Windows tool should write, and Stage 3 compares text with line endings normalised. |

## Files added or changed
`build.ps1` (new), `src/splat.cpp`, `src/utils/srtm2sdf.c`, `src/utils/usgs2sdf.c`,
`scripts/smoke_stage2.ps1` (new), `scripts/linux-ref/linux_patched.sh` (new), and this report.

## Next stage (needs owner go-ahead): Stage 3, equivalence with Linux
Run all 20 cases on Windows with `run_cases.py` (a project `.venv` from system Python 3.10, stdlib
only). Write `compare_outputs.py`: text with numeric tolerance and line endings normalised, SDFs
value by value, PPM maps pixel by pixel, and KML/GEO structurally. Prove each comparison fires on a
deliberately broken file. Then measure any floating-point differences, which **you ratify** as the
tolerance. gnuplot isn't installed on Windows yet, so the graph cases (P03/P04/P05/P06/P08) will
compare the `.gp` data files SPLAT! writes. The PNGs wait for Stage 4 (D8).

## Halted for owner review.
