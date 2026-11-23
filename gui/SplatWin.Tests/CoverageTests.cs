using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SplatWin.Core;

namespace SplatWin.Tests;

public class CoverageTerrainTests
{
    // The tiles SPLAT! itself loaded in the Linux golden runs, in its order, from their console output.
    static string[] GoldenLoads(string c) =>
        Regex.Matches(Repo.Text(c + "/stdout.txt"), "Loading \"[^\"]*/([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(':', '_')).ToArray();

    [Theory]
    [InlineData("M01", 30.0, false)]
    [InlineData("M02", 30.0, false)]
    [InlineData("M04", 10.0, false)]
    [InlineData("M07", 30.0, true)]
    public void Coverage_tiles_are_the_ones_SPLAT_loaded(string c, double radius, bool hd)
    {
        var k = Repo.Site("k4hta.qth");
        Assert.Equal(GoldenLoads(c), Terrain.TilesForCoverage(k.Lat, k.Lon, radius, hd));
    }

    [Fact]
    public void A_wide_map_needs_more_tiles_and_a_limit_applies()
    {
        var k = Repo.Site("k4hta.qth");
        var t = Terrain.TilesForCoverage(k.Lat, k.Lon, 100, false);
        Assert.Equal(24, t.Count);                         // 37..40 N by 74..79 W (1.75 deg, 2.26 deg of longitude)
        Assert.Contains("37_38_79_80.sdf", t);
        Assert.Equal(155, Terrain.MaxRadiusMiles(k.Lat, false));   // 3.5 deg x 57 x cos(38.9)
        Assert.Equal(66, Terrain.MaxRadiusMiles(k.Lat, true));     // 1.5 deg
    }

    [Theory]
    [InlineData("38_39_77_78.sdf", "N38W078", "J18")]
    [InlineData("38_39_77_78-hd.sdf", "N38W078", "J18")]
    [InlineData("48_49_348_349.sdf", "N48E011", "M32")]
    [InlineData("51_52_359_0.sdf", "N51E000", "M31")]
    [InlineData("51_52_0_1.sdf", "N51W001", "M30")]
    [InlineData("-34_-33_208_209.sdf", "S34E151", "SI56")]
    public void Source_tile_names(string sdf, string hgt, string block)
    {
        var (h, lat, lon) = Terrain.SourceTile(sdf);
        Assert.Equal(hgt, h);
        Assert.Equal(block, TerrainFetcher.VfpBlock(lat, lon));
    }

    [Fact]
    public void Sea_markers_count_as_present()
    {
        string dir = Directory.CreateTempSubdirectory("splat-sea-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, Terrain.SeaMarker("10_11_20_21.sdf")), "open water");
            Assert.Empty(Terrain.Missing(dir, new[] { "10_11_20_21.sdf" }));
            Assert.Equal(new[] { "10_11_21_22.sdf" }, Terrain.Missing(dir, new[] { "10_11_20_21.sdf", "10_11_21_22.sdf" }));
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Serves the project's cached downloads (data\raw) as the terrain sources would, and counts requests.</summary>
sealed class FakeTerrainSource : HttpMessageHandler
{
    public readonly List<string> Requests = new();
    public Func<Uri, HttpResponseMessage?>? Override;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Requests.Add(req.RequestUri!.AbsoluteUri);
        if (Override?.Invoke(req.RequestUri!) is { } o) return Task.FromResult(o);
        string name = Path.GetFileName(req.RequestUri!.LocalPath);
        string file = req.RequestUri.Host.Contains("viewfinder")
            ? Path.Combine(Repo.Root, "data", "raw", "vfp", name)
            : Path.Combine(Repo.Root, "data", "raw", "skadi", name);
        return Task.FromResult(File.Exists(file)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public class TerrainFetcherTests
{
    static string Dist => Path.Combine(Repo.Root, "dist");
    static string GoldenSdf(string set, string name) => Path.Combine(Repo.Root, "data", "golden-linux", set, name);

    [Fact]
    public async Task Standard_tile_is_downloaded_converted_and_identical_to_the_Linux_golden()
    {
        var src = new FakeTerrainSource();
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var f = new TerrainFetcher(new HttpClient(src), dir, Dist);
            var r = await f.EnsureAsync(new[] { "38_39_77_78.sdf", "39_40_77_78.sdf" }, false);
            Assert.All(r, x => Assert.True(x.Ok, x.What));
            Assert.Equal("downloaded and converted", r[0].What);
            Assert.Equal("converted from the saved download", r[1].What);   // same J18 block: downloaded once
            Assert.Equal(new[] { "http://viewfinderpanoramas.org/dem3/J18.zip" }, src.Requests);
            foreach (var t in new[] { "38_39_77_78.sdf", "39_40_77_78.sdf" })
                Assert.Equal(File.ReadAllBytes(GoldenSdf("sdf3", t)), File.ReadAllBytes(Path.Combine(dir, t)));
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, "hgt3")));   // unpacked .hgt removed
            Assert.Empty(await f.EnsureAsync(new[] { "38_39_77_78.sdf" }, false));   // present: nothing to do
            Assert.Single(src.Requests);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task HD_tile_from_skadi_is_identical_to_the_Linux_golden()
    {
        var src = new FakeTerrainSource();
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var r = await new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "38_39_76_77-hd.sdf" }, true);
            Assert.True(r[0].Ok, r[0].What);
            Assert.Equal(new[] { "https://s3.amazonaws.com/elevation-tiles-prod/skadi/N38/N38W077.hgt.gz" }, src.Requests);
            Assert.Equal(File.ReadAllBytes(GoldenSdf("sdf1", "38_39_76_77-hd.sdf")), File.ReadAllBytes(Path.Combine(dir, "38_39_76_77-hd.sdf")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task No_tile_at_the_source_is_open_water()
    {
        var src = new FakeTerrainSource();
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            // HD: skadi has no N10W021 (404). Standard: J18.zip exists but has no N36W078? It does; use a block we don't have.
            var r = await new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "10_11_20_21-hd.sdf" }, true);
            Assert.True(r[0].Ok);
            Assert.Contains("open water", r[0].What);
            Assert.True(Terrain.Present(dir, "10_11_20_21-hd.sdf"));
            Assert.False(File.Exists(Path.Combine(dir, "10_11_20_21-hd.sdf")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_truncated_download_is_refused_and_not_kept()
    {
        var src = new FakeTerrainSource();
        byte[] gz = File.ReadAllBytes(Path.Combine(Repo.Root, "data", "raw", "skadi", "N38W077.hgt.gz"));
        src.Override = u => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gz[..(gz.Length / 2)]) };
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var r = await new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "38_39_76_77-hd.sdf" }, true);
            Assert.False(r[0].Ok);
            Assert.Contains("damaged", r[0].What);
            Assert.False(File.Exists(Path.Combine(dir, "38_39_76_77-hd.sdf")));
            Assert.False(File.Exists(Path.Combine(dir, "raw", "skadi", "N38W077.hgt.gz")));   // the next run downloads again
            Assert.False(Terrain.Present(dir, "38_39_76_77-hd.sdf"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_corrupt_archive_is_refused_and_removed()
    {
        // Not a zip at all (an error page, say): it can't be opened, so it is removed and fetched again next time.
        var src = new FakeTerrainSource { Override = u => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("<html>Service unavailable</html>")) } };
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var r = await new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "38_39_77_78.sdf" }, false);
            Assert.False(r[0].Ok);
            Assert.Contains("J18.zip is damaged", r[0].What);
            Assert.False(File.Exists(Path.Combine(dir, "raw", "vfp", "J18.zip")));
            Assert.False(Terrain.Present(dir, "38_39_77_78.sdf"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_wrong_size_tile_is_refused()
    {
        var src = new FakeTerrainSource();
        // A valid gzip of a short .hgt: decompresses fine, but isn't 3601 x 3601 x 2 bytes.
        var ms = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) z.Write(new byte[1000]);
        src.Override = u => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) };
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var r = await new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "38_39_76_77-hd.sdf" }, true);
            Assert.False(r[0].Ok);
            Assert.Contains("1000 bytes, not 25934402", r[0].What);
            Assert.False(Terrain.Present(dir, "38_39_76_77-hd.sdf"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task No_internet_is_explained()
    {
        var src = new FakeTerrainSource { Override = u => throw new HttpRequestException("No such host is known.") };
        string dir = Directory.CreateTempSubdirectory("splat-fetch-").FullName;
        try
        {
            var ex = await Assert.ThrowsAsync<IOException>(() => new TerrainFetcher(new HttpClient(src), dir, Dist).EnsureAsync(new[] { "38_39_77_78.sdf" }, false));
            Assert.Contains("Check the internet connection", ex.Message);
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, "raw", "vfp")));   // no partial file left
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class CoverageJobTests
{
    static string Sdf3 => Path.Combine(Repo.Root, "data", "sdf3");
    static string Dist => Path.Combine(Repo.Root, "dist");
    static RadioSettings Radio => LrpFile.Parse(Repo.Input("k4hta.lrp"));

    [Fact]
    public async Task Line_of_sight_map_matches_the_Linux_golden_M01()
    {
        var tx = Repo.Site("k4hta.qth");
        string jobs = Directory.CreateTempSubdirectory("splat-cov-").FullName;
        try
        {
            var o = new CoverageOptions(CoverageKind.LineOfSight, 30, 6, OutputName: "los", CitiesFile: Path.Combine(AppContext.BaseDirectory, "inputs", "cities.dat"));
            var job = CoverageJob.Create(jobs, tx, Radio, o, Sdf3, Dist);
            Assert.Equal(new[] { "-t", "k4hta", "-c", "6", "-R", "30", "-d", Sdf3, "-o", "los.ppm", "-s", "cities.dat", "-kml" }, job.Args());
            var r = await job.RunAsync();
            string golden = Path.Combine(Repo.Root, "data", "golden-linux", "M01");
            Assert.Equal(File.ReadAllBytes(Path.Combine(golden, "los.ppm")), File.ReadAllBytes(Path.Combine(r.Folder, "los.ppm")));
            Assert.Equal(File.ReadAllText(Path.Combine(golden, "los.kml")).Replace("\r\n", "\n"), File.ReadAllText(r.KmlPath).Replace("\r\n", "\n"));
            Assert.Null(r.ColourKeyPath);   // a line-of-sight map has no colour key
            var rec = JobRecord.Read(r.Folder)!;
            Assert.True(rec.Finished);
            Assert.Equal("los.kml", rec.MapKml);
            Assert.Null(rec.Radio);
            Assert.False(File.Exists(Path.Combine(r.Folder, "k4hta.lrp")));
            Assert.Single(JobRecord.History(jobs));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Options_become_SPLATs_flags()
    {
        var tx = Repo.Site("k4hta.qth");
        string jobs = Directory.CreateTempSubdirectory("splat-cov-").FullName;
        try
        {
            var j = CoverageJob.Create(jobs, tx, Radio, new CoverageOptions(CoverageKind.PowerDbm, 25, 3, Metric: true, Smooth: true, Model: PathModel.LongleyRice), Sdf3, Dist);
            Assert.Equal(new[] { "-t", "k4hta", "-L", "3", "-R", "25", "-d", Sdf3, "-o", "coverage.ppm", "-kml", "-dbm", "-sc", "-metric", "-olditm" }, j.Args());
            Assert.Equal(93, LrpFile.Parse(File.ReadAllText(Path.Combine(j.Folder, "k4hta.lrp"))).ErpWatts);
            var pl = CoverageJob.Create(jobs, tx, Radio, new CoverageOptions(CoverageKind.PathLoss), Sdf3, Dist);
            Assert.Equal(0, LrpFile.Parse(File.ReadAllText(Path.Combine(pl.Folder, "k4hta.lrp"))).ErpWatts);   // SPLAT! maps path loss when ERP is 0
            Assert.Contains("-L", pl.Args());
            Assert.DoesNotContain("-dbm", pl.Args());
            Assert.Empty(JobRecord.History(jobs));   // not run: not in the history
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Problems_are_found_before_anything_runs()
    {
        var tx = Repo.Site("k4hta.qth");
        Assert.Null(CoverageJob.Problem(tx, Radio, new CoverageOptions(), Dist));
        Assert.Contains("at most about 155 miles", CoverageJob.Problem(tx, Radio, new CoverageOptions(Radius: 200), Dist));
        Assert.Contains("at most about 66 miles", CoverageJob.Problem(tx, Radio, new CoverageOptions(Radius: 70, Hd: true), Dist));
        Assert.Contains("at most about 249 km", CoverageJob.Problem(tx, Radio, new CoverageOptions(Radius: 300, Metric: true), Dist));
        Assert.Contains("radius", CoverageJob.Problem(tx, Radio, new CoverageOptions(Radius: 0), Dist));
        Assert.Contains("receiver antenna", CoverageJob.Problem(tx, Radio, new CoverageOptions(RxHeight: 0), Dist));
        Assert.Contains("frequency", CoverageJob.Problem(tx, new RadioSettings(), new CoverageOptions(), Dist));
        Assert.Contains("ERP", CoverageJob.Problem(tx, new RadioSettings { FrequencyMHz = 146 }, new CoverageOptions(), Dist));
        Assert.Null(CoverageJob.Problem(tx, new RadioSettings { FrequencyMHz = 146 }, new CoverageOptions(CoverageKind.PathLoss), Dist));   // no ERP needed
        Assert.Null(CoverageJob.Problem(tx, new RadioSettings(), new CoverageOptions(CoverageKind.LineOfSight), Dist));   // no radio needed
        Assert.Contains("Choose the transmitter", CoverageJob.Problem(null, Radio, new CoverageOptions(), Dist));
        Assert.Contains("wasn't found", CoverageJob.Problem(tx, Radio, new CoverageOptions(), Path.GetTempPath()));
        string jobs = Directory.CreateTempSubdirectory("splat-cov-").FullName;
        try
        {
            var far = Repo.Site("k4hta.qth"); far.Lat = 45;
            Assert.Contains("missing", Assert.Throws<InvalidOperationException>(() => CoverageJob.Create(jobs, far, Radio, new CoverageOptions(), Sdf3, Dist)).Message);
            Assert.Empty(Directory.GetDirectories(jobs));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public async Task Cancel_stops_SPLAT_and_records_it()
    {
        var tx = Repo.Site("k4hta.qth");
        string jobs = Directory.CreateTempSubdirectory("splat-cov-").FullName;
        try
        {
            var job = CoverageJob.Create(jobs, tx, Radio, new CoverageOptions(CoverageKind.FieldStrength, 30), Sdf3, Dist);
            using var cts = new CancellationTokenSource();
            var run = job.RunAsync(new Progress<string>(s => { if (s.Contains("Computing")) cts.Cancel(); }), cts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(3));   // in case the line is missed
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            var rec = JobRecord.Read(job.Folder)!;
            Assert.False(rec.Finished);
            Assert.Equal("cancelled", rec.Note);
            Assert.False(File.Exists(Path.Combine(job.Folder, "coverage.kml")));
            Assert.Empty(JobRecord.History(jobs));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void History_is_newest_first_and_skips_broken_jobs()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-cov-").FullName;
        try
        {
            void Job(string name, DateTime when, bool finished, bool kmlThere)
            {
                string d = Directory.CreateDirectory(Path.Combine(jobs, name)).FullName;
                File.WriteAllText(Path.Combine(d, JobRecord.FileName), new JobRecord("coverage", "K4HTA", when, new CoverageOptions(), null, "coverage.kml", null, finished, null).ToJson());
                if (kmlThere) File.WriteAllText(Path.Combine(d, "coverage.kml"), "<kml/>");
            }
            Job("a", new DateTime(2026, 11, 20), true, true);
            Job("b", new DateTime(2026, 11, 22), true, true);
            Job("c", new DateTime(2026, 11, 23), false, true);   // unfinished
            Job("d", new DateTime(2026, 11, 23), true, false);   // map file gone
            Directory.CreateDirectory(Path.Combine(jobs, "e"));   // a path job or anything else: no job.json
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(jobs, "f")).FullName, JobRecord.FileName), "{ not json");
            var h = JobRecord.History(jobs);
            Assert.Equal(new[] { "b", "a" }, h.Select(x => Path.GetFileName(x.Folder)));
            Assert.Equal("2026-11-22 00:00  K4HTA, 30 mi, signal strength, RX 6 ft", h[0].Job.Describe());
            Assert.Empty(JobRecord.History(Path.Combine(jobs, "nope")));
        }
        finally { Directory.Delete(jobs, true); }
    }
}

public class KmzKeyTests
{
    [Fact]
    public void The_colour_key_goes_into_the_KMZ_or_is_dropped()
    {
        string kml = Repo.Text("M07/hd_fs.kml");
        string with = Kmz.WebSafeKml(kml, "hd_fs.png", "hd_fs-ck.png");
        Assert.Contains("<href>hd_fs-ck.png</href>", with);
        Assert.Contains("<href>hd_fs.png</href>", with);
        string without = Kmz.WebSafeKml(kml, "hd_fs.png");
        Assert.DoesNotContain("ScreenOverlay", without);
        Assert.DoesNotContain("-ck.ppm", without);

        string dir = Directory.CreateTempSubdirectory("splat-kmz-").FullName;
        try
        {
            string png = Path.Combine(dir, "hd_fs.png"), key = Path.Combine(dir, "hd_fs-ck.png"), kmz = Path.Combine(dir, "m.kmz");
            File.WriteAllBytes(png, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(key, new byte[] { 4, 5 });
            Kmz.Write(kmz, kml, png, key);
            using var z = System.IO.Compression.ZipFile.OpenRead(kmz);
            Assert.Equal(new[] { "doc.kml", "hd_fs-ck.png", "hd_fs.png" }, z.Entries.Select(e => e.FullName).Order());
        }
        finally { Directory.Delete(dir, true); }
    }
}
