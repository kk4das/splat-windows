# SPLAT! for Windows — Runbook

A native Windows build of **SPLAT!** (KD2BD's terrestrial RF path and terrain analysis tool, v1.4.2),
ported from the Linux source at `https://github.com/jmcmellen/splat`. The result is a set of `.exe`
files (`splat`, `splat-hd`, `srtm2sdf`, `srtm2sdf-hd`, `citydecoder`, `bearing`, `usgs2sdf`) that run
from PowerShell or `cmd` and **produce the same answers as the Linux build** for the same inputs.

> **Why this project (the real objectives — shapes every decision):**
> 1. **Run SPLAT! on Windows without a Linux box or VM**: point-to-point path reports, terrain and
>    path-loss plots, and coverage maps (PPM/KML) for the club's and the owner's stations.
> 2. **Hands-on practice with semi-autonomous agent development**, in the same working style as the
>    Flex Radio and data-analysis projects (owner directs, Code builds, human-in-the-loop gates).
>
> **Working style:** owner directs and judges; Code writes, runs, verifies, and commits. The owner does
> not want to hand-write code or scripts. Every stage opens with a **plain-language statement of what
> it will accomplish**, then the evidence. Meta-observations (where the agent needed catching, where
> the upstream docs are wrong or out of date) are first-class outputs.

> **OPERATING GUARDRAILS (owner-set; bind every stage and every session).** Carried over from the
> Flex Radio runbook.
>
> 1. **No autonomous stage advancement.** Finishing a stage means producing its report and
>    **halting for owner review.** Crossing a gate is always the owner's call.
> 2. **No autonomous scope decisions.** Code may *recommend* deferring or dropping something, with
>    reasons. Only the owner defers, and a deferral isn't real until it is recorded here.
> 3. **Toolchain verified at both ends of every stage.** Prove each tool on real output (a compiled
>    `.exe` that runs and prints its banner, a real SDF file, a real plot), not an exit code. A broken
>    toolchain means halt and escalate. It never means proceed anyway.
> 4. **Bounded attempts, then escalate (the anti-thrash rule).** On a blocker, make at most **2–3
>    distinct** attempts, each testing a different hypothesis. If attempts stop producing new
>    information, stop and report what was tried, what each attempt showed, and the current best
>    hypothesis.
> 5. **Churn is a diagnostic signal.** If attempts repeat without converging (for example, chasing
>    floating-point diffs by flipping compiler flags), question the approach, not just the parameters.
> 6. **A "matches Linux" result is only as good as the comparison's coverage.** Every equivalence
>    check must be shown to **fire**: before trusting "Windows output = Linux output", prove the
>    compare script reports a mismatch when one really exists (a deliberately altered report line, a
>    changed pixel, a shifted elevation). Untested checks are not checks.

> **PROJECT-SPECIFIC HARD CONSTRAINTS (owner-set).**
> - **Port, don't rewrite.** The goal is the same program on a new OS. Every source change must be
>   needed for Windows, kept as small as possible, and marked in the code with a
>   `/* WIN32PORT: <why> */` comment. Behaviour changes (new options, new formats, "improvements")
>   need owner approval and are recorded here as such.
> - **Upstream is preserved verbatim.** The first commit is the unmodified upstream tree (commit
>   `08f06f5`, 2015-08-18). Every port change is a later, reviewable commit, so `git diff upstream`
>   always shows exactly what the port changed.
> - **The Linux build is the oracle.** "Correct" means "matches unmodified SPLAT! 1.4.2 built on
>   Linux for the same inputs", within a tolerance the owner ratifies (Stage 3). It does not mean
>   "looks plausible".
> - **Don't break existing environments.** Do not install into or modify the system Python
>   (`C:\Users\dsoul\AppData\Local\Programs\Python\Python310`), the `E:\work\*-venv` venvs, Java, the
>   .NET runtime, Docker, or the existing **Debian** WSL distro. Any new global install (MSYS2, Visual
>   Studio Build Tools, gnuplot, a new WSL distro) needs **explicit owner approval first**, and is
>   recorded in §Environment notes with its location and how to remove it.
> - **Nothing on the system PATH without approval.** Build tools are invoked by full path from the
>   project's scripts. The finished SPLAT! binaries live in the project's `dist\` folder until the
>   owner decides where they go.
> - **GPLv2 compliance.** SPLAT! and ITWOM 3.0 are GPLv2. Any copy of the Windows binaries given to
>   anyone else ships with `COPYING` and the matching source (or a written offer). The original
>   authorship and copyright headers are never removed.
> - **Code does not type passwords.** If a terrain-data site (NASA Earthdata, USGS EarthExplorer)
>   needs a login, the owner signs in in the browser. Downloaded terrain tiles are data, not code.
> - **Terrain data and large outputs stay out of git.** `data\` (HGT/SDF tiles) and `work\` (run
>   outputs) are git-ignored. Small, redistributable golden fixtures are the exception (Stage 1).

---

## Environment notes (this machine, surveyed 2026-11-21)

| Tool | Location / version | Use here |
|---|---|---|
| C/C++ compiler | Before Stage 0: **none** (no gcc, clang, MSVC, or Visual Studio). **Installed 2026-11-21 (D1): MSYS2 at `C:\msys64`**, UCRT64 GCC 16.2.0, binutils 2.47, bzip2 1.0.8, zlib 1.4 (static libs). 895 MB. **Not on any PATH.** | Call it by full path: `C:\msys64\ucrt64\bin\g++.exe`. Remove with `winget uninstall MSYS2.MSYS2`. The first `pacman -Syu` kills its own shell (exit 1, no output). That's normal; run it again. |
| Git line endings | **System-wide `core.autocrlf=true`** (Git for Windows default) | This repo sets `core.autocrlf=false` locally. Any git command run *outside* this repo (for example `git archive` from another clone) converts to CRLF. Stage 0 caught this corrupting the first import. |
| make / CMake / Ninja | not installed | Not needed for the recommended path (a PowerShell build script calls the compiler directly, like upstream's `build` shell script). |
| Git | 2.34.0 (`C:\Program Files\Git`), global identity `kk4das` | Local repo. Git's `mingw64\bin` has a `bzip2.exe` **tool**, but no compiler, headers, or `libbz2` to link against. |
| Python | 3.10.6 (system), `py` launcher | Allowed for the compare/test scripts, in a project `.venv\` only. Never `pip install` into the system Python. |
| VS Code | 1.146.0 (`%LOCALAPPDATA%\Programs\Microsoft VS Code`), with the C/C++ extension pack (`ms-vscode.cpptools`), CMake Tools, and Remote-WSL | Optional editor/debugger for the port. It's an editor, not a compiler, and works with MSYS2 UCRT64 GCC: point IntelliSense at `C:\msys64\ucrt64\bin\g++.exe` (mode `windows-gcc-x64`) and debug with MSYS2's `gdb`. Remote-WSL can open the Linux reference tree. A tracked `.vscode/` config is optional; the build never depends on VS Code. |
| winget | present | Proposed installer for MSYS2 (with approval). |
| WSL | `Debian` (stopped, default), `docker-desktop` (stopped). **Added 2026-11-21: `splat-ref`** (Debian 13.5, `%LOCALAPPDATA%\wsl\{ebb27f84-3027-43a7-b551-44aade80d4a1}`, 2.86 GB) with g++ 14.2, libbz2-dev 1.0.8, gnuplot 6.0.2; upstream built in `/opt/splat`, reference runs in `/opt/ref` | The Linux oracle. **Never modify the existing `Debian`.** Run work in `splat-ref` as script files (`wsl -d splat-ref -u root -- bash /mnt/c/.../x.sh`); `bash -c` strings through `wsl.exe` get mangled. A harmless "Failed to detect nested virtualization" warning prints on every call. Remove with `wsl --unregister splat-ref`. |
| gnuplot | **Installed 2026-11-22 (D8): MSYS2 `mingw-w64-ucrt-x86_64-gnuplot` 6.0.6** (about 1 GB with its GUI dependencies, inside `C:\msys64`; not on PATH) | `dist\gnuplot.cmd` (written by `build.ps1`) points SPLAT! at it; put `dist\` on PATH for the session. fontconfig's install hook warns "failed to write cache"; that's harmless (a per-user cache exists). |
| Image viewer for PPM | none needed (D9) | `scripts\ppm2png.ps1` converts maps to PNG with .NET, plus a Google Earth `*-png.kml` (Pro) and `.kmz` (web). Google Earth Pro is **not** installed; the owner uses Google Earth on the web. |
| Hardware | Ryzen 9 5950X (16C/32T), 64 GB RAM | Enough for the largest SPLAT! HD build (8×8°, about 3.3 GB). |

**Claude-app PATH caveat (inherited lesson):** the desktop app captures PATH at launch. If a newly
installed tool "isn't found," relaunch the app or use the full path. The build script always uses full
paths for this reason.

### Version control & runbook handling (git)
Same rules as the Flex Radio project:
- **Git versions how the project works** (port source, scripts, runbook, reports, prompts, small
  golden fixtures). It never versions terrain data, run outputs, or build products (see `.gitignore`).
- **Code owns all git operations.** The owner asks for outcomes in plain English ("undo the last
  change", "what changed in Stage 2", "show me everything the port changed from upstream"). Commit at
  each gate and after each verified change.
- **Tag `upstream-1.4.2`** on the verbatim import commit. `git diff upstream-1.4.2 -- src/` is the
  complete port patch at any time.
- **Remote: local only until D6 is decided.**
- **The committed on-disk runbook is canonical.** Make edits as targeted insertions or replacements,
  never a whole-file paste-over.
- **Reports are immutable records; the runbook is the mutable head.** A stage report says what was
  known when it ran. Corrections and ratifications go here.
- **Prompts live in `prompts/`** and are committed.

### Repository layout (proposed)
```
splat-windows-runbook.md   ← this file (canonical, mutable head)
README.md                  ← short how-to-use for whoever runs the Windows build
src/                       ← SPLAT! source: upstream import, then port commits
  splat.cpp itwom3.0.cpp fontdata.h utils/*.c docs/ sample_data/ (upstream layout kept)
  win32/                   ← small port helpers (compat shim header), if needed
build.ps1                  ← replaces configure + build: picks MAXPAGES, compiles all exes
scripts/                   ← verify_toolchain.ps1, compare_outputs.py, data helpers (postdownload.ps1)
tests/golden/              ← Linux-reference outputs for the sample cases (small, tracked)
tests/cases/               ← the test-case definitions (QTH/LRP files + command lines)
reference/                 ← port-assessment.md and other notes (tracked)
prompts/                   ← Code-session prompts (tracked)
reports/                   ← stage reports (immutable once written)
data/  work/  build/  dist/  ← terrain tiles, run outputs, objects, finished exes (ignored)
```

---

## Research summary — what SPLAT! is and what the port must change

Full findings, with file and line references, are in `reference/port-assessment.md`. In short:

- **Size and shape:** about 12,000 lines in three files: `splat.cpp` (8,838 lines, one `main`),
  `itwom3.0.cpp` (2,863 lines, the ITM/ITWOM propagation models, pure maths), and `fontdata.h`. Plus
  five small C utilities in `utils/`. It is a **command-line program**: no GUI, no threads, no
  network. That makes a native port very tractable.
- **Dependencies:** the C/C++ standard library, `libbz2` (reads `.sdf.bz2` terrain files), and `zlib`
  (only for the `fontdata` utility). It calls **gnuplot** as an external program for graphs.
- **How it is built on Linux:** `configure` asks two questions (maximum analysis region for standard
  and HD mode) and writes `#define MAXPAGES` into `splat.h`. `build` compiles `splat` and `splat-hd`
  with `g++ -O2 -ffast-math -march=x86-64 -mcmodel=medium`. These are bash scripts and won't run on
  Windows as-is.

### Things that will break on Windows (ranked)

| # | Problem | Why it matters | Planned fix |
|---|---|---|---|
| P1 | **SDF terrain filenames contain colons** (`40:41:74:75.sdf`, `…-hd.sdf`). Windows forbids `:` in filenames. | `srtm2sdf` can't write them and `splat` can't open them. **Nothing works without this fix.** | Use a Windows-legal separator in the names that are written and opened (decision D3), and keep the parsing tolerant. Provide a rename script for SDF files made on Linux. |
| P2 | **The terrain grid is one huge static array** (`dem[MAXPAGES]`, about 52 MB per page in HD mode; 3.3 GB at 8×8° HD). Linux uses `-mcmodel=medium`, which doesn't exist on Windows. Windows `.exe` images can't hold static data over about 2 GB. | Large HD builds won't link. | Allocate `dem` (and the big `path` arrays) on the heap at start-up. Same memory, same indexing, no behaviour change. |
| P3 | **`srtm2sdf` reads `.hgt` with `open(..., O_RDONLY)`**, which is *text mode* on Windows. | Bytes `0x1A` and `0x0D 0x0A` in the binary elevation data get mangled. **Silent wrong terrain**, the worst kind of bug. | Add `O_BINARY`. Guardrail 6 test: a tile whose data contains those byte values must convert identically to Linux. |
| P4 | **`$HOME/.splat_path`**: `HOME` is usually unset on Windows, so `getenv` returns NULL and is passed to `snprintf("%s")`. | A crash or garbage path on every run that doesn't use `-d`. | Fall back to `%USERPROFILE%` when `HOME` is unset (location decision D4). |
| P5 | **Temp file `/tmp/XXXXXX` via `mkstemp`** (the `-udt` user-terrain option). | `/tmp` doesn't exist; `mkstemp` isn't in MSVC. | Use the Windows temp directory. |
| P6 | **`srtm2sdf-hd` is a symlink**; the program picks HD mode from its own name (`argv[0]`). | Symlinks need admin or Developer Mode on Windows. | Build a second `.exe` named `srtm2sdf-hd.exe` (a copy). The name check still works; no code change. |
| P7 | **Shell scripts** (`configure`, `build`, `install`, `utils/build`, `utils/postdownload`). | Bash-only. | `build.ps1` replaces `configure`+`build`. `postdownload` becomes `postdownload.ps1` (it only decompresses and reblocks a file). There's no `install` step until D10 decides where binaries go. |
| P8 | **Graphs call `system("gnuplot splat.gp")`.** | Needs gnuplot on PATH, and the PNG terminal must exist in the Windows gnuplot build. | Install gnuplot (D8). Probably no code change. Verify each graph type renders. |
| P9 | **Text vs binary mode** in a few `fopen` calls (for example `usgs2sdf` writes SDF with `"w"`, giving CRLF line endings). | Output files differ from Linux byte-for-byte, though SPLAT! may still read them fine. | Use binary mode where Linux and Windows files should be identical; prove it with the compare script. |
| P11 | *(found in Stage 2)* **`srtm2sdf` finds the tile name after the last `/` only.** | A full Windows path (`C:\data\N38W078.hgt`) is rejected as "not a valid .hgt filename". | On Windows, use the last `\` or `/`. Fixed (`237b0e3`). |
| P12 | *(found in Stage 2)* **Stack overflow:** `LoadPAT()` keeps about 1.5 MB of local arrays; the Windows default stack is 1 MB (Linux 8 MB). | `splat.exe` crashes (`0xC00000FD`) on every run. | Link with `-Wl,--stack,8388608` (no source change). Fixed (`7392ee5`). |
| P10 | **`-ffast-math` + a different maths library** (MinGW/UCRT `libm` vs glibc). | Path-loss numbers may differ from Linux in the last digits. | Measure in Stage 3; the owner ratifies a tolerance (for example ±0.01 dB in reports, ≤0.1% of map pixels differing by one colour band). No flag-chasing (Guardrail 5). |

What does **not** need to change: the propagation models (`itwom3.0.cpp` is portable C++; it even
notes that "Windows dll-isms" were removed upstream), the PPM/KML/report writers (already binary
mode), the command-line options, and the file formats (QTH, LRP, AZ/EL, SCF/DCF/LCF, city and
boundary files).

---

## Recommended toolchain (owner decision D1)

**Recommended: MSYS2 with the UCRT64 environment (GCC/MinGW-w64).**
- It is the same compiler family as the Linux build, so the source changes stay minimal and
  `-O2 -ffast-math` keep their meaning.
- `bzip2`, `zlib`, and **gnuplot** all come from its package manager (`pacman`), so one install
  covers every dependency.
- With `-static` the finished `.exe` files need **no DLLs**, so they can be copied to any Windows 10/11
  PC (for example, a club member's) and just run.
- It's self-contained in `C:\msys64` (about 1–2 GB), doesn't touch the system PATH unless asked, and
  uninstalls cleanly. Install: `winget install MSYS2.MSYS2`, then
  `pacman -S mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-bzip2 mingw-w64-ucrt-x86_64-zlib`
  (and `…-gnuplot` if D8 chooses it).

**Alternative: Visual Studio 2022 Build Tools (MSVC) + vcpkg + CMake.** It's the "most native"
option and you'd want it for a later Windows GUI. But it is a 6–8 GB install, needs more source
changes (`unistd.h`, `rint`, `snprintf` quirks, `mkstemp`), and it introduces a second compiler whose
floating-point differences from Linux are larger. Recommended only as an optional Stage 6.

**For the Linux reference (decision D2): a new, disposable WSL distro** (for example a second Debian
imported as `splat-ref`), so the existing `Debian` distro is untouched. Install `g++ libbz2-dev`
there, build **unmodified** upstream, and generate the golden outputs. Remove the distro when the
project ends (`wsl --unregister splat-ref`).

---

## Plan — stages and gates (each ends in a report + HALT for owner review)

### Stage 0 — Scaffold & toolchain (DONE 2026-11-21; owner approved)
*Plain language:* set up the project, bring in the original SPLAT! source untouched, install the
compiler, and prove it can build a trivial Windows program, without touching anything else on the PC.
- `git init`; commit the **verbatim upstream tree** into `src/`; tag `upstream-1.4.2`. Record the
  upstream commit hash and URL in the report.
- Install the approved toolchain (D1). Record its location, version, and uninstall steps here.
- `scripts/verify_toolchain.ps1`: proves the compiler compiles and runs a hello-world, **links against
  `libbz2` and `zlib` statically** (a test program that compresses and decompresses a buffer), and that
  the resulting `.exe` has no unexpected DLL dependencies (checked with `objdump -p`).
- **Detector-fires tests:** wrong compiler path → FAIL; missing `bzlib.h` → FAIL; a dynamically linked
  test `.exe` → flagged by the DLL check.
- **Gate:** report with real output; system Python and the existing WSL distros are unchanged.

### Stage 1 — Linux reference baseline (the oracle) (DONE 2026-11-21; owner approved)
*Plain language:* build the original program on Linux and record exactly what it outputs, so we have
something objective to compare the Windows version against.
- In the reference WSL distro (D2), build **unmodified** upstream with its own `configure`/`build`
  (standard 3×3°, HD 1×1°; more if D5 asks).
- Get real terrain: at least **two SRTM tiles** (SRTM-3 for standard mode, SRTM-1 for HD) covering a
  real path the owner cares about (D7, for example the club's station to a member's QTH), plus the
  bundled `sample_data/wnju-dt` site.
- Define **8–12 test cases** in `tests/cases/` covering: `srtm2sdf` and `srtm2sdf-hd` conversion,
  a point-to-point report (`-t -r`), ITM vs ITWOM (`-olditm`), each graph type (`-p -e -h -H -l`),
  LOS coverage (`-c`), path loss (`-L`) with `-dbm`, `-kml` and `-geo` output, `-ano` alphanumeric
  output, metric mode, and `-udt` user terrain.
- Run them on Linux and save the outputs to `tests/golden/` (small, redistributable ones are tracked;
  large maps are stored as checksums plus a downscaled copy).
- **Gate:** report listing each case, its command line, and its golden outputs; owner confirms the
  case list covers how they'll actually use SPLAT!.

### Stage 2 — Minimum native port (make it compile and run) (DONE 2026-11-22; owner approved)
*Plain language:* make the smallest set of changes that lets every SPLAT! program build and run on
Windows.
- `build.ps1`: replaces `configure` + `build`. Takes `-StdPages 9 -HdPages 1` (or the D5 choices),
  writes `splat.h`, and compiles `splat.exe`, `splat-hd.exe`, and all utilities into `dist\` with
  upstream's optimisation flags (minus `-mcmodel=medium`, plus `-static`).
- Fix P1–P7 and P9, **one commit per problem**, each with `WIN32PORT` comments and a one-paragraph
  "why" in the commit message.
- Smoke test: every `.exe` prints its usage banner; `srtm2sdf` converts a real tile; `splat` loads it
  and writes a point-to-point report.
- **Gate:** report with the full port diff stat (`git diff --stat upstream-1.4.2`), the smoke-test
  output, and anything that surprised us.

### Stage 3 — Equivalence with Linux (prove it's the same program) (DONE 2026-11-22; owner approved)
*Plain language:* run every test case on Windows and show the answers match the Linux originals.
- `scripts/compare_outputs.py` (project `.venv`): text reports compared line-by-line with numeric
  tolerance; SDF files compared value-by-value; PPM maps compared pixel-by-pixel (count and location
  of differing pixels); KML/GEO compared structurally.
- **Detector-fires tests (Guardrail 6):** feed the comparer a deliberately altered report (one dB
  value changed), a PPM with 10 changed pixels, and an SDF with one changed elevation. Each must be
  reported as a mismatch.
- Measure floating-point differences (P10). If any exist, report their size and location. **The
  owner ratifies the tolerance**; Code doesn't pick one silently.
- **Gate:** a pass/fail table for every case, and the ratified tolerance recorded here.

### Stage 4 — Graphs, data pipeline, and helpers (DONE 2026-11-22, awaiting owner review + owner's end-to-end run)
*Plain language:* make the everyday workflow work end-to-end on Windows: get terrain, convert it,
make plots.
- Install and verify gnuplot (D8); render every graph type on Windows and compare it visually to
  Linux's (owner eyeballs both side by side).
- `scripts/get_terrain.ps1` (optional, D7): given a lat/lon box, lists the needed SRTM tiles, and after
  the owner downloads them, unzips them and runs `srtm2sdf` into `data\sdf\`.
- `postdownload.ps1` replaces the bash `postdownload` (for old USGS DEM files; may be deferred if the
  owner never uses USGS DEM data).
- Image viewing (D9): document a free PPM viewer, or (owner approval needed, since it's a new feature)
  add a post-step that converts PPM to PNG.
- **Gate:** owner runs one real job end to end from the README instructions alone.

### Stage 5 — Packaging & hand-off ⬅ next, needs D10
*Plain language:* produce a zip someone else can unpack and use.
- `dist\splat-1.4.2-win64.zip`: all `.exe`s, the English docs (text and PDF), sample data,
  `COPYING`, a Windows `README`, and the port's source (GPL).
- Test the zip on a clean account or PC: unzip, run the sample case, get the golden answer. No
  compiler or MSYS2 present.
- Decide where the binaries live on this PC (D10) and whether to add them to PATH (owner's call).
- **Gate:** clean-machine test report; README reviewed by the owner.

### Stage 6 — (optional, owner's call)
- MSVC/CMake build as a second toolchain cross-check.
- Runtime-selectable analysis region (heap allocation from P2 makes this easy) instead of separate
  compile-time builds.
- Multi-threading of coverage maps (the 5950X has 32 threads, and coverage runs are slow), **verified
  against golden output**.
- A simple GUI or PowerShell front end.

---

## Open decisions (owner) — must be resolved before the relevant stage

| # | Decision | Recommendation | Needed by |
|---|---|---|---|
| D1 | **Toolchain:** MSYS2 UCRT64 (GCC) vs Visual Studio Build Tools (MSVC) | MSYS2 UCRT64 | Stage 0 |
| D2 | **Linux reference build:** new disposable WSL distro / use the existing `Debian` / skip the oracle | New disposable distro `splat-ref` | Stage 0 |
| D3 | **SDF filename separator** to replace `:` (for example `40_41_74_75.sdf` or `40-41-74-75.sdf`), and whether `splat` should also try the old colon name for compatibility | `_` (underscore), no colon fallback since Windows can't store such files | Stage 2 |
| D4 | **Where `splat_path` lives:** `%USERPROFILE%\.splat_path` (same as Linux) or `%APPDATA%\splat\splat_path` (Windows convention) | `%USERPROFILE%\.splat_path`, plus `HOME` if set | Stage 2 |
| D5 | **Analysis-region sizes** to build (standard 2–8°, HD 1–8°). This PC has 64 GB, so the maximum is fine here, but other PCs may have less. | Standard 8×8°, HD 4×4° | Stage 1 |
| D6 | **Git hosting:** local only, or a public GitHub fork (makes GPL source distribution easy) | Local only until Stage 5 | Stage 5 |
| D7 | **Terrain-data source and test area:** NASA Earthdata / USGS EarthExplorer (owner login) or OpenTopography or viewfinderpanoramas.org (no login). Also which real path to use for tests. The old `dds.cr.usgs.gov` link in the docs is defunct. | Owner names the test path; source chosen in Stage 1 research | Stage 1 |
| D8 | **gnuplot:** MSYS2 package or the native Windows installer | MSYS2 package (no extra installer) | Stage 4 |
| D9 | **Viewing PPM maps:** document a free viewer (IrfanView/GIMP), or add a PNG conversion step (a new feature) | Document a viewer first | Stage 4 |
| D11 | **Text-output line endings on Windows:** keep CR-LF (upstream text mode) or force LF to be byte-identical to Linux | Keep CR-LF; Stage 3 compares text with line endings normalised | Stage 3 |
| D12 | **Numeric tolerance, Windows vs Linux** | Exact everywhere except `.gp` and `.ano` files: ±1 in the last printed digit (measured drift: 1e-6 in graph data, 1e-7° / 0.001° in `.ano` geometry; no dB value differs) | Stage 3 gate |
| D13 | **Graph PNGs in the acceptance test** (bytes never match across gnuplot versions) | **DECIDED 2026-11-22:** pass if same size and the `.gp` data passes D12; pixel-difference % reported for information | Stage 4 gate |
| D10 | **Who uses the Windows build** (just the owner, or club members) and where it's installed | — | Stage 5 |

### Decisions recorded
- **2026-11-21 — D2 → the Linux reference is built on a new WSL distro** (owner: "The linux version
  can be built on a new wsl distro"). The existing `Debian` distro stays untouched. The new distro's
  name, base image, and removal steps are recorded here when Stage 1 creates it.
- **2026-11-21 — D1 → MSYS2 UCRT64** (owner: "Approve, start Stage 0", in answer to "Approve MSYS2
  UCRT64 as the toolchain and start Stage 0?"). Installed in Stage 0. VS Code works with it (see
  §Environment notes); VS Code is optional.
- **2026-11-21 — D7 (test sites), partly decided.** The owner gave Google Maps links. The
  coordinates came from the links' redirects:

  | Site | Role | Lat, Lon (WGS84) | Antenna AGL | In git as |
  |---|---|---|---|---|
  | Club repeater **K4HTA** | TX | 38.924216, −77.205793 | 240 ft | exact (public site) |
  | KK4DAS home QTH | RX 1 | 38.963031, −77.276738 | 30 ft (second-story chimney mount) | **rounded to 38.96, −77.28** (private residence; about 0.6 mi, a negligible change at this path length) |
  | American Legion Hall, 330 Center St N, Vienna | RX 2 | 38.905259, −77.270717 | 6 ft (mobile) | exact (public site) |

  Radio: **146.685 MHz, ERP 93 W**, about 100 ft of hard line (negligible loss, already inside the
  ERP figure). Polarization is assumed **vertical** (FM repeater), to be confirmed. The paths are
  short: TX→home ≈ 4.7 mi (7.5 km), TX→Legion ≈ 3.7 mi (6.0 km). All three sites are in SRTM tile
  **N38W078**. A repeater coverage map out to about 30 mi also needs N38W077, N39W078, and N39W077
  (a 2×2° region).
- **2026-11-21 — D7 coverage radius → 25–30 mi** (owner: "25-30 miles is right"). Coverage test
  cases use `-R 30`. That needs the four tiles N38W078, N38W077, N39W078, and N39W077.
- **2026-11-21 — D7 receive sites completed** (owner: "american legion hall - use mobile - about
  6' - my QTH on second story chimney - about 30' - repeater call sign is K4HTS"). See the table
  above.
- **2026-11-21 — Correction: the repeater call sign is K4HTA, not K4HTS** (owner: "correction -
  repeater is k4hta"). The quote above is kept verbatim; the table is corrected.
- **2026-11-21 — Polarization → vertical, confirmed** (owner: "vertical polarization is correct").
  This completes D7.
- **2026-11-21 — D5 → standard 8×8° (MAXPAGES 64), HD 4×4° (MAXPAGES 16)** (owner: "Yes, start
  Stage 1", in answer to "Start Stage 1 with the recommended region sizes?").
- **2026-11-21 — Terrain source (part of D7) → public, no-login tiles** (owner chose "Yes, download
  both"): 1″ from the AWS `elevation-tiles-prod/skadi` bucket, 3″ from viewfinderpanoramas `dem3`.
- **2026-11-21 — D3 → underscore.** SDF names become `40_41_74_75.sdf` / `…-hd.sdf` (owner: "use
  underscore for filenames"). Stage 2 also makes the name parsing accept `_`.
- **2026-11-21 — D4 → `%USERPROFILE%\.splat_path`** (owner: "userprofile is good for settings").
  `HOME` is still used first if it is set (same as Linux); otherwise `USERPROFILE`.
- **2026-11-22 — D11 → keep CR-LF** for Windows text outputs (owner: "Approve, keep CR-LF").
  Stage 3 compares text with line endings normalised; that is the one ratified expected difference
  so far.
- **2026-11-22 — D12 → tolerance ratified** (owner: "Approve Stage 3 and D12"). Windows output must
  match Linux **exactly**, except in `.gp` (gnuplot data) and `.ano` (alphanumeric output) files,
  where a number may differ by 1 unit in its last printed digit. This is implemented as
  `compare_outputs.py --last-digit`, and it is the acceptance test for every later change to the port.
- **2026-11-22 — D8 → MSYS2 gnuplot** (owner: "Yes, start Stage 4" in answer to "Start Stage 4 and
  install gnuplot from MSYS2?"). Note: it installed at about 1 GB, not the ~50 MB Code quoted (see
  the Stage 4 report).
- **2026-11-22 — D9 → `ppm2png` helper** (owner chose "ppm2png helper (Recommended)").
- **2026-11-22 — DEFERRED by owner: PowerShell `postdownload` for old USGS DEM files** (owner chose
  "Defer it (Recommended)"). Add it only if USGS DEM data is ever needed; `usgs2sdf.exe` itself
  is built and starts.
- **2026-11-22 — D13 → graph PNGs pass on same size + passing data** (owner: "Accept D13").
  `compare_outputs.py` gives a same-size PNG the status **GRAPH** (a pass) and shows its pixel
  difference for information. It fails the graph if any `.gp` file of its case fails, if the size
  differs, or if the PNG is missing (gnuplot is installed now, so a missing PNG is no longer
  DEFERRED). Visual spot-checks with `compare_graphs.ps1` only when gnuplot changes version.
- **Still open:** D6, D10.

---

## Best practices for this semi-autonomous project (recommended)
1. **One stage per Code session, and halt at the gate.** Start each session by pointing Code at this
   runbook and the stage's prompt in `prompts/`. End each session with a report in `reports/` and a
   commit.
2. **Golden outputs before code changes.** The Linux reference (Stage 1) exists before the first port
   edit, so every change is judged against it, not against memory.
3. **One problem per commit.** Each `WIN32PORT` change is its own commit, so any one can be reviewed or
   reverted alone, and `git diff upstream-1.4.2` stays readable.
4. **Prove every comparison can fire** (Guardrail 6). For each check, the report lists the
   deliberately broken input it caught.
5. **Separate "port bug" from "upstream behaviour".** If Windows and Linux agree on something odd,
   that's upstream behaviour: record it, don't fix it (no autonomous scope decisions).
6. **Binary vs text mode is the classic Windows trap.** Every file read or written gets a deliberate
   choice, and the choice is noted.
7. **Record why odd choices were made** (inherited lesson). Comment the code and add a runbook note,
   so a later tidy-up doesn't undo a deliberate workaround.
8. **Watch for churn.** If floating-point output differs, measure and report it after 2–3 hypotheses;
   don't keep toggling compiler flags.

## Backup
Add the project folder to the Nightly Backup job (owner action, or Code with approval). Terrain data in
`data\` can be re-downloaded and needn't be backed up if space matters.

## Change log
- **2026-11-22 — Owner's README run (Stage 4 gate, in progress): fixes from what the owner hit.**
  - Project moved to `D:\ClaudeAI\projects\SPLATWIN` at the owner's request. The app's copy left
    out `.git`; Code copied it across (347 files, `git fsck` clean, all tracked files matched).
    `.venv` (pip and setuptools only) was not copied and isn't needed.
  - README step 4 opens with a worked example using the real folders. The owner found the
    commands table unclear.
  - README step 5 says to use the map's own file name. The owner's run without `-o` made
    `k4hta.ppm`, so `coverage.ppm` wasn't found.
  - Google Earth on the web can't load a KML's image from disk. It also rejects SPLAT!'s KML
    outright over `root://icons/palette-5.png`, an icon only Google Earth Pro has.
    `ppm2png.ps1` now also writes a verified `<name>.kmz` (doc.kml with that `<Style>` removed,
    plus the PNG). The owner loaded a hand-made one of these successfully, and the script's
    doc.kml is identical to it. Tested: the normal case, a forced verification failure (no KMZ
    and no `.part` left, PNG kept, exit 1), a re-run, and a map without KML. README step 5
    explains the web steps.
- **2026-11-22 — D13 accepted and implemented.** `compare_outputs.py` now decodes PNGs (stdlib
  only) and applies D13. Self-test is 20/20 (4 new graph checks: same size with other pixels
  passes; different size, missing PNG, and bad `.gp` data each fail). The Stage 4 run is
  **EQUIVALENT**: 26 SAME, 61 SAME*, 8 TOL, 8 GRAPH (2.70–5.72% of pixels differ, which matches the
  independent .NET check). Results: `tests/results/stage4-compare-d13.txt`,
  `stage4-selftest-d13.txt`. Stage 4 still waits on the owner's own README run.
- **2026-11-22 — Stage 4 complete: graphs, data pipeline, helpers. HALT for owner review and the
  owner's own end-to-end run from README.md.** MSYS2 gnuplot 6.0.6 plus a `dist\gnuplot.cmd` shim;
  all 8 graphs render. They are identical to Linux in data and commands, and the PNGs differ only in
  font rendering (2.7–5.7% of pixels, lettering only). `ppm2png.ps1` (pixel-verified, with a Google
  Earth KML companion), `get_terrain.ps1` (its SDFs are byte-identical to the Linux goldens), and
  `README.md` (dry-run followed literally: works). USGS postdownload deferred by owner. Install-size
  misquote (~1 GB vs ~50 MB) disclosed. D13 raised. Report: `reports/stage4-graphs-pipeline.md`.
- **2026-11-22 — Stage 3 approved by owner; D12 tolerance ratified** ("Approve Stage 3 and D12").
- **2026-11-22 — Stage 3 complete: equivalence with Linux. HALT for owner review.** All 20 cases
  run on Windows with the same exit codes. Of 103 files: **26 byte-identical (all 9 maps including
  the 155 MB HD map, all 8 SDFs, KML, GEO)**, 61 identical apart from the ratified expected
  differences (CR-LF, `_` SDF names, paths, gnuplot not installed), 8 differ only in the last
  printed digit (`.gp` graph data, `loss.ano` geometry; **no dB value differs anywhere**), and 8 PNGs
  deferred to Stage 4. **EQUIVALENT** under the proposed tolerance (D12). Comparer self-test 16/16;
  its first version passed for the wrong reason, which was caught and fixed. Report:
  `reports/stage3-equivalence.md`.
- **2026-11-22 — Stage 2 approved by owner; D11 → keep CR-LF** ("Approve, keep CR-LF").
- **2026-11-22 — Stage 2 complete: minimum native port. HALT for owner review.** `build.ps1` plus
  8 one-problem commits (P1–P7, P9, and new P11 backslash paths and P12 stack overflow), 110+/14−
  lines in 3 files, all Windows changes inside `#ifdef _WIN32`. **The patched source built on Linux
  reproduces 102/102 goldens bit-for-bit** (Linux untouched). Windows smoke test 27/27: all 5 SDF
  conversions are byte-identical to Linux, and every report is identical apart from CR-LF line
  endings (D11 raised). HD 8×8° (3.3 GB) builds and runs only with the P2 fix. Report:
  `reports/stage2-minimum-port.md`.
- **2026-11-21 — Stage 1 approved by owner** ("stage 1 approved"). D3 → `_`, D4 → `%USERPROFILE%`.
- **2026-11-21 — Stage 1 complete: Linux reference baseline. HALT for owner review.** New WSL distro
  `splat-ref` (the existing Debian is untouched). Upstream copied byte-exact and built with its own
  scripts (8×8° / 4×4°). 4 real tiles (1″ + 3″, about 36 MB, owner-approved). 18 cases + 2
  conversions all exit 0. **Linux is deterministic: 103/103 files identical on a second run.** The
  golden copy on Windows was verified 103/103 (a 1-bit change is detected); 77 small text outputs are
  tracked in `tests/golden/linux/`. Findings: the GitHub copy lost the exec bits on its scripts;
  **`-kml` together with `-geo` writes neither file** (upstream quirk, pinned as case M08); `-log`
  records the exe path (an expected difference for Stage 3). K4HTA→KK4DAS predicted at −64.9 dBm
  (owner sanity check requested). Report: `reports/stage1-linux-reference.md`.
- **2026-11-21 — Stage 0 complete: scaffold and toolchain. HALT for owner review.** Repo created,
  and upstream imported **verbatim** as tag `upstream-1.4.2` (the `src/` tree is the same git object
  as upstream, `ed514466`). The first import attempt was caught by the hash check (system
  `autocrlf=true` had converted it to CRLF) and redone. MSYS2 UCRT64 was installed at `C:\msys64`
  (GCC 16.2.0, bzip2 1.0.8, zlib 1.4). Preflight: 13/13 positive, 7/7 negatives fired, and 4/4
  port-assessment predictions (P1–P4) **reproduced for real**. System Python, WSL, PATH, and
  scheduled tasks are unchanged. Report: `reports/stage0-scaffold-toolchain.md`.
- **2026-11-21** — D2 decided (new WSL distro for the Linux reference). VS Code 1.146.0 with the
  C/C++ extension pack found and added to the environment notes; it is compatible with MSYS2 UCRT64.
- **2026-11-21** — Runbook created. Upstream examined (`jmcmellen/splat` @ `08f06f5`, SPLAT! 1.4.2).
  Port assessment written (`reference/port-assessment.md`). Toolchain surveyed: **no C/C++ compiler is
  installed**. Plan drafted. Awaiting owner review of the plan and decisions D1–D2; **no development
  started, nothing installed.**
