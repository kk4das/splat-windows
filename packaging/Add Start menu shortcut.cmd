@echo off
rem Adds "SPLAT! for Windows" to your own Start menu (this Windows account only; no admin needed).
rem The shortcut points at SplatForWindows.exe in this folder, so run this from where you unzipped it.
setlocal
set "EXE=%~dp0SplatForWindows.exe"
if not exist "%EXE%" (
  echo SplatForWindows.exe isn't in this folder. Unzip the whole zip first, then run this from that folder.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s = (New-Object -ComObject WScript.Shell).CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Programs'), 'SPLAT! for Windows.lnk')); $s.TargetPath = $env:EXE; $s.WorkingDirectory = [IO.Path]::GetDirectoryName($env:EXE); $s.Description = 'SPLAT! RF path and coverage analysis'; $s.Save()"
if errorlevel 1 (
  echo The shortcut couldn't be made.
) else (
  echo Done: "SPLAT! for Windows" is in your Start menu.
)
pause
