using System.Text;
using SplatWin.Core;

namespace SplatWin.Tests;

static class Repo
{
    /// <summary>The project folder (it holds dist\splat.exe and data\sdf3). Tests that need the engine fail, not skip, without it.</summary>
    public static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "dist", "splat.exe")) && Directory.Exists(Path.Combine(d.FullName, "data", "sdf3")))
                    return d.FullName;
            throw new InvalidOperationException("The project folder with dist\\splat.exe and data\\sdf3 wasn't found above " + AppContext.BaseDirectory);
        }
    }
    public static string Golden(string rel) => Path.Combine(AppContext.BaseDirectory, "golden", rel);
    public static string Text(string rel) => File.ReadAllText(Golden(rel), Encoding.Latin1);
    public static string Input(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inputs", name));
    public static Station Site(string qth, double height = 0) => Station.FromQth(QthFile.Parse(Input(qth)), "test");
}

public class LrpTests
{
    [Fact]
    public void The_test_cases_lrp_reads_as_SPLAT_reads_it()
    {
        var r = LrpFile.Parse(Repo.Input("k4hta.lrp"));
        Assert.Equal(15.0, r.Dielectric);
        Assert.Equal(0.005, r.Conductivity);
        Assert.Equal(301.0, r.Bending);
        Assert.Equal(146.685, r.FrequencyMHz);
        Assert.Equal(5, r.Climate);
        Assert.Equal(1, r.Polarization);
        Assert.Equal(0.50, r.Situations);
        Assert.Equal(0.90, r.Time);
        Assert.Equal(93.0, r.ErpWatts);
        Assert.Null(r.Problem());
    }

    [Fact]
    public void Writing_and_reading_back_keeps_every_value()
    {
        var r = new RadioSettings { Dielectric = 13, Conductivity = 0.002, Bending = 320.5, FrequencyMHz = 446.0125, Climate = 6,
                                    Polarization = 0, Situations = 0.955, Time = 0.5, ErpWatts = 12.345678 };
        string text = LrpFile.Format(r);
        var b = LrpFile.Parse(text);
        Assert.Equivalent(r, b);
        Assert.All(text.Split("\r\n").Take(9), l => Assert.True(l.Length <= LrpFile.MaxLine, l));
    }

    [Fact]
    public void ERP_in_dBm_is_converted_as_SPLAT_does()
    {
        string t = Repo.Input("k4hta.lrp").Replace("93.0\t; ERP in watts", "51.82\t; ERP in dBm");
        Assert.Equal(Math.Pow(10, (51.82 - 32.14) / 10), LrpFile.Parse(t).ErpWatts, 9);
    }

