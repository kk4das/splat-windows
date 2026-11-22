#!/usr/bin/env python3
"""Compare a Windows test run against the Linux golden run, file by file.

usage:
  compare_outputs.py --linux data/golden-linux --windows work/windows --cases tests/cases/cases.json
  compare_outputs.py --self-test --linux data/golden-linux --cases tests/cases/cases.json

Every file the Linux run produced must exist on Windows and compare equal by a method that suits
its format. The ONLY differences allowed are listed in EXPECTED below, each tied to an owner
decision or a documented cause, and each application is counted in the output. Numbers must match
exactly unless --abs-tol is given (the tolerance is the owner's call, Stage 3 gate).

Statuses: SAME (bytes identical), SAME* (identical after the listed expected differences),
TOL (numeric differences within --abs-tol), DIFF, MISSING, EXTRA, DEFERRED (output needs a tool
that isn't installed yet: gnuplot PNGs, Stage 4).
Stdlib only.
"""
import argparse, hashlib, json, re, shutil, sys, tempfile
from pathlib import Path

EXPECTED = {
    "crlf":    "text files end lines with CR-LF on Windows (upstream text mode; owner decision D11)",
    "sdfname": "SDF names use '_' instead of ':' on Windows (owner decision D3), as echoed in console output",
    "argv0":   "-log records argv[0], the program's own path, which differs by machine (Stage 1 finding 4)",
    "nognuplot": "gnuplot is not installed on Windows yet (D8, Stage 4): its 'not recognized' message and the missing PNGs",
    "inputpath": "srtm2sdf echoes the tile path it was given; the test runner passes each machine's own absolute path",
}
# Owner-ratified numeric tolerance (Stage 3 gate): in these file types only, a number may differ by
# one unit in its last printed digit (glibc vs UCRT maths-library rounding, runbook P10).
# Everything else (reports, KML/GEO, colour files, maps, SDF terrain) must match exactly.
LAST_DIGIT_EXT = {".gp", ".ano"}
TEXT_EXT = {".txt", ".gp", ".kml", ".geo", ".scf", ".dcf", ".lcf", ".lrp", ".log", ".ano"}
NUM = re.compile(r"[-+]?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?")
GNUPLOT_MSG = re.compile(r"^'gnuplot' is not recognized as an internal or external command,\n"
                         r"operable program or batch file\.\n", re.M)


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


# ---------------------------------------------------------------- format comparers
def last_digit(u: str) -> float:
    """One unit in the last printed digit of u ('5.491410' -> 1e-6, '180' -> 1)."""
    m = u.lower().split("e")[0]
    return 10.0 ** -(len(m.split(".")[1]) if "." in m else 0)


def cmp_text(lin: bytes, win: bytes, name: str, tol: float, used: set, lastdig: bool = False):
    a = lin.decode("latin-1"); b = win.decode("latin-1")
    if "\r\n" in b and "\r\n" not in a:
        b = b.replace("\r\n", "\n"); used.add("crlf")
    if name.startswith("stdout"):
        a2 = re.sub(r"(-?\d+):(-?\d+):(-?\d+):(-?\d+)", r"\1_\2_\3_\4", a)
        if a2 != a: used.add("sdfname")
        a = a2
        if name.startswith("stdout-"):
            ra = re.sub(r"(?m)^Reading .*[\\/]([NS]\d+[EW]\d+\.(?:hgt|bil))", r"Reading \1", a)
            rb_ = re.sub(r"(?m)^Reading .*[\\/]([NS]\d+[EW]\d+\.(?:hgt|bil))", r"Reading \1", b)
            if (ra, rb_) != (a, b): used.add("inputpath")
            a, b = ra, rb_
        b2 = GNUPLOT_MSG.sub("", b)
        if b2 != b: used.add("nognuplot")
        b = b2
    if name == "cmd.log":
        fa, fb = a.split(" ", 1), b.split(" ", 1)
        if len(fa) == 2 and len(fb) == 2 and fa[0] != fb[0] and Path(fa[0].replace("\\", "/")).stem == Path(fb[0].replace("\\", "/")).stem:
            a, b = "ARGV0 " + fa[1], "ARGV0 " + fb[1]; used.add("argv0")
    if a == b:
        return ("SAME*" if used else "SAME"), ""
    la, lb = a.split("\n"), b.split("\n")
    if len(la) != len(lb):
        return "DIFF", f"line count {len(la)} vs {len(lb)}"
    maxd, ndiff, first, beyond = 0.0, 0, None, 0
    for i, (x, y) in enumerate(zip(la, lb), 1):
        if x == y:
            continue
        if NUM.sub("#", x) != NUM.sub("#", y):
            return "DIFF", f"line {i} text differs: {x.strip()[:60]!r} vs {y.strip()[:60]!r}"
        for u, v in zip(NUM.findall(x), NUM.findall(y)):
            if u != v:
                d = abs(float(u) - float(v)); ndiff += 1; maxd = max(maxd, d)
                allowed = max(tol, last_digit(u) * 1.000001 if lastdig else 0.0)
                if d > allowed: beyond += 1
                if first is None: first = f"line {i}: {u} vs {v}"
    status = "TOL" if beyond == 0 else "DIFF"
    rule = "1 unit in the last printed digit" if lastdig else f"|diff| <= {tol:g}"
    return status, f"{ndiff} numbers differ, max |diff| {maxd:g}, {beyond} beyond {rule} (first: {first})"


