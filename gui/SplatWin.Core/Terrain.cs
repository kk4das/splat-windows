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

    /// <summary>Why the job can't run on this terrain folder, or null if every tile is there.</summary>
    public static string? Problem(string? folder, IReadOnlyList<string> tiles, bool hd)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Choose the folder that holds your terrain (.sdf) files.";
        if (!Directory.Exists(folder)) return $"The terrain folder {folder} doesn't exist.";
        int max = hd ? HdMaxTiles : StandardMaxTiles;
        if (tiles.Count > max)
            return $"This job needs {tiles.Count} terrain tiles, but {(hd ? "high-definition" : "standard")} SPLAT! can hold only {max} at once. Pick stations closer together{(hd ? ", or use standard terrain" : "")}.";
        var missing = tiles.Where(t => !File.Exists(Path.Combine(folder, t))).ToList();
        if (missing.Count == 0) return null;
        return $"The terrain folder is missing {missing.Count} of the {tiles.Count} {(hd ? "high-definition " : "")}tile(s) this job needs: " +
               string.Join(", ", missing) + ". SPLAT! would treat those areas as sea level, so the job wasn't run. " +
               "Get them with scripts\\get_terrain.ps1" + (hd ? " -Hd" : "") + " (fetching terrain from the app comes in Stage G4).";
    }
}
