<#
.SYNOPSIS
  Drives the real "SPLAT! for Windows" window through coverage maps (Stage G4) with Windows UI
  Automation: terrain fetched into an empty folder from a stand-in of the real sources, a line-of-
  sight map, a signal map with its colour key, cancel, and the job history. Stage G5 adds: the click
  readout, the user's stations in the Google Earth file, a map without terrain shading (clear, not
  white), the Settings window (on its own settings file) and the Google Earth on the web steps.
.DESCRIPTION
  Uses its own stations library, an empty terrain folder, its own jobs folder, a throwaway QRZ
  credential entry, its own settings file (--settings), and scripts\fake_terrain.ps1 (the project's
  cached downloads, on localhost). Nothing touches the real library, settings, terrain sources or
  Documents\SPLAT\Jobs. The readout check clicks the middle of the map once with the mouse (the app's
  window is brought to the front for it), then puts the pointer back.
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
Add-Type -Namespace G5 -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
public struct POINT { public int X; public int Y; }
'@
function Click-MapMiddle {
    $r = (ById 'Web').Current.BoundingRectangle
    $old = New-Object G5.Mouse+POINT; [void][G5.Mouse]::GetCursorPos([ref]$old)
    [void][G5.Mouse]::SetForegroundWindow([IntPtr]$script:win.Current.NativeWindowHandle); Start-Sleep -Milliseconds 300
    [void][G5.Mouse]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 150
    [G5.Mouse]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [G5.Mouse]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    [void][G5.Mouse]::SetCursorPos($old.X, $old.Y)
}
# What SPLAT!'s files say a point should read, worked out here independently of the app: the pixel in the
# job's .ppm under the KML's box, and its colour's band in the job's .scf (SPLAT!'s band rule).
function Expected-Band([string]$job, [double]$lat, [double]$lon) {
    [xml]$k = Get-Content (Join-Path $job 'coverage.kml') -Raw
    $box = $k.GetElementsByTagName('LatLonBox')[0]
    $n = [double]$box.north; $s = [double]$box.south; $e = [double]$box.east; $w = [double]$box.west
    $b = [IO.File]::ReadAllBytes((Join-Path $job 'coverage.ppm'))
    $hdr = [Text.Encoding]::ASCII.GetString($b, 0, 32) -split "`n"; $wd, $ht = [int[]]($hdr[1] -split ' '); $start = ($hdr[0..2] -join "`n").Length + 1
    $x = [Math]::Min($wd - 1, [int][Math]::Floor(($lon - $w) / ($e - $w) * $wd)); $y = [Math]::Min($ht - 1, [int][Math]::Floor(($n - $lat) / ($n - $s) * $ht))
    $i = $start + 3 * ($y * $wd + $x); $rgb = $b[$i], $b[$i + 1], $b[$i + 2]
    $bands = @(Get-Content (Get-ChildItem $job -Filter *.scf)[0].FullName | Where-Object { $_ -match '^\s*-?\d+\s*:' } |
        ForEach-Object { $lv, $c = $_ -split ':'; ,(@([int]$lv.Trim()) + [int[]](($c -split ';')[0] -split ',' | ForEach-Object { $_.Trim() })) })
    if ($rgb[0] -eq $rgb[1] -and $rgb[1] -eq $rgb[2]) { return "no colour: below $($bands[-1][0]) dBµV/m" }
    for ($z = 0; $z -lt $bands.Count; $z++) {
        if ($bands[$z][1] -eq $rgb[0] -and $bands[$z][2] -eq $rgb[1] -and $bands[$z][3] -eq $rgb[2]) {
            if ($z -eq 0) { return "$($bands[0][0]) dBµV/m or more" } else { return "$($bands[$z][0]) to $($bands[$z - 1][0]) dBµV/m" }
        }
    }
    return "colour $($rgb -join ',')"
}

