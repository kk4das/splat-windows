<#
.SYNOPSIS
  Stage G8 window test: the 3D path view.
.DESCRIPTION
  Runs a path job through the app (--path-job, K4HTA to Legion_Hall from tests\cases\inputs: a path with
  obstructions), checks the 3D numbers against SPLAT!'s files (--path3d-check), then opens the 3D view
  (--view3d) and drives it through UI Automation as a user would: each viewpoint, the height
  exaggeration, the Fresnel zone and the ground. What the page drew is read from the view's
  accessibility text (rows, columns, obstruction count, viewpoint, the page's own exaggeration
  self-check), and a picture of each viewpoint is kept in the run folder. Then the main window's
  "3D view..." button is checked to open the same view, and a job folder without terrain is refused politely.
.EXAMPLE
  .\scripts\gui_view3d_uitest.ps1 -App .\gui\publish\SplatForWindows.exe -Work .\work\g8-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Sdf3 = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace G8 -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$App = (Resolve-Path $App).Path
if (-not $Sdf3) { $Sdf3 = Join-Path $root 'data\sdf3' }
$inputs = Join-Path $root 'tests\cases\inputs'
$run = New-Item -ItemType Directory (Join-Path (New-Item -ItemType Directory -Force $Work).FullName (Get-Date -Format 'yyyyMMdd-HHmmss'))
$jobs = New-Item -ItemType Directory (Join-Path $run 'jobs')
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $PC = [System.Windows.Automation.PropertyCondition]
$script:fails = 0; $script:n = 0
function Check([bool]$ok, [string]$what, [string]$detail = '') {
    $script:n++; if (-not $ok) { $script:fails++ }
    "[{0}] {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what, $detail
}
function Wait-For([scriptblock]$cond, [int]$ms = 15000) {
    $t = [Diagnostics.Stopwatch]::StartNew()
    while ($t.ElapsedMilliseconds -lt $ms) { try { $r = & $cond; if ($r) { return $r } } catch { }; Start-Sleep -Milliseconds 200 }
    return $null
}
function Site([string]$file) {
    $l = Get-Content (Join-Path $inputs $file); $west = [double]$l[2]
    [ordered]@{ Name = $l[0].Trim(); Lat = [double]$l[1]; Lon = $(if ($west -gt 180) { 360 - $west } else { -$west });
                AntennaHeight = [double]($l[3] -replace '[^0-9.]', ''); HeightUnit = 'Feet'; Source = "test case $file"; Updated = '2026-11-22T00:00:00' }
}
$l = Get-Content (Join-Path $inputs 'k4hta.lrp') | ForEach-Object { ($_ -split ';')[0].Trim() }
$radio = [ordered]@{ Dielectric = [double]$l[0]; Conductivity = [double]$l[1]; Bending = [double]$l[2]; FrequencyMHz = [double]$l[3];
                     Climate = [int]$l[4]; Polarization = [int]$l[5]; Situations = [double]$l[6]; Time = [double]$l[7]; ErpWatts = [double]$l[8] }

# 1. A path job, as the window runs it.
$spec = Join-Path $run 'P03.json'
[ordered]@{ Tx = (Site 'k4hta.qth'); Rx = (Site 'legion.qth'); Radio = $radio; Model = 'Itwom'; Metric = $false; FresnelPercent = 60; Clutter = 0; Hd = $false } |
    ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $spec
$p = Start-Process $App -PassThru -Wait -ArgumentList @('--path-job', "`"$spec`"", '--terrain', "`"$Sdf3`"", '--jobs', "`"$jobs`"")
Check ($p.ExitCode -eq 0) 'path job K4HTA to Legion_Hall ran' "exit $($p.ExitCode)"
$job = (Get-Content "$spec.out")[0]
$report = Get-ChildItem $job -Filter '*-to-*.txt' | Select-Object -First 1
$obsCount = @(Select-String -Path $report.FullName -Pattern '^\s+\d+\.\d{4} [NS],').Count
Check ($obsCount -gt 0) 'the path has obstructions in its report' "$obsCount"

