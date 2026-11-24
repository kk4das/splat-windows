<#
.SYNOPSIS
  Drives the real "SPLAT! for Windows" window through a path analysis (Stage G3) with Windows UI
  Automation, on the port's test stations and terrain, and checks the numbers against the Linux
  golden reports.
.DESCRIPTION
  Uses its own stations library (the test cases' K4HTA, KK4DAS and Legion_Hall, plus a station
  with no terrain), the project's terrain (--terrain, not saved), and its own jobs folder.
  Nothing touches the real library, settings or Documents\SPLAT\Jobs.
.EXAMPLE
  .\scripts\gui_path_uitest.ps1 -App .\work\g3-app\SplatForWindows.exe -Work .\work\g3-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Shot = ''   # optional: save a screenshot of the window with the result here
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$golden = Join-Path $root 'tests\golden\linux'

$Work = (New-Item -ItemType Directory -Force $Work).FullName
$run = (New-Item -ItemType Directory (Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss'))).FullName
$lib = Join-Path $run 'stations.json'
$jobs = Join-Path $run 'jobs'

# The test library: the port's test sites (tests\cases\inputs), ordinary signs, no radio settings yet.
function St([string]$n, [double]$lat, [double]$lon, [double]$h) { [ordered]@{ Name = $n; Lat = $lat; Lon = $lon; AntennaHeight = $h; HeightUnit = 'Feet'; Source = 'test case'; Updated = '2026-11-22T00:00:00' } }
[ordered]@{ Format = 'SPLATforWindows.stations'; Version = 1; Stations = @(
    (St 'K4HTA' 38.924216 -77.205793 240.0), (St 'KK4DAS' 38.96 -77.28 30.0), (St 'Legion_Hall' 38.905259 -77.270717 6.0),
    (St 'Far North' 41.5 -77.2 30.0)) } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $lib

$script:results = @()
function Check([bool]$ok, [string]$what, [string]$detail = '') {
    $script:results += [pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $what; Detail = $detail }
    Write-Host ("[{0}] {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what, ($detail -replace "`r?`n", ' | '))
}
function Wait-For([scriptblock]$cond, [int]$ms = 10000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { $r = & $cond; if ($r) { return $r }; Start-Sleep -Milliseconds 150 }
    return $null
}
function ById([string]$id, $root = $script:win) { Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))) } }
function ByName([string]$name, $root = $script:win) { Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $name))) } }
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
function SetText([string]$id, [string]$t) { (ById $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function Value([string]$id) { (ById $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Hidden([string]$id) { $e = $script:win.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))); -not $e -or $e.Current.IsOffscreen }
function Text([string]$id) { (ById $id).Current.Name }
function Choose([string]$combo, [string]$item) {
    $c = ById $combo
    $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $i = Wait-For { $c.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $item))) }
    $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 200
}
function Selected([string]$combo) { (ById $combo).GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name } }
function Tab([string]$name) { (ByName $name (ById 'ResultTabs')).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
function ReportText { Tab 'Report'; Value 'ReportBox' }
function Analyze { Click (ById 'AnalyseButton') }
function Wait-Analyzed { Wait-For { (Text 'Status') -like 'Path analyzed*' -or (Text 'PathError') } 60000 | Out-Null }
function Norm([string]$s) { $s -replace "`r`n", "`n" }
function Golden([string]$rel) { Norm ([IO.File]::ReadAllText((Join-Path $golden $rel), [Text.Encoding]::GetEncoding(28591))) }
function JobCount { @(Get-ChildItem $jobs -Directory -ErrorAction SilentlyContinue).Count }

# A copy of the project's terrain, so nothing is ever added to data\sdf3. Terrain sources point at a
# closed local port: a tile the test sites don't have must fail to download, never reach the internet.
$terrain = Join-Path $run 'terrain'
New-Item -ItemType Directory $terrain | Out-Null
Copy-Item (Join-Path $root 'data\sdf3\*.sdf') $terrain
# This test's own settings file, with the first-run setup marked done (gui_help_uitest.ps1 tests the setup).
'{"SetupDone": true}' | Set-Content -Encoding ascii (Join-Path $run 'settings.json')
$script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$lib`"", '--settings', "`"$(Join-Path $run 'settings.json')`"", '--terrain', "`"$terrain`"", '--jobs', "`"$jobs`"", '--qrz-credential', "SPLATforWindows/ui-test/$([guid]::NewGuid())",
    '--terrain-std-url', 'http://localhost:9/dem3/', '--terrain-hd-url', 'http://localhost:9/skadi/')
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    $null = Wait-For { (Text 'Status') -like 'Ready.*' } 20000
    Check ((Text 'Status') -like 'Ready. 4 station(s)*') 'app started on the test library' (Text 'Status')

    # 1. Open the path panel: transmitter and receiver chosen, no radio settings yet.
    # The editor's Back (fixed under the four buttons) leaves without saving.
    Check (Hidden 'BackButton') 'no Back button on the stations list'
    Click (ById 'AddButton')
    Check (-not (Hidden 'EditorPanel') -and (Text 'BackButton') -eq 'Back to stations (nothing saved)') 'the editor shows Back to stations (nothing saved)' (Text 'BackButton')
    Click (ById 'BackButton')
    Check ((Hidden 'EditorPanel') -and -not (Hidden 'StationList') -and (Text 'Status') -eq 'Nothing was changed.') 'Back leaves the editor without saving' (Text 'Status')
    Click (ById 'PathButton')
    Check (-not (Hidden 'PathPanel') -and (Hidden 'StationList') -and -not (Hidden 'CoverageButton') -and -not (Hidden 'BackButton')) 'path panel opens; the four buttons and Back to stations are shown'
    Check ((Selected 'TxBox') -eq 'Far North' -or (Selected 'TxBox') -eq 'K4HTA') 'a transmitter is chosen' (Selected 'TxBox')
    Choose 'TxBox' 'K4HTA'
    Choose 'RxBox' 'KK4DAS'
    Check ((Text 'RadioHint') -like 'Not set for K4HTA yet*') 'no radio settings yet, and it says so' (Text 'RadioHint')
    Check ((Text 'TerrainText') -like '*\terrain (4 .sdf files). Missing tiles are downloaded automatically.') 'terrain folder shown with its tile count' (Text 'TerrainText')

    # 2. Refusals before anything runs.
    Analyze
    Check ((Text 'PathError') -eq 'Enter the frequency in MHz as a number.') 'blank frequency refused' (Text 'PathError')
    SetText 'FreqBox' '146.685'; SetText 'ErpBox' '93'
    Choose 'RxBox' 'Far North'
    Analyze
    $e = Wait-For { $t = Text 'PathError'; if ($t) { $t } } 30000
    Check ($e -like "Terrain couldn't be fetched, so nothing was run: Couldn't download http://localhost:9/dem3/K18.zip*") 'missing terrain fetched; a failed download stops the job' $e
    Check ((JobCount) -eq 0) 'no job folder made for a refused job' "$(JobCount)"
    Choose 'RxBox' 'KK4DAS'

    # 3. P01: K4HTA to KK4DAS, ITWOM. The app's numbers must be the Linux golden report's.
    Analyze
    Wait-Analyzed
    Check ((Text 'Status') -like 'Path analyzed: K4HTA to KK4DAS*') 'path analyzed' (Text 'Status')
    $rep = Norm (ReportText)
    Check ($rep -eq (Golden 'P01\K4HTA-to-KK4DAS.txt')) 'report tab = Linux golden P01, every character'
    $n = Text 'SumNumbers'
    Check ($n -like '*-64.86 dBm*' -and $n -like '*55.73 dBuV/meter*' -and $n -like '*116.68 dB (ITWOM Version 3.0)*' -and $n -like '*93.36 dB*' -and $n -like '*23.33 dB*' -and $n -like '*Double Horizon Diffraction Dominant*') 'summary shows the golden numbers' $n
    Check ((Text 'SumTitle') -eq 'K4HTA to KK4DAS: 4.69 miles, azimuth 301.82 degrees') 'distance and azimuth' (Text 'SumTitle')
    Check ((Text 'SumLos') -like '*blocked by terrain at 3 point(s)*') 'line of sight blocked, 3 obstructions' (Text 'SumLos')
    Check ((Text 'SumClearance') -like '*raised to at least 39.00 feet AGL*first Fresnel zone*183.00 feet AGL to clear 60%*') 'clearance sentences' (Text 'SumClearance')
    $help = Wait-For { $h = (ById 'Web').Current.HelpText; if ($h) { $h } }
    Check ($help -eq 'Map shows the path with 3 obstruction mark(s).') 'map drew the path and 3 obstructions' $help

    # 4. The four graphs, drawn in the app.
    $want = @{ 'Terrain profile' = @('GraphProfile', '*Terrain Profile Between KK4DAS and K4HTA*1 line(s)*');
               'Elevation angle' = @('GraphElevation', '*Elevation Profile Between KK4DAS and K4HTA*2 line(s)*');
               'Height and Fresnel zone' = @('GraphHeight', '*Path Profile Between KK4DAS and K4HTA*With First Fresnel Zone*5 line(s)*');
               'Path loss' = @('GraphLoss', '*Loss Profile Along Path Between KK4DAS and K4HTA*1 line(s)*') }
    foreach ($t in $want.Keys) {
        Tab $t
        $g = ById $want[$t][0]
        Check ($g -and $g.Current.Name -like $want[$t][1] -and $g.Current.BoundingRectangle.Width -gt 300) "graph drawn: $t" $g.Current.Name
    }

    # 5. Saved with the transmitter; the job folder can be re-run from the command line.
    $k = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'K4HTA'
    Check ($k.Radio.FrequencyMHz -eq 146.685 -and $k.Radio.ErpWatts -eq 93 -and $k.Radio.Polarization -eq 1) 'radio settings saved with K4HTA' ($k.Radio | ConvertTo-Json -Compress)
    $job = Get-ChildItem $jobs -Directory | Select-Object -First 1
    Check ((JobCount) -eq 1 -and $job.Name -match '^\d{4}-\d\d-\d\d \d{6} K4HTA to KK4DAS$') 'one job folder, named by date and stations' $job.Name
    # G5: the path's Google Earth file (SPLAT!'s path KML, web-safe), opened by the same button as maps.
    $pk = Join-Path $job.FullName 'K4HTA-to-KK4DAS.kmz'
    Check ((Test-Path $pk) -and (ById 'EarthButton').Current.IsEnabled -and (ById 'EarthWebButton').Current.IsEnabled -and (Text 'SumEarth') -like '*both stations, the path*') 'G5: path Google Earth file made; Google Earth buttons on' (Text 'SumEarth')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($pk); $sr = New-Object IO.StreamReader($z.GetEntry('doc.kml').Open(), [Text.Encoding]::UTF8); $doc = $sr.ReadToEnd(); $sr.Dispose(); $z.Dispose()
    Check ($doc -like ('*38' + [char]0xB0 + " 55' 27`" North*") -and $doc -notlike '*root://*' -and $doc -like '*Line-of-Sight Path*') 'G5: its descriptions show the degree sign, and it opens on the web (no Pro-only icon)' ''
    Check ((Text 'RadioHint') -like '*Antenna: the same in all directions*') 'G5: the transmitter''s antenna (no pattern) is stated' (Text 'RadioHint')
    $copy = Join-Path $run 'rerun'
    Copy-Item $job.FullName $copy -Recurse
    Get-ChildItem $copy -Recurse -File | Where-Object { $_.Extension -notin '.qth', '.lrp', '.cmd' } | Remove-Item
    $null = cmd /c "`"$(Join-Path $copy 'run-again.cmd')`" >nul 2>nul"
    $again = Norm ([IO.File]::ReadAllText((Join-Path $copy 'K4HTA-to-KK4DAS.txt'), [Text.Encoding]::GetEncoding(28591)))
    Check ($again -eq (Golden 'P01\K4HTA-to-KK4DAS.txt')) 'run-again.cmd from the command line gives the same report'
    Check (Test-Path (Join-Path $copy 'graph-height\fresnel.gp')) 'run-again.cmd also makes the graph data'

    # 6. P02 settings: Longley-Rice and 50 W, compared with the Linux golden P02 report.
    (ById 'MoreBox').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Choose 'ModelBox' 'Longley-Rice ITM (-olditm)'
    SetText 'ErpBox' '50'
    Analyze
    Wait-Analyzed
    Check ((Norm (ReportText)) -eq (Golden 'P02\K4HTA-to-KK4DAS.txt')) 'Longley-Rice, 50 W: report = Linux golden P02'
    Check ((Text 'SumWarning') -like 'Longley-Rice error number 3. Warning: A combination of parameters is out of range*') 'model warning shown' (Text 'SumWarning')

    # 7. P04 settings: Legion_Hall, Fresnel 80%, clutter 30 ft, back to ITWOM and 93 W.
    Choose 'ModelBox' 'ITWOM 3.0 (SPLAT!''s default)'
    SetText 'ErpBox' '93'; SetText 'FzBox' '80'; SetText 'ClutterBox' '30'
    Choose 'RxBox' 'Legion_Hall'
    Analyze
    Wait-Analyzed
    Check ((Norm (ReportText)) -eq (Golden 'P04\K4HTA-to-Legion_Hall.txt')) 'Fresnel 80%, clutter 30 ft: report = Linux golden P04'
    Check ((Text 'SumClearance') -like 'Terrain has been raised by 30.00 feet*80% of the first Fresnel zone.') 'clutter and 80% shown' (Text 'SumClearance')
    Check ((ById 'Web').Current.HelpText -eq 'Map shows the path with 6 obstruction mark(s).') 'map updated: 6 obstructions' (ById 'Web').Current.HelpText

    # 8. Metric (P05).
    SetText 'FzBox' '60'; SetText 'ClutterBox' '0'
    Choose 'RxBox' 'KK4DAS'
    Choose 'UnitsBox' 'Kilometers and meters (-metric)'
    Check ((Text 'ClutterLabel') -eq 'Ground clutter (m)') 'clutter label follows the units'
    Analyze
    Wait-Analyzed
    Check ((Norm (ReportText)) -eq (Golden 'P05\K4HTA-to-KK4DAS.txt')) 'metric: report = Linux golden P05'
    Choose 'UnitsBox' 'Miles and feet'

    if ($Shot) {
        Tab 'Height and Fresnel zone'
        Start-Sleep -Milliseconds 800
        # PrintWindow asks the app's own window to draw itself, so nothing else on the screen is captured,
        # even if another window covers it (PW_RENDERFULLCONTENT = 2, needed for the WebView2 map).
        Add-Type -Namespace G3 -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);'
        $r = $script:win.Current.BoundingRectangle
        $bmp = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height)
        $gfx = [Drawing.Graphics]::FromImage($bmp)
        $hdc = $gfx.GetHdc()
        $ok = [G3.Win]::PrintWindow([IntPtr]$script:win.Current.NativeWindowHandle, $hdc, 2)
        $gfx.ReleaseHdc($hdc)
        if ($ok) { $bmp.Save($Shot, [Drawing.Imaging.ImageFormat]::Png) }
        $gfx.Dispose(); $bmp.Dispose()
    }

    # 9. Straight to the coverage panel: the path's results go; then back to path analysis.
    Click (ById 'CoverageButton')
    Check (-not (Hidden 'CoveragePanel') -and (Hidden 'PathPanel') -and (Hidden 'ResultTabs')) 'Coverage map… switches straight from path analysis, clearing its results'
    Click (ById 'PathButton')
    Check (-not (Hidden 'PathPanel') -and (Hidden 'CoveragePanel')) 'and back to path analysis'
    # Back to stations: the list, results and the map path gone.
    Click (ById 'BackButton')
    Start-Sleep -Milliseconds 400
    Check ((Hidden 'PathPanel') -and -not (Hidden 'StationList')) 'Back to stations returns to the stations list'
    Check (Hidden 'ResultTabs') 'results hidden'
    Check ((JobCount) -eq 4) 'four job folders, one per analysis' "$(JobCount)"
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(5000)) { $script:proc.Kill() } }
}
$fail = @($script:results | Where-Object Result -eq 'FAIL').Count
"{0} checks, {1} failed (run folder {2})" -f $script:results.Count, $fail, $run
exit [int]($fail -gt 0)
