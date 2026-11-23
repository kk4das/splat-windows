<#
.SYNOPSIS
  Drives the real "SPLAT! for Windows" window through coverage maps (Stage G4) with Windows UI
  Automation: terrain fetched into an empty folder from a stand-in of the real sources, a line-of-
  sight map, a signal map with its colour key, cancel, and the job history.
.DESCRIPTION
  Uses its own stations library, an empty terrain folder, its own jobs folder, a throwaway QRZ
  credential entry, and scripts\fake_terrain.ps1 (the project's cached downloads, on localhost).
  Nothing touches the real library, settings, terrain sources or Documents\SPLAT\Jobs.
.EXAMPLE
  .\scripts\gui_coverage_uitest.ps1 -App .\gui\publish\SplatForWindows.exe -Work .\work\g4-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [int]$Port = 18733,
    [string]$Shot = ''   # optional: a picture of the app's own window (PrintWindow; nothing else on screen)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$Work = (New-Item -ItemType Directory -Force $Work).FullName
$run = (New-Item -ItemType Directory (Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss'))).FullName
$lib = Join-Path $run 'stations.json'
$jobs = Join-Path $run 'jobs'
$terrain = (New-Item -ItemType Directory (Join-Path $run 'terrain')).FullName
$log = Join-Path $run 'fake-terrain.log'; $stop = Join-Path $run 'fake-terrain.stop'

function St([string]$n, [double]$lat, [double]$lon, [double]$h) { [ordered]@{ Name = $n; Lat = $lat; Lon = $lon; AntennaHeight = $h; HeightUnit = 'Feet'; Source = 'test case'; Updated = '2026-11-22T00:00:00' } }
[ordered]@{ Format = 'SPLATforWindows.stations'; Version = 1; Stations = @(
    (St 'K4HTA' 38.924216 -77.205793 240.0), (St 'KK4DAS' 38.96 -77.28 30.0)) } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $lib

$script:results = @()
function Check([bool]$ok, [string]$what, [string]$detail = '') {
    $script:results += [pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $what; Detail = $detail }
    Write-Host ("[{0}] {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what, ($detail -replace "`r?`n", ' | '))
}
function Wait-For([scriptblock]$cond, [int]$ms = 10000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { $r = & $cond; if ($r) { return $r }; Start-Sleep -Milliseconds 200 }
    return $null
}
function ById([string]$id, $root = $script:win) { Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))) } }
function ByName([string]$name, $root = $script:win) { Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $name))) } }
function Hidden([string]$id) { $e = $script:win.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))); -not $e -or $e.Current.IsOffscreen }
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
function SetText([string]$id, [string]$t) { (ById $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function Text([string]$id) { (ById $id).Current.Name }
function Choose([string]$combo, [string]$item) {
    $c = ById $combo
    $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $i = Wait-For { $c.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $item))) }
    $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 200
}
function Tick([string]$id, [bool]$on) {
    $t = (ById $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($t.Current.ToggleState -eq 'On') -ne $on) { $t.Toggle() }
}
function Get-MapList { @((ById 'CovHistory').FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) | ForEach-Object { $_.Current.Name }) }
function Requests { @(Get-Content $log | Where-Object { $_ -ne 'listening' }) }
function Run-Map { Click (ById 'CovRunButton') }
function Wait-Map([int]$ms = 180000) { Wait-For { (Text 'Status') -like 'Coverage map shown*' -or (Text 'CovError') } $ms | Out-Null }
function SdfCount { @(Get-ChildItem $terrain -Filter *.sdf).Count }

