<#
.SYNOPSIS
  Stage G7: prepares the Windows Sandbox clean-PC test: work\sandbox\kit (terrain, job specs,
  expected Linux answers, the in-Sandbox script), work\sandbox\results, and work\sandbox\package-test.wsb.
.DESCRIPTION
  Double-click the .wsb (or Start-Process it) to start a fresh Windows Sandbox. It maps the release
  folder and the kit read-only and the results folder writable, then runs
  scripts\sandbox\sandbox_test.ps1 at logon. Results land in work\sandbox\results\results.txt.
  Needs Windows Sandbox turned on (Windows Features).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_sandbox_kit.ps1
#>
param([string]$Release = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Release) { $Release = Join-Path $root 'release' }
$Release = (Resolve-Path $Release).Path
$base = Join-Path $root 'work\sandbox'
if (Test-Path $base) { Remove-Item -Recurse -Force $base -Confirm:$false }
$kit = New-Item -ItemType Directory (Join-Path $base 'kit')
$res = New-Item -ItemType Directory (Join-Path $base 'results')
New-Item -ItemType Directory (Join-Path $kit 'sdf3'), (Join-Path $kit 'specs'), (Join-Path $kit 'expected') | Out-Null

Copy-Item (Join-Path $root 'data\sdf3\*.sdf') (Join-Path $kit 'sdf3')
Copy-Item (Join-Path $PSScriptRoot 'sandbox\sandbox_test.ps1') $kit
Copy-Item (Join-Path $root 'tests\golden\linux\P01\K4HTA-to-KK4DAS.txt') (Join-Path $kit 'expected\P01-K4HTA-to-KK4DAS.txt')
$m = Get-Content (Join-Path $root 'tests\golden\linux-g5\manifest.json') -Raw | ConvertFrom-Json
Set-Content -Encoding ascii (Join-Path $kit 'expected\M10-los_ngs.ppm.sha256') $m.cases.M10.files.'los_ngs.ppm'.sha256

# The job specs, from the test cases' own files, as the gate scripts build them.
$inputs = Join-Path $root 'tests\cases\inputs'
function Site([string]$file) {
    $l = Get-Content (Join-Path $inputs $file); $west = [double]$l[2]
    [ordered]@{ Name = $l[0].Trim(); Lat = [double]$l[1]; Lon = $(if ($west -gt 180) { 360 - $west } else { -$west });
                AntennaHeight = [double]($l[3] -replace '[^0-9.]', ''); HeightUnit = 'Feet'; Source = "test case $file"; Updated = '2026-11-22T00:00:00' }
}
$l = Get-Content (Join-Path $inputs 'k4hta.lrp') | ForEach-Object { ($_ -split ';')[0].Trim() }
$radio = [ordered]@{ Dielectric = [double]$l[0]; Conductivity = [double]$l[1]; Bending = [double]$l[2]; FrequencyMHz = [double]$l[3];
                     Climate = [int]$l[4]; Polarization = [int]$l[5]; Situations = [double]$l[6]; Time = [double]$l[7]; ErpWatts = [double]$l[8] }
[ordered]@{ Tx = (Site 'k4hta.qth'); Rx = (Site 'kk4das.qth'); Radio = $radio; Model = 'Itwom'; Metric = $false; FresnelPercent = 60; Clutter = 0; Hd = $false } |
    ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $kit 'specs\P01.json')
[ordered]@{ Tx = (Site 'k4hta.qth'); Radio = $radio; Options = [ordered]@{ Kind = 'LineOfSight'; Radius = 20; RxHeight = 6; Metric = $false; Hd = $false; Smooth = $false;
            Model = 'Itwom'; OutputName = 'los_ngs'; CitiesFile = $null; NoShading = $true; UdtFile = $null } } |
    ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $kit 'specs\M10.json')
'{"SetupDone": true}' | Set-Content -Encoding ascii (Join-Path $kit 'specs\settings.json')

$sandboxDesk = 'C:\Users\WDAGUtilityAccount\Desktop'
$wsb = @"
<Configuration>
  <MappedFolders>
    <MappedFolder><HostFolder>$Release</HostFolder><SandboxFolder>$sandboxDesk\release</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$($kit.FullName)</HostFolder><SandboxFolder>$sandboxDesk\kit</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$($res.FullName)</HostFolder><SandboxFolder>$sandboxDesk\results</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sandboxDesk\kit\sandbox_test.ps1</Command>
  </LogonCommand>
  <Networking>Enable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
</Configuration>
"@
[IO.File]::WriteAllText((Join-Path $base 'package-test.wsb'), $wsb, (New-Object Text.UTF8Encoding($false)))   # no BOM
"Sandbox kit ready: $base\package-test.wsb (results will be in $($res.FullName)\results.txt)"
