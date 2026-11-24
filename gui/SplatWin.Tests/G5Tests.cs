using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SplatWin.Core;

namespace SplatWin.Tests;

/// <summary>Stage G5: antenna patterns, user terrain, no terrain shading, path KML/KMZ, station pins, colour readout.</summary>
static class G5
{
    public static string Golden(string rel) => Path.Combine(Repo.Root, "tests", "golden", "linux-g5", rel);
    public static string GoldenBig(string rel) => Path.Combine(Repo.Root, "data", "golden-linux-g5", rel);   // the maps (not in git)
    public static string Inputs(string name) => Path.Combine(Repo.Root, "tests", "cases", "inputs", name);
    public static string Sdf3 => Path.Combine(Repo.Root, "data", "sdf3");
    public static string Dist => Path.Combine(Repo.Root, "dist");
    public static string Lf(string path) => File.ReadAllText(path, Encoding.Latin1).Replace("\r\n", "\n");
    public static RadioSettings Radio(string lrp = "k4hta.lrp") => LrpFile.Parse(File.ReadAllText(Inputs(lrp)));
    public static Station PatternTx()
    {
        var tx = Repo.Site("k4hta_pat.qth");
        tx.Pattern = AntennaPattern.Load(Inputs("k4hta_pat.az"));
        return tx;
    }

    /// <summary>Same text, except numbers that differ by at most one unit in their last printed digit (owner decision 2026-11-23, path KML).</summary>
    public static void SameWithinLastDigit(string expected, string actual)
    {
        var e = expected.Split('\n'); var a = actual.Split('\n');
        Assert.Equal(e.Length, a.Length);
        var num = new Regex(@"-?\d+\.\d+");
        for (int i = 0; i < e.Length; i++)
        {
            if (e[i] == a[i]) continue;
            Assert.Equal(num.Replace(e[i], "#"), num.Replace(a[i], "#"));
            var en = num.Matches(e[i]); var an = num.Matches(a[i]);
            for (int k = 0; k < en.Count; k++)
            {
                double unit = Math.Pow(10, -en[k].Value.Split('.')[1].Length);
                Assert.True(Math.Abs(double.Parse(en[k].Value, CultureInfo.InvariantCulture) - double.Parse(an[k].Value, CultureInfo.InvariantCulture)) <= unit * 1.000001,
                            $"line {i + 1}: {en[k].Value} vs {an[k].Value}");
            }
        }
    }
}

public class AntennaPatternTests
{
    [Fact]
    public void The_test_pattern_loads_with_its_elevation_file()
    {
        var p = AntennaPattern.Load(G5.Inputs("k4hta_pat.az"));
        Assert.NotNull(p.El);
        Assert.Equal("k4hta_pat.az and k4hta_pat.el", p.Source);
        Assert.Null(p.Problem());
        Assert.Equal(File.ReadAllText(G5.Inputs("k4hta_pat.az"), Encoding.Latin1), p.Az);   // passed to SPLAT! unchanged
        var viaEl = AntennaPattern.Load(G5.Inputs("k4hta_pat.el"));                          // either file may be picked
        Assert.Equal(p.Az, viaEl.Az);
    }

    [Theory]
    [InlineData("0\n0 0.5\n90 1.5\n", "amplitude 1.5")]
    [InlineData("0\n0 0.5\n400 1\n", "angle 400")]
    [InlineData("", "empty")]
    [InlineData("0\n0 0.5\n", "at least two")]
    [InlineData("0\nNorth 1\n90 1\n", "isn't numbers")]
    [InlineData("rotation\n0 1\n90 1\n", "isn't numbers")]
    public void Bad_azimuth_files_are_refused(string az, string expected)
    {
        var why = new AntennaPattern { Az = az, Source = "t.az" }.Problem();
        Assert.NotNull(why);
        Assert.Contains(expected, why);
    }

