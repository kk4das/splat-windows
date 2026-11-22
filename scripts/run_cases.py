#!/usr/bin/env python3
"""Run the SPLAT! test cases (tests/cases/cases.json) and record what every case produced.

Same script on both sides, so the procedure is identical:
  Linux reference (Stage 1):  python3 run_cases.py --bin /usr/local/bin --work /opt/work ...
  Windows port   (Stage 3):   .venv\\Scripts\\python.exe run_cases.py --bin dist --work work\\windows ...

Writes <work>/manifest.json: per case the exit code, run time, and sha256/size of every file it
produced (inputs excluded). Stdlib only. No case output is interpreted here; comparing is a
separate script (Stage 3), so this runner can't hide a difference.
"""
import argparse, hashlib, json, os, platform, shutil, subprocess, sys, time
from pathlib import Path


def sha256(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def exe(bindir: Path, name: str) -> str:
    return str(bindir / (name + (".exe" if os.name == "nt" else "")))


def snapshot(d: Path, exclude: set) -> dict:
    out = {}
    for p in sorted(d.rglob("*")):
        if p.is_file() and p.name not in exclude:
            out[p.relative_to(d).as_posix()] = {"sha256": sha256(p), "size": p.stat().st_size}
    return out


def run(cmd, cwd: Path, log: Path, timeout: int):
    t0 = time.time()
    with open(log, "wb") as f:
        try:
            r = subprocess.run(cmd, cwd=cwd, stdout=f, stderr=subprocess.STDOUT, timeout=timeout)
            code = r.returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
    return code, round(time.time() - t0, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bin", required=True, type=Path, help="directory holding splat, splat-hd, srtm2sdf, ...")
    ap.add_argument("--work", required=True, type=Path, help="empty working directory for this run")
    ap.add_argument("--cases", required=True, type=Path)
    ap.add_argument("--inputs", required=True, type=Path)
    ap.add_argument("--hgt3", required=True, type=Path)
    ap.add_argument("--hgt1", required=True, type=Path)
    ap.add_argument("--only", nargs="*", help="run only these case ids (after conversions)")
    ap.add_argument("--timeout", type=int, default=3600)
    a = ap.parse_args()

    spec = json.loads(a.cases.read_text())
    work = a.work.resolve()
    if work.exists() and any(work.iterdir()):
        sys.exit(f"--work {work} is not empty; refusing to mix runs")
    work.mkdir(parents=True, exist_ok=True)
    inputs = sorted(p for p in a.inputs.iterdir() if p.is_file())
    input_names = {p.name for p in inputs} | {"stdout.txt"}
    manifest = {"platform": platform.platform(), "python": platform.python_version(),
                "bin": str(a.bin), "started": time.strftime("%Y-%m-%d %H:%M:%S %z"), "cases": {}}

    for c in spec["convert"]:
        out = work / c["out"]; out.mkdir()
        tiles = sorted((a.hgt3 if c["hgt"] == "hgt3" else a.hgt1).glob("*.hgt"))
        codes, secs = [], 0.0
        for t in tiles:  # each tile once, in a fresh dir (srtm2sdf may read earlier SDFs as replacement data)
            code, s = run([exe(a.bin, c["program"]), str(t.resolve())], out, out / f"stdout-{t.stem}.txt", a.timeout)
            codes.append(code); secs += s
        manifest["cases"][c["id"]] = {"program": c["program"], "tiles": [t.name for t in tiles],
                                      "exit": codes, "seconds": round(secs, 2), "files": snapshot(out, set())}
        print(f"{c['id']} {c['program']:<12} exit={codes} {secs:7.1f}s  {len(manifest['cases'][c['id']]['files'])} files", flush=True)

    for c in spec["cases"]:
        if a.only and c["id"] not in a.only:
            continue
        d = work / c["id"]; d.mkdir()
        for p in inputs:
            shutil.copy2(p, d / p.name)
        for n in c.get("needs", []):
            shutil.copy2(work / n, d / Path(n).name)
        excl = input_names | {Path(n).name for n in c.get("needs", [])}
        code, s = run([exe(a.bin, c["program"])] + c["args"], d, d / "stdout.txt", a.timeout)
        files = snapshot(d, excl)
        files["stdout.txt"] = {"sha256": sha256(d / "stdout.txt"), "size": (d / "stdout.txt").stat().st_size}
        manifest["cases"][c["id"]] = {"program": c["program"], "args": c["args"], "exit": code,
                                      "seconds": s, "files": files}
        print(f"{c['id']} {c['program']:<12} exit={code} {s:7.1f}s  {len(files)} files", flush=True)

    manifest["finished"] = time.strftime("%Y-%m-%d %H:%M:%S %z")
    (work / "manifest.json").write_text(json.dumps(manifest, indent=1))
    print(f"manifest: {work / 'manifest.json'}")


if __name__ == "__main__":
    main()
