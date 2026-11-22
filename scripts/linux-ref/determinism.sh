#!/bin/bash
set -euo pipefail
cd /opt/ref && rm -rf work2 && python3 run_cases.py --bin /opt/splat-bin --work /opt/ref/work2 --cases cases/cases.json --inputs cases/inputs --hgt3 hgt3 --hgt1 hgt1 > /tmp/run2.log 2>&1
python3 - <<'PY'
import json
a=json.load(open('/opt/ref/work/manifest.json'))['cases']; b=json.load(open('/opt/ref/work2/manifest.json'))['cases']
n=d=0
for k in a:
    for f,v in a[k]['files'].items():
        n+=1
        w=b[k]['files'].get(f)
        if not w or w['sha256']!=v['sha256']: d+=1; print('DIFFERS', k, f)
print(f'determinism: {n} files compared, {d} differ; exit codes equal: {all(a[k]["exit"]==b[k]["exit"] for k in a)}')
PY
