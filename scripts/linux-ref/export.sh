#!/bin/bash
set -euo pipefail
rm -f /opt/ref/golden.tar
cd /opt/ref/work && tar -cf /opt/ref/golden.tar --transform 's/:/_/g' .
cp /opt/ref/golden.tar "/mnt/c/Users/dsoul/AppData/Local/Temp/claude/19cac3bdfffab608/ed97a06d-308c-4435-82d8-3911fe4f8670/scratchpad/golden-linux.tar"
cp /opt/splat/splat.h /opt/ref/; dpkg-query -W -f='${Package} ${Version}\n' g++ libbz2-dev gnuplot-nox libc6 > "/mnt/c/Users/dsoul/AppData/Local/Temp/claude/19cac3bdfffab608/ed97a06d-308c-4435-82d8-3911fe4f8670/scratchpad/ref-versions.txt"
echo exported $(tar -tf /opt/ref/golden.tar | grep -vc '/$') files
