@echo off
rem Removes the "SPLAT! for Windows" Start menu shortcut that "Add Start menu shortcut.cmd" made.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = [IO.Path]::Combine([Environment]::GetFolderPath('Programs'), 'SPLAT! for Windows.lnk'); if (Test-Path $p) { Remove-Item $p; 'Removed the Start menu shortcut.' } else { 'There was no Start menu shortcut to remove.' }"
pause
