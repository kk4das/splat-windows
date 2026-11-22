<#
.SYNOPSIS
  Stage 0 preflight for the SPLAT! Windows port. Proves the MSYS2 UCRT64 toolchain on real output,
  shows every check can FIRE (runbook Guardrail 6), and confirms the port-assessment predictions.

.NOTES
  - Tools are called by FULL PATH (Claude-app PATH caveat; nothing is added to the system PATH).
  - Build products go to build\toolchain\ (git-ignored). Nothing outside the repo is written except
    a scratch folder under %TEMP% that is removed at the end.
  - Exit code 0 = every positive passed AND every negative fired AND every finding was reproduced.
#>
param(
    [string]$Msys = 'C:\msys64'
)
$ErrorActionPreference = 'Stop'
$Root   = Split-Path -Parent $PSScriptRoot
$Tests  = Join-Path $PSScriptRoot 'toolchain-tests'
$Out    = Join-Path $Root 'build\toolchain'
$Bin    = Join-Path $Msys 'ucrt64\bin'
$Gpp    = Join-Path $Bin 'g++.exe'
$Gcc    = Join-Path $Bin 'gcc.exe'
$Objdump= Join-Path $Bin 'objdump.exe'
# Upstream's flags from src/build, minus -mcmodel=medium (not available on Windows), plus -static.
$SplatFlags = @('-O2','-s','-fomit-frame-pointer','-ffast-math','-pipe','-march=x86-64','-static')
# A PATH with only Windows itself: proves an .exe needs nothing from MSYS2 at run time.
$CleanPath = "$env:SystemRoot\System32;$env:SystemRoot"
$Scratch = Join-Path $env:TEMP ("splat-verify-" + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $Out, $Scratch | Out-Null

function Invoke-Proc([string]$File, [string[]]$ArgList = @(), [string]$PathOverride = $null) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = ($ArgList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"','\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($PathOverride) { $psi.EnvironmentVariables['PATH'] = $PathOverride }
    else { $psi.EnvironmentVariables['PATH'] = "$Bin;$env:PATH" }  # gcc needs its own bin dir for cc1/as/ld
    try {
        $p = [System.Diagnostics.Process]::Start($psi)
        $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync()
        $p.WaitForExit()
        return [pscustomobject]@{ Code = $p.ExitCode; Out = ($o.Result + $e.Result).Trim() }
    } catch {
        return [pscustomobject]@{ Code = -1; Out = "could not start: $($_.Exception.Message)" }
    }
}
function New-Result([bool]$ok, [string]$detail) { [pscustomobject]@{ Ok = $ok; Detail = $detail } }
function FirstLines([string]$s, [int]$n = 2) { (($s -split "`r?`n") | Where-Object { $_ } | Select-Object -First $n) -join ' | ' }

# ---------------- checks (each used by a positive AND a negative case) ----------------
function Test-Compiler([string]$exe) {
    $r = Invoke-Proc $exe @('--version')
    if ($r.Code -eq 0 -and $r.Out -match 'gcc|g\+\+') { New-Result $true (FirstLines $r.Out 1) } else { New-Result $false (FirstLines $r.Out) }
}
function Test-Compile([string]$cc, [string]$src, [string]$exe, [string[]]$extra) {
    Remove-Item $exe -ErrorAction SilentlyContinue
    $r = Invoke-Proc $cc (@($src, '-o', $exe) + $extra)
    if ($r.Code -eq 0 -and (Test-Path $exe)) { New-Result $true ("built {0} ({1:N0} KB)" -f (Split-Path $exe -Leaf), ((Get-Item $exe).Length/1KB)) }
    else { New-Result $false ("exit $($r.Code): " + (FirstLines ($r.Out -replace [regex]::Escape($Root),'.'))) }
}
function Test-Run([string]$exe, [string[]]$a, [string]$expect, [string]$path = $CleanPath) {
    $r = Invoke-Proc $exe $a $path
    if ($r.Code -eq 0 -and $r.Out -match $expect) { New-Result $true (FirstLines $r.Out 1) }
    else { New-Result $false ("exit 0x{0:X}: {1}" -f $r.Code, (FirstLines $r.Out)) }
}
$AllowedDll = '^(KERNEL32|ntdll|ADVAPI32|USER32|ucrtbase|msvcrt|api-ms-win-[\w-]+)\.dll$'
function Test-Static([string]$exe) {
    $r = Invoke-Proc $Objdump @('-p', $exe)
    if ($r.Code -ne 0) { return New-Result $false "objdump failed: $(FirstLines $r.Out)" }
    $dlls = [regex]::Matches($r.Out, 'DLL Name:\s*(\S+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $bad = $dlls | Where-Object { $_ -notmatch $AllowedDll }
    $crt = @($dlls | Where-Object { $_ -match '^api-ms-win-crt' }).Count
    $sys = ($dlls | Where-Object { $_ -notmatch '^api-ms-win-crt' }) -join ', '
    if ($bad) { New-Result $false ("non-Windows DLLs: " + ($bad -join ', ')) }
    else { New-Result $true "imports only Windows DLLs: $sys + $crt api-ms-win-crt-* (UCRT, built into Windows 10/11)" }
}

$results = New-Object System.Collections.ArrayList
function Report([string]$group, [string]$name, $res, [bool]$wantOk) {
    $pass = ($res.Ok -eq $wantOk)
    $tag = if ($pass) { 'PASS' } else { 'FAIL' }
    $verb = if ($wantOk) { '' } elseif ($res.Ok) { 'did NOT fire: ' } else { 'fired: ' }
    "[{0}] {1,-36} {2}{3}" -f $tag, $name, $verb, $res.Detail | Write-Output
    [void]$results.Add([pscustomobject]@{ Group = $group; Pass = $pass })
}

$hello = Join-Path $Out 'hello.exe'; $bz2 = Join-Path $Out 'bz2test.exe'; $zt = Join-Path $Out 'ztest.exe'
$probe = Join-Path $Out 'portprobes.exe'; $bz2dyn = Join-Path $Out 'bz2test-dynamic.exe'
$bzfile = Join-Path $Scratch 'roundtrip.bz2'

Write-Output "== positive checks (must PASS) =="
Report pos 'g++ compiler'                 (Test-Compiler $Gpp) $true
Report pos 'gcc compiler'                 (Test-Compiler $Gcc) $true
Report pos 'compile C++ w/ upstream flags' (Test-Compile $Gpp "$Tests\hello.cpp" $hello $SplatFlags) $true
Report pos 'run C++ probe (clean PATH)'   (Test-Run $hello @() '^hello splat-toolchain abs=5\.0 rint=3 ptr=8$') $true
Report pos 'C++ probe is self-contained'  (Test-Static $hello) $true
Report pos 'compile+link libbz2 static'   (Test-Compile $Gcc "$Tests\bz2test.c" $bz2 ($SplatFlags + '-lbz2')) $true
Report pos 'bz2 write (clean PATH)'       (Test-Run $bz2 @('write', $bzfile) '^BZ2 WROTE 1048576 bytes') $true
Report pos 'bz2 read+verify (clean PATH)' (Test-Run $bz2 @('read', $bzfile) '^BZ2 OK 1048576 bytes') $true
Report pos 'bz2 probe is self-contained'  (Test-Static $bz2) $true
Report pos 'compile+link zlib static'     (Test-Compile $Gcc "$Tests\ztest.c" $zt ($SplatFlags + '-lz')) $true
Report pos 'zlib round trip (clean PATH)' (Test-Run $zt @((Join-Path $Scratch 'z.gz')) '^ZLIB OK 65536 bytes') $true
Report pos 'compile port probes'          (Test-Compile $Gcc "$Tests\portprobes.c" $probe $SplatFlags) $true
Report pos '3.4 GB heap grid (P2 fix)'    (Test-Run $probe @('heap') '^HEAP OK 3400 MB') $true

Write-Output ""
Write-Output "== negative checks (must FIRE) =="
Report neg 'compiler: wrong path'         (Test-Compiler (Join-Path $Bin 'no-such-gcc.exe')) $false
Report neg 'compile: bzlib.h not found'   (Test-Compile $Gcc "$Tests\bz2test.c" (Join-Path $Out 'neg1.exe') @('-nostdinc','-lbz2')) $false
Report neg 'link: libbz2 omitted'         (Test-Compile $Gcc "$Tests\bz2test.c" (Join-Path $Out 'neg2.exe') $SplatFlags) $false
$null = Test-Compile $Gcc "$Tests\bz2test.c" $bz2dyn @('-O2','-lbz2')   # deliberately NOT -static
Report neg 'static: dynamic exe flagged'  (Test-Static $bz2dyn) $false
Report neg 'run: dynamic exe, clean PATH' (Test-Run $bz2dyn @('read', $bzfile) '^BZ2 OK') $false
$bad = Join-Path $Scratch 'corrupt.bz2'; $b = [IO.File]::ReadAllBytes($bzfile); $b[[int]($b.Length/2)] = $b[[int]($b.Length/2)] -bxor 0xFF; [IO.File]::WriteAllBytes($bad, $b)
Report neg 'bz2: corrupted stream'        (Test-Run $bz2 @('read', $bad) '^BZ2 OK') $false
Report neg 'run: wrong output'            (Test-Run $hello @() '^hello splat-toolchain abs=6\.0') $false

Write-Output ""
Write-Output "== port-assessment findings (must REPRODUCE on this toolchain) =="
# P1 colon filename
$cdir = Join-Path $Scratch 'colon'; New-Item -ItemType Directory $cdir | Out-Null
$r = Invoke-Proc $probe @('colon', $cdir) $CleanPath
$names = (Get-ChildItem $cdir -Force | ForEach-Object Name) -join ', '
$streams = ''
if (Test-Path (Join-Path $cdir '40')) { $streams = (Get-Item (Join-Path $cdir '40') -Stream * | ForEach-Object Stream) -join ', ' }
$repro = -not (Test-Path -LiteralPath (Join-Path $cdir '40:41:74:75.sdf') -PathType Leaf -ErrorAction SilentlyContinue) -or $names -notmatch '40:41'
$detail = "$(FirstLines $r.Out 1); directory holds [$names]" + $(if ($streams) { "; streams on '40': [$streams]" } else { '' })
Report find 'P1 colon in SDF filename'    (New-Result (-not $repro) $detail) $false
# P2 static > 2 GB
$big = Join-Path $Out 'bigstatic.exe'
$c = Test-Compile $Gcc "$Tests\bigstatic.c" $big @('-O0','-static')
if ($c.Ok) { $c = Test-Run $big @() '.*'; $c = New-Result $c.Ok ("links, but at run time: " + $c.Detail) } else { $c = New-Result $false ("link fails: " + $c.Detail) }
Report find 'P2 3 GB static array'        $c $false
# P3 text-mode read
$r = Invoke-Proc $probe @('textmode', $Scratch) $CleanPath
$m = [regex]::Match($r.Out, 'read\(O_RDONLY\)=(\d+) read\(O_RDONLY\|O_BINARY\)=(\d+)')
$ok = $m.Success -and $m.Groups[1].Value -ne '8' -and $m.Groups[2].Value -eq '8'
Report find 'P3 open() text mode'         (New-Result (-not $ok) (FirstLines $r.Out 1)) $false
# P4 HOME
$r = Invoke-Proc $probe @('home') $null
Report find 'P4 HOME unset'               (New-Result ($r.Out -notmatch 'HOME UNSET') (FirstLines $r.Out 1)) $false

Remove-Item -Recurse -Force $Scratch -ErrorAction SilentlyContinue

Write-Output ""
foreach ($g in 'pos','neg','find') {
    $set = @($results | Where-Object Group -eq $g)
    "-- {0}: {1}/{2} as expected" -f @{pos='positive';neg='negative';find='findings'}[$g], @($set | Where-Object Pass).Count, $set.Count | Write-Output
}
$allOk = -not ($results | Where-Object { -not $_.Pass })
Write-Output ("OVERALL: " + $(if ($allOk) { 'OK' } else { 'PROBLEMS - halt and escalate' }))
exit $(if ($allOk) { 0 } else { 1 })
