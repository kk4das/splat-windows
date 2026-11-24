<#
.SYNOPSIS
  Drives the real "SPLAT! for Windows" window through QRZ.com sign-in and lookup (Stage G2) with
  Windows UI Automation, against a stand-in QRZ service on localhost (scripts\fake_qrz.ps1).
.DESCRIPTION
  Never talks to the real QRZ.com and never touches the real sign-in: the app is started with
  --qrz-url (the stand-in) and --qrz-credential (a throwaway Credential Manager entry, removed at
  the end). The test account's password is the stand-in's "test-password".
.EXAMPLE
  .\scripts\gui_qrz_uitest.ps1 -App .\gui\publish\SplatForWindows.exe -Work .\work\g2-uitest
#>
param(
    [Parameter(Mandatory)] [string]$App,
    [Parameter(Mandatory)] [string]$Work,
    [int]$Port = 18731
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]

$Work = (New-Item -ItemType Directory -Force $Work).FullName
$lib = Join-Path $Work 'stations.json'
$log = Join-Path $Work 'fake-qrz.log'
$stop = Join-Path $Work 'fake-qrz.stop'
foreach ($f in $lib, "$lib.bak", $log, $stop) { Remove-Item $f -ErrorAction SilentlyContinue }
$samples = (Resolve-Path (Join-Path $PSScriptRoot '..\gui\SplatWin.Tests\samples')).Path
$target = 'SPLATforWindows/ui-test/' + [guid]::NewGuid()
function Saved { -not ((cmdkey "/list:$target" | Out-String) -match 'NONE') }

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
function SignInWindow { Wait-For { $AE::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition(
    (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id)), (New-Object $PC($AE::NameProperty, 'QRZ.com sign-in'))))) } }
function SignIn([string]$user, [string]$pass) {
    Click (ById $script:win 'QrzButton')
    $script:dlg = SignInWindow
    SetText (ById $script:dlg 'UserBox') $user
    SetText (ById $script:dlg 'PassBox') $pass
    Click (ById $script:dlg 'SignInButton')
}
function DlgText([string]$id) { (ById $script:dlg $id).Current.Name }
function Find-Call([string]$call) {
    SelectItem (ByName $script:win 'Call sign')
    SetText (ById $script:win 'FindBox') $call
    Click (ById $script:win 'FindButton')
}

