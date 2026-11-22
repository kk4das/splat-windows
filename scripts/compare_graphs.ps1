<#
.SYNOPSIS
  Side-by-side check of SPLAT! graph PNGs, Linux vs Windows. Different gnuplot versions never give
  byte-identical PNGs, so this measures pixel differences and builds a page for a human to judge:
  Linux | Windows | the pixels that differ (in red).
.EXAMPLE
  .\scripts\compare_graphs.ps1 -Linux data\golden-linux -Windows work\windows-s4 -Out work\graph-compare
#>
param(
    [Parameter(Mandatory = $true)] [string]$Linux,
    [Parameter(Mandatory = $true)] [string]$Windows,
    [Parameter(Mandatory = $true)] [string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class GraphDiff {
    // Returns {width, height, differing pixels}; writes a diff image: matching pixels faded, differing ones red.
    public static int[] Diff(string a, string b, string outPng) {
        using (var A = new Bitmap(a)) using (var B = new Bitmap(b)) {
            if (A.Width != B.Width || A.Height != B.Height) return new int[] { A.Width, A.Height, -1, B.Width, B.Height };
            int w = A.Width, h = A.Height, n = 0;
            var r = new Rectangle(0, 0, w, h);
            var da = A.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = B.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pa = new int[w * h]; var pb = new int[w * h];
            Marshal.Copy(da.Scan0, pa, 0, pa.Length); Marshal.Copy(db.Scan0, pb, 0, pb.Length);
            A.UnlockBits(da); B.UnlockBits(db);
            var pd = new int[w * h];
            for (int i = 0; i < pa.Length; i++) {
                if (pa[i] == pb[i]) { int c = pa[i]; int g = (((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255)) / 3; g = 200 + g * 55 / 255; pd[i] = unchecked((int)0xFF000000) | (g << 16) | (g << 8) | g; }
                else { n++; pd[i] = unchecked((int)0xFFFF0000); }
            }
            using (var D = new Bitmap(w, h, PixelFormat.Format32bppArgb)) {
                var dd = D.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(pd, 0, dd.Scan0, pd.Length); D.UnlockBits(dd); D.Save(outPng, ImageFormat.Png);
            }
            return new int[] { w, h, n };
        }
    }
}
'@
New-Item -ItemType Directory -Force $Out | Out-Null
$rows = @(); $html = New-Object Text.StringBuilder
function B64([string]$p) { 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($p)) }
foreach ($l in Get-ChildItem $Linux -Recurse -Filter *.png | Sort-Object FullName) {
    $rel = $l.FullName.Substring((Resolve-Path $Linux).Path.Length + 1)
    $w = Join-Path $Windows $rel
    if (-not (Test-Path $w)) { $rows += [pscustomobject]@{ Graph = $rel; Size = '-'; Differ = 'MISSING on Windows' }; continue }
    $dp = Join-Path $Out (($rel -replace '[\\/]', '_') -replace '\.png$', '-diff.png')
    $r = [GraphDiff]::Diff($l.FullName, $w, $dp)
    if ($r[2] -lt 0) { $rows += [pscustomobject]@{ Graph = $rel; Size = "$($r[0])x$($r[1]) vs $($r[3])x$($r[4])"; Differ = 'SIZE DIFFERS' }; continue }
    $pct = 100.0 * $r[2] / ($r[0] * $r[1])
    $rows += [pscustomobject]@{ Graph = $rel; Size = "$($r[0])x$($r[1])"; Differ = ('{0:N0} px ({1:N2}%)' -f $r[2], $pct) }
    [void]$html.Append("<section><h2>$rel</h2><p>$($r[0])&times;$($r[1]) &middot; $('{0:N0}' -f $r[2]) pixels differ ($('{0:N2}' -f $pct)%)</p><div class='row'>")
    [void]$html.Append("<figure><figcaption>Linux (gnuplot 6.0.2)</figcaption><img src='$(B64 $l.FullName)'></figure>")
    [void]$html.Append("<figure><figcaption>Windows (gnuplot 6.0.6)</figcaption><img src='$(B64 $w)'></figure>")
    [void]$html.Append("<figure><figcaption>Differing pixels in red</figcaption><img src='$(B64 $dp)'></figure></div></section>")
}
$page = @"
<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>SPLAT! graphs: Linux vs Windows</title>
<style>:root{--bg:#fff;--fg:#1b1b1b;--mut:#666;--card:#f4f4f4}@media (prefers-color-scheme:dark){:root{--bg:#161616;--fg:#eee;--mut:#aaa;--card:#222}}
body{background:var(--bg);color:var(--fg);font:15px/1.4 system-ui,sans-serif;margin:0 16px 40px}h1{font-size:20px}h2{font-size:15px;margin:24px 0 4px}p{color:var(--mut);margin:0 0 8px}
.row{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:10px}figure{margin:0;background:var(--card);padding:6px;border-radius:6px}figcaption{font-size:13px;color:var(--mut);margin-bottom:4px}img{width:100%;height:auto;background:#fff}</style></head>
<body><h1>SPLAT! graphs: Linux vs Windows</h1><p>Same SPLAT! data (the .gp files match within the ratified last-digit tolerance); only the gnuplot version differs. Judge whether each pair shows the same graph.</p>
$($html.ToString())</body></html>
"@
[IO.File]::WriteAllText((Join-Path $Out 'index.html'), $page)
$rows | Format-Table -AutoSize | Out-String -Width 160
"page: $(Join-Path $Out 'index.html')"
