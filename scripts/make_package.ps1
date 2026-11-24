<#
.SYNOPSIS
  Stage G7: builds the release zip, release\SPLATforWindows-<version>-win64.zip, and its SHA-256.
.DESCRIPTION
  Publishes the app self-contained (it brings .NET with it; nothing to install) with the project's
  own .NET SDK, into a fresh folder, then adds README.txt (with the version and source address),
  LICENSE.txt (GPLv2, SPLAT!'s COPYING), the Start-menu shortcut scripts and SPLAT!'s own
  documentation. Checks the result before zipping: the engine, the help, no debug files, no
  other-language resource folders. The zip has one top folder, so it unzips tidily.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_package.ps1
#>
param(
    [string]$SourceUrl = 'https://github.com/kk4das/splat-windows',
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Out) { $Out = Join-Path $root 'release' }
$csproj = Join-Path $root 'gui\SplatWin.App\SplatWin.App.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'no <Version> in the app project' }
$name = "SPLATforWindows-$version"
$stage = Join-Path $root "work\package\$name"
$Out = (New-Item -ItemType Directory -Force $Out).FullName
$zip = Join-Path $Out "$name-win64.zip"

# 1. Publish, self-contained, into a fresh folder (same settings as gui\build-gui.ps1 publish).
if (Test-Path (Split-Path $stage)) { Remove-Item -Recurse -Force (Split-Path $stage) -Confirm:$false }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = 'D:\ClaudeAI\tools\nuget-packages'; $env:DOTNET_ROOT = 'D:\ClaudeAI\tools\dotnet10'
$log = Join-Path $root 'work\package-publish.log'
cmd /c "`"D:\ClaudeAI\tools\dotnet10\dotnet.exe`" publish `"$csproj`" -c Release -r win-x64 --self-contained true -o `"$stage`" > `"$log`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "publish failed; see $log" }
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item   # debug symbols: not needed to run

# 2. The files around the app.
$readme = [IO.File]::ReadAllText((Join-Path $root 'packaging\README.txt')).Replace('{VERSION}', $version).Replace('{SOURCE_URL}', $SourceUrl)
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($false)))
Copy-Item (Join-Path $root 'src\COPYING') (Join-Path $stage 'LICENSE.txt')
foreach ($f in 'Add Start menu shortcut.cmd', 'Remove Start menu shortcut.cmd') { Copy-Item (Join-Path $root "packaging\$f") (Join-Path $stage $f) }
$docs = New-Item -ItemType Directory (Join-Path $stage 'docs\SPLAT')
Copy-Item (Join-Path $root 'src\docs\english\text\splat.txt') $docs
Copy-Item (Join-Path $root 'src\docs\english\pdf\splat.pdf') $docs

# 3. Check before zipping.
$problems = @()
foreach ($f in 'SplatForWindows.exe', 'hostfxr.dll', 'README.txt', 'LICENSE.txt', 'engine\splat.exe', 'engine\splat-hd.exe', 'engine\srtm2sdf.exe',
               'engine\srtm2sdf-hd.exe', 'engine\COPYING', 'web\index.html', 'help\index.html', 'help\troubleshooting.html', 'docs\SPLAT\splat.pdf') {
    if (-not (Test-Path (Join-Path $stage $f))) { $problems += "missing $f" }
}
if (@(Get-ChildItem (Join-Path $stage 'help') -Filter *.html).Count -lt 13) { $problems += 'help pages missing' }
if (Get-ChildItem $stage -Filter *.pdb -Recurse) { $problems += 'debug files left in' }
$langs = @(Get-ChildItem $stage -Directory | Where-Object { $_.Name -match '^[a-z]{2}(-[A-Za-z]+)?$' })
if ($langs) { $problems += "other-language folders: $($langs.Name -join ', ')" }
if ($readme -match '\{[A-Z_]+\}') { $problems += 'README has an unfilled {TOKEN}' }
$exeVer = (Get-Item (Join-Path $stage 'SplatForWindows.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($exeVer -ne $version) { $problems += "exe version $exeVer, project $version" }
if ($problems) { throw ("package check failed: " + ($problems -join '; ')) }

# 4. Zip (one top folder) and its SHA-256.
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$z = [IO.Compression.ZipFile]::OpenRead($zip); $n = $z.Entries.Count; $z.Dispose()
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -Encoding ascii "$zip.sha256" "$hash  $(Split-Path $zip -Leaf)"
"Package: {0}`n  {1:N1} MB, {2} files, version {3}, SHA-256 {4}`n  unpacked: {5:N0} MB" -f $zip, ((Get-Item $zip).Length / 1MB), $n, $version, $hash,
    ((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