# 2. The 3D numbers against SPLAT!'s files.
$chk = Join-Path $run 'path3d-check.txt'
$p = Start-Process $App -PassThru -Wait -ArgumentList @('--path3d-check', "`"$job`"", "`"$chk`"")
Get-Content $chk | ForEach-Object { "      $_" }
Check ($p.ExitCode -eq 0) '--path3d-check: terrain, curvature, Fresnel zones and obstruction list match SPLAT!' "exit $($p.ExitCode)"

# 3. The 3D view.
function Shot([string]$name) {
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    $r = $script:win.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [G8.Win]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $file = Join-Path $run "$name.png"; $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    # A picture that is one flat color means the 3D drawing wasn't captured (or wasn't drawn).
    $seen = @{}; for ($x = 40; $x -lt $bmp.Width - 40; $x += 37) { for ($y = 80; $y -lt $bmp.Height - 120; $y += 41) { $seen[$bmp.GetPixel($x, $y).ToArgb()] = 1 } }
    $bmp.Dispose()
    return @{ File = $file; Colors = $seen.Count; Ok = $ok }
}
function PageText { $script:page.Current.HelpText }
function Shown([string]$want) { Wait-For { $t = PageText; if ($t -like "*$want*") { $t } } 30000 }
function Btn([string]$name, $type = [System.Windows.Automation.ControlType]::Button) {
    $cond = New-Object System.Windows.Automation.AndCondition((New-Object $PC($AE::NameProperty, $name)), (New-Object $PC($AE::ControlTypeProperty, $type)))
    Wait-For { $script:win.FindFirst($TS::Descendants, $cond) } 10000
}
# The page's own controls aren't in the UI Automation tree (WebView2 content), so the test uses the
# page's keyboard shortcuts, as a keyboard user would: 1-4 viewpoints, + and - height exaggeration.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace G8 -Name Fg -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);'
function Keys([string]$k) {
    [void][G8.Fg]::SetForegroundWindow([IntPtr]$script:win.Current.NativeWindowHandle)
    try { $script:page.SetFocus() } catch { }
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($k); Start-Sleep -Milliseconds 700
}
function Press([string]$name) { $key = @{ 'From K4HTA' = '2'; 'Side' = '3'; 'Overview' = '4'; 'From Legion_Hall' = '1' }[$name]; Keys $key; $true }

$proc = Start-Process $App -PassThru -ArgumentList @('--view3d', "`"$job`"")
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $proc.Id))) } 20000
    Check ($null -ne $script:win) '3D view window opens' $(if ($script:win) { $script:win.Current.Name })
    Check ($script:win.Current.Name -eq '3D view: Legion_Hall to K4HTA - SPLAT! for Windows') 'window title names the path, receiver first' $script:win.Current.Name
    $script:page = Wait-For { $script:win.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, '3D view of the path'))) }
    $t = Shown 'selfcheck='
    Check ($null -ne $t) 'the page drew the path' $t
    Check ($t -match 'selfcheck=ok') 'exaggeration self-check: every point stays on its side of the line of sight at 1x to 20x' (($t -split '; ') | Where-Object { $_ -like 'selfcheck=*' })
    Check ($t -match "obstructions=$obsCount;") "all $obsCount of SPLAT!'s obstruction points are drawn" $t
    Check ($t -match 'rows=(\d+); cols=(\d+)' -and [int]$Matches[1] -gt 50 -and [int]$Matches[2] -gt 40) 'a strip of terrain, rows x columns' "$($Matches[1]) x $($Matches[2])"
    Check ($t -match 'summary=Blocked: SPLAT! finds terrain in the way') 'summary says blocked, as the report does' ($t -replace '.*summary=', '')
    Check ($t -match 'view=rx;') 'opens looking from the receiver''s antenna' ''
    $m = Wait-For { $x = PageText; if ($x -match 'ground=(topo|failed|heights)') { $Matches[1] } } 40000
    Check ($m -eq 'topo') 'topographic map loaded onto the ground (decision G11)' "ground=$m"

    Start-Sleep -Milliseconds 800
    $s = Shot 'view-from-rx'; Check ($s.Ok -and $s.Colors -gt 40) 'picture: from Legion_Hall''s antenna' "$($s.Colors) colors, $($s.File)"
    foreach ($v in @(@('From K4HTA', 'tx', 'view-from-tx'), @('Side', 'side', 'view-side'), @('Overview', 'over', 'view-overview'))) {
        $pressed = Press $v[0]
        $t = Shown "view=$($v[1]);"
        Check ($pressed -and $null -ne $t) "viewpoint button '$($v[0])'" ''
        $s = Shot $v[2]; Check ($s.Ok -and $s.Colors -gt 40) "picture: $($v[2])" "$($s.Colors) colors"
    }
    # Height exaggeration: the slider, through its accessibility value.
    Keys ('{+}' * 40)
    $ok = $null -ne (Shown 'exaggeration=20;')
    Check $ok 'height exaggeration up to 20x (key +, stops at 20) redraws' (PageText | Select-String -Pattern 'selfcheck=\w+' | ForEach-Object { $_.Matches[0].Value })
    Check ((PageText) -match 'selfcheck=ok') 'self-check ok at 20x' ''
    $s = Shot 'view-overview-20x'; Check ($s.Colors -gt 40) 'picture: overview at 20x' "$($s.Colors) colors"
    Press 'Side' | Out-Null; Shown 'view=side;' | Out-Null
    $s = Shot 'view-side-20x'; Check ($s.Colors -gt 40) 'picture: side view at 20x' "$($s.Colors) colors"
    $closed = $script:win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern); $closed.Close()
    Check ($proc.WaitForExit(10000)) 'closing the 3D view closes it' ''
}
finally { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Confirm:$false } }

# 4. A job folder whose terrain is gone: refused politely, no crash.
$gone = Join-Path $run 'job-without-terrain'
Copy-Item $job $gone -Recurse
$rr = Join-Path $gone 'run-again.cmd'
(Get-Content $rr) -replace [regex]::Escape($Sdf3), (Join-Path $run 'no-such-terrain') | Set-Content -Encoding ascii $rr
$proc = Start-Process $App -PassThru -ArgumentList @('--view3d', "`"$gone`"", '--terrain-std-url', 'http://localhost:9/dem3/')
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $proc.Id))) } 20000
    $script:page = Wait-For { $script:win.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, '3D view of the path'))) }
    $t = Shown 'Problem:'
    Check ($t -match 'no longer in' -and $t -match 'Run the path again') 'missing terrain: a plain message, "Run the path again"' $t
    $script:win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
finally { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Confirm:$false } }

"3D view window test: $script:n checks, $script:fails failed (run folder $run)"
