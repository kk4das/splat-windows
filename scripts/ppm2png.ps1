<#
.SYNOPSIS
  Converts SPLAT! .ppm maps to .png so they open in Windows Photos, a browser, or Google Earth
  (owner decision D9). SPLAT! itself is unchanged; this only reads its output.

.DESCRIPTION
  Uses .NET (System.Drawing), which is built into Windows, so nothing needs installing. Every pixel
  is copied exactly (PNG is lossless), and the result is checked pixel-by-pixel against the PPM
  before it is kept.

  If a SPLAT! .kml next to the map refers to the .ppm (which Google Earth can't show), a companion
  <name>-png.kml is written that refers to the .png instead. SPLAT!'s own .kml is never modified.

.EXAMPLE
  .\scripts\ppm2png.ps1 los.ppm
  .\scripts\ppm2png.ps1 C:\splat-work\*.ppm
#>
param(
    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)] [string[]]$Path,
    [switch]$NoKml
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The pixel loops run in compiled C# (PowerShell loops would take minutes on a 5.7-megapixel map).
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.IO; using System.Runtime.InteropServices;
public static class SplatPpm {
    static int ReadToken(byte[] b, ref int i) {
        while (true) {                                   // skip whitespace and # comments
            while (i < b.Length && char.IsWhiteSpace((char)b[i])) i++;
            if (i < b.Length && b[i] == (byte)'#') { while (i < b.Length && b[i] != 10) i++; continue; }
            break;
        }
        int v = 0, n = 0;
        while (i < b.Length && b[i] >= (byte)'0' && b[i] <= (byte)'9') { v = v * 10 + (b[i] - 48); i++; n++; }
        if (n == 0) throw new InvalidDataException("bad PPM header");
        return v;
    }
    public static string Convert(string ppm, string png) {
        byte[] b = File.ReadAllBytes(ppm);
        if (b.Length < 2 || b[0] != (byte)'P' || b[1] != (byte)'6') throw new InvalidDataException("not a binary (P6) PPM: " + ppm);
        int i = 2, w = ReadToken(b, ref i), h = ReadToken(b, ref i), max = ReadToken(b, ref i);
        i++;                                             // exactly one whitespace byte before pixel data
        if (max != 255) throw new InvalidDataException("unsupported maxval " + max);
        if (b.Length - i < (long)w * h * 3) throw new InvalidDataException("PPM is truncated");
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb)) {
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[d.Stride];
            for (int y = 0; y < h; y++) {
                int s = i + y * w * 3;
                for (int x = 0; x < w; x++) { row[x*3] = b[s+x*3+2]; row[x*3+1] = b[s+x*3+1]; row[x*3+2] = b[s+x*3]; }  // RGB -> BGR
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, d.Stride);
            }
            bmp.UnlockBits(d);
            bmp.Save(png, ImageFormat.Png);
        }
        // Verify: read the PNG back and compare every pixel with the PPM.
        using (var chk = new Bitmap(png)) {
            if (chk.Width != w || chk.Height != h) throw new Exception("verify: size mismatch");
            var d = chk.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var row = new byte[d.Stride];
            for (int y = 0; y < h; y++) {
                Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, d.Stride);
                int s = i + y * w * 3;
                for (int x = 0; x < w; x++)
                    if (row[x*3] != b[s+x*3+2] || row[x*3+1] != b[s+x*3+1] || row[x*3+2] != b[s+x*3]) {
                        chk.UnlockBits(d); throw new Exception("verify: pixel (" + x + "," + y + ") differs");
                    }
            }
            chk.UnlockBits(d);
        }
        return w + "x" + h;
    }
}
'@

$files = foreach ($p in $Path) { Get-ChildItem -Path $p -File | Where-Object Extension -eq '.ppm' }
if (-not $files) { throw "no .ppm files match: $($Path -join ', ')" }
$fail = 0
foreach ($f in $files) {
    $png = [IO.Path]::ChangeExtension($f.FullName, '.png')
    try {
        $size = [SplatPpm]::Convert($f.FullName, $png)
        "{0,-28} -> {1}  ({2}, {3:N0} KB -> {4:N0} KB, verified pixel-for-pixel)" -f $f.Name, (Split-Path $png -Leaf), $size, ($f.Length/1KB), ((Get-Item $png).Length/1KB)
        if (-not $NoKml) {
            foreach ($k in Get-ChildItem -Path $f.DirectoryName -Filter *.kml -File | Where-Object { $_.Name -notlike '*-png.kml' }) {
                $txt = [IO.File]::ReadAllText($k.FullName)
                $ref = "<href>$($f.Name)</href>"
                if ($txt.Contains($ref)) {
                    $out = Join-Path $k.DirectoryName ($k.BaseName + '-png.kml')
                    [IO.File]::WriteAllText($out, $txt.Replace($ref, "<href>$(Split-Path $png -Leaf)</href>"))
                    "{0,-28} -> {1}  (for Google Earth; {0} itself unchanged)" -f $k.Name, (Split-Path $out -Leaf)
                }
            }
        }
    } catch {
        $fail++; Remove-Item $png -ErrorAction SilentlyContinue
        Write-Warning ("{0}: {1}" -f $f.Name, $_.Exception.Message)
    }
}
exit $(if ($fail) { 1 } else { 0 })
