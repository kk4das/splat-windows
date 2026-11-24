#!/bin/bash
# Stage G5: run tests/cases/cases-g5.json on the Linux reference build (same binaries as Stage 1).
# Run as: wsl -d splat-ref -u root -- bash /mnt/d/ClaudeAI/projects/SPLATWIN/scripts/linux-ref/run_g5.sh
set -euo pipefail
R="/mnt/d/ClaudeAI/projects/SPLATWIN"
rm -rf /opt/ref-g5 && mkdir -p /opt/ref-g5
cp -r "$R"/tests/cases /opt/ref-g5/cases; cp "$R"/scripts/run_cases.py /opt/ref-g5/
cd /opt/ref-g5 && python3 run_cases.py --bin /opt/splat-bin --work /opt/ref-g5/work --cases cases/cases-g5.json \
   --inputs cases/inputs --hgt3 /opt/ref/hgt3 --hgt1 /opt/ref/hgt1
# Export without the terrain, with ':' -> '_' in names (Windows can't store colons).
rm -rf "$R"/data/golden-linux-g5 && mkdir -p "$R"/data/golden-linux-g5
cd /opt/ref-g5/work
for d in P09 P10 M09 M10; do mkdir -p "$R/data/golden-linux-g5/$d"; for f in "$d"/*; do cp "$f" "$R/data/golden-linux-g5/$d/$(basename "$f" | tr ':' '_')"; done; done
cp manifest.json "$R"/data/golden-linux-g5/
sha256sum /opt/splat-bin/splat