def ppm(b: bytes):
    # P6\n<w> <h>\n255\n<data>  (SPLAT! writes exactly this header)
    m = re.match(rb"P6\s+(\d+)\s+(\d+)\s+(\d+)\s", b)
    if not m:
        raise ValueError("not a P6 PPM")
    return int(m[1]), int(m[2]), b[m.end():]


def cmp_ppm(lin: bytes, win: bytes, *_):
    if lin == win:
        return "SAME", ""
    (wa, ha, da), (wb, hb, db) = ppm(lin), ppm(win)
    if (wa, ha) != (wb, hb):
        return "DIFF", f"size {wa}x{ha} vs {wb}x{hb}"
    n = 0; x0 = y0 = 10**9; x1 = y1 = -1
    for i in range(0, min(len(da), len(db)), 3):
        if da[i:i + 3] != db[i:i + 3]:
            n += 1; px = i // 3; x, y = px % wa, px // wa
            x0, y0, x1, y1 = min(x0, x), min(y0, y), max(x1, x), max(y1, y)
    if len(da) != len(db):
        return "DIFF", f"pixel data length {len(da)} vs {len(db)}"
    return "DIFF", f"{n} of {wa*ha} pixels differ ({100*n/(wa*ha):.4f}%), box x{x0}-{x1} y{y0}-{y1}"


def cmp_sdf(lin: bytes, win: bytes, *_):
    if lin == win:
        return "SAME", ""
    a, b = lin.split(), win.split()
    if len(a) != len(b):
        return "DIFF", f"{len(a)} vs {len(b)} values"
    d = [(i, x, y) for i, (x, y) in enumerate(zip(a, b)) if x != y]
    if not d:
        return "SAME*", "same values, different whitespace"
    i, x, y = d[0]
    return "DIFF", f"{len(d)} of {len(a)} values differ (first: value #{i}: {x.decode()} vs {y.decode()})"


def compare_file(name: str, lp: Path, wp: Path, tol: float, lastdig_ext=frozenset()):
    used = set()
    if not wp.exists():
        if name.endswith(".png"):
            return "DEFERRED", "PNG not produced (gnuplot not installed: Stage 4)", {"nognuplot"}
        return "MISSING", "", used
    lin, win = lp.read_bytes(), wp.read_bytes()
    suf = Path(name).suffix.lower()
    if suf == ".ppm":
        s, d = cmp_ppm(lin, win)
    elif suf == ".sdf":
        s, d = cmp_sdf(lin, win)
    elif suf == ".png":
        s, d = ("SAME", "") if lin == win else ("DIFF", "PNG bytes differ (compare visually)")
    elif suf in TEXT_EXT or name.startswith("stdout"):
        s, d = cmp_text(lin, win, name, tol, used, suf in lastdig_ext)
    else:
        s, d = ("SAME", "") if lin == win else ("DIFF", "binary differs")
    return s, d, used


# ---------------------------------------------------------------- driver
def case_dirs(cases_json: Path):
    spec = json.loads(cases_json.read_text())
    return {c["id"]: c["out"] for c in spec["convert"]}