$fake = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    "`"$(Join-Path $PSScriptRoot 'fake_terrain.ps1')`"", '-Port', $Port, '-Raw', "`"$(Join-Path $root 'data\raw')`"", '-Log', "`"$log`"", '-Stop', "`"$stop`"")
$null = Wait-For { (Test-Path $log) -and ((Get-Content $log) -contains 'listening') } 15000
Check (Test-Path $log) 'stand-in terrain server started'

$script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$lib`"", '--terrain', "`"$terrain`"", '--jobs', "`"$jobs`"",
    '--qrz-credential', "SPLATforWindows/ui-test/$([guid]::NewGuid())", '--terrain-std-url', "http://localhost:$Port/dem3/", '--terrain-hd-url', "http://localhost:$Port/skadi/")
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    $null = Wait-For { (Text 'Status') -like 'Ready.*' } 20000
    Check ((Text 'Status') -like 'Ready. 2 station(s)*') 'app started on the test library' (Text 'Status')

    # 1. Open the coverage panel.
    Click (ById 'CoverageButton')
    Check (-not (Hidden 'CoveragePanel') -and (Hidden 'AddButton')) 'coverage panel opens'
    Choose 'CovTxBox' 'K4HTA'
    Check ((Text 'CovRadioHint') -like 'No radio settings for K4HTA yet*') 'no radio settings yet, and it says so' (Text 'CovRadioHint')
    Check ((Text 'CovTerrainText') -like '*\terrain (0 .sdf files). Missing tiles are downloaded automatically.') 'empty terrain folder shown' (Text 'CovTerrainText')
    Check ((Get-MapList).Count -eq 0 -and (Text 'CovHistoryHint') -like 'None yet*') 'no earlier maps yet' (Text 'CovHistoryHint')

    # 2. Refusals before anything runs or downloads.
    Run-Map
    Check ((Text 'CovError') -eq 'Enter the frequency in MHz as a number.') 'blank frequency refused' (Text 'CovError')
    SetText 'CovFreqBox' '146.685'; SetText 'CovErpBox' '93'; SetText 'CovRadiusBox' '200'
    Run-Map
    Check ((Text 'CovError') -like 'SPLAT! can map at most about 155 miles around K4HTA*') 'too large a radius refused, with the limit' (Text 'CovError')
    Check ((Requests).Count -eq 0 -and -not (Test-Path $jobs)) 'nothing downloaded, no job folder' "$((Requests).Count) request(s)"

    # 3. Line of sight, 30 mi, RX 6 ft: the terrain is fetched first (from the stand-in), then the map.
    SetText 'CovRadiusBox' '30'; SetText 'CovRxBox' '6'
    Choose 'CovKindBox' 'Line of sight'
    Check ((Text 'CovKindHint') -like '*can see the transmitter*No radio settings needed*') 'line-of-sight hint' (Text 'CovKindHint')
    Run-Map
    Wait-Map
    Check ((Text 'Status') -like 'Coverage map shown: *K4HTA, 30 mi, line of sight, RX 6 ft*') 'line-of-sight map made' (Text 'Status')
    Check ((SdfCount) -eq 4) 'the 4 terrain tiles were fetched and converted' "$(SdfCount) .sdf"
    Check (((Requests) -join ' ') -eq 'GET /dem3/J18.zip 200') 'one download (J18.zip), from the stand-in only' ((Requests) -join ' / ')
    Check ((Text 'CovResultNote') -like '*Terrain fetched: 38_39_77_78.sdf: downloaded and converted; 38_39_76_77.sdf: converted from the saved download*') 'the result says what was fetched' (Text 'CovResultNote')
    Check ((Text 'CovKeyText') -eq 'Green: a receiver antenna at 6 ft has line of sight to K4HTA.') 'line-of-sight key explained' (Text 'CovKeyText')
    $help = Wait-For { $h = (ById 'Web').Current.HelpText; if ($h -like 'Map shows coverage.kml*') { $h } } 20000
    Check ($help -like 'Map shows coverage.kml: 2400×2400 map, 38° to 39.999° N, -78° to -76.001° E') 'overlay shown on the map, with its extent (SPLAT!''s box)' $help
    Check ((ById 'EarthButton').Current.IsEnabled -and (ById 'OpacitySlider').Current.IsEnabled) 'Google Earth and opacity enabled'
    $job1 = Get-ChildItem $jobs -Directory | Select-Object -First 1
    Check ($job1.Name -match '^\d{4}-\d\d-\d\d \d{6} K4HTA coverage$' -and (Test-Path (Join-Path $job1.FullName 'coverage.ppm')) -and (Test-Path (Join-Path $job1.FullName 'run-again.cmd'))) 'job folder with the map and run-again.cmd' $job1.Name
    $k = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'K4HTA'
    Check ($null -eq $k.Radio) 'line of sight saves no radio settings'

    # 4. Signal strength, 20 mi: no new download; SPLAT!'s colour key; radio settings saved.
    Choose 'CovKindBox' 'Signal strength (dBµV/m)'
    SetText 'CovRadiusBox' '20'
    Run-Map
    Wait-Map
    Check ((Text 'Status') -like 'Coverage map shown: *K4HTA, 20 mi, signal strength, RX 6 ft*') 'signal map made' (Text 'Status')
    Check ((Requests).Count -eq 1) 'no new download (terrain kept)' "$((Requests).Count) request(s)"
    $key = ById 'CovKeyImage'
    Check ($key.Current.BoundingRectangle.Width -ge 50 -and (Text 'CovKeyText') -eq "SPLAT!'s colour key for this map.") 'colour key shown' ("{0}x{1}" -f $key.Current.BoundingRectangle.Width, $key.Current.BoundingRectangle.Height)
    $k = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'K4HTA'
    Check ($k.Radio.FrequencyMHz -eq 146.685 -and $k.Radio.ErpWatts -eq 93) 'radio settings saved with K4HTA' ($k.Radio | ConvertTo-Json -Compress)
    $h = Wait-For { $x = Get-MapList; if ($x.Count -ge 2) { ,$x } } 10000
    Check ($h.Count -eq 2 -and $h[0] -like '*K4HTA, 20 mi, signal strength*' -and $h[1] -like '*K4HTA, 30 mi, line of sight*') 'history: two maps, newest first' ($h -join ' / ')
    $kmz = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SPLATforWindows\map-cache') -Recurse -Filter coverage.kmz | Sort-Object LastWriteTime | Select-Object -Last 1
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($kmz.FullName); $names = @($z.Entries | ForEach-Object FullName); $z.Dispose()
    Check ($names.Count -eq 3 -and $names -contains 'doc.kml' -and $names -contains 'coverage.png' -and $names -contains 'coverage-ck.png') 'Google Earth file carries the colour key' ($names -join ',')

    # 5. Cancel: an HD map (slow), cancelled once SPLAT! is computing.
    (ById 'CovMoreBox').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Tick 'CovHdBox' $true
    SetText 'CovRadiusBox' '30'
    Run-Map
    $computing = Wait-For { $t = Text 'Status'; $t -like 'K4HTA coverage: *Computing*' -or $t -like 'K4HTA coverage: *% to*' } 180000
    Check ([bool]$computing) 'HD terrain fetched, SPLAT! computing' (Text 'Status')
    Click (ById 'CovCloseButton')   # "Cancel" while running
    $null = Wait-For { (Text 'Status') -like 'Cancelled*' } 20000
    Check ((Text 'Status') -like 'Cancelled. The unfinished job folder is *') 'cancelled' (Text 'Status')
    Check (-not (Get-Process splat-hd -ErrorAction SilentlyContinue)) 'SPLAT! HD stopped'
    Check ((ById 'CovRunButton').Current.IsEnabled -and (Text 'CovCloseButton') -eq 'Close') 'ready again'
    Check ((Get-MapList).Count -eq 2) 'a cancelled map is not in the history'
    Tick 'CovHdBox' $false

    # 6. History: show the first map again, without re-running.
    $before = @(Get-ChildItem $jobs -Directory).Count
    $item = ((ById 'CovHistory').FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))[1]
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $null = Wait-For { (Text 'CovResultNote') -like 'From the job history*' } 20000
    Check ((Text 'CovResultText') -like '*K4HTA, 30 mi, line of sight, RX 6 ft' -and (Text 'CovResultNote') -like 'From the job history*') 'history item shown again' (Text 'CovResultText')
    Check (@(Get-ChildItem $jobs -Directory).Count -eq $before) 'nothing re-run'

    if ($Shot) {
        Add-Type -Namespace G4 -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);'
        $item = ((ById 'CovHistory').FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))[0]
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Seconds 3
        $r = $script:win.Current.BoundingRectangle
        $bmp = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height); $gfx = [Drawing.Graphics]::FromImage($bmp); $hdc = $gfx.GetHdc()
        $ok = [G4.Win]::PrintWindow([IntPtr]$script:win.Current.NativeWindowHandle, $hdc, 2); $gfx.ReleaseHdc($hdc)
        if ($ok) { $bmp.Save($Shot, [Drawing.Imaging.ImageFormat]::Png) }; $gfx.Dispose(); $bmp.Dispose()
    }

    # 7. Close.
    Click (ById 'CovCloseButton')
    Start-Sleep -Milliseconds 400
    Check ((Hidden 'CoveragePanel') -and -not (Hidden 'AddButton')) 'Close returns to the stations list'
    Check (-not ((Requests) | Where-Object { $_ -notlike 'GET /dem3/*' -and $_ -notlike 'GET /skadi/*' })) 'only the stand-in terrain server was asked' ((Requests) -join ' / ')
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(5000)) { $script:proc.Kill() } }
    New-Item -ItemType File $stop -Force | Out-Null
    if (-not $fake.WaitForExit(5000)) { $fake.Kill() }
}
$fail = @($script:results | Where-Object Result -eq 'FAIL').Count
"{0} checks, {1} failed (run folder {2})" -f $script:results.Count, $fail, $run
exit [int]($fail -gt 0)
