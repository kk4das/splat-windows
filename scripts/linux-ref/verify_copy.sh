#!/bin/bash
set -euo pipefail
echo "whoami=$(whoami) home=$HOME"; ls -ld /tmp /root
cd /opt/splat
find . -type f | sed 's|^\./||' | xargs sha256sum | LC_ALL=C sort -k2 > /tmp/m.txt
LC_ALL=C sort -k2 /mnt/c/Users/dsoul/AppData/Local/Temp/claude/19cac3bdfffab608/ed97a06d-308c-4435-82d8-3911fe4f8670/scratchpad/upstream-manifest.txt > /tmp/u.txt
echo "linux files: $(wc -l < /tmp/m.txt)  manifest: $(wc -l < /tmp/u.txt)"
if cmp -s /tmp/m.txt /tmp/u.txt; then echo "LINUX COPY BYTE-EXACT"; else echo "MISMATCH"; diff /tmp/m.txt /tmp/u.txt | head; exit 1; fi
ls -l README2
