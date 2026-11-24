<#
  Runs INSIDE Windows Sandbox (a fresh, throwaway Windows) at logon. Stage G7's clean-PC test.
  Mapped by the .wsb that scripts\make_sandbox_kit.ps1 writes:
    Desktop\release  (read only)  the release zip, as club members get it
    Desktop\kit      (read only)  this script, terrain (4 tiles), job specs and the expected answers
    Desktop\results  (writable)   what this script finds: results.txt
  Checks: what this Windows has (no .NET SDK, no MSYS2); unzip; SPLAT! runs on its own; a path and a
  line-of-sight map through the app give the Linux answers; the Start-menu scripts work. Then it opens
  the app as a first-time user sees it, for the owner to look at.
#>
$ErrorActionPreference = 'Continue'
$desk = [Environment]::GetFolderPath('Desktop')
$kit = Join-Path $desk 'kit'; $rel = Join-Path $desk 'release'; $res = Join-Path $desk 'results'
$out = Join-Path $res 'results.txt'
$script:fails = 0
function Say([string]$s) { Add-Content -Path $out -Value $s -Encoding utf8; Write-Host $s }
function Check([bool]$ok, [string]$what, [string]$detail = '') {
    if (-not $ok) { $script:fails++ }
    Say ("[{0}] {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what, $detail)
}
Set-Content -Path $out -Value "Windows Sandbox clean-PC test, $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8

# 1. What this Windows has.
$os = Get-CimInstance Win32_OperatingSystem
Say "Windows: $($os.Caption) $($os.Version) ($($os.OSArchitecture))"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
Check ($null -eq $dotnet) 'no .NET SDK or dotnet command on this PC' $(if ($dotnet) { $dotnet.Source } else { 'none' })
Check (-not (Test-Path 'C:\msys64')) 'no MSYS2 (the build toolchain) on this PC'
$wv = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -ErrorAction SilentlyContinue).pv
Say "WebView2 runtime: $(if ($wv) { $wv } else { 'not registered (Windows 11 has it built in with Edge)' })"
Say "Google Earth Pro: $(if (Test-Path 'C:\Program Files\Google\Google Earth Pro\client\googleearth.exe') { 'installed' } else { 'not installed' })"

# 2. Unzip, as a club member would (to a folder of their own).
$zip = Get-ChildItem $rel -Filter 'SPLATforWindows-*-win64.zip' | Select-Object -First 1
Say "Package: $($zip.Name), SHA-256 $((Get-FileHash $zip.FullName -Algorithm SHA256).Hash.ToLower())"
$expected = (Get-Content (Join-Path $rel ($zip.Name + '.sha256'))).Split(' ')[0]
Check ((Get-FileHash $zip.FullName -Algorithm SHA256).Hash.ToLower() -eq $expected) 'the zip matches its published SHA-256'
$home2 = 'C:\SPLATtest'; New-Item -ItemType Directory -Force $home2 | Out-Null
Expand-Archive $zip.FullName -DestinationPath $home2 -Force
$app = Get-ChildItem $home2 -Recurse -Filter SplatForWindows.exe | Select-Object -First 1
Check ($null -ne $app) 'unzipped; SplatForWindows.exe is there' $app.FullName
$dir = $app.DirectoryName

# 3. SPLAT! on its own (no DLLs from a build toolchain needed).
$p = Start-Process (Join-Path $dir 'engine\splat.exe') -PassThru -Wait -NoNewWindow -RedirectStandardOutput (Join-Path $res 'splat-usage.txt')
$usage = Get-Content (Join-Path $res 'splat-usage.txt') -Raw
Check ($usage -match 'SPLAT!' -and $usage -match 'Available Options') 'engine\splat.exe runs on its own (prints its option list)' "exit $($p.ExitCode)"
$p = Start-Process (Join-Path $dir 'engine\splat-hd.exe') -PassThru -Wait -NoNewWindow -RedirectStandardOutput (Join-Path $res 'splat-hd-usage.txt')
Check ((Get-Content (Join-Path $res 'splat-hd-usage.txt') -Raw) -match 'SPLAT! HD' -and (Get-Content (Join-Path $res 'splat-hd-usage.txt') -Raw) -match 'Available Options') 'engine\splat-hd.exe runs on its own (prints its option list)' "exit $($p.ExitCode)"

