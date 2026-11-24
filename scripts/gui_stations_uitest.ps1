<#
.SYNOPSIS
  Drives the real "SPLAT! for Windows" window through the stations features (Stage G1), using
  Windows UI Automation, and checks the results, including the stations file it writes.
.DESCRIPTION
  Uses its own stations file in -Work (never Documents\SPLAT\stations.json). Needs the internet
  for the two callook.info lookups (W1AW, and ZZ9ZZZ which must be refused).
  It does not move the mouse; clicking and dragging on the map itself is checked in the page.
.EXAMPLE
  .\scripts\gui_stations_uitest.ps1 -App .\work\g1-app\SplatForWindows.exe -Work .\work\g1-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Inputs = ''   # default: tests\cases\inputs ($PSScriptRoot is empty in param defaults on PowerShell 5.1)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]

$Work = (New-Item -ItemType Directory -Force $Work).FullName
if (-not $Inputs) { $Inputs = Join-Path $PSScriptRoot '..\tests\cases\inputs' }
$Inputs = (Resolve-Path $Inputs).Path
$lib = Join-Path $Work 'stations.json'
foreach ($f in 'stations.json', 'stations.json.bak', 'K4HTA.qth') { Remove-Item (Join-Path $Work $f) -ErrorAction SilentlyContinue }

