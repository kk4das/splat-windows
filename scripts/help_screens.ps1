<#
.SYNOPSIS
  Makes the pictures for Help > Getting started (gui\SplatWin.App\help\images): drives the real app
  through the steps with UI Automation and captures only the app's own windows (PrintWindow).
.DESCRIPTION
  Uses the project's test stations (the K4HTA repeater site and KK4DAS's rounded position, from
  tests\cases\inputs), a fresh library and settings file of its own, the project's terrain
  (data\sdf3, read only) and a jobs folder under -Work. The map background needs the internet.
  Nothing touches the real library, settings or Documents\SPLAT\Jobs.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\help_screens.ps1 -App gui\publish\SplatForWindows.exe -Work work\help-screens
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [string]$Out = '',
    [int]$Width = 1280, [int]$Height = 800
)
$ErrorActionPreference = 'Stop'
if (-not $Out) { $Out = Join-Path $PSScriptRoot '..\gui\SplatWin.App\help\images' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $PC = [System.Windows.Automation.PropertyCondition]
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Work = (New-Item -ItemType Directory -Force $Work).FullName
$run = (New-Item -ItemType Directory (Join-Path $Work (Get-Date -Format 'yyyyMMdd-HHmmss'))).FullName
$Out = (New-Item -ItemType Directory -Force $Out).FullName
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class HS {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
function Wait-For([scriptblock]$cond, [int]$ms = 10000) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { $got = & $cond; if ($got) { return $got }; Start-Sleep -Milliseconds 200 }; $null }
function ById([string]$id, $r = $script:win) { Wait-For { $r.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id))) } }
function ByName([string]$n, $r = $script:win) { Wait-For { $r.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $n))) } }
function Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 300 }
function SetText([string]$id, [string]$t) { (ById $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t) }
function Choose([string]$combo, [string]$item) {
    $c = ById $combo; $e = $c.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern); $e.Expand(); Start-Sleep -Milliseconds 300
    (Wait-For { $c.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $item))) }).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $e.Collapse(); Start-Sleep -Milliseconds 200
}
function Owned([string]$title) { Wait-For { $script:win.FindFirst($TS::Children, (New-Object $PC($AE::NameProperty, $title))) } 20000 }
function Native($parent, [string]$id) { Wait-For { $parent.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($AE::AutomationIdProperty, $id)), (New-Object $PC($AE::ClassNameProperty, 'Button'))))) } }
function Capture($el, [string]$file, [int]$maxWidth = 1100) {
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height); $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [HS]::PrintWindow([IntPtr]$el.Current.NativeWindowHandle, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    if (-not $ok) { throw "PrintWindow failed for $file" }
    $scale = [Math]::Min(1.0, $maxWidth / $bmp.Width)
    $small = New-Object Drawing.Bitmap([int]($bmp.Width * $scale), [int]($bmp.Height * $scale))
    $g2 = [Drawing.Graphics]::FromImage($small); $g2.InterpolationMode = 'HighQualityBicubic'; $g2.DrawImage($bmp, 0, 0, $small.Width, $small.Height); $g2.Dispose(); $bmp.Dispose()
    $path = Join-Path $Out $file
    if ($file.EndsWith('.jpg')) {
        $codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
        $ep = New-Object Drawing.Imaging.EncoderParameters(1); $ep.Param[0] = New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality, [long]88)
        $small.Save($path, $codec, $ep)
    } else { $small.Save($path, [Drawing.Imaging.ImageFormat]::Png) }
    $small.Dispose()
    Write-Host ("  {0}: {1:N0} bytes" -f $file, (Get-Item $path).Length)
}
function Add-Station([string]$coords, [string]$name, [string]$height, [string]$shot = '') {
    (ByName 'Coordinates').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    SetText 'FindBox' $coords; Click (ById 'FindButton')
    $t = (ById 'ConfirmBox').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); if ($t.Current.ToggleState -ne 'On') { $t.Toggle() }
    SetText 'NameBox' $name; SetText 'HeightBox' $height
    if ($shot) { Start-Sleep -Seconds 5; Capture $script:win $shot }   # time for the map tiles
    Click (ById 'SaveButton')
}

'{}' | Set-Content -Encoding ascii (Join-Path $run 'settings.json')   # a first run: the setup opens
$script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$(Join-Path $run 'stations.json')`"", '--settings', "`"$(Join-Path $run 'settings.json')`"",
    '--terrain', "`"$(Join-Path $root 'data\sdf3')`"", '--jobs', "`"$(Join-Path $run 'jobs')`"", '--qrz-credential', "SPLATforWindows/ui-test/$([guid]::NewGuid())")
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    [void][HS]::MoveWindow([IntPtr]$script:win.Current.NativeWindowHandle, 40, 40, $Width, $Height, $true)
    # 1. The setup's first page.
    $setup = Owned 'Set up SPLAT! for Windows'
    Start-Sleep -Milliseconds 800
    Capture $setup 'gs-wizard.png' 700
    foreach ($i in 1..3) { Click (ById 'NextButton' $setup) }
    Click (ById 'NextButton' $setup)          # Add my first station
    # 2. Adding a station (the pin on the map).
    Add-Station '38.924216, -77.205793' 'K4HTA' '240' 'gs-add-station.jpg'
    $mb = Owned 'SPLAT! for Windows'
    $null = [HS]::PostMessage([IntPtr](Native $mb '7').Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600   # No
    Click (ById 'AddButton')
    Add-Station '38.96, -77.28' 'KK4DAS' '30'
    # 3. A path analysis.
    Click (ById 'PathButton')
    Choose 'TxBox' 'K4HTA'; Choose 'RxBox' 'KK4DAS'
    SetText 'FreqBox' '146.685'; SetText 'ErpBox' '93'
    Click (ById 'AnalyseButton')
    $null = Wait-For { (ById 'Status').Current.Name -like 'Path analysed*' } 60000
    Start-Sleep -Seconds 5
    Capture $script:win 'gs-path.jpg'
    # 4. A coverage map.
    Click (ById 'CoverageButton')
    Choose 'CovTxBox' 'K4HTA'
    SetText 'CovRadiusBox' '30'; SetText 'CovRxBox' '6'
    Click (ById 'CovRunButton')
    $null = Wait-For { (ById 'Status').Current.Name -like 'Coverage map shown*' } 120000
    Start-Sleep -Seconds 6
    Capture $script:win 'gs-coverage.jpg'
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(8000)) { $script:proc.Kill() } }
}
Write-Host "Pictures in $Out"
