<#
.SYNOPSIS
  Stage G3 gate: the app's own path jobs must give the same answers as the Linux golden run.
.DESCRIPTION
  Runs the published app headless (--path-job) for the port's path test cases, with the stations
  and radio settings of tests\cases\inputs, exactly as the window would. Then it lays the app's
  outputs out like a test run and compares them with tests\golden\linux using the Stage 3/4
  comparer (scripts\compare_outputs.py, with the ratified last-digit tolerance for .gp data).

  What is compared, per case (the rest of each golden case is listed as not compared, with why):
    P01  report folder                 K4HTA to KK4DAS, ITWOM
    P02  report folder                 Longley-Rice (-olditm), ERP 50 W
    P03  graph-height folder           K4HTA to Legion_Hall: report and the height/Fresnel graph data
    P04  report folder                 Fresnel 80%, ground clutter 30 ft
    P05  graph-loss folder             metric units: report and path-loss graph data
    P08  graph-profile folder (HD)     splat-hd on 1" terrain: report and terrain-profile data
.EXAMPLE
  .\scripts\gui_path_equiv.ps1 -App .\work\g3-app\SplatForWindows.exe -Work .\work\g3-equiv
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Python = 'python',
    # Terrain: data\sdf3 (standard) and the Stage 4 suite's 1" tiles (converted on Windows, identical to the Linux goldens).
    [string]$Sdf3 = '',
    [string]$Sdf1 = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$inputs = Join-Path $root 'tests\cases\inputs'
$golden = Join-Path $root 'tests\golden\linux'
if (-not $Sdf3) { $Sdf3 = Join-Path $root 'data\sdf3' }
if (-not $Sdf1) { $Sdf1 = Join-Path $root 'work\windows-s4\sdf1' }
$Work = (New-Item -ItemType Directory -Force $Work).FullName
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$run = New-Item -ItemType Directory (Join-Path $Work $stamp)
$jobs = New-Item -ItemType Directory (Join-Path $run 'jobs')

function Site([string]$file) {
    $l = Get-Content (Join-Path $inputs $file)
    $west = [double]$l[2]
    [ordered]@{ Name = $l[0].Trim(); Lat = [double]$l[1]; Lon = $(if ($west -gt 180) { 360 - $west } else { -$west });
                AntennaHeight = [double]($l[3] -replace '[^0-9.]', ''); HeightUnit = 'Feet'; Source = "test case $file"; Updated = '2026-11-22T00:00:00' }
}
function Radio([double]$erp) {
    $l = Get-Content (Join-Path $inputs 'k4hta.lrp') | ForEach-Object { ($_ -split ';')[0].Trim() }
    [ordered]@{ Dielectric = [double]$l[0]; Conductivity = [double]$l[1]; Bending = [double]$l[2]; FrequencyMHz = [double]$l[3];
                Climate = [int]$l[4]; Polarization = [int]$l[5]; Situations = [double]$l[6]; Time = [double]$l[7]; ErpWatts = $erp }
}
$k4hta = Site 'k4hta.qth'; $kk4das = Site 'kk4das.qth'; $legion = Site 'legion.qth'

# case id, receiver, ERP, model, metric, Fresnel %, clutter, HD, which job folder to compare
$cases = @(
    @{ Id = 'P01'; Rx = $kk4das; Erp = 93; Model = 'Itwom'; Metric = $false; Fz = 60; Gc = 0; Hd = $false; Sub = '' },
    @{ Id = 'P02'; Rx = $kk4das; Erp = 50; Model = 'LongleyRice'; Metric = $false; Fz = 60; Gc = 0; Hd = $false; Sub = '' },
    @{ Id = 'P03'; Rx = $legion; Erp = 93; Model = 'Itwom'; Metric = $false; Fz = 60; Gc = 0; Hd = $false; Sub = 'graph-height' },
    @{ Id = 'P04'; Rx = $legion; Erp = 93; Model = 'Itwom'; Metric = $false; Fz = 80; Gc = 30; Hd = $false; Sub = '' },
    @{ Id = 'P05'; Rx = $kk4das; Erp = 93; Model = 'Itwom'; Metric = $true; Fz = 60; Gc = 0; Hd = $false; Sub = 'graph-loss' },
    @{ Id = 'P08'; Rx = $kk4das; Erp = 93; Model = 'Itwom'; Metric = $false; Fz = 60; Gc = 0; Hd = $true; Sub = 'graph-profile' }
)
# Golden files not compared, and why. Everything else in each golden case must match.
$notCompared = @{
    'stdout.txt' = 'console output: the app passes an absolute terrain path (-d), and the app never runs gnuplot'
    '*.png'      = 'graph pictures: the app draws graphs itself from the .gp data, which is compared'
}
$renamed = @{ 'P05' = 'loss_metric.png'; 'P08' = 'profile_hd.png' }   # the golden graph's file name; the app's is loss.png / profile.png
$skipGp = @{ 'P04' = 'normalized-height (-H) graph data: the app draws the height graph (-h), compared in P03' }

