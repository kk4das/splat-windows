<#
.SYNOPSIS
  Stage 2 smoke test: does every Windows program start, and do the fixed code paths work on real
  data? Where a Linux golden exists, the Windows output is compared to it by sha256. (The full
  20-case comparison is Stage 3.)
.NOTES
  Every program runs with a CLEAN PATH (Windows only), so nothing from MSYS2 can leak in.
  USERPROFILE / TEMP / HOME are set only inside the test processes; the real profile is untouched.
  Output goes to work\smoke (git-ignored).
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Dist = Join-Path $Root 'dist'
$Gold = Join-Path $Root 'data\golden-linux'
$W    = Join-Path $Root 'work\smoke'
if (Test-Path $W) { Remove-Item -Recurse -Force $W }
New-Item -ItemType Directory -Force $W | Out-Null
$Clean = "$env:SystemRoot\System32;$env:SystemRoot"

function Invoke-Exe([string]$exe, [string[]]$a, [string]$cwd, [hashtable]$envs = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $Dist $exe
    $psi.Arguments = ($a | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $cwd; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['PATH'] = $Clean
    [void]$psi.EnvironmentVariables.Remove('HOME')                 # as on a normal Windows PC
    foreach ($k in $envs.Keys) { $psi.EnvironmentVariables[$k] = $envs[$k] }
    $p = [System.Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync(); $p.WaitForExit()
    [pscustomobject]@{ Code = $p.ExitCode; Out = ($o.Result + $e.Result) }
}
function Hash($p) { (Get-FileHash -Algorithm SHA256 $p).Hash.ToLower() }
$res = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail) {
    "[{0}] {1,-44} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail | Write-Output
    [void]$res.Add($ok)
}
# Text reports are written in text mode, so Windows ends lines with CR-LF (expected; Notepad
# needs it). A file passes if it is byte-identical, or identical once CR-LF is turned into LF.
function Get-Lf([string]$p) { [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($p)).Replace("`r`n", "`n") }
function Compare-Linux([string]$win, [string]$lin) {
    if (-not (Test-Path $win)) { return 'missing' }
    if ((Hash $win) -eq (Hash $lin)) { return 'identical' }
    if ((Get-Lf $win) -ceq (Get-Lf $lin)) { return 'crlf' }
    return 'differs'
}
function Same-AsLinux([string]$label, [string]$win, [string]$lin) {
    $c = Compare-Linux $win $lin
    $kb = if (Test-Path $win) { [math]::Round((Get-Item $win).Length/1KB) } else { 0 }
    $msg = @{ identical = "identical to Linux ($kb KB)"; crlf = "identical to Linux apart from CR-LF line endings ($kb KB)"; missing = 'Windows file missing'; differs = 'DIFFERS from Linux' }[$c]
    Check $label ($c -eq 'identical' -or $c -eq 'crlf') $msg
}
function New-Case([string]$id) { $d = Join-Path $W $id; New-Item -ItemType Directory $d | Out-Null; Copy-Item "$Root\tests\cases\inputs\*" $d; $d }

Write-Output "== every program starts (usage banner, clean PATH) =="
foreach ($x in 'splat','splat-hd','srtm2sdf','srtm2sdf-hd','usgs2sdf','citydecoder','bearing','fontdata') {
    $r = Invoke-Exe "$x.exe" @() $W
    $first = ($r.Out -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
    # A crash is a Windows NTSTATUS exit (0xC000xxxx: access violation, stack overflow, missing
    # DLL...). Upstream's own exit(-1) after a usage message (fontdata) is not a crash.
    $crash = $r.Code -le -1073741000
    Check "$x.exe starts" ((-not $crash) -and $first) ("exit=$($r.Code): " + $first.Trim())
}

Write-Output ""; Write-Output "== srtm2sdf: full Windows paths (P11), binary read (P3), '_' names (P1) =="
$sdf3 = Join-Path $W 'sdf3'; New-Item -ItemType Directory $sdf3 | Out-Null
foreach ($t in Get-ChildItem "$Root\data\hgt3\*.hgt") {
    $r = Invoke-Exe 'srtm2sdf.exe' @($t.FullName) $sdf3
    $name = ($r.Out | Select-String -Pattern 'Writing (\S+)\.\.\.' -AllMatches).Matches.Groups[1].Value
    Same-AsLinux "srtm2sdf $($t.Name) -> $name" (Join-Path $sdf3 $name) (Join-Path "$Gold\sdf3" $name)
}
$sdf1 = Join-Path $W 'sdf1'; New-Item -ItemType Directory $sdf1 | Out-Null
$t = Get-Item "$Root\data\hgt1\N38W078.hgt"
$r = Invoke-Exe 'srtm2sdf-hd.exe' @($t.FullName) $sdf1
$name = ($r.Out | Select-String -Pattern 'Writing (\S+)\.\.\.').Matches.Groups[1].Value
Same-AsLinux "srtm2sdf-hd $($t.Name) -> $name (P6 HD by name)" (Join-Path $sdf1 $name) (Join-Path "$Gold\sdf1" $name)
Check 'no colon-named or stray files written' (-not (Get-ChildItem $sdf3, $sdf1 -Force | Where-Object { $_.Name -match ':' -or $_.Name -match '^\d+$' })) ((Get-ChildItem $sdf3, $sdf1 | ForEach-Object Name) -join ', ')

Write-Output ""; Write-Output "== splat point-to-point (P12 stack, P1 SDF loading, P2 heap) =="
$d = New-Case 'P01'
$r = Invoke-Exe 'splat.exe' @('-t','k4hta','-r','kk4das','-d','../sdf3') $d
Check 'splat P01 exit code' ($r.Code -eq 0) "exit=$($r.Code)"
Check 'P01 loaded real terrain (not sea level)' ($r.Out -notmatch 'assumed as sea-level' -and $r.Out -match 'Loading') (($r.Out -split "`n" | Select-String 'Loading|sea-level' | Select-Object -First 1).Line.Trim())
Same-AsLinux 'P01 report K4HTA-to-KK4DAS.txt' "$d\K4HTA-to-KK4DAS.txt" "$Gold\P01\K4HTA-to-KK4DAS.txt"
Same-AsLinux 'P01 site report' "$d\K4HTA-site_report.txt" "$Gold\P01\K4HTA-site_report.txt"

$bad = Join-Path $W 'P01-altered.txt'
$txt = [IO.File]::ReadAllText("$W\P01\K4HTA-to-KK4DAS.txt").Replace('116.68 dB', '116.69 dB')
[IO.File]::WriteAllText($bad, $txt)
Check 'negative: 0.01 dB change still detected' ((Compare-Linux $bad "$Gold\P01\K4HTA-to-KK4DAS.txt") -eq 'differs') 'CRLF-tolerant comparison still fires on a real change'

Write-Output ""; Write-Output "== %USERPROFILE%\.splat_path, with HOME unset (P4) =="
$prof = Join-Path $W 'fake-profile'; New-Item -ItemType Directory $prof | Out-Null
[IO.File]::WriteAllText((Join-Path $prof '.splat_path'), "$sdf3`r`n")   # CRLF, as Notepad would save it
$d = New-Case 'P01-splatpath'
$r = Invoke-Exe 'splat.exe' @('-t','k4hta','-r','kk4das') $d @{ USERPROFILE = $prof }
Check 'splat found SDFs via %USERPROFILE%\.splat_path' ($r.Code -eq 0 -and $r.Out -notmatch 'sea-level') "exit=$($r.Code)"
Same-AsLinux 'report via .splat_path' "$d\K4HTA-to-KK4DAS.txt" "$Gold\P01\K4HTA-to-KK4DAS.txt"
$d = New-Case 'P01-noprofile'
$r = Invoke-Exe 'splat.exe' @('-t','k4hta','-r','kk4das') $d @{ USERPROFILE = (Join-Path $W 'empty-profile') }
Check 'negative: no .splat_path -> sea level (fires)' ($r.Out -match 'assumed as sea-level') 'sea-level fallback reported, as upstream does'

Write-Output ""; Write-Output "== user-defined terrain temp file (P5) =="
$tmp = Join-Path $W 'tmp'; New-Item -ItemType Directory $tmp | Out-Null
$d = New-Case 'P06'
$r = Invoke-Exe 'splat.exe' @('-t','k4hta','-r','kk4das','-d','../sdf3','-udt','obstacle.udt') $d @{ TEMP = $tmp; TMP = $tmp }
Check 'splat -udt exit code' ($r.Code -eq 0) "exit=$($r.Code)"
Same-AsLinux 'P06 report (with 200 ft obstacle)' "$d\K4HTA-to-KK4DAS.txt" "$Gold\P06\K4HTA-to-KK4DAS.txt"
$left = @(Get-ChildItem $tmp -Force)
Check 'temp file created in %TEMP% and removed' ($left.Count -eq 0) "$($left.Count) file(s) left in TEMP"

Write-Output ""; Write-Output "== HD point-to-point (heap terrain pages, P2) =="
foreach ($t in Get-ChildItem "$Root\data\hgt1\*.hgt" | Where-Object Name -ne 'N38W078.hgt') { [void](Invoke-Exe 'srtm2sdf-hd.exe' @($t.FullName) $sdf1) }
$d = New-Case 'P08'
$r = Invoke-Exe 'splat-hd.exe' @('-t','k4hta','-r','kk4das','-d','../sdf1') $d
Check 'splat-hd exit code' ($r.Code -eq 0) "exit=$($r.Code)"
Same-AsLinux 'P08 HD report' "$d\K4HTA-to-KK4DAS.txt" "$Gold\P08\K4HTA-to-KK4DAS.txt"

$pass = @($res | Where-Object { $_ }).Count
Write-Output ""; Write-Output "-- $pass/$($res.Count) passed"
Write-Output ("SMOKE: " + $(if ($pass -eq $res.Count) { 'OK' } else { 'PROBLEMS' }))
exit $(if ($pass -eq $res.Count) { 0 } else { 1 })
