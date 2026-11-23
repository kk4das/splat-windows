using System.Globalization;

namespace SplatWin.Core;

/// <summary>
/// Which SPLAT! terrain (.sdf) files a job needs. Mirrors SPLAT!'s own main() and LoadTopoData:
/// the box of whole degrees around the sites, by floor of latitude and of WEST longitude (0-359).
/// A missing tile is not an error to SPLAT!: it quietly treats the area as sea level, which gives
/// wrong answers. So the app checks first and refuses.
/// </summary>
public static class Terrain
{
    public const int StandardMaxTiles = 64;   // build.ps1 -StdPages (owner D5: 8x8 degrees)
    public const int HdMaxTiles = 16;         // build.ps1 -HdPages (owner D5: 4x4 degrees)

    /// <summary>SPLAT!'s file name for the 1x1 degree tile whose south-east corner is (lat, westLon), Windows spelling ('_' not ':').</summary>
    public static string TileName(int lat, int westLon, bool hd)
    {
        int ymin = ((westLon % 360) + 360) % 360, ymax = (ymin + 1) % 360;
        return string.Format(CultureInfo.InvariantCulture, "{0}_{1}_{2}_{3}{4}.sdf", lat, lat + 1, ymin, ymax, hd ? "-hd" : "");
    }

    static int West(double lon)   // ordinary signs (east positive) to SPLAT!'s 0-360 west
    {
        double w = -lon;
        if (w < 0) w += 360;
        if (w >= 360) w -= 360;
        return (int)Math.Floor(w);
    }

    // SPLAT!'s LonDiff: the difference reduced to (-180, 180].
    static double LonDiff(double a, double b)
    {
        double d = a - b;
        if (d <= -180) d += 360;
        if (d >= 180) d -= 360;
        return d;
    }

    /// <summary>The tiles SPLAT! loads for these sites (ordinary signs), in its loading order.</summary>
    public static List<string> TilesFor(IEnumerable<(double Lat, double Lon)> sites, bool hd)
    {
        var list = sites.ToList();
        if (list.Count == 0) return new();
        int minLat = int.MaxValue, maxLat = int.MinValue;
        int minLon = West(list[0].Lon), maxLon = minLon;
        foreach (var (lat, lon) in list)
        {
            int la = (int)Math.Floor(lat), lo = West(lon);
            minLat = Math.Min(minLat, la); maxLat = Math.Max(maxLat, la);
            if (LonDiff(lo, minLon) < 0) minLon = lo;
            if (LonDiff(lo, maxLon) >= 0) maxLon = lo;
        }
        int width = ((maxLon - minLon) % 360 + 360) % 360;
        var tiles = new List<string>();
        for (int y = 0; y <= width; y++)
            for (int la = minLat; la <= maxLat; la++)
                tiles.Add(TileName(la, minLon + y, hd));
        return tiles;
    }

    /// <summary>
    /// The marker written when the terrain source has no tile for an area: it is open water, and
    /// SPLAT!'s sea level is then the right answer. A tile with a marker counts as present.
    /// </summary>
    public static string SeaMarker(string tile) => tile + ".sea";

    public static bool Present(string folder, string tile) =>
        File.Exists(Path.Combine(folder, tile)) || File.Exists(Path.Combine(folder, SeaMarker(tile)));

    /// <summary>Tiles not in the folder (and not known to be open water).</summary>
    public static List<string> Missing(string folder, IEnumerable<string> tiles) =>
        tiles.Where(t => !Present(folder, t)).ToList();