# 4. A path and a line-of-sight map through the app, against the Linux answers.
$terrain = 'C:\SPLATterrain'; New-Item -ItemType Directory -Force $terrain | Out-Null
Copy-Item (Join-Path $kit 'sdf3\*.sdf') $terrain
$jobs = 'C:\SPLATjobs'
$spec = 'C:\SPLATspecs'; New-Item -ItemType Directory -Force $spec | Out-Null
Copy-Item (Join-Path $kit 'specs\*.json') $spec
$p = Start-Process $app.FullName -PassThru -Wait -ArgumentList @('--path-job', "`"$spec\P01.json`"", '--terrain', $terrain, '--jobs', $jobs, '--settings', "$spec\settings.json")
$o = if (Test-Path "$spec\P01.json.out") { Get-Content "$spec\P01.json.out" } else { @() }
$report = if ($o) { Join-Path $o[0] 'K4HTA-to-KK4DAS.txt' } else { '' }
$got = if ($report -and (Test-Path $report)) { (Get-Content $report -Raw) -replace "`r`n", "`n" } else { '' }
$want = (Get-Content (Join-Path $kit 'expected\P01-K4HTA-to-KK4DAS.txt') -Raw) -replace "`r`n", "`n"
Check ($p.ExitCode -eq 0 -and $got -eq $want) 'path K4HTA to KK4DAS: the report is the Linux report, character for character' "exit $($p.ExitCode)$(if (Test-Path "$spec\P01.json.err") { ': ' + (Get-Content "$spec\P01.json.err" -Raw) })"
$p = Start-Process $app.FullName -PassThru -Wait -ArgumentList @('--coverage-job', "`"$spec\M10.json`"", '--terrain', $terrain, '--jobs', $jobs, '--settings', "$spec\settings.json",
    '--terrain-std-url', 'http://localhost:9/dem3/')
$o = if (Test-Path "$spec\M10.json.out") { Get-Content "$spec\M10.json.out" } else { @() }
$ppm = if ($o) { Join-Path $o[0] 'los_ngs.ppm' } else { '' }
$hash = if ($ppm -and (Test-Path $ppm)) { (Get-FileHash $ppm -Algorithm SHA256).Hash.ToLower() } else { 'no map' }
$wantHash = (Get-Content (Join-Path $kit 'expected\M10-los_ngs.ppm.sha256')).Trim()
Check ($p.ExitCode -eq 0 -and $hash -eq $wantHash) 'line-of-sight map, 20 mi, no shading: byte-identical to the Linux map' "exit $($p.ExitCode), $hash$(if (Test-Path "$spec\M10.json.err") { ': ' + (Get-Content "$spec\M10.json.err" -Raw) })"

# 5. The Start-menu shortcut scripts (each ends with "pause": given no input, it returns).
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'SPLAT! for Windows.lnk'
cmd /c "`"$dir\Add Start menu shortcut.cmd`" < nul" | Out-Null
$target = if (Test-Path $lnk) { (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath } else { '' }
Check ($target -eq $app.FullName) 'Add Start menu shortcut.cmd makes a shortcut to the app' $target
cmd /c "`"$dir\Remove Start menu shortcut.cmd`" < nul" | Out-Null
Check (-not (Test-Path $lnk)) 'Remove Start menu shortcut.cmd removes it'

Say ''
Say $(if ($script:fails -eq 0) { 'CLEAN-PC TEST: ALL PASSED' } else { "CLEAN-PC TEST: $($script:fails) FAILED" })
Say 'The app is now open as a first-time user sees it: the setup should be showing. Look around (Help, the map), then close the Sandbox window; nothing is kept.'

# 6. Leave the app open for the owner, as on a first start (its own settings and stations: none yet).
Start-Process $app.FullName
