using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace SplatWin.Core;

/// <summary>What happened to one tile.</summary>
public sealed record TileOutcome(string Tile, string Source, string What, bool Ok);

/// <summary>
/// Fetches and converts the terrain a job needs, into one terrain folder: the logic of
/// scripts\get_terrain.ps1 (Stage 4), moved into the app. Same sources (runbook D7, no login):
///   standard (3"): viewfinderpanoramas.org dem3 zip blocks (6 x 4 degrees), void-filled SRTM;
///   HD (1"):       AWS open data, elevation-tiles-prod/skadi .hgt.gz.
/// Downloads are kept in &lt;folder&gt;\raw and never repeated. Each tile is checked: the .hgt must
/// be exactly the right size and srtm2sdf must convert it without error, or nothing is kept,
/// because SPLAT! would quietly treat a missing or broken area as sea level.
/// A tile the source doesn't have is open water; that is recorded with a .sea marker.
/// </summary>
public sealed class TerrainFetcher
{
    public static readonly Uri DefaultStandardSource = new("http://viewfinderpanoramas.org/dem3/");
    public static readonly Uri DefaultHdSource = new("https://s3.amazonaws.com/elevation-tiles-prod/skadi/");

    readonly HttpClient _http;
    readonly string _folder, _engine;
    readonly Uri _std, _hd;

    public TerrainFetcher(HttpClient http, string folder, string engineFolder, Uri? standardSource = null, Uri? hdSource = null)
    {
        _http = http; _folder = Path.GetFullPath(folder); _engine = engineFolder;
        _std = standardSource ?? DefaultStandardSource; _hd = hdSource ?? DefaultHdSource;
    }

    /// <summary>viewfinderpanoramas' block: a 4-degree latitude band letter and a 6-degree zone (as get_terrain.ps1).</summary>
    public static string VfpBlock(int lat, int lon)
    {
        char band = (char)('A' + (int)Math.Floor(Math.Abs(lat + (lat < 0 ? 1 : 0)) / 4.0));
        int zone = (int)Math.Floor((lon + 180) / 6.0) + 1;
        return $"{(lat < 0 ? "S" : "")}{band}{zone:00}";
    }

    public Uri SourceUrl(string sdf, bool hd)
    {
        var (hgt, lat, lon) = Terrain.SourceTile(sdf);
        return hd ? new Uri(_hd, $"{hgt[..3]}/{hgt}.hgt.gz") : new Uri(_std, $"{VfpBlock(lat, lon)}.zip");
    }