$lin = New-Item -ItemType Directory (Join-Path $run 'linux'); $win = New-Item -ItemType Directory (Join-Path $run 'windows')
$lm = @{}; $wm = @{}; $notes = @(); $problems = 0
foreach ($c in $cases) {
    $spec = Join-Path $run "$($c.Id).json"
    [ordered]@{ Tx = $k4hta; Rx = $c.Rx; Radio = (Radio $c.Erp); Model = $c.Model; Metric = $c.Metric; FresnelPercent = $c.Fz; Clutter = $c.Gc; Hd = $c.Hd } |
        ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $spec
    $terrain = if ($c.Hd) { $Sdf1 } else { $Sdf3 }
    $p = Start-Process $App -PassThru -Wait -ArgumentList @('--path-job', "`"$spec`"", '--terrain', "`"$terrain`"", '--jobs', "`"$($jobs.FullName)`"")
    if ($p.ExitCode -ne 0 -or -not (Test-Path "$spec.out")) {
        Write-Host "[FAIL] $($c.Id): the app's job failed (exit $($p.ExitCode)): $(if (Test-Path "$spec.err") { Get-Content "$spec.err" -Raw })"
        $problems++; continue
    }
    $out = Get-Content "$spec.out"
    $folder = $out[0]; $exit = [int](($out | Where-Object { $_ -match "^$([regex]::Escape($(if ($c.Sub) { $c.Sub } else { '.' })))\t" }) -split "`t")[1]
    $src = if ($c.Sub) { Join-Path $folder $c.Sub } else { $folder }

    # The golden case, minus what isn't compared.
    $gfiles = @{}
    New-Item -ItemType Directory (Join-Path $lin $c.Id) | Out-Null
    foreach ($f in Get-ChildItem (Join-Path $golden $c.Id) -File) {
        $why = $null
        if ($f.Name -eq 'stdout.txt') { $why = $notCompared['stdout.txt'] }
        elseif ($f.Extension -eq '.png') { $why = $notCompared['*.png'] }
        elseif ($skipGp[$c.Id] -and $f.Extension -eq '.gp') { $why = $skipGp[$c.Id] }
        elseif ($renamed[$c.Id] -and $f.Name -eq 'splat.gp') { $why = "checked separately below (its output-file line names $($renamed[$c.Id]))" }
        if ($why) { $notes += "$($c.Id)  $($f.Name): not compared - $why"; continue }
        Copy-Item $f.FullName (Join-Path $lin $c.Id)
        $gfiles[$f.Name] = @{ sha256 = (Get-FileHash $f.FullName).Hash.ToLower(); size = $f.Length }
    }
    $lm[$c.Id] = @{ exit = 0; files = $gfiles }

    # What the app's job produced there: SPLAT!'s outputs, not the app's own inputs and log.
    $wfiles = @{}
    New-Item -ItemType Directory (Join-Path $win $c.Id) | Out-Null
    foreach ($f in Get-ChildItem $src -File | Where-Object { $_.Extension -notin '.qth', '.lrp' -and $_.Name -notin 'run-again.cmd', 'splat-log.txt' }) {
        if ($renamed[$c.Id] -and $f.Name -eq 'splat.gp') { continue }
        Copy-Item $f.FullName (Join-Path $win $c.Id)
        $wfiles[$f.Name] = @{ sha256 = (Get-FileHash $f.FullName).Hash.ToLower(); size = $f.Length }
    }
    $wm[$c.Id] = @{ exit = $exit; files = $wfiles }

    # splat.gp for renamed graphs: identical apart from the "set output" line.
    if ($renamed[$c.Id]) {
        $g = (Get-Content (Join-Path $golden "$($c.Id)\splat.gp") -Encoding Byte -Raw)
        $a = [Text.Encoding]::GetEncoding(28591).GetString($g) -replace "set output `"[^`"]*`"", 'set output X'
        $b = [IO.File]::ReadAllText((Join-Path $src 'splat.gp'), [Text.Encoding]::GetEncoding(28591)) -replace "`r`n", "`n" -replace "set output `"[^`"]*`"", 'set output X'
        if ($a -eq $b) { Write-Host "[PASS] $($c.Id)  splat.gp identical apart from the output file name" }
        else { Write-Host "[FAIL] $($c.Id)  splat.gp differs beyond the output file name"; $problems++ }
    }
}
@{ cases = $lm } | ConvertTo-Json -Depth 5 | Set-Content -Encoding ascii (Join-Path $lin 'manifest.json')
@{ cases = $wm } | ConvertTo-Json -Depth 5 | Set-Content -Encoding ascii (Join-Path $win 'manifest.json')
$notes | ForEach-Object { Write-Host $_ }
Write-Host "--- compare_outputs.py (Stage 3/4 comparer, last-digit tolerance for .gp) ---"
& $Python (Join-Path $root 'scripts\compare_outputs.py') --linux $lin.FullName --windows $win.FullName --cases (Join-Path $root 'tests\cases\cases.json') --last-digit
$cmp = $LASTEXITCODE
Write-Host ("GUI path equivalence: {0} (comparer exit {1}, {2} other problem(s)); run folder {3}" -f $(if ($cmp -eq 0 -and $problems -eq 0) { 'EQUIVALENT' } else { 'NOT EQUIVALENT' }), $cmp, $problems, $run.FullName)
exit [int]($cmp -ne 0 -or $problems -gt 0)
