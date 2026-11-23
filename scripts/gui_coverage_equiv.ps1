<#
.SYNOPSIS
  Stage G4 check: the app's own coverage jobs, starting from an EMPTY terrain folder, must give the
  same maps as the Linux golden run.
.DESCRIPTION
  For each case the published app runs headless (--coverage-job), exactly as the window does:
  it fetches the terrain from a stand-in of the real sources (scripts\fake_terrain.ps1, serving the
  project's cached downloads), converts it with the engine's srtm2sdf, and runs SPLAT! with -kml.
  The outputs are compared with data\golden-linux by scripts\compare_outputs.py.
    M01  line of sight, 30 mi, RX 6 ft, town names (standard terrain)
    M07  signal strength, 30 mi, RX 6 ft (HD terrain; the 7200x7200 map and its colour key)
.EXAMPLE
  .\scripts\gui_coverage_equiv.ps1 -App .\gui\publish\SplatForWindows.exe -Work .\work\g4-equiv
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Python = 'python',
    [int]$Port = 18732
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$inputs = Join-Path $root 'tests\cases\inputs'
$golden = Join-Path $root 'data\golden-linux'
$Work = (New-Item -ItemType Directory -Force $Work).FullName
$run = New-Item -ItemType Directory (Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss'))
$jobs = New-Item -ItemType Directory (Join-Path $run 'jobs')
$log = Join-Path $run 'fake-terrain.log'; $stop = Join-Path $run 'fake-terrain.stop'

$l = Get-Content (Join-Path $inputs 'k4hta.qth')
$k4hta = [ordered]@{ Name = $l[0].Trim(); Lat = [double]$l[1]; Lon = - [double]$l[2]; AntennaHeight = [double]$l[3]; HeightUnit = 'Feet'; Source = 'test case'; Updated = '2026-11-22T00:00:00' }
$p = Get-Content (Join-Path $inputs 'k4hta.lrp') | ForEach-Object { ($_ -split ';')[0].Trim() }
$radio = [ordered]@{ Dielectric = [double]$p[0]; Conductivity = [double]$p[1]; Bending = [double]$p[2]; FrequencyMHz = [double]$p[3]; Climate = [int]$p[4]; Polarization = [int]$p[5]; Situations = [double]$p[6]; Time = [double]$p[7]; ErpWatts = [double]$p[8] }
$cases = @(
    @{ Id = 'M01'; Kind = 'LineOfSight'; Hd = $false; Name = 'los'; Cities = (Join-Path $inputs 'cities.dat') },
    @{ Id = 'M07'; Kind = 'FieldStrength'; Hd = $true; Name = 'hd_fs'; Cities = $null }
)
$notCompared = @{ 'stdout.txt' = 'console output: the app passes an absolute terrain path (-d)'; 'cmd.log' = 'the -log command record: the app keeps its own (splat-log.txt, run-again.cmd)' }

$fake = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    "`"$(Join-Path $PSScriptRoot 'fake_terrain.ps1')`"", '-Port', $Port, '-Raw', "`"$(Join-Path $root 'data\raw')`"", '-Log', "`"$log`"", '-Stop', "`"$stop`"")
$problems = 0
$lm = @{}; $wm = @{}
$lin = New-Item -ItemType Directory (Join-Path $run 'linux'); $win = New-Item -ItemType Directory (Join-Path $run 'windows')
$gm = (Get-Content (Join-Path $golden 'manifest.json') -Raw | ConvertFrom-Json).cases
try {
    $sw = [Diagnostics.Stopwatch]::StartNew(); while (-not ((Test-Path $log) -and ((Get-Content $log) -contains 'listening'))) { if ($sw.Elapsed.TotalSeconds -gt 15) { throw 'stand-in terrain server did not start' }; Start-Sleep -Milliseconds 200 }
    foreach ($c in $cases) {
        $terrain = New-Item -ItemType Directory (Join-Path $run "terrain-$($c.Id)")   # empty: everything is fetched
        $spec = Join-Path $run "$($c.Id).json"
        [ordered]@{ Tx = $k4hta; Radio = $radio; Options = [ordered]@{ Kind = $c.Kind; Radius = 30; RxHeight = 6; Metric = $false; Hd = $c.Hd; Smooth = $false; Model = 'Itwom'; OutputName = $c.Name; CitiesFile = $c.Cities } } |
            ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $spec
        $t0 = Get-Date
        $proc = Start-Process $App -PassThru -Wait -ArgumentList @('--coverage-job', "`"$spec`"", '--terrain', "`"$($terrain.FullName)`"", '--jobs', "`"$($jobs.FullName)`"",
            '--terrain-std-url', "http://localhost:$Port/dem3/", '--terrain-hd-url', "http://localhost:$Port/skadi/")
        if ($proc.ExitCode -ne 0 -or -not (Test-Path "$spec.out")) {
            Write-Host "[FAIL] $($c.Id): the app's job failed (exit $($proc.ExitCode)): $(if (Test-Path "$spec.err") { Get-Content "$spec.err" -Raw })"; $problems++; continue
        }
        $out = Get-Content "$spec.out"
        Write-Host ("[ OK ] {0}: app job finished in {1:0} s; terrain: {2}" -f $c.Id, ((Get-Date) - $t0).TotalSeconds, (($out | Select-Object -Skip 2) -join '; '))
        $folder = $out[0]

        $gfiles = @{}; New-Item -ItemType Directory (Join-Path $lin $c.Id) | Out-Null
        foreach ($name in $gm.($c.Id).files.PSObject.Properties.Name) {
            if ($notCompared[$name]) { Write-Host "  $($c.Id)  $($name): not compared - $($notCompared[$name])"; continue }
            Copy-Item (Join-Path $golden "$($c.Id)\$name") (Join-Path $lin $c.Id)
            $gfiles[$name] = $gm.($c.Id).files.$name
        }
        $lm[$c.Id] = @{ exit = 0; files = $gfiles }
        $wfiles = @{}; New-Item -ItemType Directory (Join-Path $win $c.Id) | Out-Null
        foreach ($f in Get-ChildItem $folder -File | Where-Object { $_.Extension -notin '.qth', '.lrp' -and $_.Name -notin 'run-again.cmd', 'splat-log.txt', 'job.json', 'cities.dat' }) {
            $name = $f.Name
            Copy-Item $f.FullName (Join-Path $win "$($c.Id)\$name")
            $wfiles[$name] = @{ sha256 = (Get-FileHash $f.FullName).Hash.ToLower(); size = $f.Length }
        }
        $wm[$c.Id] = @{ exit = [int]$out[1]; files = $wfiles }
        $sdfs = @(Get-ChildItem $terrain -Filter *.sdf).Count
        Write-Host "  $($c.Id)  terrain folder now holds $sdfs .sdf file(s), all made by the app from the stand-in downloads"
    }
}
finally { New-Item -ItemType File $stop -Force | Out-Null; if (-not $fake.WaitForExit(5000)) { $fake.Kill() } }
Write-Host "  stand-in terrain server requests: $((Get-Content $log | Where-Object { $_ -ne 'listening' }) -join ' / ')"
@{ cases = $lm } | ConvertTo-Json -Depth 5 | Set-Content -Encoding ascii (Join-Path $lin 'manifest.json')
@{ cases = $wm } | ConvertTo-Json -Depth 5 | Set-Content -Encoding ascii (Join-Path $win 'manifest.json')
Write-Host "--- compare_outputs.py (Stage 3/4 comparer) ---"
& $Python (Join-Path $root 'scripts\compare_outputs.py') --linux $lin.FullName --windows $win.FullName --cases (Join-Path $root 'tests\cases\cases.json') --last-digit
$cmp = $LASTEXITCODE
Write-Host ("GUI coverage equivalence: {0} (comparer exit {1}, {2} other problem(s)); run folder {3}" -f $(if ($cmp -eq 0 -and $problems -eq 0) { 'EQUIVALENT' } else { 'NOT EQUIVALENT' }), $cmp, $problems, $run.FullName)
exit [int]($cmp -ne 0 -or $problems -gt 0)