$fake = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    "`"$(Join-Path $PSScriptRoot 'fake_terrain.ps1')`"", '-Port', $Port, '-Raw', "`"$(Join-Path $root 'data\raw')`"", '-Log', "`"$log`"", '-Stop', "`"$stop`"")
$null = Wait-For { (Test-Path $log) -and ((Get-Content $log) -contains 'listening') } 15000
Check (Test-Path $log) 'stand-in terrain server started'

$script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$lib`"", '--settings', "`"$(Join-Path $run 'settings.json')`"", '--terrain', "`"$terrain`"", '--jobs', "`"$jobs`"",
    '--qrz-credential', "SPLATforWindows/ui-test/$([guid]::NewGuid())", '--terrain-std-url', "http://localhost:$Port/dem3/", '--terrain-hd-url', "http://localhost:$Port/skadi/")
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    $null = Wait-For { (Text 'Status') -like 'Ready.*' } 20000
    Check ((Text 'Status') -like 'Ready. 2 station(s)*') 'app started on the test library' (Text 'Status')

    # 1. Open the coverage panel.
    Click (ById 'CoverageButton')
    Check (-not (Hidden 'CoveragePanel') -and (Hidden 'StationList')) 'coverage panel opens'
    Check (-not (Hidden 'AddButton') -and -not (Hidden 'ImportButton') -and -not (Hidden 'PathButton') -and -not (Hidden 'CoverageButton') -and -not (Hidden 'BackButton')) 'the four buttons stay at the top, and Back to stations is shown'
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
    Check ($key.Current.BoundingRectangle.Width -ge 50 -and (Text 'CovKeyText') -eq "SPLAT!'s colour key for this map. Click the map to read the value at a point.") 'colour key shown' ("{0}x{1}" -f $key.Current.BoundingRectangle.Width, $key.Current.BoundingRectangle.Height)
    $k = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'K4HTA'
    Check ($k.Radio.FrequencyMHz -eq 146.685 -and $k.Radio.ErpWatts -eq 93) 'radio settings saved with K4HTA' ($k.Radio | ConvertTo-Json -Compress)
    $h = Wait-For { $x = Get-MapList; if ($x.Count -ge 2) { ,$x } } 10000
    Check ($h.Count -eq 2 -and $h[0] -like '*K4HTA, 20 mi, signal strength*' -and $h[1] -like '*K4HTA, 30 mi, line of sight*') 'history: two maps, newest first' ($h -join ' / ')
    $kmz = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SPLATforWindows\map-cache') -Recurse -Filter coverage.kmz | Sort-Object LastWriteTime | Select-Object -Last 1
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($kmz.FullName); $names = @($z.Entries | ForEach-Object FullName); $z.Dispose()
    Check ($names.Count -eq 3 -and $names -contains 'doc.kml' -and $names -contains 'coverage.png' -and $names -contains 'coverage-ck.png') 'Google Earth file carries the colour key' ($names -join ',')
    # G5: the library's stations as pins (KK4DAS; K4HTA is already SPLAT!'s own placemark).
    $z = [IO.Compression.ZipFile]::OpenRead($kmz.FullName); $sr = New-Object IO.StreamReader($z.GetEntry('doc.kml').Open()); [xml]$doc = $sr.ReadToEnd(); $sr.Dispose(); $z.Dispose()
    $folder = @($doc.GetElementsByTagName('Folder') | Where-Object { $_.name -eq 'Stations' })
    $pins = @($folder | ForEach-Object { $_.Placemark } | ForEach-Object { $_.name })
    Check ($folder.Count -eq 1 -and ($pins -join ',') -eq 'KK4DAS') 'G5: Google Earth file has the stations (KK4DAS; K4HTA once, as SPLAT!''s)' ($pins -join ',')

    # G5: the click readout. One click in the middle of the map; the answer must be what SPLAT!'s own files say there.
    $statusBefore = Text 'Status'
    Click-MapMiddle
    $said = Wait-For { $t = Text 'Status'; if ($t -ne $statusBefore -and $t -like '*(read from the map''s colour, so approximate)') { $t } } 10000
    $job2 = Get-ChildItem $jobs -Directory | Sort-Object Name | Select-Object -Last 1
    if ($said -match '^(-?[\d.]+), (-?[\d.]+): (.*) \(read from') {
        $want = Expected-Band $job2.FullName ([double]$Matches[1]) ([double]$Matches[2]); $got = $Matches[3]
        Check ($got.StartsWith($want)) 'G5: click readout matches SPLAT!''s map and colour file at that point' "app: $got / from the files: $want"
    } else { Check $false 'G5: click readout matches SPLAT!''s map and colour file at that point' "status: $(Text 'Status')" }
    Check ((ById 'Web').Current.ItemStatus -like 'Readout: *') 'G5: readout given to screen readers' ((ById 'Web').Current.ItemStatus)

    # 5. Cancel: an HD map (slow), cancelled once SPLAT! is computing.
    (ById 'CovMoreBox').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Tick 'CovHdBox' $true
    SetText 'CovRadiusBox' '30'
    Run-Map
    $computing = Wait-For { $t = Text 'Status'; $t -like 'K4HTA coverage: *Computing*' -or $t -like 'K4HTA coverage: *% to*' } 180000
    Check ([bool]$computing) 'HD terrain fetched, SPLAT! computing' (Text 'Status')
    Check (-not (Hidden 'CovCancelButton') -and -not (ById 'PathButton').Current.IsEnabled -and -not (ById 'BackButton').Current.IsEnabled) 'while running: Cancel shown, switching views waits' ''
    Click (ById 'CovCancelButton')
    $null = Wait-For { (Text 'Status') -like 'Cancelled*' } 20000
    Check ((Text 'Status') -like 'Cancelled. The unfinished job folder is *') 'cancelled' (Text 'Status')
    Check (-not (Get-Process splat-hd -ErrorAction SilentlyContinue)) 'SPLAT! HD stopped'
    Check ((ById 'CovRunButton').Current.IsEnabled -and (Hidden 'CovCancelButton') -and (ById 'PathButton').Current.IsEnabled) 'ready again'
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

    # 6b. G5: a map without terrain shading. SPLAT! paints the no-signal areas white (-ngs); the app shows them clear.
    (ById 'CovMoreBox').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Tick 'CovShadingBox' $false
    Choose 'CovKindBox' 'Line of sight'
    SetText 'CovRadiusBox' '20'
    $cacheBefore = @(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SPLATforWindows\map-cache') -Directory).Count
    Run-Map
    Wait-Map
    Check ((Text 'Status') -like 'Coverage map shown: *K4HTA, 20 mi, line of sight, RX 6 ft*') 'G5: map without terrain shading made' (Text 'Status')
    $job3 = Get-ChildItem $jobs -Directory | Sort-Object Name | Select-Object -Last 1
    $rec = Get-Content (Join-Path $job3.FullName 'job.json') -Raw | ConvertFrom-Json
    Check ($rec.Coverage.NoShading -eq $true -and (Get-Content (Join-Path $job3.FullName 'run-again.cmd') -Raw) -match ' -ngs') 'G5: SPLAT! asked for no shading (-ngs), recorded in job.json' ''
    $merc = Wait-For { Get-ChildItem (Join-Path $env:LOCALAPPDATA 'SPLATforWindows\map-cache') -Recurse -Filter 'coverage-mercator.png' | Sort-Object LastWriteTime | Select-Object -Last 1 } 10000
    $bmp = New-Object Drawing.Bitmap $merc.FullName
    $clear = 0; $solid = 0; $green = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 24) { for ($x = 0; $x -lt $bmp.Width; $x += 24) { $c = $bmp.GetPixel($x, $y); if ($c.A -eq 0) { $clear++ } elseif ($c.A -eq 255) { $solid++; if ($c.G -eq 255 -and $c.R -eq 0) { $green++ } } } }
    $bmp.Dispose()
    Check ($clear -gt 0 -and $green -gt 0) 'G5: the map shown has clear areas (no white) and solid green' "$clear clear, $solid solid ($green green) of the sampled pixels"
    # SPLAT!'s own map in the job folder is left as SPLAT! wrote it: white, not clear (PPM has no transparency).
    $ppm = [IO.File]::ReadAllBytes((Join-Path $job3.FullName 'coverage.ppm')); $white = 0
    for ($i = $ppm.Length - 3 * 2400 * 1200; $i -lt $ppm.Length - 2; $i += 3 * 997) { if ($ppm[$i] -eq 255 -and $ppm[$i + 1] -eq 255 -and $ppm[$i + 2] -eq 255) { $white++ } }
    Check ($white -gt 0) 'G5: SPLAT!''s own map file in the job folder still has its white areas' "$white white of the sampled pixels"
    Tick 'CovShadingBox' $true

    # 6c. G5: Settings, on this test's own settings file; the real one is never touched.
    $real = Join-Path $env:LOCALAPPDATA 'SPLATforWindows\settings.json'
    $realHash = if (Test-Path $real) { (Get-FileHash $real).Hash } else { 'none' }
    Click (ById 'SettingsButton')
    $sw = Wait-For { $AE::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($AE::ProcessIdProperty, $script:proc.Id)), (New-Object $PC($AE::NameProperty, 'Settings'))))) } 10000
    Check ($null -ne $sw) 'G5: Settings window opens'
    if ($sw) {
        Check ((ById 'TerrainText' $sw).Current.Name -like "$terrain*") 'G5: Settings shows the terrain folder in use' (ById 'TerrainText' $sw).Current.Name
        Check ((ById 'EarthText' $sw).Current.Name -like 'Google Earth Pro*') 'G5: Settings says where Google Earth Pro stands' (ById 'EarthText' $sw).Current.Name
        (ById 'RadiusBox' $sw).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('-5')
        Click (ById 'SaveButton' $sw)
        Check ((ById 'ErrorText' $sw).Current.Name -like 'The coverage radius should be*') 'G5: a bad default is refused' (ById 'ErrorText' $sw).Current.Name
        (ById 'RadiusBox' $sw).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('25')
        Click (ById 'SaveButton' $sw)
        $saved = Wait-For { if (Test-Path (Join-Path $run 'settings.json')) { Get-Content (Join-Path $run 'settings.json') -Raw | ConvertFrom-Json } } 5000
        Check ($saved.CoverageRadius -eq 25) 'G5: Settings saved to the test''s own file' "CoverageRadius $($saved.CoverageRadius)"
        $realAfter = if (Test-Path $real) { (Get-FileHash $real).Hash } else { 'none' }
        Check ($realAfter -eq $realHash) 'G5: the real settings file is untouched' ''
    }

    # 6d. G5: Google Earth on the web steps, for the map shown.
    Click (ById 'EarthWebButton')
    $ew = Wait-For { $AE::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($AE::ProcessIdProperty, $script:proc.Id)), (New-Object $PC($AE::NameProperty, 'Google Earth on the web'))))) } 10000
    if ($ew) {
        $f = (ById 'FileBox' $ew).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Check ($f -like '*\coverage.kmz' -and (Test-Path $f) -and (ById 'StepsText' $ew).Current.Name -like '*Import KML file from computer*') 'G5: web steps name the Google Earth file to import' $f
        $ew.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    } else { Check $false 'G5: web steps name the Google Earth file to import' 'window not found' }

    # 7. Close.
    # Straight to path analysis and back, without the stations list in between; what was typed stays.
    Click (ById 'PathButton')
    Check (-not (Hidden 'PathPanel') -and (Hidden 'CoveragePanel')) 'Analyse a path… switches straight from the coverage panel'
    Click (ById 'CoverageButton')
    $radius = (ById 'CovRadiusBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Check (-not (Hidden 'CoveragePanel') -and (Hidden 'PathPanel') -and $radius -eq '20') 'and back, with the radius still as typed' $radius
    Click (ById 'BackButton')
    Start-Sleep -Milliseconds 400
    Check ((Hidden 'CoveragePanel') -and -not (Hidden 'StationList')) 'Back to stations returns to the stations list'
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