def compare_runs(lin_root: Path, win_root: Path, cases_json: Path, tol: float, quiet=False, lastdig_ext=frozenset()):
    lm = json.loads((lin_root / "manifest.json").read_text())["cases"]
    wm_path = win_root / "manifest.json"
    wm = json.loads(wm_path.read_text())["cases"] if wm_path.exists() else {}
    dirs = case_dirs(cases_json)
    rows, counts, expected_used = [], {}, {}
    for cid, c in lm.items():
        d = dirs.get(cid, cid)
        wexit = wm.get(cid, {}).get("exit", "not run")
        if wexit != c["exit"]:
            rows.append((cid, "(exit code)", "DIFF", f"Linux {c['exit']} vs Windows {wexit}"))
        lin_names = set()
        for name in c["files"]:
            wname = name.replace(":", "_")
            lin_names.add(wname)
            s, det, used = compare_file(name, lin_root / d / wname, win_root / d / wname, tol, lastdig_ext)
            rows.append((cid, name, s, det))
            for u in used:
                expected_used[u] = expected_used.get(u, 0) + 1
        for wname in wm.get(cid, {}).get("files", {}):
            if wname not in lin_names:
                rows.append((cid, wname, "EXTRA", "produced on Windows only"))
    for r in rows:
        counts[r[2]] = counts.get(r[2], 0) + 1
    if not quiet:
        for cid, name, s, det in rows:
            if s not in ("SAME",):
                print(f"{cid:4} {name:34} {s:8} {det}")
        print("-" * 78)
        print("files: " + ", ".join(f"{k}={v}" for k, v in sorted(counts.items())))
        for k, v in expected_used.items():
            print(f"expected difference '{k}' applied to {v} file(s): {EXPECTED[k]}")
    bad = sum(counts.get(k, 0) for k in ("DIFF", "MISSING", "EXTRA"))
    return bad, rows, counts


