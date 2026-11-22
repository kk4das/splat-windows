<#
.SYNOPSIS
  Builds, tests or publishes the "SPLAT! for Windows" GUI with the project's own .NET 10 SDK.
.DESCRIPTION
  The SDK lives at D:\ClaudeAI\tools\dotnet10 (installed 2026-11-22, not on PATH; C: is short of
  space). .NET's usage telemetry is turned off and the NuGet package cache is kept on D:.
.EXAMPLE
  .\gui\build-gui.ps1 test        # build + unit tests
  .\gui\build-gui.ps1 publish     # self-contained app in gui\publish\ (no .NET needed to run it)
#>
param(
    [ValidateSet('build', 'test', 'publish')] [string]$Task = 'build',
    [string]$Dotnet = 'D:\ClaudeAI\tools\dotnet10\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Dotnet)) { throw ".NET SDK not found at $Dotnet (see the runbook environment table)" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = 'D:\ClaudeAI\tools\nuget-packages'
$env:DOTNET_ROOT = Split-Path $Dotnet
$gui = $PSScriptRoot
switch ($Task) {
    'build'   { & $Dotnet build "$gui\SplatWin.slnx" -c Release }
    'test'    { & $Dotnet test "$gui\SplatWin.Tests\SplatWin.Tests.csproj" -c Release }
    'publish' {
        & $Dotnet publish "$gui\SplatWin.App\SplatWin.App.csproj" -c Release -r win-x64 --self-contained true -o "$gui\publish"
    }
}
exit $LASTEXITCODE
