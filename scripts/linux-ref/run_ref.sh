#!/bin/bash
set -euo pipefail
R="/mnt/c/Users/dsoul/AppData/Local/Claude-Data/claude_pzs8sxrjxfjjc/scratch-workspaces/332ae713-e035-4118-9ffe-050c9ad248c3/6db70c9a-21fe-4432-974d-4016a31839a6/scratch-2026-11-22-0492f3"
rm -rf /opt/splat-bin /opt/ref && mkdir -p /opt/splat-bin /opt/ref/hgt3 /opt/ref/hgt1
cd /opt/splat
cp splat splat-hd utils/srtm2sdf utils/citydecoder utils/usgs2sdf utils/bearing utils/fontdata /opt/splat-bin/
ln -s srtm2sdf /opt/splat-bin/srtm2sdf-hd
cp "$R"/data/hgt3/*.hgt /opt/ref/hgt3/; cp "$R"/data/hgt1/*.hgt /opt/ref/hgt1/
cp -r "$R"/tests/cases /opt/ref/cases; cp "$R"/scripts/run_cases.py /opt/ref/
echo "HOME=$HOME  .splat_path present: $( [ -e ~/.splat_path ] && echo yes || echo no )"
echo "gnuplot: $(gnuplot --version)"
cd /opt/ref && python3 run_cases.py --bin /opt/splat-bin --work /opt/ref/work --cases cases/cases.json --inputs cases/inputs --hgt3 hgt3 --hgt1 hgt1