    [Fact]
    public void A_bad_elevation_file_is_refused_and_a_missing_az_is_explained()
    {
        Assert.Contains("-20", new AntennaPattern { Az = "0\n0 1\n180 1\n", El = "0 0\n-20 1\n0 1\n" }.Problem());
        string dir = Directory.CreateTempSubdirectory("splat-pat-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "only.el"), "0 0\n0 1\n10 1\n");
            var ex = Assert.Throws<InvalidDataException>(() => AntennaPattern.Load(Path.Combine(dir, "only.el")));
            Assert.Contains("only.az", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_pattern_is_saved_with_the_station()
    {
        string dir = Directory.CreateTempSubdirectory("splat-lib-").FullName;
        try
        {
            string file = Path.Combine(dir, "stations.json");
            var lib = StationLibrary.Load(file);
            lib.Stations.Add(G5.PatternTx());
            lib.Save();
            var back = StationLibrary.Load(file).Stations.Single();
            Assert.Equal(lib.Stations[0].Pattern!.Az, back.Pattern!.Az);
            Assert.Equal(lib.Stations[0].Pattern!.El, back.Pattern!.El);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class UserTerrainTests
{
    [Fact]
    public void The_test_obstacles_read_as_SPLAT_reads_them()
    {
        var (pts, why) = UserTerrain.Parse(File.ReadAllText(G5.Inputs("obstacle.udt")));
        Assert.Null(why);
        Assert.Equal(49, pts.Count);
        Assert.Equal(38.939608, pts[0].Lat, 6);
        Assert.Equal(-77.240400, pts[0].Lon, 6);          // the file says 77.2404 (degrees west)
        Assert.Equal(200 * 0.3048, pts[0].HeightMetres, 6); // feet unless it ends in m
    }

    [Theory]
    [InlineData("38.9, 77.2, 30m", 30.0)]
    [InlineData("38.9, 77.2, 30 meters", 30.0)]
    [InlineData("38 54 0, 77 12 0, 100", 30.48)]
    public void Heights_and_bearings(string line, double metres)
    {
        var (pts, why) = UserTerrain.Parse(line);
        Assert.Null(why);
        Assert.Equal(metres, pts[0].HeightMetres, 6);
        Assert.Equal(38.9, pts[0].Lat, 6);
        Assert.Equal(-77.2, pts[0].Lon, 6);
    }

    [Theory]
    [InlineData("38.9, 77.2", "latitude, longitude, height")]
    [InlineData("38.9, 77.2, 0", "above zero")]
    [InlineData("north, 77.2, 10", "isn't a latitude")]
    [InlineData("; only a comment\n", "no obstacles")]
    public void Bad_lines_are_refused(string text, string expected) => Assert.Contains(expected, UserTerrain.Parse(text).Problem);

    [Fact]
    public void East_negative_longitudes_are_caught_by_the_area_check()
    {
        var (tx, rx) = (Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"));
        var tiles = PathJob.TilesFor(tx, rx, new PathOptions());
        Assert.Null(UserTerrain.Problem(G5.Inputs("obstacle.udt"), tiles, false));
        string f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, File.ReadAllText(G5.Inputs("obstacle.udt")).Replace(", 77.", ", -77."));
            Assert.Contains("degrees WEST", UserTerrain.Problem(f, tiles, false));
        }
        finally { File.Delete(f); }
    }
}

public class G5PathJobTests
{
    [Fact]
    public void A_path_job_makes_SPLATs_path_KML_and_a_Google_Earth_file_P09()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g5p-").FullName;
        try
        {
            var job = PathJob.Create(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), G5.Radio(), new PathOptions(), G5.Sdf3, G5.Dist);
            var r = job.RunAsync().GetAwaiter().GetResult();
            Assert.Equal(G5.Lf(G5.Golden("P09/K4HTA-to-KK4DAS.txt")), G5.Lf(r.ReportPath));
            G5.SameWithinLastDigit(G5.Lf(G5.Golden("P09/K4HTA-to-KK4DAS.kml")), G5.Lf(Path.Combine(r.Folder, "K4HTA-to-KK4DAS.kml")));

            Assert.NotNull(r.KmzPath);
            using var z = ZipFile.OpenRead(r.KmzPath!);
            Assert.Single(z.Entries);
            string doc = new StreamReader(z.GetEntry("doc.kml")!.Open(), Encoding.UTF8).ReadToEnd();
            XDocument.Parse(doc);
            Assert.DoesNotContain("root://", doc);
            Assert.DoesNotContain("\uFFFD", doc);
            Assert.Contains("38° 55' 27\" North", doc);                       // SPLAT!'s Latin-1 degree sign, now UTF-8
            Assert.Equal(2, Regex.Matches(doc, "<LineStyle>").Count);           // the path lines keep their colours
            Assert.Contains("<name>Line-of-Sight Path</name>", doc);
            Assert.Equal(2, Regex.Matches(doc, "<Point>").Count);               // both stations
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Pattern_obstacles_and_metric_match_Linux_P10()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g5p-").FullName;
        try
        {
            var o = new PathOptions(Metric: true, UdtFile: G5.Inputs("obstacle.udt"));
            var job = PathJob.Create(jobs, G5.PatternTx(), Repo.Site("legion.qth"), G5.Radio("k4hta_pat.lrp"), o, G5.Sdf3, G5.Dist);
            Assert.Equal(new[] { "-t", "k4hta_pattern", "-r", "legion_hall", "-d", G5.Sdf3, "-metric", "-udt", "obstacles.udt", "-kml" }, job.Args(null));
            Assert.Contains("-udt", job.Args(GraphKind.Profile));
            Assert.True(File.Exists(Path.Combine(job.Folder, "k4hta_pattern.az")) && File.Exists(Path.Combine(job.Folder, "graph-loss", "k4hta_pattern.el")));
            var r = job.RunAsync().GetAwaiter().GetResult();
            Assert.Equal(G5.Lf(G5.Golden("P10/K4HTA_pattern-to-Legion_Hall.txt")), G5.Lf(r.ReportPath));
            Assert.Equal(G5.Lf(G5.Golden("P10/K4HTA_pattern-site_report.txt")), G5.Lf(Path.Combine(r.Folder, "K4HTA_pattern-site_report.txt")));
            G5.SameWithinLastDigit(G5.Lf(G5.Golden("P10/K4HTA_pattern-to-Legion_Hall.kml")), G5.Lf(Path.Combine(r.Folder, "K4HTA_pattern-to-Legion_Hall.kml")));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Pattern_alone_matches_Linux_P07_and_obstacles_alone_P06()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g5p-").FullName;
        try
        {
            var p07 = PathJob.Create(jobs, G5.PatternTx(), Repo.Site("kk4das.qth"), G5.Radio("k4hta_pat.lrp"), new PathOptions(), G5.Sdf3, G5.Dist).RunAsync().GetAwaiter().GetResult();
            Assert.Equal(Repo.Text("P07/K4HTA_pattern-to-KK4DAS.txt").Replace("\r\n", "\n"), G5.Lf(p07.ReportPath));
            var p06 = PathJob.Create(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), G5.Radio(), new PathOptions(UdtFile: G5.Inputs("obstacle.udt")), G5.Sdf3, G5.Dist)
                             .RunAsync().GetAwaiter().GetResult();
            Assert.Equal(Repo.Text("P06/K4HTA-to-KK4DAS.txt").Replace("\r\n", "\n"), G5.Lf(p06.ReportPath));
            // Obstacles must change the answer, or this test proves nothing.
            Assert.NotEqual(Repo.Text("P01/K4HTA-to-KK4DAS.txt").Replace("\r\n", "\n"), G5.Lf(p06.ReportPath));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void A_bad_pattern_or_obstacles_file_stops_the_job_before_it_runs()
    {
        var tx = G5.PatternTx();
        tx.Pattern!.Az = "0\n0 2\n90 1\n";
        Assert.Contains("antenna pattern", PathJob.Problem(tx, Repo.Site("kk4das.qth"), G5.Radio(), new PathOptions(), G5.Sdf3, G5.Dist));
        Assert.Contains("doesn't exist", PathJob.Problem(Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), G5.Radio(),
                                                         new PathOptions(UdtFile: @"C:\nowhere\x.udt"), G5.Sdf3, G5.Dist));
    }
}

public class G5CoverageTests
{
    [Fact]
    public void No_shading_line_of_sight_map_is_byte_identical_to_Linux_M10()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g5c-").FullName;
        try
        {
            var o = new CoverageOptions(CoverageKind.LineOfSight, 20, 6, OutputName: "los_ngs", NoShading: true);
            var job = CoverageJob.Create(jobs, Repo.Site("k4hta.qth"), G5.Radio(), o, G5.Sdf3, G5.Dist);
            Assert.Equal(new[] { "-t", "k4hta", "-c", "6", "-R", "20", "-d", G5.Sdf3, "-o", "los_ngs.ppm", "-ngs", "-kml" }, job.Args());
            var r = job.RunAsync().GetAwaiter().GetResult();
            Assert.Equal(File.ReadAllBytes(G5.GoldenBig("M10/los_ngs.ppm")), File.ReadAllBytes(Path.Combine(r.Folder, "los_ngs.ppm")));
            Assert.Equal(G5.Lf(G5.Golden("M10/los_ngs.kml")), G5.Lf(r.KmlPath));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Pattern_obstacles_and_no_shading_signal_map_is_byte_identical_to_Linux_M09()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g5c-").FullName;
        try
        {
            var o = new CoverageOptions(CoverageKind.FieldStrength, 30, 6, OutputName: "pat", NoShading: true, UdtFile: G5.Inputs("obstacle.udt"));
            var job = CoverageJob.Create(jobs, G5.PatternTx(), G5.Radio("k4hta_pat.lrp"), o, G5.Sdf3, G5.Dist);
            var r = job.RunAsync().GetAwaiter().GetResult();
            Assert.Equal(File.ReadAllBytes(G5.GoldenBig("M09/pat.ppm")), File.ReadAllBytes(Path.Combine(r.Folder, "pat.ppm")));
            Assert.Equal(File.ReadAllBytes(G5.GoldenBig("M09/pat-ck.ppm")), File.ReadAllBytes(Path.Combine(r.Folder, "pat-ck.ppm")));
            Assert.Equal(G5.Lf(G5.Golden("M09/pat.kml")), G5.Lf(r.KmlPath));
            // The colour file names its own file in its header; the app's site file is k4hta_pattern, the test's k4hta_pat.
            Assert.Equal(G5.Lf(G5.Golden("M09/k4hta_pat.scf")).Replace("k4hta_pat.scf", "X"), G5.Lf(Path.Combine(r.Folder, "k4hta_pattern.scf")).Replace("k4hta_pattern.scf", "X"));
            var rec = JobRecord.Read(r.Folder)!;
            Assert.Equal("k4hta_pat.az and k4hta_pat.el", rec.Pattern);
            Assert.True(rec.Coverage!.NoShading);
            Assert.Contains("antenna pattern, obstacles", rec.Describe());
        }
        finally { Directory.Delete(jobs, true); }
    }
}

public class KmzG5Tests
{
    [Fact]
    public void Station_pins_are_added_in_their_own_folder()
    {
        string kml = Repo.Text("M01/los.kml");
        string doc = Kmz.WebSafeKml(kml, "los.png", null, new[] { new KmlPin("KK4DAS", 38.96, -77.28, "30 ft"), new KmlPin("A & B <club>", 39.0, -77.0, "x") });
        var x = XDocument.Parse(doc);
        var stations = x.Descendants().Single(e => e.Name.LocalName == "Folder" && e.Elements().Any(c => c.Name.LocalName == "name" && c.Value == "Stations"));
        Assert.Equal(2, stations.Elements().Count(e => e.Name.LocalName == "Placemark"));
        Assert.Contains("A &amp; B &lt;club&gt;", doc);
        Assert.Contains("-77.280000,38.960000,0", doc);
        Assert.Equal(Kmz.WebSafeKml(kml, "los.png"), Kmz.WebSafeKml(kml, "los.png", null, Array.Empty<KmlPin>()));
    }

    [Fact]
    public void Coverage_KML_is_cleaned_as_before()
    {
        // Only the Pro-only icon styles go; a coverage KML has no others, so its result is what G4 made.
        string kml = Repo.Text("M01/los.kml");
        string g4 = Regex.Replace(kml, @"\s*<Style>.*?</Style>", "", RegexOptions.Singleline);
        g4 = Regex.Replace(g4, @"(<GroundOverlay>.*?<href>)[^<]*(</href>)", m => m.Groups[1].Value + "los.png" + m.Groups[2].Value, RegexOptions.Singleline);
        g4 = Regex.Replace(g4, @"\s*<ScreenOverlay>.*?</ScreenOverlay>", "", RegexOptions.Singleline);
        Assert.Equal(g4, Kmz.WebSafeKml(kml, "los.png"));
    }

    [Fact]
    public void Latin1_KML_is_read_as_Latin1_and_UTF8_as_UTF8()
    {
        string f = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(f, Encoding.Latin1.GetBytes("<a>38\u00B0</a>"));
            Assert.Equal("<a>38°</a>", Kmz.ReadSplatKml(f));
            File.WriteAllBytes(f, Encoding.UTF8.GetBytes("<a>38\u00B0 é</a>"));
            Assert.Equal("<a>38° é</a>", Kmz.ReadSplatKml(f));
        }
        finally { File.Delete(f); }
    }
}

public class ColourKeyTests
{
    static ColourKey Scf() => ColourKey.Load(G5.Golden("M09/k4hta_pat.scf"));

    [Fact]
    public void Bands_read_back_as_SPLAT_paints_them()
    {
        var k = Scf();
        Assert.Equal(13, k.Bands.Count);
        Assert.Equal("128 dBµV/m or more", k.Describe(255, 0, 0, false));
        Assert.Equal("38 to 48 dBµV/m", k.Describe(80, 80, 255, false));
        Assert.Equal("8 to 18 dBµV/m", k.Describe(140, 0, 128, false));
        Assert.StartsWith("no colour: below 8 dBµV/m", k.Describe(255, 255, 255, false));   // -ngs
        Assert.StartsWith("no colour: below 8 dBµV/m", k.Describe(97, 97, 97, false));      // terrain shading
        Assert.Null(k.Describe(1, 2, 3, false));
    }

    [Fact]
    public void Every_colour_in_a_real_SPLAT_map_is_understood()
    {
        // M09 (signal, no shading): each pixel is a band colour or white, apart from SPLAT!'s own markings.
        byte[] b = File.ReadAllBytes(G5.GoldenBig("M09/pat.ppm"));
        int start = 0;
        for (int nl = 0; nl < 3; start++) if (b[start] == '\n') nl++;
        var k = Scf();
        var unknown = new HashSet<(byte, byte, byte)>();
        int described = 0;
        for (int i = start; i + 2 < b.Length; i += 3)
        {
            if (k.Describe(b[i], b[i + 1], b[i + 2], false) != null) described++;
            else unknown.Add((b[i], b[i + 1], b[i + 2]));
        }
        Assert.Empty(unknown);
        Assert.Equal((b.Length - start) / 3, described);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Smooth_colours_give_an_estimate_within_two_units(bool loss)
    {
        // Colours made with SPLAT!'s own interpolate() (ceil), for every level between the first and last band.
        var k = loss ? ColourKey.Parse("80: 255, 0, 0\n90: 255, 128, 0\n100: 255, 255, 0\n110: 0, 255, 0\n120: 0, 0, 255\n", ColourKey.Kinds.Loss) : Scf();
        static int Interp(int y0, int y1, int x0, int x1, int n) =>
            n <= x0 ? y0 : n >= x1 ? y1 : y0 == y1 || x0 == x1 ? y0 : y0 + (int)Math.Ceiling(((double)(y0 - y1) / (x0 - x1)) * (n - x0));
        var L = k.Bands;
        int tested = 0;
        for (int z = 1; z < L.Count; z++)
        {
            var (a, b) = loss ? (L[z - 1], L[z]) : (L[z], L[z - 1]);   // a: the lower level, as SPLAT! calls it
            for (int n = Math.Min(a.Level, b.Level) + 1; n < Math.Max(a.Level, b.Level); n++)
            {
                byte r = (byte)Interp(a.R, b.R, a.Level, b.Level, n), g = (byte)Interp(a.G, b.G, a.Level, b.Level, n), bl = (byte)Interp(a.B, b.B, a.Level, b.Level, n);
                string? d = k.Describe(r, g, bl, true);
                Assert.NotNull(d);
                var m = Regex.Match(d!, @"^about (-?\d+)");
                if (!m.Success) continue;   // an exact band colour
                Assert.InRange(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), n - 2, n + 2);
                tested++;
            }
        }
        Assert.True(tested > (loss ? 30 : 100), $"only {tested} smooth colours tested");
    }
}
