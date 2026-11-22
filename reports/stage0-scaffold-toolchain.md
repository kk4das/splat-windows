# Stage 0 report: Scaffold & toolchain (2026-11-21)

## What this stage was meant to accomplish
Set up the project, bring in the original SPLAT! source untouched, install the compiler, and prove it
can build self-contained Windows programs that use SPLAT!'s libraries, **without changing anything
else on the PC.** No SPLAT! code was compiled or changed. That is Stage 2. The Linux reference distro
is Stage 1.

## Outcome: gate criteria met
- **Repo created** (`main`, local only) with repo-local `core.autocrlf=false`.
- **Upstream imported verbatim and tagged `upstream-1.4.2`** (commit `0d4c79e`). `src/` is the
  **identical git tree object** to upstream (`ed514466…`), so the bytes, file modes, and the `README2`
  symlink entry are all the same. Every working-tree file also re-hashes to its upstream blob (0
  mismatches).
- **MSYS2 installed** (owner-approved, D1) at `C:\msys64` via `winget install MSYS2.MSYS2`
  (installer 2026-09-27, hash verified by winget). The UCRT64 packages are **GCC 16.2.0**,
  binutils 2.47, **bzip2 1.0.8**, and **zlib 1.4** (static `libbz2.a` and `libz.a` present).
  895 MB on disk.
- **Preflight `scripts/verify_toolchain.ps1`:** 13/13 positive checks passed, 7/7 negative checks
  fired, and 4/4 port-assessment predictions reproduced. `OVERALL: OK`.
- **Nothing else changed** (before/after snapshots): system Python `pip list` (26 packages), WSL
  distros (`Debian` and `docker-desktop`, both still stopped), User PATH, Machine PATH, and all 229
  scheduled tasks are identical. **MSYS2 is not on any PATH.**

## Evidence (verbatim run, 2026-11-21)
```
== positive checks (must PASS) ==
[PASS] g++ compiler                         g++.exe (Rev7, Built by MSYS2 project) 16.2.0
[PASS] gcc compiler                         gcc.exe (Rev7, Built by MSYS2 project) 16.2.0
[PASS] compile C++ w/ upstream flags        built hello.exe (13 KB)
[PASS] run C++ probe (clean PATH)           hello splat-toolchain abs=5.0 rint=3 ptr=8
[PASS] C++ probe is self-contained          imports only Windows DLLs: KERNEL32.dll + 8 api-ms-win-crt-* (UCRT, built into Windows 10/11)
[PASS] compile+link libbz2 static           built bz2test.exe (104 KB)
[PASS] bz2 write (clean PATH)               BZ2 WROTE 1048576 bytes
[PASS] bz2 read+verify (clean PATH)         BZ2 OK 1048576 bytes round-tripped
[PASS] bz2 probe is self-contained          imports only Windows DLLs: KERNEL32.dll + 11 api-ms-win-crt-* (UCRT, built into Windows 10/11)
[PASS] compile+link zlib static             built ztest.exe (135 KB)
[PASS] zlib round trip (clean PATH)         ZLIB OK 65536 bytes round-tripped (zlib 1.4)
[PASS] compile port probes                  built portprobes.exe (15 KB)
[PASS] 3.4 GB heap grid (P2 fix)            HEAP OK 3400 MB allocated and touched (870400 pages)

== negative checks (must FIRE) ==
[PASS] compiler: wrong path                 fired: could not start: ... "The system cannot find the file specified"
[PASS] compile: bzlib.h not found           fired: exit 1: ...bz2test.c:5:19: error: no include path in which to search for stdio.h
[PASS] link: libbz2 omitted                 fired: exit 1: ...undefined reference to `BZ2_bzReadOpen' | ...`BZ2_bzRead'
[PASS] static: dynamic exe flagged          fired: non-Windows DLLs: libbz2-1.dll
[PASS] run: dynamic exe, clean PATH         fired: exit 0xC0000135
[PASS] bz2: corrupted stream                fired: exit 0x1: BZ2 MISMATCH err=-4 bytes=0
[PASS] run: wrong output                    fired: exit 0x0: hello splat-toolchain abs=5.0 rint=3 ptr=8

== port-assessment findings (must REPRODUCE on this toolchain) ==
[PASS] P1 colon in SDF filename             fired: COLON fopen("40:41:74:75.sdf","wb") -> failed; directory holds []
[PASS] P2 3 GB static array                 fired: link fails: ...relocation truncated to fit: IMAGE_REL_AMD64_REL32 against symbol `__imp_Sleep' ...
[PASS] P3 open() text mode                  fired: TEXTMODE wrote=8 read(O_RDONLY)=2 read(O_RDONLY|O_BINARY)=8
[PASS] P4 HOME unset                        fired: HOME UNSET, USERPROFILE set

-- positive: 13/13 as expected
-- negative: 7/7 as expected
-- findings: 4/4 as expected
OVERALL: OK
```
"Clean PATH" means the program ran with PATH set to only `C:\Windows\System32;C:\Windows`. That proves
the `.exe` needs nothing from MSYS2 at run time, so it can be copied to another Windows 10/11 PC.
Exit `0xC0000135` is Windows' "DLL not found" error.

