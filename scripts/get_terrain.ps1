<#
.SYNOPSIS
  Works out which 1x1-degree SRTM tiles an analysis needs, optionally downloads them from the
  owner-approved public sources (no login), and converts them to SPLAT! SDF files.

.DESCRIPTION
  Area: either -Lat/-Lon/-RadiusMiles (a circle around a site, as for a coverage map) or a box
  -South/-North/-West/-East. Use ordinary coordinates, as Google Maps shows them (west longitude
  negative). SPLAT!'s "west is positive" convention is handled here.

  Sources (runbook D7, approved 2026-11-21):
    standard (3"): viewfinderpanoramas.org dem3 zip blocks (6 deg x 4 deg each), void-filled SRTM
    HD (1"):       AWS open data, s3.amazonaws.com/elevation-tiles-prod/skadi
  Downloads are cached in data\raw and never repeated. Without -Download, the script only lists
  what is needed and converts what is already present.

  Each converted SDF is checked: a tile that converts with an error (e.g. a truncated download) is
  reported and its SDF removed, because SPLAT! would otherwise treat that area as sea level without
  any warning.

.EXAMPLE
  .\scripts\get_terrain.ps1 -Lat 38.924 -Lon -77.206 -RadiusMiles 30               # list only
  .\scripts\get_terrain.ps1 -Lat 38.924 -Lon -77.206 -RadiusMiles 30 -Download -Convert
  .\scripts\get_terrain.ps1 -South 38.5 -North 39.5 -West -78 -East -77 -Hd -Download -Convert
#>
[CmdletBinding(DefaultParameterSetName = 'Circle')]
param(
    [Parameter(ParameterSetName = 'Circle', Mandatory = $true)] [double]$Lat,
    [Parameter(ParameterSetName = 'Circle', Mandatory = $true)] [double]$Lon,
    [Parameter(ParameterSetName = 'Circle')] [double]$RadiusMiles = 30,
    [Parameter(ParameterSetName = 'Box', Mandatory = $true)] [double]$South,
    [Parameter(ParameterSetName = 'Box', Mandatory = $true)] [double]$North,
    [Parameter(ParameterSetName = 'Box', Mandatory = $true)] [double]$West,
    [Parameter(ParameterSetName = 'Box', Mandatory = $true)] [double]$East,
    [switch]$Hd,                   # 1" tiles for splat-hd (default: 3" for splat)
    [switch]$Download,
    [switch]$Convert,
    [string]$Data,                 # default: <repo>\data
    [string]$SdfDir,               # default: data\sdf3 or data\sdf1
    [string]$Dist                  # default: <repo>\dist
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so resolve them here.
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Data) { $Data = Join-Path $RepoRoot 'data' }
if (-not $Dist) { $Dist = Join-Path $RepoRoot 'dist' }
if ($PSCmdlet.ParameterSetName -eq 'Circle') {
    $dLat = $RadiusMiles / 69.05
    $dLon = $RadiusMiles / (69.17 * [math]::Cos($Lat * [math]::PI / 180))
    $South = $Lat - $dLat; $North = $Lat + $dLat; $West = $Lon - $dLon; $East = $Lon + $dLon
}
if ($South -ge $North -or $West -ge $East) { throw "empty area: south must be < north and west < east" }
$res = if ($Hd) { 'hgt1' } else { 'hgt3' }
if (-not $SdfDir) { $SdfDir = Join-Path $Data $(if ($Hd) { 'sdf1' } else { 'sdf3' }) }
$HgtDir = Join-Path $Data $res
$Raw = Join-Path $Data $(if ($Hd) { 'raw\skadi' } else { 'raw\vfp' })
New-Item -ItemType Directory -Force $HgtDir, $Raw | Out-Null

function Get-TileName([int]$la, [int]$lo) {
    ('{0}{1:00}{2}{3:000}' -f $(if ($la -ge 0) { 'N' } else { 'S' }), [math]::Abs($la), $(if ($lo -ge 0) { 'E' } else { 'W' }), [math]::Abs($lo))
}
function Get-VfpBlock([int]$la, [int]$lo) {   # viewfinderpanoramas: 4-degree latitude band letter + 6-degree zone
    $band = [char]([int][char]'A' + [int][math]::Floor([math]::Abs($la + $(if ($la -lt 0) { 1 } else { 0 })) / 4))
    $zone = [int][math]::Floor(($lo + 180) / 6) + 1
    '{0}{1}{2:00}' -f $(if ($la -lt 0) { 'S' } else { '' }), $band, $zone
}
$tiles = foreach ($la in [math]::Floor($South)..([math]::Ceiling($North) - 1)) {
    foreach ($lo in [math]::Floor($West)..([math]::Ceiling($East) - 1)) { [pscustomobject]@{ Lat = $la; Lon = $lo; Name = (Get-TileName $la $lo) } }
}
"Area: {0:N3} to {1:N3} N, {2:N3} to {3:N3} E -> {4} tile(s), {5} resolution" -f $South, $North, $West, $East, @($tiles).Count, $(if ($Hd) { 'HD 1"' } else { 'standard 3"' })

$problems = 0
foreach ($t in $tiles) {
    $hgt = Join-Path $HgtDir "$($t.Name).hgt"
    $state = if (Test-Path $hgt) { 'present' } else { 'missing' }
    if ($state -eq 'missing' -and $Download) {
        $fetched = $false
        try {
            if ($Hd) {
                $gz = Join-Path $Raw "$($t.Name).hgt.gz"
                if (-not (Test-Path $gz)) {
                    $url = "https://s3.amazonaws.com/elevation-tiles-prod/skadi/$($t.Name.Substring(0,3))/$($t.Name).hgt.gz"
                    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$gz.part"; Move-Item "$gz.part" $gz; $fetched = $true
                }
                $in = [IO.File]::OpenRead($gz); $z = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
                $out = [IO.File]::Create("$hgt.part"); $z.CopyTo($out); $out.Close(); $z.Close(); $in.Close(); Move-Item "$hgt.part" $hgt
            } else {
                $blk = Get-VfpBlock $t.Lat $t.Lon; $zip = Join-Path $Raw "$blk.zip"
                if (-not (Test-Path $zip)) {
                    Invoke-WebRequest -UseBasicParsing -Uri "http://viewfinderpanoramas.org/dem3/$blk.zip" -OutFile "$zip.part"; Move-Item "$zip.part" $zip; $fetched = $true
                }
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $za = [IO.Compression.ZipFile]::OpenRead($zip)
                $e = $za.Entries | Where-Object { $_.Name -eq "$($t.Name).hgt" } | Select-Object -First 1
                if ($e) { [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $hgt, $true) }
                $za.Dispose()
                if (-not $e) { $state = "not in $blk.zip (open water: SPLAT! uses sea level)" }
            }
            if (Test-Path $hgt) { $state = if ($fetched) { 'downloaded' } else { 'unpacked from the cache in data' + [char]92 + 'raw' } }
        } catch {
            $msg = $_.Exception.Message
            $state = if ($msg -match '404|403|Not Found') { 'no tile at source (open water: SPLAT! uses sea level)' } else { "download FAILED: $msg" }
            if ($state -like 'download FAILED*') { $problems++ }
        }
    }
    if (Test-Path $hgt) {
        $want = $(if ($Hd) { 3601 } else { 1201 }); $len = (Get-Item $hgt).Length
        if ($len -ne $want * $want * 2) { $state = "BAD SIZE $len bytes (expected $($want*$want*2))"; $problems++ }
    }
    $sdfState = ''
    if ($Convert -and (Test-Path $hgt) -and $state -notlike 'BAD*') {
        New-Item -ItemType Directory -Force $SdfDir | Out-Null
        $exe = Join-Path $Dist $(if ($Hd) { 'srtm2sdf-hd.exe' } else { 'srtm2sdf.exe' })
        $minW = if ($t.Lon -lt 0) { -$t.Lon - 1 } else { 360 - $t.Lon - 1 }; $maxW = $minW + 1
        if ($maxW -eq 360) { $maxW = 0 }
        $sdf = Join-Path $SdfDir ('{0}_{1}_{2}_{3}{4}.sdf' -f $t.Lat, ($t.Lat + 1), $minW, $maxW, $(if ($Hd) { '-hd' } else { '' }))
        if (Test-Path $sdf) { $sdfState = '; SDF already present' }
        else {
            Push-Location $SdfDir
            try { $o = & $exe $hgt 2>&1 | Out-String } finally { Pop-Location }
            if ($o -match 'Error|Premature' -or -not (Test-Path $sdf)) {
                Remove-Item $sdf -ErrorAction SilentlyContinue; $problems++
                $sdfState = "; CONVERSION FAILED: " + (($o -split "`r?`n" | Where-Object { $_ -match 'Error|Premature' } | Select-Object -First 1))
            } else { $sdfState = "; -> $(Split-Path $sdf -Leaf)" }
        }
    }
    "  {0}  {1}{2}" -f $t.Name, $state, $sdfState
}
if (-not $Download -and ($tiles | Where-Object { -not (Test-Path (Join-Path $HgtDir "$($_.Name).hgt")) })) { "  (add -Download to fetch the missing tiles; -Convert to make SDF files)" }
if ($Convert) { "SDF folder: $SdfDir   (use it with splat -d, or put it in %USERPROFILE%\.splat_path)" }
exit $(if ($problems) { 1 } else { 0 })