$script:results = @()
function Check([bool]$ok, [string]$what, [string]$detail = '') {
    $script:results += [pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $what; Detail = $detail }
    Write-Host ("[{0}] {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $what, $detail)
}
function Wait-For([scriptblock]$cond, [int]$ms = 10000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { $r = & $cond; if ($r) { return $r }; Start-Sleep -Milliseconds 150 }
    return $null
}
function ById($root, [string]$id) {
    Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))) }
}
function ByName($root, [string]$name) {
    Wait-For { $root.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $name))) }
}
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
function SetText($el, [string]$t) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function Text([string]$id) { (ById $script:win $id).Current.Name }
function SelectItem($el) { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 250 }
function Tick([bool]$on) {
    $cb = ById $script:win 'ConfirmBox'
    $t = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($t.Current.ToggleState -eq 'On') -ne $on) { $t.Toggle() }
}
function Unit([string]$u) {
    $cb = ById $script:win 'UnitBox'
    $ec = $cb.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand(); Start-Sleep -Milliseconds 300
    SelectItem (ByName $cb $u)
    $ec.Collapse()
}
function Dialog([string]$title) {
    Wait-For {
        $d = $script:win.FindFirst($TS::Children, (New-Object $PC($AE::NameProperty, $title)))
        if (-not $d) {
            $d = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.AndCondition(
                (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id)), (New-Object $PC($AE::NameProperty, $title)))))
        }
        $d
    }
}
# The Windows file dialogs and message boxes show up here only as plain panes, so their edit field and
# buttons are driven with ordinary window messages (no mouse or keyboard involved).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W32 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
function Native($root, [string]$id, [string]$class) {
    Wait-For { $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
        (New-Object $PC($AE::AutomationIdProperty, $id)), (New-Object $PC($AE::ClassNameProperty, $class))))) }
}
function SetNativeText($el, [string]$t) { $null = [W32]::SendMessage([IntPtr]$el.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $t) }   # WM_SETTEXT
function ClickNative($el) { $null = [W32]::PostMessage([IntPtr]$el.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }   # BM_CLICK
function FileName($dlg, [string]$t) {
    $e = Native $dlg '1148' 'Edit'; if (-not $e) { $e = Native $dlg '1001' 'Edit' }   # open / save dialogs
    SetNativeText $e $t
    ClickNative (Native $dlg '1' 'Button')                                            # Open / Save
}
function Find-Place([string]$tab, [string]$text) {
    SelectItem (ByName $script:win $tab)
    SetText (ById $script:win 'FindBox') $text
    Click (ById $script:win 'FindButton')
}
function Add-Station([string]$tab, [string]$find, [string]$name, [string]$height, [string]$unit = 'ft') {
    Click (ById $script:win 'AddButton')
    Find-Place $tab $find
    if ($tab -eq 'Call sign') { $null = Wait-For { (Text 'NoteText') -like '*mailing address*' -or (Text 'EditorError') } 15000 }
    if ($name) { SetText (ById $script:win 'NameBox') $name }
    SetText (ById $script:win 'HeightBox') $height
    Unit $unit
    Tick $true
    Click (ById $script:win 'SaveButton')
}
function Library { (Get-Content $lib -Raw | ConvertFrom-Json).Stations }

# A throwaway QRZ credential entry that never exists: this test checks the free callook.info path,
# and must not use the owner's real QRZ sign-in (added 2026-11-22 after the owner signed in at G2).
# This test's own settings file, with the first-run setup marked done (gui_help_uitest.ps1 tests the setup).
'{"SetupDone": true}' | Set-Content -Encoding ascii (Join-Path $Work 'settings.json')
$script:proc = Start-Process $App -ArgumentList @('--stations', "`"$lib`"", '--settings', "`"$(Join-Path $Work 'settings.json')`"", '--qrz-credential', "SPLATforWindows/ui-test/$([guid]::NewGuid())") -PassThru
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    Check ($null -ne $script:win) 'window opens'
    $ready = Wait-For { (Text 'Status') -like 'Ready.*' } 20000
    Check ($null -ne $ready) 'map page ready' (Text 'Status')
    Check ((Text 'Status') -like "*0 station(s) in $lib*") 'uses the test stations file, empty'

    # 1. Grid square, and the checks that must stop a save.
    Click (ById $script:win 'AddButton')
    Find-Place 'Grid' 'FM18jy'
    Check ((Text 'EditorError') -like '*letters from A to X*') 'bad grid refused' (Text 'EditorError')
    Find-Place 'Grid' 'fm18jw'
    Check ((Text 'NoteText') -like 'FM18jw is a square about 2.9 × 4.5 mi. The pin is at its centre. That''s too coarse*') 'grid note' (Text 'NoteText')
    Check ((Text 'PinText') -like '38.937500, -77.208333*Grid FM18jw*') 'pin at the square centre' ((Text 'PinText') -replace "`n", ' | ')
    SetText (ById $script:win 'NameBox') 'Post 180'
    SetText (ById $script:win 'HeightBox') '6'
    Click (ById $script:win 'SaveButton')
    Check ((Text 'EditorError') -like '*tick*') 'save refused until the pin is confirmed' (Text 'EditorError')
    Tick $true
    SetText (ById $script:win 'HeightBox') 'six'
    Click (ById $script:win 'SaveButton')
    Check ((Text 'EditorError') -like '*as a number*') 'height must be a number' (Text 'EditorError')
    SetText (ById $script:win 'HeightBox') '6'
    Click (ById $script:win 'SaveButton')
    $null = Wait-For { (Text 'Status') -like 'Saved Post 180*' }
    Check ((Text 'Status') -like 'Saved Post 180 (FM18jw*antenna 6 ft*') 'Post 180 saved' (Text 'Status')

    # 2. Call sign (live callook.info), including one that must be refused.
    Click (ById $script:win 'AddButton')
    Find-Place 'Call sign' 'ZZ9ZZZ'
    $e = Wait-For { $t = Text 'EditorError'; if ($t) { $t } } 15000
    Check ($e -like "*isn't in the FCC database*") 'unknown call refused (live)' $e
    Find-Place 'Call sign' 'KK4DAS/P'
    Check ((Text 'EditorError') -like '*without /P*') 'portable suffix refused before sending' (Text 'EditorError')
    Click (ById $script:win 'CancelButton')
    Add-Station 'Call sign' 'w1aw' '' '30'
    $null = Wait-For { (Text 'Status') -like 'Saved W1AW*' } 15000
    Check ((Text 'Status') -like 'Saved W1AW (FN31pr*') 'W1AW looked up and saved, name filled in' (Text 'Status')

    # 2b. Street address (live US Census + OpenStreetMap, a public address only). Cancelled, not saved.
    Click (ById $script:win 'AddButton')
    Find-Place 'Address' 'P.O. Box 123, Vienna, VA 22183'
    Check ((Text 'EditorError') -like '*PO box is at the post office*') 'PO box refused before sending' (Text 'EditorError')
    Find-Place 'Address' '225 Main St, Newington, CT 06111'
    $null = Wait-For { (Text 'NoteText') -like '*OpenStreetMap*' -or (Text 'EditorError') } 20000
    $st = Text 'Status'   # the list only shows the matches that fit, so count them from the status line
    Check ($st -match '^Found (\d+) matches' -and [int]$Matches[1] -ge 2 -and $st -notmatch '\(') 'several address matches, both services answered' "$st $(Text 'EditorError')"
    $pin = Text 'PinText'
    Check ($pin -match '^41\.714\d+, -72\.72\d+') 'pin on the ARRL building (live)' ($pin -replace "`n", ' | ')
    Check ((Text 'NoteText') -like '*the building or house itself*drag the pin to the antenna*') 'address note says how exact it is' (Text 'NoteText')
    Click (ById $script:win 'CancelButton')

    # 3. Coordinates in deg-min-sec, metres, and a duplicate name.
    Add-Station 'Coordinates' '38°55''27.18"N 77°12''20.85"W' 'K4HTA' '73.15' 'm'
    $null = Wait-For { (Text 'Status') -like 'Saved K4HTA*' }
    Check ((Text 'Status') -like 'Saved K4HTA (FM18jw51*antenna 73.15 m*') 'K4HTA from deg-min-sec, in metres' (Text 'Status')
    Add-Station 'Grid' 'FM18jw' 'k4hta' '10'
    Check ((Text 'EditorError') -like '*already a station called k4hta*') 'duplicate name refused' (Text 'EditorError')
    Click (ById $script:win 'CancelButton')

    # 4. Import .qth files: two new, and K4HTA which exists (answer No: keep ours).
    Click (ById $script:win 'ImportButton')
    $dlg = Dialog 'Import SPLAT! site files'
    Check ($null -ne $dlg) 'import dialog opens'
    FileName $dlg $Inputs; Start-Sleep -Milliseconds 800
    FileName $dlg '"kk4das.qth" "legion.qth" "k4hta.qth"'
    $mb = Dialog 'SPLAT! for Windows'
    Check ($null -ne $mb -and ($mb.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, 'There is already a station called K4HTA. Replace it with the one in k4hta.qth?'))) -ne $null)) 'asks before replacing K4HTA'
    ClickNative (Native $mb '7' 'Button')   # No
    $null = Wait-For { (Text 'Status') -like 'Imported*' }
    Check ((Text 'Status') -like 'Imported KK4DAS (30 ft), Legion_Hall (6 ft)*check them.') 'imported two, heights shown' (Text 'Status')

    # 5. The stations file.
    $s = Library
    Check ($s.Count -eq 5) 'five stations in the file' (($s | ForEach-Object Name) -join ', ')
    $k = $s | Where-Object Name -eq 'K4HTA'
    Check ([math]::Abs($k.Lat - 38.924217) -lt 1e-5 -and [math]::Abs($k.Lon + 77.205792) -lt 1e-5 -and $k.HeightUnit -eq 'Meters') 'K4HTA kept ours, not the import' "$($k.Lat), $($k.Lon), $($k.AntennaHeight) $($k.HeightUnit), $($k.Source)"
    $w = $s | Where-Object Name -eq 'W1AW'
    Check ($w.Lat -eq 41.714707 -and $w.Lon -eq -72.728411 -and $w.Source -eq 'callook.info: W1AW mailing address') 'W1AW position and source' "$($w.Lat), $($w.Lon), $($w.Source)"
    $p = $s | Where-Object Name -eq 'Post 180'
    Check ($p.Source -eq 'Grid FM18jw' -and $p.Lat -eq 38.9375) 'Post 180 source' $p.Source
    Check (($s | Where-Object Name -eq 'KK4DAS').Source -eq 'Imported from kk4das.qth') 'import source recorded'

    # 6. Edit: change the height, keep the position and source.
    SelectItem (ByName (ById $script:win 'StationList') 'Post 180')
    Click (ById $script:win 'EditButton')
    Check ((Text 'PinText') -like '38.937500, -77.208333*') 'edit opens with the saved pin'
    SetText (ById $script:win 'HeightBox') '25'
    Click (ById $script:win 'SaveButton')
    $null = Wait-For { (Text 'Status') -like 'Saved Post 180*' }
    $p = Library | Where-Object Name -eq 'Post 180'
    Check ($p.AntennaHeight -eq 25 -and $p.Source -eq 'Grid FM18jw' -and (Library).Count -eq 5) 'edit saved in place' "$($p.AntennaHeight) ft, $($p.Source)"

    # 7. Export K4HTA as .qth: west-positive longitude, height in metres.
    SelectItem (ByName (ById $script:win 'StationList') 'K4HTA')
    Click (ById $script:win 'ExportButton')
    $dlg = Dialog 'Export K4HTA as a SPLAT! site file'
    $qth = Join-Path $Work 'K4HTA.qth'
    FileName $dlg $qth
    $null = Wait-For { Test-Path $qth }
    $want = "K4HTA`r`n38.924217`r`n77.205792`r`n73.15 meters`r`n"
    $got = if (Test-Path $qth) { [IO.File]::ReadAllText($qth) } else { '' }
    Check ($got -ceq $want) 'exported .qth is what SPLAT! expects' ($got -replace "`r`n", ' | ')

    # 8. Delete (answer Yes), with the previous list kept as .bak.
    SelectItem (ByName (ById $script:win 'StationList') 'Legion_Hall')
    Click (ById $script:win 'DeleteButton')
    $mb = Dialog 'SPLAT! for Windows'
    ClickNative (Native $mb '6' 'Button')   # Yes
    $null = Wait-For { (Text 'Status') -like 'Deleted*' }
    Check ((Library).Count -eq 4 -and -not ((Library).Name -contains 'Legion_Hall')) 'deleted'
    Check (((Get-Content "$lib.bak" -Raw | ConvertFrom-Json).Stations.Count) -eq 5) 'previous list kept as .bak'
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(5000)) { $script:proc.Kill() } }
}
$fail = @($script:results | Where-Object Result -eq 'FAIL').Count
"{0} checks, {1} failed" -f $script:results.Count, $fail
exit [int]($fail -gt 0)
