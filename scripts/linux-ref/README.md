# Linux reference scripts (Stage 1), kept exactly as they were run on 2026-11-21

They run inside the `splat-ref` WSL distro as root:
`wsl -d splat-ref -u root -- bash /mnt/c/<path>/script.sh`

| Script | Does |
|---|---|
| `verify_copy.sh` | checks that `/opt/splat` matches the upstream tag file for file (sha256) |
| `build_ref.sh` | `chmod +x` on the 9 upstream scripts (the GitHub copy lost the bit), then upstream `./configure` with answers `8` (standard 8×8°) and `4` (HD 4×4°) |
| `run_ref.sh` | collects the binaries in `/opt/splat-bin`, copies tiles and cases, runs `scripts/run_cases.py` |
| `determinism.sh` | runs the whole suite a second time and compares every hash |
| `export.sh` | tars `/opt/ref/work` with `:`→`_` in names (Windows can't store colons) for `data/golden-linux/` |

The scripts contain this session's absolute paths (the session scratchpad for the manifest and
tarball, and the repo's `/mnt/c/...` path). Update them before re-running from another folder.
Always invoke them as script files: passing the command line straight through `wsl.exe -- bash -c`
mangled the quoting (Stage 1 finding).
