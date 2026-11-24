using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SplatWin.Core;
using Xunit;

namespace SplatWin.Tests;

/// <summary>
/// Stage G8 (3D path view): the view's numbers are SPLAT!'s own. Each test runs a real path job with
/// the engine, then recomputes SPLAT!'s terrain profile, height-graph data (curvature, line of sight,
/// Fresnel zones) and obstruction list with the 3D view's code, and requires them to match the files
/// SPLAT! wrote: graph data exactly or to the last printed digit, the obstruction list character for character.
/// </summary>
public class G8Tests
{
    static PathResult Run(string jobs, Station tx, Station rx, PathOptions o, RadioSettings? radio = null) =>
        PathJob.Create(jobs, tx, rx, radio ?? G5.Radio(), o, G5.Sdf3, G5.Dist).RunAsync().GetAwaiter().GetResult();

    static void AllPass(PathResult r, int minObstructions = 0)
    {
        var scene = PathScene.Load(PathJobSetup.FromJobFolder(r.Folder));
        var checks = scene.Verify();
        Assert.True(checks.All(c => c.Ok), string.Join("\n", checks.Select(c => $"{(c.Ok ? "ok  " : "FAIL")} {c.What}: {c.Detail}")));
        Assert.True(checks.Count >= 8, $"only {checks.Count} checks ran");
        Assert.True(scene.Obstructions().Count >= minObstructions, "the case should have obstructions, or the list check proves little");
    }