    [Fact]
    public void Things_SPLAT_would_misread_are_refused()
    {
        string good = Repo.Input("k4hta.lrp");
        Assert.Contains("line 4 (frequency)", Assert.Throws<FormatException>(() => LrpFile.Parse(good.Replace("146.685", "MHz 146.685"))).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("longer than 78", Assert.Throws<FormatException>(() => LrpFile.Parse(good.Replace("; Earth Conductivity", "; " + new string('x', 80)))).Message);
        Assert.Contains("eight lines", Assert.Throws<FormatException>(() => LrpFile.Parse("15\n0.005\n")).Message);
        // SPLAT! reads "1.5" as the whole number 1 for the climate line, and so does the app.
        Assert.Equal(1, LrpFile.Parse(good.Replace("5\t; Radio Climate", "1.5\t; Radio Climate")).Climate);
    }

    [Theory]
    [InlineData("FrequencyMHz", 19.9, "frequency")]
    [InlineData("FrequencyMHz", 0, "frequency")]
    [InlineData("ErpWatts", 0, "ERP")]
    [InlineData("Dielectric", 0.5, "dielectric")]
    [InlineData("Conductivity", 0, "conductivity")]
    [InlineData("Bending", 200, "bending")]
    [InlineData("Situations", 1.0, "situations")]
    [InlineData("Time", 0, "time")]
    public void Impossible_settings_are_explained(string field, double value, string word)
    {
        var r = LrpFile.Parse(Repo.Input("k4hta.lrp"));
        typeof(RadioSettings).GetProperty(field)!.SetValue(r, value);
        Assert.Contains(word, r.Problem(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Climate_and_polarization_are_checked()
    {
        var r = LrpFile.Parse(Repo.Input("k4hta.lrp"));
        r.Climate = 8; Assert.Contains("climate", r.Problem());
        r.Climate = 5; r.Polarization = 2; Assert.Contains("polarization", r.Problem());
    }
}

public class TerrainTests
{
    [Fact]
    public void The_golden_path_needs_the_tile_SPLAT_loaded()
    {
        // Linux P01's console output: Loading "../sdf3/38:39:77:78.sdf" (Windows spells it with '_').
        var k = Repo.Site("k4hta.qth"); var r = Repo.Site("kk4das.qth");
        Assert.Equal(new[] { "38_39_77_78.sdf" }, Terrain.TilesFor(new[] { (k.Lat, k.Lon), (r.Lat, r.Lon) }, false));
        Assert.Equal(new[] { "38_39_77_78-hd.sdf" }, Terrain.TilesFor(new[] { (k.Lat, k.Lon), (r.Lat, r.Lon) }, true));
        Assert.Contains("Loading \"../sdf3/38:39:77:78.sdf\"", Repo.Text("P01/stdout.txt"));
    }

    [Fact]
    public void A_box_across_tiles_lists_them_in_SPLATs_order()
    {
        var t = Terrain.TilesFor(new[] { (38.5, -77.5), (39.5, -76.5) }, false);
        Assert.Equal(new[] { "38_39_76_77.sdf", "39_40_76_77.sdf", "38_39_77_78.sdf", "39_40_77_78.sdf" }, t);
    }

    [Theory]
    [InlineData(38.5, -77.0, "38_39_77_78.sdf")]     // exactly 77° W: SPLAT! floors the WEST longitude
    [InlineData(48.1, 11.6, "48_49_348_349.sdf")]    // east of Greenwich: 360 - 11.6 = 348.4 west
    [InlineData(51.5, -0.1, "51_52_0_1.sdf")]
    [InlineData(51.5, 0.1, "51_52_359_0.sdf")]       // just east of Greenwich wraps to 359..0
    [InlineData(-33.9, 151.2, "-34_-33_208_209.sdf")] // south and east
    public void Tile_names_across_the_world(double lat, double lon, string name) =>
        Assert.Equal(new[] { name }, Terrain.TilesFor(new[] { (lat, lon) }, false));

    [Fact]
    public void Across_Greenwich_the_box_wraps()
    {
        var t = Terrain.TilesFor(new[] { (51.5, -0.5), (51.5, 0.5) }, false);
        Assert.Equal(new[] { "51_52_359_0.sdf", "51_52_0_1.sdf" }, t);
    }

    [Fact]
    public void Missing_tiles_are_named_and_the_job_refused()
    {
        string dir = Directory.CreateTempSubdirectory("splat-terrain-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "38_39_77_78.sdf"), "x");
            var tiles = new[] { "38_39_77_78.sdf", "39_40_77_78.sdf" };
            string p = Terrain.Problem(dir, tiles, false)!;
            Assert.Contains("missing 1 of the 2", p);
            Assert.Contains("39_40_77_78.sdf", p);
            Assert.Contains("sea level", p);
            Assert.Null(Terrain.Problem(dir, tiles.Take(1).ToList(), false));
            Assert.Contains("Choose the folder", Terrain.Problem(null, tiles, false));
            Assert.Contains("doesn't exist", Terrain.Problem(Path.Combine(dir, "nope"), tiles, false));
            Assert.Contains("only 16", Terrain.Problem(dir, Enumerable.Repeat("a.sdf", 17).ToList(), true));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class PathReportTests
{
    [Fact]
    public void P01_summary_is_the_reports_own_text()
    {
        var s = PathSummary.Load(Repo.Golden("P01/K4HTA-to-KK4DAS.txt"));
        Assert.Equal("K4HTA", s.TxName);
        Assert.Equal("KK4DAS", s.RxName);
        Assert.Equal("4.69 miles", s.Distance);
        Assert.Equal("301.82 degrees", s.AzimuthToRx);
        Assert.Equal("121.78 degrees", s.AzimuthToTx);
        Assert.Equal("344.49 feet AMSL", s.TxGround);
        Assert.Equal("275.59 feet AMSL", s.RxGround);
        Assert.Equal("240.00 feet AGL / 584.49 feet AMSL", s.TxAntenna);
        Assert.Equal("ITWOM Version 3.0", s.Model);
        Assert.Equal("146.685 MHz", s.Frequency);
        Assert.Equal("93 Watts (+49.68 dBm)", s.Erp);
        Assert.Equal("93.36 dB", s.FreeSpaceLoss);
        Assert.Equal("116.68 dB", s.PathLoss);
        Assert.Equal("23.33 dB", s.Attenuation);
        Assert.Equal("55.73 dBuV/meter", s.FieldStrength);
        Assert.Equal("-64.86 dBm", s.SignalPower);
        Assert.Equal("Double Horizon Diffraction Dominant", s.Mode);
        Assert.Equal(0, s.ErrorNumber);
        Assert.Null(s.ErrorNote);
        Assert.False(s.LineOfSightClear);
        Assert.Equal(3, s.Obstructions.Count);
        Assert.Equal(new Obstruction(38.9568, -77.2733, "0.42 miles", "331.36 feet AMSL"), s.Obstructions[0]);
        Assert.Equal(new[]
        {
            "Antenna at KK4DAS must be raised to at least 39.00 feet AGL to clear all obstructions detected by SPLAT!.",
            "Antenna at KK4DAS must be raised to at least 328.00 feet AGL to clear the first Fresnel zone.",
            "Antenna at KK4DAS must be raised to at least 183.00 feet AGL to clear 60% of the first Fresnel zone.",
        }, s.Clearance);
    }

    [Fact]
    public void Every_value_shown_appears_verbatim_in_the_report()
    {
        foreach (var c in new[] { "P01/K4HTA-to-KK4DAS.txt", "P02/K4HTA-to-KK4DAS.txt", "P03/K4HTA-to-Legion_Hall.txt",
                                  "P04/K4HTA-to-Legion_Hall.txt", "P05/K4HTA-to-KK4DAS.txt", "P08/K4HTA-to-KK4DAS.txt" })
        {
            string text = Repo.Text(c);
            var s = PathSummary.Parse(text);
            foreach (var v in new[] { s.Distance, s.AzimuthToRx, s.AzimuthToTx, s.TxGround, s.RxGround, s.Model, s.Frequency, s.Erp,
                                      s.FreeSpaceLoss, s.PathLoss, s.Attenuation, s.FieldStrength, s.SignalPower, s.Mode })
            {
                Assert.NotNull(v);
                Assert.Contains(v!, text);
            }
            Assert.NotEmpty(s.Obstructions);
            Assert.All(s.Obstructions, o => Assert.Contains($"{o.Distance}, {o.Height}", text));
        }
    }

    [Fact]
    public void Longley_Rice_warnings_and_metric_units_come_through()
    {
        var p2 = PathSummary.Parse(Repo.Text("P02/K4HTA-to-KK4DAS.txt"));
        Assert.Equal("Longley-Rice", p2.Model);
        Assert.Equal("101.25 dB", p2.PathLoss);
        Assert.Equal(3, p2.ErrorNumber);
        Assert.Equal("Warning: A combination of parameters is out of range. Results are probably invalid.", p2.ErrorNote);
        Assert.Equal("Double Horizon, Diffraction Dominant", p2.Mode);

        var p5 = PathSummary.Parse(Repo.Text("P05/K4HTA-to-KK4DAS.txt"));
        Assert.Equal("7.55 kilometers", p5.Distance);
        Assert.Equal(new Obstruction(38.9568, -77.2733, "0.68 kilometers", "101.00 meters AMSL"), p5.Obstructions[0]);

        var p4 = PathSummary.Parse(Repo.Text("P04/K4HTA-to-Legion_Hall.txt"));
        Assert.Equal("Terrain has been raised by 30.00 feet to account for ground clutter.", p4.Clearance[0]);
        Assert.EndsWith("to clear 80% of the first Fresnel zone.", p4.Clearance[^1]);
        Assert.Equal(6, p4.Obstructions.Count);
    }

    [Fact]
    public void A_clear_path_and_a_missing_ERP_are_handled()
    {
        // The P01 report, edited the way SPLAT! words a clear path without an ERP (PathReport, ObstructionAnalysis).
        string t = Repo.Text("P01/K4HTA-to-KK4DAS.txt");
        int obs = t.IndexOf("Between KK4DAS", StringComparison.Ordinal);
        t = t[..obs] + "No obstructions to LOS path due to terrain were detected by SPLAT!\n\nThe first Fresnel zone is clear.\n\n60% of the first Fresnel zone is clear.\n";
        t = string.Join("\n", t.Split('\n').Where(l => !l.StartsWith("Transmitter ERP") && !l.StartsWith("Field strength") && !l.StartsWith("Signal power level")));
        var s = PathSummary.Parse(t);
        Assert.True(s.LineOfSightClear);
        Assert.Empty(s.Obstructions);
        Assert.Null(s.Erp);
        Assert.Null(s.FieldStrength);
        Assert.Null(s.SignalPower);
        Assert.Equal(3, s.Clearance.Count);
    }

    [Fact]
    public void Something_else_is_refused() =>
        Assert.Throws<InvalidDataException>(() => PathSummary.Parse(Repo.Text("P01/K4HTA-site_report.txt")));
}

public class GraphDataTests
{
    static GraphData P(string c) => GraphData.Parse(Repo.Text(c + "/splat.gp"), f => Repo.Text(c + "/" + f));

    [Fact]
    public void The_P03_height_graph_reads_with_all_five_lines()
    {
        var g = P("P03");
        Assert.Equal("SPLAT! Path Profile Between Legion_Hall and K4HTA (69.41° azimuth)\nWith First Fresnel Zone", g.Title);
        Assert.Equal("Distance Between Legion_Hall and K4HTA (3.73 miles)", g.XLabel);
        Assert.Equal((-310.712, 28.730), g.YRange);
        Assert.Equal((-601.686, -262.244), g.Y2Range);
        Assert.Equal((-0.5, 4.0), g.XRange);
        Assert.Equal(new[] { "Point-to-Point Profile", "Line of Sight Path", "Earth's Curvature Contour", "First Fresnel Zone (146.685 MHz)", "60% of First Fresnel Zone" },
                     g.Series.Select(s => s.Title));
        Assert.Equal(new[] { false, false, true, false, false }, g.Series.Select(s => s.RightAxis));
        Assert.Equal(83, g.Series[0].Points.Count);
        Assert.Equal((0.045936, -21.570475), g.Series[0].Points[1]);
        Assert.Equal(82, g.Series[2].Points.Count);
    }

    [Fact]
    public void Loss_and_HD_profile_graphs_read()
    {
        var l = P("P05");
        Assert.Equal("ITWOM Version 3.0 Path Loss (dB)", l.YLabel);
        Assert.Single(l.Series);
        Assert.Equal("Path Loss", l.Series[0].Title);
        Assert.Null(l.XRange);
        var h = P("P08");
        Assert.Equal("", h.Series[0].Title);
        Assert.Equal((258.563, 390.420), h.YRange);
        Assert.Contains("HD Terrain Profile", h.Title);
    }

    [Fact]
    public void Commands_SPLAT_never_writes_are_refused()
    {
        string cmd = Repo.Text("P05/splat.gp");
        Func<string, string> data = f => Repo.Text("P05/" + f);
        Assert.Contains("doesn't know", Assert.Throws<InvalidDataException>(() => GraphData.Parse(cmd + "set logscale y\n", data)).Message);
        Assert.Contains("plot command", Assert.Throws<InvalidDataException>(() => GraphData.Parse(cmd.Replace("with lines", "with points"), data)).Message);
        Assert.Contains("no plot", Assert.Throws<InvalidDataException>(() => GraphData.Parse(string.Join("\n", cmd.Split('\n').Where(l => !l.StartsWith("plot"))), data)).Message);
        Assert.Contains("two numbers", Assert.Throws<InvalidDataException>(() => GraphData.Parse(cmd, f => "1 2\n3 x\n")).Message);
        Assert.Contains("no data", Assert.Throws<InvalidDataException>(() => GraphData.Parse(cmd, f => "\n")).Message);
    }

    [Fact]
    public void A_graph_can_only_read_files_in_its_own_folder()
    {
        string dir = Directory.CreateTempSubdirectory("splat-graph-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "splat.gp"), "set grid\nplot \"..\\secret.gp\" title \"x\" with lines\n");
            Assert.Contains("outside its folder", Assert.Throws<InvalidDataException>(() => GraphData.Load(dir)).Message);
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Runs the real engine (dist\splat.exe) on the real terrain (data\sdf3), as the app does.</summary>
public class PathJobTests
{
    static string Norm(string s) => s.Replace("\r\n", "\n");

    static (Station, Station, RadioSettings) P01Sites() =>
        (Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), LrpFile.Parse(Repo.Input("k4hta.lrp")));

    [Fact]
    public async Task The_app_job_gives_the_Linux_golden_report_P01()
    {
        var (tx, rx, radio) = P01Sites();
        string jobs = Directory.CreateTempSubdirectory("splat-jobs-").FullName;
        try
        {
            var job = PathJob.Create(jobs, tx, rx, radio, new PathOptions(), Path.Combine(Repo.Root, "data", "sdf3"), Path.Combine(Repo.Root, "dist"));
            var r = await job.RunAsync();
            Assert.Equal(Norm(Repo.Text("P01/K4HTA-to-KK4DAS.txt")), Norm(r.ReportText));
            Assert.Equal("116.68 dB", r.Summary.PathLoss);
            Assert.Equal(4, r.Graphs.Count);
            Assert.Empty(r.GraphErrors);
            Assert.Equal(5, r.Runs.Count);
            Assert.All(r.Runs, x => Assert.Equal(0, x.ExitCode));
            // The re-run file has every command, and SPLAT! never found gnuplot (only the engine is on PATH).
            string rerun = File.ReadAllText(Path.Combine(r.Folder, PathJob.ReRunFile));
            Assert.Contains("-t k4hta -r kk4das -d", rerun);
            Assert.Contains("pushd graph-height && ", rerun);
            Assert.Contains("-h height.png -gpsav", rerun);
            Assert.Contains("'gnuplot' is not recognized", File.ReadAllText(Path.Combine(r.Folder, PathJob.LogFile)));
            Assert.Single(Directory.GetFiles(r.Folder, "*-to-*.txt"));
            Assert.Empty(Directory.GetFiles(r.Folder, "*.png", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public async Task The_height_graph_matches_the_Linux_golden_P03()
    {
        var tx = Repo.Site("k4hta.qth"); var rx = Repo.Site("legion.qth"); var radio = LrpFile.Parse(Repo.Input("k4hta.lrp"));
        string jobs = Directory.CreateTempSubdirectory("splat-jobs-").FullName;
        try
        {
            var r = await PathJob.Create(jobs, tx, rx, radio, new PathOptions(), Path.Combine(Repo.Root, "data", "sdf3"), Path.Combine(Repo.Root, "dist")).RunAsync();
            var golden = GraphData.Parse(Repo.Text("P03/splat.gp"), f => Repo.Text("P03/" + f));
            var mine = r.Graphs[GraphKind.Height];
            Assert.Equal(golden.Title, mine.Title);
            Assert.Equal(golden.YRange, mine.YRange);
            Assert.Equal(golden.Series.Select(s => s.Title), mine.Series.Select(s => s.Title));
            for (int i = 0; i < golden.Series.Count; i++)
                for (int j = 0; j < golden.Series[i].Points.Count; j++)
                {
                    // D12/P10: at most one unit in the last printed digit (1e-6).
                    Assert.True(Math.Abs(golden.Series[i].Points[j].X - mine.Series[i].Points[j].X) <= 1.0001e-6);
                    Assert.True(Math.Abs(golden.Series[i].Points[j].Y - mine.Series[i].Points[j].Y) <= 1.0001e-6);
                }
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Problems_are_found_before_anything_runs()
    {
        var (tx, rx, radio) = P01Sites();
        string sdf = Path.Combine(Repo.Root, "data", "sdf3"), dist = Path.Combine(Repo.Root, "dist");
        Assert.Null(PathJob.Problem(tx, rx, radio, new PathOptions(), sdf, dist));
        Assert.Contains("Choose a transmitter", PathJob.Problem(tx, null, radio, new PathOptions(), sdf, dist));
        Assert.Contains("same place", PathJob.Problem(tx, tx, radio, new PathOptions(), sdf, dist));
        Assert.Contains("frequency", PathJob.Problem(tx, rx, new RadioSettings { ErpWatts = 10 }, new PathOptions(), sdf, dist));
        Assert.Contains("wasn't found", PathJob.Problem(tx, rx, radio, new PathOptions(), sdf, Path.GetTempPath()));
        Assert.Contains("missing 1 of the 1 high-definition", PathJob.Problem(tx, rx, radio, new PathOptions(Hd: true), sdf, dist));
        Assert.Contains("Fresnel", PathJob.Problem(tx, rx, radio, new PathOptions(FresnelPercent: 0), sdf, dist));
        var bad = Repo.Site("kk4das.qth"); bad.Name = "KK4DAS: home";
        Assert.Contains("Rename the station", PathJob.Problem(tx, bad, radio, new PathOptions(), sdf, dist));
        var far = Repo.Site("kk4das.qth"); far.Lat = 41.5;
        Assert.Contains("missing", PathJob.Problem(tx, far, radio, new PathOptions(), sdf, dist));
    }

    [Theory]
    [InlineData("K4HTA", "K4HTA")]
    [InlineData("Post 180", "Post_180")]
    [InlineData("W1AW/4", "W1AW_4")]
    [InlineData("A.B", "A_B")]
    [InlineData("...", "tx")]
    public void Qth_names_are_safe_for_SPLAT(string name, string file) => Assert.Equal(file, PathJob.SafeName(name, "tx"));

    [Fact]
    public void Report_names_follow_SPLATs_substitutions() =>
        Assert.Equal("Post_180-to-W1AW_4_x.txt", PathJob.ReportName("Post 180", "W1AW/4*x"));

    [Fact]
    public void Options_become_SPLATs_flags()
    {
        var (tx, rx, radio) = P01Sites();
        string jobs = Directory.CreateTempSubdirectory("splat-jobs-").FullName;
        try
        {
            var j = PathJob.Create(jobs, tx, rx, radio, new PathOptions(PathModel.LongleyRice, true, 80, 9.5), Path.Combine(Repo.Root, "data", "sdf3"), Path.Combine(Repo.Root, "dist"));
            Assert.Equal(new[] { "-t", "k4hta", "-r", "kk4das", "-d", Path.Combine(Repo.Root, "data", "sdf3"), "-olditm", "-metric", "-fz", "80", "-gc", "9.5", "-kml" }, j.Args(null));
            Assert.Equal(new[] { "-l", "loss.png", "-gpsav" }, j.Args(GraphKind.Loss).TakeLast(3));
            Assert.DoesNotContain("-kml", j.Args(GraphKind.Loss));   // the path KML comes from the report run only
            // Each graph folder has its own copy of the inputs (SPLAT! cuts .lrp names at the first '.').
            foreach (var g in PathJob.GraphRuns.Values)
                Assert.Equal(new[] { "k4hta.lrp", "k4hta.qth", "kk4das.qth" }, Directory.GetFiles(Path.Combine(j.Folder, g.Folder)).Select(Path.GetFileName).Order());
            Assert.Equal(LrpFile.Format(radio), File.ReadAllText(Path.Combine(j.Folder, "k4hta.lrp")));
        }
        finally { Directory.Delete(jobs, true); }
    }
}