    /// <summary>Makes sure every tile is in the folder, fetching and converting what isn't.</summary>
    public async Task<List<TileOutcome>> EnsureAsync(IEnumerable<string> tiles, bool hd, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        var results = new List<TileOutcome>();
        var todo = Terrain.Missing(_folder, tiles);
        int n = 0;
        foreach (var tile in todo)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Terrain: tile {++n} of {todo.Count} ({tile})…");
            results.Add(await OneAsync(tile, hd, progress, ct));
        }
        return results;
    }

    async Task<TileOutcome> OneAsync(string sdf, bool hd, IProgress<string>? progress, CancellationToken ct)
    {
        var (hgtName, lat, lon) = Terrain.SourceTile(sdf);
        string raw = Path.Combine(_folder, "raw", hd ? "skadi" : "vfp");
        Directory.CreateDirectory(raw);
        Uri url = SourceUrl(sdf, hd);
        string archive = Path.Combine(raw, Path.GetFileName(url.LocalPath));
        string source = url.Host;
        bool downloaded = false;
        if (!File.Exists(archive))
        {
            progress?.Report($"Terrain: downloading {Path.GetFileName(archive)} from {url.Host}…");
            var got = await DownloadAsync(url, archive, ct);
            if (got == HttpStatusCode.NotFound || got == HttpStatusCode.Forbidden)
                return Sea(sdf, source, $"{url.Host} has no {Path.GetFileName(archive)}: open water, so sea level is right");
            if (got != HttpStatusCode.OK)
                return new TileOutcome(sdf, source, $"download failed ({(int)got} {got}) from {url}", false);
            downloaded = true;
        }

        string hgtDir = Path.Combine(_folder, hd ? "hgt1" : "hgt3");
        Directory.CreateDirectory(hgtDir);
        string hgt = Path.Combine(hgtDir, hgtName + ".hgt");
        try
        {
            if (hd)
            {
                await using var input = File.OpenRead(archive);
                await using var gz = new GZipStream(input, CompressionMode.Decompress);
                await using var output = File.Create(hgt + ".part");
                await gz.CopyToAsync(output, ct);
            }
            else
            {
                using var zip = ZipFile.OpenRead(archive);
                var e = zip.Entries.FirstOrDefault(x => string.Equals(x.Name, hgtName + ".hgt", StringComparison.OrdinalIgnoreCase));
                if (e == null) return Sea(sdf, source, $"{Path.GetFileName(archive)} has no {hgtName}.hgt: open water, so sea level is right");
                e.ExtractToFile(hgt + ".part", true);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            File.Delete(hgt + ".part");
            if (downloaded) File.Delete(archive);   // a broken download must not be reused
            return new TileOutcome(sdf, source, $"the downloaded {Path.GetFileName(archive)} is damaged ({ex.Message}); it was removed, so the next run downloads it again", false);
        }
        File.Move(hgt + ".part", hgt, true);
        try
        {
            int side = hd ? 3601 : 1201;
            long want = (long)side * side * 2, len = new FileInfo(hgt).Length;
            if (len != want)
            {
                if (downloaded) File.Delete(archive);
                return new TileOutcome(sdf, source, $"{hgtName}.hgt is {len} bytes, not {want}: a damaged tile, not used", false);
            }
            progress?.Report($"Terrain: converting {hgtName} to {sdf}…");
            string exe = Path.Combine(_engine, hd ? "srtm2sdf-hd.exe" : "srtm2sdf.exe");
            if (!File.Exists(exe)) return new TileOutcome(sdf, source, $"the converter wasn't found at {exe}", false);
            var (code, output) = await RunAsync(exe, hgt, ct);
            string made = Path.Combine(_folder, sdf);
            if (code != 0 || output.Contains("Error") || output.Contains("Premature") || !File.Exists(made))
            {
                File.Delete(made);
                string first = output.Split('\n').FirstOrDefault(l => l.Contains("Error") || l.Contains("Premature"))?.Trim() ?? $"exit code {code}";
                return new TileOutcome(sdf, source, $"conversion failed: {first}", false);
            }
            return new TileOutcome(sdf, source, downloaded ? "downloaded and converted" : "converted from the saved download", true);
        }
        finally { File.Delete(hgt); }
    }

    TileOutcome Sea(string sdf, string source, string why)
    {
        File.WriteAllText(Path.Combine(_folder, Terrain.SeaMarker(sdf)), why + "\r\n", Encoding.ASCII);
        return new TileOutcome(sdf, source, why, true);
    }

    async Task<HttpStatusCode> DownloadAsync(Uri url, string target, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var r = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!r.IsSuccessStatusCode) return r.StatusCode;
            string part = target + ".part";
            await using (var f = File.Create(part)) await r.Content.CopyToAsync(f, timeout.Token);
            long? want = r.Content.Headers.ContentLength;
            if (want is long w && new FileInfo(part).Length != w) { File.Delete(part); return HttpStatusCode.PartialContent; }
            File.Move(part, target, true);
            return HttpStatusCode.OK;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            File.Delete(target + ".part");
            throw new IOException($"Downloading {url} took more than 10 minutes and was stopped.");
        }
        catch (HttpRequestException ex)
        {
            File.Delete(target + ".part");
            throw new IOException($"Couldn't download {url}: {ex.Message} Check the internet connection.", ex);
        }
        catch (OperationCanceledException) { File.Delete(target + ".part"); throw; }
    }

    async Task<(int, string)> RunAsync(string exe, string hgt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = _folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add(hgt);
        using var p = Process.Start(psi) ?? throw new IOException("srtm2sdf could not be started.");
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var e = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        return (p.ExitCode, (await o) + (await e));
    }
}
