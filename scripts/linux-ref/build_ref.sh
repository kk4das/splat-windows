#!/bin/bash
set -uo pipefail
cd /opt/splat
chmod +x configure build install clean utils/build utils/install utils/postdownload docs/*/man/docmaker
echo "chmod +x applied to the 9 upstream shell scripts (GitHub copy has mode 100644)"
export TERM=dumb
printf '8\n4\n' | ./configure > /tmp/configure.log 2>&1; echo "configure exit=$?"
grep -vE '^\s*$|\*\*|Please select|Maximum Analysis|=====|\([0-9]\) ' /tmp/configure.log | tail -20
echo "--- std-parms.h"; cat std-parms.h; echo "--- hd-parms.h"; cat hd-parms.h
echo "--- binaries"; for f in splat splat-hd utils/citydecoder utils/usgs2sdf utils/srtm2sdf utils/srtm2sdf-hd utils/fontdata utils/bearing; do printf '%-22s ' $f; file -b $f | cut -c1-60; done
echo "--- installed"; ls -l /usr/local/bin
echo "--- banner"; /usr/local/bin/splat 2>&1 | head -3; /usr/local/bin/splat-hd 2>&1 | head -3
