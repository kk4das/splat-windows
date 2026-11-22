using System.Text;
using SplatWin.Core;

namespace SplatWin.Tests;

public class KmlOverlayTests
{
    // The real coverage KML from the Linux golden run (case M01, K4HTA line-of-sight map).
    static string Golden(string rel) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", rel));

    [Fact]
    public void Parses_SPLAT_coverage_kml()
    {
        var k = KmlOverlay.Parse(Golden("M01/los.kml"));
        Assert.Equal("los.ppm", k.ImageHref);
        Assert.Equal(39.99917, k.North, 5); Assert.Equal(38.0, k.South, 5);
        Assert.Equal(-76.00083, k.East, 5); Assert.Equal(-78.0, k.West, 5);
        var tx = Assert.Single(k.Placemarks);
        Assert.Equal("K4HTA", tx.Name);
        Assert.Equal(38.924216, tx.Lat, 6); Assert.Equal(-77.205793, tx.Lon, 6);   // west is negative here
    }

    [Fact]
    public void Rejects_kml_without_a_map()
    {
        const string pathKml = "<kml><Folder><Placemark><name>A</name><Point><coordinates>-77,38,30</coordinates></Point></Placemark></Folder></kml>";
        Assert.Throws<FormatException>(() => KmlOverlay.Parse(pathKml));
    }

    [Fact]
    public void Rejects_an_inside_out_box()
    {
        var bad = Golden("M01/los.kml").Replace("<north>39.99917</north>", "<north>37.0</north>");
        Assert.Throws<FormatException>(() => KmlOverlay.Parse(bad));
    }
}

public class PpmTests
{
    static byte[] Ppm(int w, int h, int dataBytes) =>
        Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n").Concat(new byte[dataBytes]).ToArray();

    [Fact] public void Reads_SPLAT_header() { var i = RgbImage.ReadPpm(Ppm(4, 3, 36)); Assert.Equal((4, 3), (i.Width, i.Height)); }
    [Fact] public void Rejects_truncated() => Assert.Throws<FormatException>(() => RgbImage.ReadPpm(Ppm(4, 3, 35)));
    [Fact] public void Rejects_other_formats() => Assert.Throws<FormatException>(() => RgbImage.ReadPpm(Encoding.ASCII.GetBytes("P3\n1 1\n255\n0 0 0")));
}

public class MercatorWarpTests
{
    const double N = 39.99917, S = 38.0;   // the K4HTA map's box
    const int H = 2400;

    // Where Leaflet draws latitude `lat` on an overlay stretched linearly between N and S in Mercator.
    static int LeafletRow(double lat) =>
        (int)Math.Floor((MercatorWarp.MercatorY(N) - MercatorWarp.MercatorY(lat)) / (MercatorWarp.MercatorY(N) - MercatorWarp.MercatorY(S)) * H);
    // The row of SPLAT!'s image that holds latitude `lat`.
    static int SplatRow(double lat) => (int)Math.Floor((N - lat) / (N - S) * H);

    [Theory]
    [InlineData(38.924216)]   // K4HTA
    [InlineData(38.0005)] [InlineData(38.5)] [InlineData(39.0)] [InlineData(39.5)] [InlineData(39.999)]
    public void Warped_image_puts_each_latitude_where_the_web_map_draws_it(double lat)
    {
        int shown = MercatorWarp.SourceRow(LeafletRow(lat), H, H, N, S);
        Assert.InRange(shown - SplatRow(lat), -1, 1);       // within one pixel (about 150 ft)
    }

    [Fact]
    public void Without_the_warp_the_same_check_fails()  // proves the check above can fire
    {
        double lat = 38.924216;
        Assert.True(Math.Abs(LeafletRow(lat) - SplatRow(lat)) > 5, "an unwarped overlay should be several pixels out");
    }

    [Fact]
    public void Southern_hemisphere_and_equator_work()
    {
        for (double lat = -1.9; lat < 0.95; lat += 0.37)
        {
            double n = 1.0, s = -2.0; int h = 3600;
            int leaflet = (int)Math.Floor((MercatorWarp.MercatorY(n) - MercatorWarp.MercatorY(lat)) / (MercatorWarp.MercatorY(n) - MercatorWarp.MercatorY(s)) * h);
            int splat = (int)Math.Floor((n - lat) / (n - s) * h);
            Assert.InRange(MercatorWarp.SourceRow(leaflet, h, h, n, s) - splat, -1, 1);
        }
    }

    [Fact]
    public void Colours_are_copied_exactly_and_rows_stay_in_order()
    {
        int w = 3, h = 500; var px = new byte[w * h * 3];
        for (int r = 0; r < h; r++) for (int c = 0; c < w * 3; c++) px[r * w * 3 + c] = (byte)(r % 251);
        var src = new RgbImage(w, h, px);
        var dst = MercatorWarp.ToMercator(src, N, S);
        int last = -1;
        for (int j = 0; j < h; j++)
        {
            int r = MercatorWarp.SourceRow(j, h, h, N, S);
            Assert.True(r >= last); last = r;
            Assert.Equal(src.Pixels.AsSpan(r * w * 3, w * 3).ToArray(), dst.Pixels.AsSpan(j * w * 3, w * 3).ToArray());
        }
    }
}

public class KmzTests
{
    static string Golden(string rel) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", rel));

    [Fact]
    public void Web_safe_kml_drops_the_Pro_only_icon_and_points_at_the_png()
    {
        var doc = Kmz.WebSafeKml(Golden("M01/los.kml"), "los.png");
        Assert.DoesNotContain("root://", doc);
        Assert.Contains("<href>los.png</href>", doc);
        Assert.Contains("<name>K4HTA</name>", doc);
    }

    [Fact]
    public void A_Pro_only_reference_outside_a_Style_is_refused()   // the KMZ check can fire
    {
        var bad = Golden("M01/los.kml").Replace("<name>K4HTA</name>", "<name>K4HTA</name><styleUrl>root://styles/x</styleUrl>");
        Assert.Throws<InvalidDataException>(() => Kmz.WebSafeKml(bad, "los.png"));
    }

    [Fact]
    public void Writes_a_complete_kmz_and_leaves_no_part_file()
    {
        var dir = Directory.CreateTempSubdirectory("splatwin-kmz-").FullName;
        try
        {
            var png = Path.Combine(dir, "los.png"); File.WriteAllBytes(png, new byte[] { 137, 80, 78, 71, 1, 2, 3 });
            var kmz = Path.Combine(dir, "los.kmz");
            Kmz.Write(kmz, Golden("M01/los.kml"), png);
            using var z = System.IO.Compression.ZipFile.OpenRead(kmz);
            Assert.Equal(new[] { "doc.kml", "los.png" }, z.Entries.Select(e => e.FullName).OrderBy(x => x));
            Assert.False(File.Exists(kmz + ".part"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
