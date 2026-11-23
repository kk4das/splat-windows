<#
.SYNOPSIS
  A stand-in for the two terrain sources (runbook D7), on localhost only, for the G4 tests. It
  serves the project's own cached downloads in data\raw, so tests never hit the real servers.
.DESCRIPTION
  http://localhost:<Port>/dem3/<block>.zip          from data\raw\vfp     (viewfinderpanoramas.org)
  http://localhost:<Port>/skadi/<Nxx>/<tile>.hgt.gz  from data\raw\skadi   (AWS elevation-tiles-prod)
  Anything else is 404, as the real sources answer for open water.
  Every request is logged to -Log (method and path). Stops when the -Stop file appears, or after 20 minutes.
#>
param(
    [Parameter(Mandatory)] [int]$Port,
    [Parameter(Mandatory)] [string]$Raw,
    [Parameter(Mandatory)] [string]$Log,
    [Parameter(Mandatory)] [string]$Stop
)
$ErrorActionPreference = 'Stop'
$l = New-Object System.Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
Add-Content $Log "listening"
$until = (Get-Date).AddMinutes(20)
try {
    while ((Get-Date) -lt $until -and -not (Test-Path $Stop)) {
        $task = $l.GetContextAsync()
        while (-not $task.Wait(300)) { if ((Test-Path $Stop) -or (Get-Date) -gt $until) { return } }
        $ctx = $task.Result
        $path = $ctx.Request.Url.AbsolutePath
        $file = $null
        if ($path -match '^/dem3/([A-Z0-9]+\.zip)$') { $file = Join-Path $Raw "vfp\$($Matches[1])" }
        elseif ($path -match '^/skadi/[NS]\d\d/([NS]\d\d[EW]\d{3}\.hgt\.gz)$') { $file = Join-Path $Raw "skadi\$($Matches[1])" }
        Add-Content $Log ("{0} {1} {2}" -f $ctx.Request.HttpMethod, $path, $(if ($file -and (Test-Path $file)) { 200 } else { 404 }))
        if ($file -and (Test-Path $file)) {
            $bytes = [IO.File]::ReadAllBytes($file)
            $ctx.Response.ContentType = 'application/octet-stream'
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else { $ctx.Response.StatusCode = 404 }
        $ctx.Response.Close()
    }
}
finally { $l.Stop() }