def self_test(lin_root: Path, cases_json: Path):
    """Guardrail 6: make a copy of the Linux run that is 'Windows-like' (renamed SDFs, CR-LF text),
    confirm it compares clean, then break one thing at a time and confirm each is caught."""
    tmp = Path(tempfile.mkdtemp(prefix="splat-selftest-"))
    try:
        base = tmp / "win"
        shutil.copytree(lin_root, base)
        m = json.loads((base / "manifest.json").read_text())
        for cid, c in m["cases"].items():                       # mimic a Windows manifest
            c["files"] = {k.replace(":", "_"): v for k, v in c["files"].items()}
        (base / "manifest.json").write_text(json.dumps(m))
        for so in base.rglob("stdout*.txt"):                    # console output names SDFs with '_'
            so.write_bytes(re.sub(rb"(-?\d+):(-?\d+):(-?\d+):(-?\d+)", rb"\1_\2_\3_\4", so.read_bytes()))
        r = base / "P01" / "K4HTA-to-KK4DAS.txt"
        r.write_bytes(r.read_bytes().replace(b"\n", b"\r\n"))   # D11 CR-LF must be tolerated
        bad, _, _ = compare_runs(lin_root, base, cases_json, 0.0, quiet=True)
        results = [("clean Windows-like copy passes", bad == 0, f"{bad} problems")]

        def mutate(label, fn, expect_status, case, fname, lastdig_ext=frozenset()):
            # The change must be caught IN THE FILE THAT WAS CHANGED, not anywhere in the run
            # (a first version of this test "passed" on an unrelated difference).
            t = tmp / ("m" + str(len(results)))
            shutil.copytree(base, t)
            fn(t)
            _, rows, _ = compare_runs(lin_root, t, cases_json, 0.0, quiet=True, lastdig_ext=lastdig_ext)
            others = [x for x in rows if x[2] in ("DIFF", "MISSING", "EXTRA") and (x[0], x[1]) != (case, fname)]
            hit = [x for x in rows if x[2] == expect_status and (x[0], x[1]) == (case, fname)]
            if others:
                hit = []
            results.append((label, bool(hit), (hit[0][1] + ": " + hit[0][3]) if hit else
                            ("NOT detected" if not others else f"detected elsewhere only: {others[0][1]}")))

        def edit(p, old, new):
            b = p.read_bytes(); assert old in b, (p, old); p.write_bytes(b.replace(old, new, 1))

        mutate("report: 116.68 dB -> 116.69 dB", lambda t: edit(t / "P01/K4HTA-to-KK4DAS.txt", b"116.68 dB", b"116.69 dB"), "DIFF", "P01", "K4HTA-to-KK4DAS.txt")
        mutate("report: a word changed", lambda t: edit(t / "P02/K4HTA-to-KK4DAS.txt", b"Longley-Rice", b"Longley-Ricf"), "DIFF", "P02", "K4HTA-to-KK4DAS.txt")

        def px(t):
            p = t / "M02/fs.ppm"; b = bytearray(p.read_bytes())
            for k in range(10): b[-3 * (1000 + 97 * k)] ^= 0x40
            p.write_bytes(bytes(b))
        mutate("PPM map: 10 pixels changed", px, "DIFF", "M02", "fs.ppm")

        def sdf(t):
            p = t / "sdf3/38_39_77_78.sdf"; b = p.read_bytes(); lines = b.split(b"\n")
            lines[5000] = str(int(lines[5000]) + 1).encode(); p.write_bytes(b"\n".join(lines))
        mutate("SDF: one elevation +1 m", sdf, "DIFF", "C01", "38:39:77:78.sdf")
        mutate("KML: one coordinate digit", lambda t: edit(t / "M01/los.kml", b"38.", b"39."), "DIFF", "M01", "los.kml")
        mutate("gnuplot data: one value", lambda t: edit(t / "P03/profile.gp", b".", b"9."), "DIFF", "P03", "profile.gp")
        mutate("file missing on Windows", lambda t: (t / "M04/loss.ano").unlink(), "MISSING", "M04", "loss.ano")

        def extra(t):
            mm = json.loads((t / "manifest.json").read_text())
            mm["cases"]["M08"]["files"]["both.kml"] = {"sha256": "x", "size": 1}
            (t / "manifest.json").write_text(json.dumps(mm))
        mutate("extra file on Windows (M08 quirk broken)", extra, "EXTRA", "M08", "both.kml")

        def exitcode(t):
            mm = json.loads((t / "manifest.json").read_text()); mm["cases"]["P05"]["exit"] = 1
            (t / "manifest.json").write_text(json.dumps(mm))
        mutate("exit code changed", exitcode, "DIFF", "P05", "(exit code)")
        mutate("stdout: real change (not just SDF names)", lambda t: edit(t / "P01/stdout.txt", b"Done", b"Dome"), "DIFF", "P01", "stdout.txt")

        # The ratified tolerance must accept exactly a last-digit change in .gp/.ano and nothing more.
        L = frozenset(LAST_DIGIT_EXT)
        mutate("tolerance: .gp last digit +1 accepted", lambda t: edit(t / "P03/profile.gp", b"5.491410", b"5.491411"), "TOL", "P03", "profile.gp", L)
        mutate("tolerance: .gp last digit +2 rejected", lambda t: edit(t / "P03/profile.gp", b"5.491410", b"5.491412"), "DIFF", "P03", "profile.gp", L)
        mutate("tolerance: .ano path loss +0.002 rejected", lambda t: edit(t / "M04/loss.ano", b"115.368", b"115.370"), "DIFF", "M04", "loss.ano", L)
        mutate("tolerance: report 0.01 dB still rejected", lambda t: edit(t / "P01/K4HTA-to-KK4DAS.txt", b"116.68 dB", b"116.69 dB"), "DIFF", "P01", "K4HTA-to-KK4DAS.txt", L)
        mutate("stdout-: tile path normalised, content not", lambda t: edit(t / "sdf3/stdout-N38W078.txt", b"Done", b"Dome"), "DIFF", "C01", "stdout-N38W078.txt")
        ok = all(r[1] for r in results)
        for label, passed, det in results:
            print(f"[{'PASS' if passed else 'FAIL'}] {label:44} {det}")
        print(f"SELF-TEST: {'OK' if ok else 'PROBLEMS'} ({sum(r[1] for r in results)}/{len(results)})")
        return 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--linux", required=True, type=Path)
    ap.add_argument("--windows", type=Path)
    ap.add_argument("--cases", required=True, type=Path)
    ap.add_argument("--abs-tol", type=float, default=0.0, help="allowed |difference| for numbers in text (default exact)")
    ap.add_argument("--last-digit", action="store_true", help="apply the ratified last-printed-digit tolerance to " + ", ".join(sorted(LAST_DIGIT_EXT)))
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        sys.exit(self_test(a.linux, a.cases))
    bad, _, _ = compare_runs(a.linux, a.windows, a.cases, a.abs_tol, lastdig_ext=frozenset(LAST_DIGIT_EXT) if a.last_digit else frozenset())
    print("RESULT: " + ("EQUIVALENT" if bad == 0 else f"{bad} PROBLEM(S)"))
    sys.exit(0 if bad == 0 else 1)


if __name__ == "__main__":
    main()