## Findings worth noting
1. **The first upstream import was wrong, and the hash check caught it.** `git archive` from the
   reading clone applied the machine's **system-wide `core.autocrlf=true`** and wrote every text file
   with CRLF line endings. `tar` also couldn't create the `README2` symlink. The blob-hash comparison
   flagged all 42 files, so the 5-minute-old repo was discarded and re-imported with
   `git read-tree --prefix=src/`, which copies git objects directly. **Lesson for later stages:** on
   this PC, any git operation outside this repo converts line endings unless told not to, and the
   golden Linux outputs in Stage 1 must be moved into the repo with that in mind.
2. **All four predicted Windows problems are real on this toolchain** (not just on paper):
   - **P1:** opening a colon filename for writing simply **fails** (no hidden NTFS stream was created).
     Unpatched `srtm2sdf` will report it can't write its output, and `splat` will find no terrain.
   - **P2:** a 3 GB static array **fails to link** ("relocation truncated to fit"), which confirms the
     `-mcmodel=medium` gap. The heap alternative works: 3.4 GB allocated and touched in a 64-bit exe.
   - **P3:** text-mode `open()` returned **2 of 8 bytes**: CR-LF was collapsed and `0x1A` was taken as
     end of file. Unpatched `srtm2sdf` would silently truncate or corrupt real SRTM tiles. With
     `O_BINARY`, all 8 bytes were read.
   - **P4:** `HOME` is unset here and `USERPROFILE` is set, as predicted.
3. **"Static" still uses the Windows UCRT DLLs** (`api-ms-win-crt-*`). These ship with Windows 10/11,
   so this is expected. Every non-Windows library (libbz2, zlib, libstdc++, libgcc, winpthread) is
   linked in.
4. **MSYS2's first update terminates its own shell** (pass 1 exited 1 with no output). This is normal
   MSYS2 behaviour when its core runtime updates, and pass 2 completed. Recorded so a later session
   doesn't take it for a failure.
5. **Script lesson:** a helper function named `R` silently invoked PowerShell's built-in `r` alias
   (`Invoke-History`). It was renamed to `New-Result`.

## Declared coverage gaps
- **"bzlib.h not found" fired for the wrong header.** `-nostdinc` removes *every* include path, so
  the compile failed on `stdio.h` before it reached `bzlib.h`. The check does fail when headers are
  missing, but this test didn't prove it specifically for `bzlib.h`. The `libbz2 omitted` link test
  covers the library side. Stage 2's real `srtm2sdf` and `splat` builds compile against `bzlib.h`
  for real.
- **The 3.4 GB heap test proves allocation, not SPLAT!'s use of it.** Stage 2 proves the real `dem`
  change.
- **gnuplot is not installed yet** (decision D8, Stage 4), so nothing was tested for it.

## What was installed (for the runbook and for removal)
- `C:\msys64` (895 MB). Start-menu folder `MSYS2` (6 shortcuts). Uninstall entry "MSYS2 20260927"
  (`C:\msys64\uninstall.exe`). To remove it: Settings → Apps → MSYS2 → Uninstall (or
  `winget uninstall MSYS2.MSYS2`).
- Packages: `mingw-w64-ucrt-x86_64-gcc` 16.2.0-7, `…-bzip2` 1.0.8-4, `…-zlib` 1.4-2, plus their
  dependencies.

## Files added
`.gitignore`, `scripts/verify_toolchain.ps1`, `scripts/toolchain-tests/{hello.cpp, bz2test.c,
ztest.c, portprobes.c, bigstatic.c}`, this report, and the runbook, prompts, and reference notes from
planning.

## Toolchain handed to Stage 1
g++/gcc 16.2.0 (UCRT64) ✔ · static libbz2 ✔ · static zlib ✔ · self-contained exes ✔ · heap above 2 GB ✔.

## Next stage (needs owner go-ahead): Stage 1, the Linux reference baseline
1. Create the new WSL distro (D2, already approved) without touching `Debian`, install `g++` and
   `libbz2-dev`, and build **unmodified** upstream with its own scripts.
2. **Your input needed:** D7, the real radio path to test (for example the club station to a
   member's QTH), and D5, the region sizes to build (recommended: standard 8×8°, HD 4×4°).
3. Download the SRTM tiles for that area. If the source needs a login, you sign in.
4. Define the 8–12 test cases and record the golden Linux outputs.

## Halted for owner review.
