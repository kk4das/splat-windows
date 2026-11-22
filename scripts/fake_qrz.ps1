<#
.SYNOPSIS
  A stand-in for the QRZ.com XML service, on localhost only, for gui_qrz_uitest.ps1. It answers
  with QRZ's own example replies (gui\SplatWin.Tests\samples\qrz-*.xml).
.DESCRIPTION
  Test account: user "test-user", password "test-password"; anything else is refused the way QRZ
  refuses it. Calls: AA7BQ (position set by the ham), KZ1ZIP (only a zip-code centre); any other call
  is "Not found". A wrong session key gets "Session Timeout".
  Every request is logged to -Log: the method, the URL, and the form's field names (never values).
  Stops when -Stop file appears, or after 10 minutes.
#>
param(
    [Parameter(Mandatory)] [int]$Port,
    [Parameter(Mandatory)] [string]$Samples,
    [Parameter(Mandatory)] [string]$Log,
    [Parameter(Mandatory)] [string]$Stop
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$key = 'TESTKEY0123456789'
function S([string]$n) { [IO.File]::ReadAllText((Join-Path $Samples $n)) }
$login = (S 'qrz-login.xml') -replace '<Key>[^<]*</Key>', "<Key>$key</Key>"
$aa7bq = (S 'qrz-AA7BQ.xml') -replace '<Key>[^<]*</Key>', "<Key>$key</Key>"
$zip = $aa7bq -replace '<call>AA7BQ</call>', '<call>KZ1ZIP</call>' -replace '<aliases>[^<]*</aliases>', '' -replace '<geoloc>user</geoloc>', '<geoloc>zip</geoloc>'

$l = New-Object System.Net.HttpListener
$l.Prefixes.Add("http://localhost:$Port/")
$l.Start()
Add-Content $Log "listening"
$until = (Get-Date).AddMinutes(10)
try {
    while ((Get-Date) -lt $until -and -not (Test-Path $Stop)) {
        $task = $l.GetContextAsync()
        while (-not $task.Wait(300)) { if ((Test-Path $Stop) -or (Get-Date) -gt $until) { return } }
        $ctx = $task.Result
        $body = (New-Object IO.StreamReader($ctx.Request.InputStream)).ReadToEnd()
        $f = [System.Web.HttpUtility]::ParseQueryString($body)
        Add-Content $Log ("{0} {1} fields={2}" -f $ctx.Request.HttpMethod, $ctx.Request.Url.AbsoluteUri, (($f.AllKeys | Sort-Object) -join ','))
        $reply =
            if ($f['username']) {
                if ($f['username'] -eq 'test-user' -and $f['password'] -eq 'test-password') { $login } else { S 'qrz-login-badpass.xml' }
            } elseif ($f['s'] -ne $key) { S 'qrz-timeout.xml' }
            elseif ($f['callsign'] -eq 'AA7BQ') { $aa7bq }
            elseif ($f['callsign'] -eq 'KZ1ZIP') { $zip }
            else { (S 'qrz-notfound.xml') -replace 'g1srdd', $f['callsign'] -replace '<Key>[^<]*</Key>', "<Key>$key</Key>" }
        $bytes = [Text.Encoding]::UTF8.GetBytes($reply)
        $ctx.Response.ContentType = 'text/xml; charset=utf-8'
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $ctx.Response.Close()
    }
}
finally { $l.Stop() }
