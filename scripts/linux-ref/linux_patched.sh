#!/bin/bash
set -euo pipefail
rm -rf /opt/splat-patched /opt/patched-bin /opt/ref/work-patched
mkdir -p /opt/splat-patched /opt/patched-bin && tar -xf /mnt/c/Users/dsoul/AppData/Local/Temp/claude/19cac3bdfffab608/ed97a06d-308c-4435-82d8-3911fe4f8670/scratchpad/patched-src.tar -C /opt/splat-patched
cd /opt/splat-patched
chmod +x configure build install clean utils/build utils/install utils/postdownload
# Same parameter files configure writes for answers 8 / 4 (configure itself would also install
# into /usr/local/bin over the upstream reference binaries, so call ./build directly).
printf '#define HD_MODE 0\n#define MAXPAGES 64\n' > std-parms.h
printf '#define HD_MODE 1\n#define MAXPAGES 16\n' > hd-parms.h
./build all 2>&1 | grep -E 'Compiling|Done|failed|error' | tr '\n' ' '; echo
cp splat splat-hd utils/srtm2sdf utils/citydecoder utils/usgs2sdf utils/bearing utils/fontdata /opt/patched-bin/
ln -s srtm2sdf /opt/patched-bin/srtm2sdf-hd
cd /opt/ref && python3 run_cases.py --bin /opt/patched-bin --work /opt/ref/work-patched --cases cases/cases.json --inputs cases/inputs --hgt3 hgt3 --hgt1 hgt1 > /tmp/patched-run.log 2>&1
python3 - <<'PY'
import json
a=json.load(open('/opt/ref/work/manifest.json'))['cases']; b=json.load(open('/opt/ref/work-patched/manifest.json'))['cases']
n=d=0
for k in a:
    for f,v in a[k]['files'].items():
        if f=='cmd.log': continue   # contains argv[0] = the binary's path, which differs by design
        n+=1; w=b[k]['files'].get(f)
        if not w or w['sha256']!=v['sha256']: d+=1; print('DIFFERS',k,f)
    extra=set(b[k]['files'])-set(a[k]['files'])
    if extra: d+=1; print('EXTRA FILES',k,extra)
print(f'patched-on-Linux vs golden: {n} files compared, {d} differ; exit codes equal: {all(a[k]["exit"]==b[k]["exit"] for k in a)}')
PY
diff <(sed 's|/opt/splat-bin/|BIN/|' /opt/ref/work/M01/cmd.log) <(sed 's|/opt/patched-bin/|BIN/|' /opt/ref/work-patched/M01/cmd.log) && echo "cmd.log identical apart from the binary path"