$fake = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    "`"$(Join-Path $PSScriptRoot 'fake_qrz.ps1')`"", '-Port', $Port, '-Samples', "`"$samples`"", '-Log', "`"$log`"", '-Stop', "`"$stop`"")
$null = Wait-For { (Test-Path $log) -and ((Get-Content $log) -contains 'listening') } 15000
Check (Test-Path $log) 'stand-in QRZ service started'

# This test's own settings file, with the first-run setup marked done (gui_help_uitest.ps1 tests the setup).
'{"SetupDone": true}' | Set-Content -Encoding ascii (Join-Path $Work 'settings.json')
$script:proc = Start-Process $App -PassThru -ArgumentList @('--stations', "`"$lib`"", '--settings', "`"$(Join-Path $Work 'settings.json')`"", '--qrz-url', "http://localhost:$Port/xml/current/", '--qrz-credential', "`"$target`"")
try {
    $script:win = Wait-For { $AE::RootElement.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $script:proc.Id))) } 20000
    $null = Wait-For { (Text 'Status') -like 'Ready.*' } 20000
    Check ((ById $script:win 'QrzButton').Current.Name -eq 'QRZ.com: sign in…') 'starts signed out' (ById $script:win 'QrzButton').Current.Name
    Check (-not (Saved)) 'no test sign-in saved yet'

    # 1. Signed out, call signs go to callook.info.
    Click (ById $script:win 'AddButton')
    SelectItem (ByName $script:win 'Call sign')
    Check ((Text 'MethodHint') -like '*callook.info*Sign in to QRZ.com*') 'signed out: hint says callook.info' (Text 'MethodHint')
    Click (ById $script:win 'CancelButton')

    # 2. A wrong password is refused and nothing is saved.
    SignIn 'test-user' 'wrong-password'
    $e = Wait-For { $t = DlgText 'ErrorText'; if ($t -and $t -ne 'Signing in…') { $t } }
    Check ($e -eq 'QRZ.com: Username/password incorrect') 'wrong password refused with QRZ''s reason' $e
    Check (-not (Saved)) 'wrong password not saved'
    Click (ById $script:dlg 'CloseButton')

    # 3. The right password signs in and is saved in Credential Manager.
    SignIn 'test-user' 'test-password'
    $null = Wait-For { (Text 'Status') -like 'Signed in to QRZ.com*' }
    Check ((Text 'Status') -eq 'Signed in to QRZ.com as test-user. XML Logbook Data subscription until Wed Jan 1 12:34:03 2013.') 'signed in, subscription shown' (Text 'Status')
    Check ((ById $script:win 'QrzButton').Current.Name -eq 'QRZ.com: test-user') 'toolbar shows the QRZ user' (ById $script:win 'QrzButton').Current.Name
    Check (Saved) 'sign-in saved in Credential Manager'

    # 4. Lookups through QRZ.
    Click (ById $script:win 'AddButton')
    SelectItem (ByName $script:win 'Call sign')
    Check ((Text 'MethodHint') -like '*QRZ.com (signed in as test-user)*') 'signed in: hint says QRZ.com' (Text 'MethodHint')
    Find-Call 'G1SRDD'
    $e = Wait-For { $t = Text 'EditorError'; if ($t) { $t } }
    Check ($e -like "G1SRDD isn't on QRZ.com*") 'unknown call explained' $e
    Find-Call 'kz1zip'
    $null = Wait-For { (Text 'NoteText') -like '*KZ1ZIP*' }
    Check ((Text 'NoteText') -like '*WARNING: this is only the center of KZ1ZIP''s zip code*') 'zip-code position warned loudly' (Text 'NoteText')
    SetText (ById $script:win 'NameBox') ''
    Find-Call 'aa7bq'
    $null = Wait-For { (Text 'NoteText') -like 'AA7BQ*' }
    Check ((Text 'NoteText') -like 'AA7BQ, FRED "The Boss" LLOYD. AA7BQ set this position on QRZ.com themselves*') 'user-set position noted' (Text 'NoteText')
    Check ((Text 'PinText') -like '34.234560, -112.343560*') 'pin at QRZ''s position' ((Text 'PinText') -replace "`n", ' | ')
    Check ((ById $script:win 'NameBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'AA7BQ') 'name filled in'
    SetText (ById $script:win 'HeightBox') '30'
    Tick $true
    Click (ById $script:win 'SaveButton')
    $null = Wait-For { (Text 'Status') -like 'Saved AA7BQ*' }
    $s = (Get-Content $lib -Raw | ConvertFrom-Json).Stations | Where-Object Name -eq 'AA7BQ'
    Check ($s -and $s.Lat -eq 34.23456 -and $s.Lon -eq -112.34356 -and $s.Source -eq 'QRZ.com: AA7BQ (user)') 'saved with QRZ position and source' "$($s.Lat), $($s.Lon), $($s.Source)"

    # 5. Sign out removes the saved password; call signs go back to callook.info.
    Click (ById $script:win 'QrzButton')
    $script:dlg = SignInWindow
    Check ((DlgText 'SavedText') -eq 'Signed in as test-user.') 'sign-in window shows who is signed in' (DlgText 'SavedText')
    Click (ById $script:dlg 'SignOutButton')
    $null = Wait-For { (Text 'Status') -like 'Signed out*' }
    Check (-not (Saved)) 'sign out removed it from Credential Manager'
    Check ((ById $script:win 'QrzButton').Current.Name -eq 'QRZ.com: sign in…') 'toolbar back to signed out'

    # 6. What went over the wire: POSTs only, the password never in a URL, nothing secret in the files.
    $lines = @(Get-Content $log | Where-Object { $_ -ne 'listening' })
    Check ($lines.Count -ge 5 -and -not ($lines | Where-Object { $_ -notlike 'POST *' })) 'every request was a POST' ($lines -join ' / ')
    Check (-not ($lines | Where-Object { $_ -match 'password=|test-password|wrong-password' })) 'password never in a URL'
    Check (@($lines | Where-Object { $_ -like '*fields=agent,password,username' }).Count -eq 2) 'two sign-ins, each with the agent name'
    $leak = Get-ChildItem $Work -File | Where-Object { (Get-Content $_.FullName -Raw) -match 'test-password' }
    Check (-not $leak) 'password in no file in the work folder' (($leak | ForEach-Object Name) -join ', ')
    Check (-not ((Text 'Status') -match 'test-password')) 'password not shown in the window'
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $null = $script:proc.CloseMainWindow(); if (-not $script:proc.WaitForExit(5000)) { $script:proc.Kill() } }
    New-Item -ItemType File $stop -Force | Out-Null
    if (-not $fake.WaitForExit(5000)) { $fake.Kill() }
    $null = cmdkey "/delete:$target" 2>$null
}
$fail = @($script:results | Where-Object Result -eq 'FAIL').Count
"{0} checks, {1} failed" -f $script:results.Count, $fail
exit [int]($fail -gt 0)