    /// <summary>Why the job can't run on this terrain folder, or null if every tile is there.</summary>
    public static string? Problem(string? folder, IReadOnlyList<string> tiles, bool hd)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose the folder that holds your terrain (.sdf) files.";
        if (!Directory.Exists(folder)) return $"The terrain folder {folder} doesn't exist.";
        int max = hd ? HdMaxTiles : StandardMaxTiles;
        if (tiles.Count > max)
            return $"This job needs {tiles.Count} terrain tiles, but {(hd ? "high-definition" : "standard")} SPLAT! can hold only {max} at once. Pick stations closer together{(hd ? ", or use standard terrain" : "")}.";
        var missing = Missing(folder, tiles);
        if (missing.Count == 0) return null;
        return $"The terrain folder is missing {missing.Count} of the {tiles.Count} {(hd ? "high-definition " : "")}tile(s) this job needs: " +
               string.Join(", ", missing) + ". SPLAT! would treat those areas as sea level, so the job wasn't run.";
    }

    // SPLAT!'s "deg_limit": how far (degrees) it will load terrain around a coverage transmitter,
    // by the MAXPAGES it was built with (main(), area mode). build.ps1: 64 standard, 16 HD.
    static double DegLimit(bool hd) => hd ? 1.5 : 3.5;

    /// <summary>The largest coverage radius (miles) SPLAT! can map in full at this latitude; beyond it SPLAT! cuts the map short.</summary>
    public static double MaxRadiusMiles(double lat, bool hd) =>
        Math.Floor(DegLimit(hd) * 57.0 * Math.Cos(Math.Min(Math.Abs(lat), 70.0) * Math.PI / 180));

    /// <summary>
    /// The tiles SPLAT! loads for a coverage map of radius <paramref name="radiusMiles"/> around a
    /// transmitter (ordinary signs): the site's own tile, then SPLAT!'s box of max_range/57 degrees.
    /// </summary>
    public static List<string> TilesForCoverage(double lat, double lon, double radiusMiles, bool hd)
    {
        double west = -lon; if (west < 0) west += 360; if (west >= 360) west -= 360;
        double dr = radiusMiles / 57.0;
        double drLon = Math.Abs(lat) < 70 ? dr / Math.Cos(lat * Math.PI / 180) : dr / Math.Cos(70 * Math.PI / 180);
        dr = Math.Min(dr, DegLimit(hd)); drLon = Math.Min(drLon, DegLimit(hd));
        int Norm(int w) { while (w < 0) w += 360; while (w >= 360) w -= 360; return w; }
        int minLat = (int)Math.Floor(lat), maxLat = minLat, minLon = (int)Math.Floor(west), maxLon = minLon;
        int nMin = (int)Math.Floor(lat - dr), nMax = (int)Math.Floor(lat + dr);
        int wMin = Norm((int)Math.Floor(west - drLon)), wMax = Norm((int)Math.Floor(west + drLon));
        minLat = Math.Min(minLat, nMin); maxLat = Math.Max(maxLat, nMax);
        if (LonDiff(wMin, minLon) < 0) minLon = wMin;
        if (LonDiff(wMax, maxLon) >= 0) maxLon = wMax;
        int width = ((maxLon - minLon) % 360 + 360) % 360;
        // SPLAT! first loads the transmitter's own tile (the site box), then the rest of the area.
        var tiles = new List<string> { TileName((int)Math.Floor(lat), (int)Math.Floor(west), hd) };
        for (int y = 0; y <= width; y++)
            for (int la = minLat; la <= maxLat; la++)
            {
                string t = TileName(la, minLon + y, hd);
                if (!tiles.Contains(t)) tiles.Add(t);
            }
        return tiles;
    }

    /// <summary>The SRTM tile name (e.g. N38W078) for an SDF tile name (e.g. 38_39_77_78.sdf).</summary>
    public static (string Hgt, int Lat, int Lon) SourceTile(string sdf)
    {
        var p = Path.GetFileName(sdf).Replace("-hd.sdf", "").Replace(".sdf", "").Split('_');
        int lat = int.Parse(p[0], CultureInfo.InvariantCulture), minW = int.Parse(p[2], CultureInfo.InvariantCulture);
        int lon = -(minW + 1);
        if (lon < -180) lon += 360;
        string name = string.Format(CultureInfo.InvariantCulture, "{0}{1:00}{2}{3:000}", lat >= 0 ? 'N' : 'S', Math.Abs(lat), lon >= 0 ? 'E' : 'W', Math.Abs(lon));
        return (name, lat, lon);
    }
}
