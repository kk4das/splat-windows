# Stage 3 report: Equivalence with Linux (2026-11-22)

## What this stage was meant to accomplish
Run all 20 test cases on Windows and show that the answers match the unmodified Linux SPLAT!, using
a comparison that is itself proven to catch differences. Measure any floating-point drift (P10) and
bring the tolerance to you to ratify. No source code changed in this stage.

## Outcome: gate criteria met, pending your ratification of the tolerance (D12)
- **Windows run:** all 20 cases (18 + 2 conversions) exit with the same codes as Linux. The build is
  `a3022be`, run with a Windows-only PATH and HOME unset, in 278 s.
- **Comparison: 103 Linux output files checked.**

  | Result | Files | What |
  |---|---|---|
  | **Byte-identical** | **26** | **all 9 PPM maps** (including the 155 MB HD 30-mile map), **all 8 SDF terrain files**, both KML files, the GEO file, and 6 gnuplot data files |
  | **Identical apart from ratified differences** | **61** | every report, site report, colour file, log, and console output (see "Expected differences") |
  | **Within the proposed tolerance** | **8** | 7 gnuplot data files + `loss.ano`: last-printed-digit differences only (see below) |
  | **Deferred to Stage 4** | **8** | gnuplot PNG graphs (gnuplot isn't installed on Windows yet, D8) |
  | **Different / missing / extra** | **0** | |

  **Result: EQUIVALENT** with the proposed tolerance. Without it there are 8 problems, all of them
  last-digit differences (saved in `tests/results/stage3-compare-exact.txt`).
- **The comparer is proven to fire** (Guardrail 6), 16/16 in `tests/results/stage3-selftest.txt`.
  It catches a 0.01 dB change in a report, one changed word, 10 changed pixels out of 5.76 million,
  one elevation off by 1 m, one KML coordinate digit, one gnuplot value, a missing file, an extra
  file, a changed exit code, and real console changes. It also shows the proposed tolerance accepts a
  change of 1 in the last digit and **rejects** 2, and that it doesn't apply to reports at all.

## Floating-point drift (P10): what was measured
The drift is confined to the **last printed digit of geometry values**. **No path-loss,
field-strength, signal-level, or elevation value differs anywhere**, and every map pixel matches.

| File(s) | Values that differ | Size | What they are |
|---|---|---|---|
| `profile.gp`, `curvature.gp`, `clutter.gp` (P03, P04, P06, P08) | 1–4 per file | ±0.000001 (1 in the 6th decimal) | distance/height points for the gnuplot graphs |
| `M04/loss.ano` | 20 of about 610,000 numbers | 19 longitudes ±0.0000001° (about 1 cm); 1 azimuth 180.491° vs 180.490° | the point coordinates and bearings of the alphanumeric output; **none of its path-loss values** |

**Cause:** glibc (Linux) and UCRT (Windows) maths functions (`sin`, `cos`, `atan2`, ...) can differ in
the last bit. Combined with `-ffast-math`, that occasionally flips a rounded printed digit. This is
expected and harmless.

## Decision for the owner
| # | Decision | Recommendation |
|---|---|---|
| **D12** | **Numeric tolerance for Windows vs Linux** | **Exact everywhere, except gnuplot data (`.gp`) and alphanumeric output (`.ano`) files, where a number may differ by 1 unit in its last printed digit.** That is the narrowest rule that passes: reports, KML/GEO, maps, and terrain stay exact. |

## Expected differences (each named, justified, and counted by the comparer)
| Name | Files | Why |
|---|---|---|
| `crlf` | 61 | CR-LF line endings on Windows text output (your D11) |
| `sdfname` | 24 | console output says `38_39_77_78.sdf` where Linux says `38:39:77:78.sdf` (your D3) |
| `inputpath` | 8 | `srtm2sdf` echoes the tile path it was given; the runner gives each machine's own path |
| `nognuplot` | 13 | the "'gnuplot' is not recognized" message, until gnuplot is installed (Stage 4) |
| `argv0` | 1 | `-log` records the program's own path (Stage 1 finding 4) |

## Findings worth noting
1. **The comparer's first self-test passed for the wrong reason, and I caught it.** Nine of ten
   "detected" mutations were flagged because of an unrelated difference elsewhere in the run, not
   the change that had been made. The self-test now requires each change to be caught **in the
   file that was changed**, and the clean baseline to have zero problems. Lesson: *a detector test
   must check what it detected, not just that it said "different".*
2. **Escaping layers corrupted a script twice.** Backreferences such as `\1` passed through
   bash → heredoc → Python and became control bytes, which display as `___`. Both times the
   self-test exposed it (the clean baseline failed). Script edits containing backslashes are now
   built with `chr(92)` or written as whole files, never through shell quoting.
3. **HD terrain conversion is slower on Windows:** `srtm2sdf-hd` took 87.5 s for 4 tiles vs 24.3 s
   on Linux. Standard conversion took 8.7 s vs 2.9 s. SPLAT! itself runs at the same speed: the HD
   30-mile map took 154.7 s vs 147.3 s. The likely cause is slower formatted-text output in the
   Windows C runtime and/or Defender scanning the 40 MB files as they're written. Results are
   identical, conversion is a one-time job per tile, and this wasn't investigated further (a
   Stage 6 item if it matters to you).
4. **The `-kml` + `-geo` quirk (M08) behaves the same on Windows:** neither file is written. The
   EXTRA-file check would catch a port that "fixed" it.

## Declared coverage gaps
- **Graph PNGs** are deferred until gnuplot is installed (Stage 4, D8). The data behind every graph
  (`.gp` files) is compared above.
- **No case uses more than one transmitter, boundary files, or USGS data** (the same gaps as
  Stage 1).
- **The tolerance is measured on these 20 cases.** Other sites could flip a last digit in a
  2-decimal report value, which would show up as a DIFF. That is the conservative failure direction.

## Files added
`scripts/compare_outputs.py`, `tests/results/stage3-*` (Windows manifest, exact and tolerance
comparisons, self-test output), and this report. The `.venv\` is git-ignored. `work\windows\`
(434 MB of run outputs) is git-ignored.

## Next stage (needs owner go-ahead): Stage 4, graphs, data pipeline, helpers
Install gnuplot (D8: the MSYS2 package is recommended, about 50 MB, nothing on the system PATH), make
SPLAT! find it, and compare every graph with the Linux PNGs side by side (your eyes). Add
`get_terrain.ps1` (optional) and `postdownload.ps1` (if you use USGS DEM data), document a PPM
viewer (D9), and finish with you running one job end to end from the README alone.

## Halted for owner review.
