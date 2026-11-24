<#
.SYNOPSIS
  Stage G6: drives the real "SPLAT! for Windows" window through the first-run setup, the help
  window (Contents, Index, Search, About, F1, "What does this mean?") and the file choosers for an
  antenna pattern and an obstacles file, with Windows UI Automation.
.DESCRIPTION
  Starts the app as on a first run: an empty stations library and a settings file of its own
  (--settings), so the setup opens. Uses a throwaway QRZ credential entry and an empty terrain folder;
  nothing is downloaded (no map or path is run). Nothing touches the real library, settings,
  terrain or Documents\SPLAT\Jobs. One F1 key press is sent to the app's window (brought to the
  front for it).
.EXAMPLE
  .\scripts\gui_help_uitest.ps1 -App .\gui\publish\SplatForWindows.exe -Work .\work\g6-help-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Shot = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$inputs = Join-Path $root 'tests\cases\inputs'

$Work = (New-Item -ItemType Directory -Force $Work).FullName
$run = (New-Item -ItemType Directory (Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss'))).FullName
$lib = Join-Path $run 'stations.json'          # not there yet: an empty library, as on a first run
$settings = Join-Path $run 'settings.json'     # not there yet: the setup hasn't run
$jobs = Join-Path $run 'jobs'
$terrain = (New-Item -ItemType Directory (Join-Path $run 'terrain')).FullName
$cred = "SPLATforWindows/ui-test/$([guid]::NewGuid())"

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
function ById([string]$id, $r = $script:win) { Wait-For { $r.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))) } }
function ByName([string]$name, $r = $script:win) { Wait-For { $r.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $name))) } }
function Hidden([string]$id, $r = $script:win) { $e = $r.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))); -not $e -or $e.Current.IsOffscreen }
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 300 }
function SetText($el, [string]$t) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function Select-It($el) { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
function TopWindow([string]$title, [int]$ms = 10000) {   # a window of the app: top level, or owned by the main window
    Wait-For {
        $w = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.AndCondition(
            (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id)), (New-Object $PC($AE::NameProperty, $title)))))
        if (-not $w -and $script:win) { $w = $script:win.FindFirst($TS::Children, (New-Object $PC($AE::NameProperty, $title))) }
        $w
    } $ms
}
function Dialog([string]$title) {   # owned dialogs (file pickers, message boxes) sit under the main window
    Wait-For { $d = $script:win.FindFirst($TS::Children, (New-Object $PC($AE::NameProperty, $title))); if (-not $d) { $d = TopWindow $title 300 }; $d }
}
function HelpShows { $h = TopWindow 'SPLAT! for Windows help' 5000; if ($h) { (ById 'Page' $h).Current.HelpText } }
function Wait-Help([string]$like) { Wait-For { $t = HelpShows; if ($t -like $like) { $t } } 15000 }
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class G6W {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
}
"@
function Native($r, [string]$id, [string]$class) {
    Wait-For { $r.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
        (New-Object $PC($AE::AutomationIdProperty, $id)), (New-Object $PC($AE::ClassNameProperty, $class))))) }
}
function FileName($dlg, [string]$t) {   # the Windows file picker, driven with window messages (as the stations test does)
    $e = Native $dlg '1148' 'Edit'; if (-not $e) { $e = Native $dlg '1001' 'Edit' }
    $null = [G6W]::SendMessage([IntPtr]$e.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $t)
    $b = Native $dlg '1' 'Button'
    $null = [G6W]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600
}
function Start-App {
    $script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$lib`"", '--settings', "`"$settings`"", '--terrain', "`"$terrain`"",
        '--jobs', "`"$jobs`"", '--qrz-credential', $cred, '--terrain-std-url', 'http://localhost:9/dem3/', '--terrain-hd-url', 'http://localhost:9/skadi/')
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) | Where-Object { $_.Current.Name -like '*SPLAT! for Windows (preview)*' } } 20000
}
function Stop-App {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(8000)) { $script:proc.Kill() } }
}
function Settings-Json { if (Test-Path $settings) { Get-Content $settings -Raw | ConvertFrom-Json } }
function Menu([string]$item) {   # the Help menu is a pop-up; if it doesn't show (timing), open it again (up to 3 times)
    for ($try = 1; $try -le 3; $try++) {
        Click (ById 'HelpButton')
        $m = Wait-For { $AE::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
            (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)), (New-Object $PC($AE::NameProperty, $item))))) } 4000
        if ($m) { Click $m; return }
        Write-Host "  (Help menu didn't show on try $try)"
    }
    throw "Help menu item '$item' not found after 3 tries"
}
function Shot($el, [string]$file) {
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height); $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [G6W]::PrintWindow([IntPtr]$el.Current.NativeWindowHandle, $hdc, 2); $g.ReleaseHdc($hdc)
    if ($ok) { $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png) }; $g.Dispose(); $bmp.Dispose()
}

try {
    # 1. First start: the setup opens by itself.
    Start-App
    $sw = TopWindow 'Set up SPLAT! for Windows' 20000
    Check ($null -ne $sw) 'first start with no stations: the setup opens'
    if (-not $sw) { throw 'setup window not found' }
    Check ((ById 'StepText' $sw).Current.Name -eq 'Step 1 of 4' -and (Hidden 'BackButton' $sw)) 'step 1 of 4, no Back on the first step' (ById 'StepText' $sw).Current.Name
    Check ($null -eq (Settings-Json) -or -not (Settings-Json).SetupDone) 'nothing saved yet' ''
    Click (ById 'NextButton' $sw)
    Check ((ById 'TerrainText' $sw).Current.Name -like "$terrain*") 'step 2 shows the terrain folder in use' (ById 'TerrainText' $sw).Current.Name
    Click (ById 'NextButton' $sw)
    Check ((ById 'QrzText' $sw).Current.Name -like 'Not signed in*callook.info*') 'step 3: not signed in, callook.info for US calls' (ById 'QrzText' $sw).Current.Name
    Click (ById 'BackButton' $sw)
    Check ((ById 'StepText' $sw).Current.Name -eq 'Step 2 of 4') 'Back goes back a step'
    Click (ById 'NextButton' $sw); Click (ById 'NextButton' $sw)
    Check ((ById 'NextButton' $sw).Current.Name -eq 'Add my first station' -and (ById 'SkipButton' $sw).Current.Name -eq 'Finish without a station') 'step 4 offers to add the first station' ''
    Click (ById 'NextButton' $sw)
    $null = Wait-For { -not (TopWindow 'Set up SPLAT! for Windows' 300) } 5000
    Check (-not (Hidden 'EditorPanel')) 'the station editor opens' ''
    Check ((Settings-Json).SetupDone -eq $true) 'the setup is recorded as done (its own settings file)' ''

    # 2. "What does this mean?" in the editor opens help at the antenna pattern.
    Click (ById 'PatternHelpButton')
    $h = Wait-Help 'Help shows stations.html#antenna-pattern*'
    Check ([bool]$h) '"What does this mean?" opens help at antenna patterns' "$(HelpShows)"

    # 3. Choosing a pattern file (the Windows file picker).
    Click (ById 'PatternButton')
    $dlg = Dialog "Choose the antenna's SPLAT! pattern file (.az; a .el beside it is read too)"
    if ($dlg) { FileName $dlg (Join-Path $inputs 'k4hta_pat.az') }
    $pt = Wait-For { $t = (ById 'PatternText').Current.Name; if ($t -like 'From k4hta_pat.az and k4hta_pat.el*') { $t } } 8000
    Check ([bool]$pt) 'antenna pattern chosen with the file picker (the .el comes too)' (ById 'PatternText').Current.Name

    # 4. Save the first station: the app offers a first coverage map.
    Select-It (ByName 'Coordinates')
    SetText (ById 'FindBox') '38.924216, -77.205793'
    Click (ById 'FindButton')
    $t = (ById 'ConfirmBox').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); if ($t.Current.ToggleState -ne 'On') { $t.Toggle() }
    SetText (ById 'NameBox') 'K4HTA'; SetText (ById 'HeightBox') '240'
    Click (ById 'SaveButton')
    $mb = Dialog 'SPLAT! for Windows'
    $offer = if ($mb) { ($mb.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' ' } else { '' }
    Check ($offer -like '*K4HTA is saved. Make a first coverage map for it now?*') 'after the first station, a first coverage map is offered' $offer
    if ($mb) { $y = Native $mb '6' 'Button'; $null = [G6W]::PostMessage([IntPtr]$y.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }   # IDYES, BM_CLICK
    $hint = Wait-For { $x = (ById 'CovRadioHint').Current.Name; if ($x -like '*K4HTA*') { $x } } 5000
    Check (-not (Hidden 'CoveragePanel') -and $hint -like 'No radio settings for K4HTA yet*') 'Yes opens the coverage panel for K4HTA' "$hint"
    $st = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'K4HTA'
    Check ($st.Pattern.Source -eq 'k4hta_pat.az and k4hta_pat.el' -and $st.Pattern.El.Length -gt 0) 'the pattern is saved with the station' $st.Pattern.Source
    Check ((Get-Content (Join-Path $inputs 'k4hta_pat.az') -Raw) -eq $st.Pattern.Az) 'the pattern file is kept exactly as it was' ''
    Check ((ById 'CovRadioHint').Current.Name -like '*Antenna pattern: azimuth and elevation pattern (k4hta_pat.az and k4hta_pat.el).*') 'the coverage panel says the pattern is used' (ById 'CovRadioHint').Current.Name

    # 5. Obstacles file chosen with the file picker.
    (ById 'CovMoreBox').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Click (ById 'CovUdtButton')
    $dlg = Dialog 'Choose a SPLAT! user terrain file of obstacles (.udt)'
    if ($dlg) { FileName $dlg (Join-Path $inputs 'obstacle.udt') }
    $ut = Wait-For { $x = (ById 'CovUdtText').Current.Name; if ($x -like 'obstacle.udt*') { $x } } 8000
    Check ($ut -eq 'obstacle.udt (49 obstacles)') 'obstacles file chosen with the file picker' (ById 'CovUdtText').Current.Name

    # 6. F1 in the coverage panel: help for coverage maps.
    (ById 'CovRadiusBox').SetFocus()
    [void][G6W]::SetForegroundWindow([IntPtr]$script:win.Current.NativeWindowHandle); Start-Sleep -Milliseconds 400
    (ById 'CovRadiusBox').SetFocus(); Start-Sleep -Milliseconds 200
    [G6W]::keybd_event(0x70, 0, 0, [IntPtr]::Zero); [G6W]::keybd_event(0x70, 0, 2, [IntPtr]::Zero)
    $h = Wait-Help 'Help shows coverage-maps.html*'
    Check ([bool]$h) 'F1 in the coverage panel opens help at coverage maps' "$(HelpShows)"

    # 7. Help > Index: type to narrow it, pick "white circles".
    Menu 'Index'
    $hw = TopWindow 'SPLAT! for Windows help'
    Check ((ById 'IndexTab' $hw).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) 'Help > Index opens the index' ''
    $all = @((ById 'IndexList' $hw).FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))).Count
    SetText (ById 'IndexFilter' $hw) 'white'
    $item = Wait-For { (ById 'IndexList' $hw).FindFirst($TS::Children, (New-Object $PC($AE::NameProperty, 'white circles'))) } 5000
    $few = @((ById 'IndexList' $hw).FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))).Count
    Check ($all -gt 150 -and $few -lt 5 -and $item) 'typing narrows the index' "$all entries, then $few for 'white'"
    if ($item) { Select-It $item }
    Check ([bool](Wait-Help 'Help shows path-analysis.html*')) 'index entry "white circles" opens path analysis' "$(HelpShows)"

    # 8. Search.
    Select-It (ById 'SearchTab' $hw)
    SetText (ById 'SearchBox' $hw) 'color key'
    Click (ById 'SearchButton' $hw)
    $first = Wait-For { (ById 'SearchResults' $hw).FindFirst($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) } 5000
    Check ($first -and $first.Current.Name -eq 'Coverage maps') 'search "color key": Coverage maps first' $(if ($first) { $first.Current.Name })
    Check ([bool](Wait-Help 'Help shows coverage-maps.html#color-key*')) 'and it opens at "Reading the color key"' "$(HelpShows)"
    SetText (ById 'SearchBox' $hw) 'zzqqxx'
    Click (ById 'SearchButton' $hw)
    Check ((ById 'SearchHint' $hw).Current.Name -like 'No page has all of: zzqqxx*') 'a search with no answer says so' (ById 'SearchHint' $hw).Current.Name

    # 9. Contents and About (the version is filled in).
    Menu 'Contents'
    $pages = @((ById 'ContentsList' $hw).FindAll($TS::Children, (New-Object $PC($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) | ForEach-Object { $_.Current.Name })
    Check ($pages.Count -ge 13 -and $pages[0] -eq 'SPLAT! for Windows help' -and $pages[1] -eq 'Getting started') 'Contents lists the pages in order' ($pages -join ' / ')
    Menu 'About SPLAT! for Windows'
    $ver = (Get-Item $App).VersionInfo.ProductVersion.Split('+')[0]
    Check ([bool](Wait-Help "Help shows about.html: About SPLAT! for Windows (version $ver)")) 'About shows the app''s version' "$(HelpShows)"
    if ($Shot) { Shot $hw $Shot }

    # 10. Help > Setup… opens it again; Skip closes it.
    Menu 'Setup…'
    $sw = TopWindow 'Set up SPLAT! for Windows'
    Check ($null -ne $sw) 'Help > Setup… opens the setup again'
    if ($sw) { Click (ById 'SkipButton' $sw) }
    $gone = Wait-For { -not (TopWindow 'Set up SPLAT! for Windows' 300) } 5000
    Check ([bool]$gone) 'Skip setup closes it' ''

    # 11. Closing the main window closes help too (the app ends).
    Stop-App
    Check ($script:proc.HasExited) 'closing the main window closes the app, help window included' ''

    # 12. Second start: no setup.
    Start-App
    $null = Wait-For { (ById 'Status').Current.Name -like 'Ready.*' } 20000
    Start-Sleep -Seconds 3
    Check (-not (TopWindow 'Set up SPLAT! for Windows' 500)) 'second start: the setup doesn''t open again' (ById 'Status').Current.Name
}
finally { Stop-App }
$fail = @($script:results | Where-Object Result -eq 'FAIL').Count
"{0} checks, {1} failed (run folder {2})" -f $script:results.Count, $fail, $run
exit [int]($fail -gt 0)