    [Fact]
    public void Standard_terrain_K4HTA_to_KK4DAS_and_Legion_Hall()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            AllPass(Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), new PathOptions()));
            AllPass(Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("legion.qth"), new PathOptions()), minObstructions: 1);
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Metric_Fresnel_80_and_clutter_and_user_terrain()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            AllPass(Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("legion.qth"), new PathOptions(Metric: true)), minObstructions: 1);
            AllPass(Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), new PathOptions(FresnelPercent: 80, Clutter: 30)), minObstructions: 1);
            AllPass(Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), new PathOptions(UdtFile: G5.Inputs("obstacle.udt"))), minObstructions: 1);
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Shown_to_fire_a_wrong_terrain_height_or_curvature_is_caught()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            var r = Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("legion.qth"), new PathOptions());
            // Change one terrain point in SPLAT!'s profile data: the terrain check must fail.
            string gp = Path.Combine(r.Folder, "graph-profile", "profile.gp");
            var lines = File.ReadAllLines(gp);
            var p = lines[40].Split('\t');
            lines[40] = p[0] + "\t" + (double.Parse(p[1], CultureInfo.InvariantCulture) + 3.28084).ToString("F6", CultureInfo.InvariantCulture);
            File.WriteAllLines(gp, lines);
            // And one height above the line of sight by a hundredth of a foot: the height check must fail.
            string hp = Path.Combine(r.Folder, "graph-height", "profile.gp");
            var h = File.ReadAllLines(hp);
            var q = h[60].Split('\t');
            h[60] = q[0] + "\t" + (double.Parse(q[1], CultureInfo.InvariantCulture) + 0.01).ToString("F6", CultureInfo.InvariantCulture);
            File.WriteAllLines(hp, h);
            var checks = PathScene.Load(PathJobSetup.FromJobFolder(r.Folder)).Verify();
            Assert.Contains(checks, c => !c.Ok && c.What.StartsWith("terrain along the path") && c.Detail.StartsWith("line 41"));
            Assert.Contains(checks, c => !c.Ok && c.What.Contains("profile.gp (curvature") && c.Detail.StartsWith("line 61"));
            Assert.Contains(checks, c => c.Ok && c.What.Contains("fresnel.gp"));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void The_scene_puts_the_line_of_sight_from_antenna_top_to_antenna_top_and_marks_SPLATs_points()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            var r = Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("legion.qth"), new PathOptions());
            var scene = PathScene.Load(PathJobSetup.FromJobFolder(r.Folder));
            using var doc = JsonDocument.Parse(scene.BuildJson(1.0));
            var s = doc.RootElement;
            double rxTop = s.GetProperty("rx").GetProperty("top").GetDouble(), txTop = s.GetProperty("tx").GetProperty("top").GetDouble();
            Assert.Equal(scene.RxTop, rxTop); Assert.Equal(scene.TxTop, txTop);
            var prof = s.GetProperty("profile");
            var ps = prof.GetProperty("s").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var pz = prof.GetProperty("z").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var ph = prof.GetProperty("h").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double L = s.GetProperty("lengthMiles").GetDouble();
            // Each point's height above the line of sight is SPLAT!'s height-graph value (graph-height\profile.gp, feet).
            var gp = File.ReadAllLines(Path.Combine(r.Folder, "graph-height", "profile.gp")).Select(l => double.Parse(l.Split('\t')[1], CultureInfo.InvariantCulture)).ToArray();
            for (int i = 1; i < ps.Length - 1; i++)
            {
                Assert.True(Math.Abs(ph[i] - gp[i]) <= 1.0000001e-6, $"point {i}: {ph[i]} vs SPLAT! {gp[i]}");
                double los = rxTop + (txTop - rxTop) * ps[i] / L;
                Assert.True(Math.Abs((pz[i] - los) - ph[i]) < 1e-6, $"point {i}: the drawn ground isn't at its height above the line of sight");
            }
            var obs = s.GetProperty("obstructions");
            Assert.Equal(scene.Obstructions().Count, obs.GetArrayLength());
            Assert.True(obs.GetArrayLength() > 0);
            Assert.StartsWith("Blocked:", s.GetProperty("summary").GetString());
            // The strip: rows x columns of heights, the middle column on the path itself.
            int nr = s.GetProperty("rows").GetArrayLength(), nc = s.GetProperty("cross").GetArrayLength();
            Assert.Equal(nr * nc * 4, Convert.FromBase64String(s.GetProperty("z").GetString()!).Length);
            Assert.Equal(0.0, s.GetProperty("cross")[nc / 2].GetDouble());
            Assert.InRange(s.GetProperty("cross")[nc - 1].GetDouble(), 1.0, 1.05);
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Clear_path_says_clear_and_the_summary_follows_the_report()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            var r = Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("kk4das.qth"), new PathOptions());
            var scene = PathScene.Load(PathJobSetup.FromJobFolder(r.Folder));
            bool reportClear = r.Summary.Obstructions.Count == 0;
            using var doc = JsonDocument.Parse(scene.BuildJson(0.5));
            string summary = doc.RootElement.GetProperty("summary").GetString()!;
            Assert.Equal(!reportClear, summary.StartsWith("Blocked:"));
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void Google_Earth_file_has_the_true_line_of_sight_masts_points_and_antenna_views()
    {
        string jobs = Directory.CreateTempSubdirectory("splat-g8-").FullName;
        try
        {
            var r = Run(jobs, Repo.Site("k4hta.qth"), Repo.Site("legion.qth"), new PathOptions());
            var scene = PathScene.Load(PathJobSetup.FromJobFolder(r.Folder));
            using var z = ZipFile.OpenRead(r.KmzPath!);
            string doc = new StreamReader(z.GetEntry("doc.kml")!.Open(), Encoding.UTF8).ReadToEnd();
            var x = XDocument.Parse(doc);
            Assert.Contains("xmlns=\"http://www.opengis.net/kml/2.2\"", doc);
            // Google Earth opens at the receiver's antenna, looking at the transmitter.
            var cam = x.Descendants().First(e => e.Name.LocalName == "Camera");
            double V(XElement e, string n) => double.Parse(e.Elements().First(c => c.Name.LocalName == n).Value, CultureInfo.InvariantCulture);
            Assert.Equal(Math.Round(scene.RxTop * 0.3048 + 2, 1), V(cam, "altitude"), 1);
            Assert.Equal(SplatGeo.Azimuth(scene.Job.Rx, scene.Job.Tx), V(cam, "heading"), 2);
            Assert.Contains("<name>Overview (SPLAT!'s view)</name>", doc);
            Assert.Single(Regex.Matches(doc, "<LookAt>"));
            // The line of sight: absolute altitudes from one antenna top to the other.
            var los = x.Descendants().First(e => e.Name.LocalName == "Placemark" && e.Elements().Any(n => n.Name.LocalName == "name" && n.Value == "Line of sight (true height)"));
            var coords = los.Descendants().First(e => e.Name.LocalName == "coordinates").Value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            double Alt(string c) => double.Parse(c.Split(',')[2], CultureInfo.InvariantCulture);
            Assert.Equal(Math.Round(scene.RxTop * 0.3048, 2), Alt(coords[0]), 2);
            Assert.Equal(Math.Round(scene.TxTop * 0.3048, 2), Alt(coords[^1]), 2);
            Assert.True(coords.Length > 50);
            Assert.Contains(los.Descendants(), e => e.Name.LocalName == "altitudeMode" && e.Value == "absolute");
            // Between the antennas, the line bows above a straight interpolation of the tops: the 4/3-Earth curvature is in it.
            double mid = Alt(coords[coords.Length / 2]), straight = (Alt(coords[0]) + Alt(coords[^1])) / 2;
            Assert.True(mid > straight, $"midpoint {mid} m should be above {straight} m");
            // SPLAT!'s obstruction points, one placemark each, and both masts.
            int n = scene.Obstructions().Count;
            Assert.True(n > 0);
            Assert.Contains($"Terrain in the way ({n} point", doc);
            Assert.Equal(2, Regex.Matches(doc, "antenna, [0-9]+ ft above sea level").Count);
            Assert.Equal(2, Regex.Matches(doc, "<name>View from ").Count);
        }
        finally { Directory.Delete(jobs, true); }
    }

    [Fact]
    public void ReadBearing_and_LoadQth_read_as_SPLAT_does()
    {
        Assert.Equal(38.924216, SplatGeo.ReadBearing("38.924216\n"));
        Assert.Equal(38 + 55 / 60.0 + 27.18 / 3600.0, SplatGeo.ReadBearing("38 55 27.18\n"), 12);
        Assert.Equal(-(0 + 30 / 60.0), SplatGeo.ReadBearing("0 -30 0\n"), 12);
        Assert.Equal(0.0, SplatGeo.ReadBearing("38 55\n"));          // one space: SPLAT! reads 0
        Assert.Equal(0.0, SplatGeo.ReadBearing("400\n"));            // out of range: 0
        string f = Path.GetTempFileName();
        try
        {
            File.WriteAllText(f, "Mast\n38.5\n-77.25\n9.14 meters\n");
            var (name, s) = SplatGeo.LoadQth(f);
            Assert.Equal("Mast", name);
            Assert.Equal(360 - 77.25, s.Lon);                         // negative longitudes wrap, as LoadQTH does
            Assert.Equal((float)((float)9.14 * 3.28084), s.Alt);      // a float, as SPLAT! keeps it
        }
        finally { File.Delete(f); }
    }
}
