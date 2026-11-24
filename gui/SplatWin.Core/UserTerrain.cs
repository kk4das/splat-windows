using System.Globalization;
using System.Text;

namespace SplatWin.Core;

/// <summary>
/// A SPLAT! user-defined terrain file (.udt, the -udt option): buildings, towers or trees added on
/// top of the ground. Each line is "latitude, longitude, height", with the longitude in degrees
/// WEST as everywhere in SPLAT!, the height in feet, or meters when it ends in 'm' (LoadUDT). The
/// file is passed to SPLAT! unchanged; this only checks it, so a mistake doesn't go unnoticed.
/// </summary>
public sealed record UdtPoint(double Lat, double Lon, double HeightMetres, int Line);

public static class UserTerrain
{
    public const string JobFileName = "obstacles.udt";

    /// <summary>The obstacles in the file (ordinary signs: east-positive longitude), or a reason it can't be used.</summary>
    public static (List<UdtPoint> Points, string? Problem) Parse(string text)
    {
        var points = new List<UdtPoint>();
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            // SPLAT! reads 77 characters a line; anything longer is cut and misread.
            if (raw.TrimEnd().Length > 76) return (points, $"Line {i + 1} is too long for SPLAT! (at most 76 characters).");
            string line = raw.Split(';')[0].Trim();
            if (line.Length == 0) continue;
            var f = line.Split(',');
            if (f.Length != 3) return (points, $"Line {i + 1} should be \"latitude, longitude, height\": \"{raw.Trim()}\".");
            if (Bearing(f[0]) is not double lat || lat < -90 || lat > 90) return (points, $"Line {i + 1}: \"{f[0].Trim()}\" isn't a latitude.");
            if (Bearing(f[1]) is not double west || west < -360 || west > 360) return (points, $"Line {i + 1}: \"{f[1].Trim()}\" isn't a longitude.");
            string h = f[2].Trim();
            bool meters = h.EndsWith("m", StringComparison.OrdinalIgnoreCase) || h.EndsWith("meters", StringComparison.OrdinalIgnoreCase)
                          || h.EndsWith("meters", StringComparison.OrdinalIgnoreCase);
            string num = new string(h.TakeWhile(c => char.IsDigit(c) || c is '.' or '-' or '+').ToArray());
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
                return (points, $"Line {i + 1}: \"{h}\" isn't a height.");
            if (height <= 0) return (points, $"Line {i + 1}: the height must be above zero (SPLAT! ignores it otherwise).");
            if (west < 0) west += 360;                       // as SPLAT! does
            double lon = west > 180 ? 360 - west : -west;     // to ordinary signs
            points.Add(new UdtPoint(lat, lon, meters ? height : height * 0.3048, i + 1));
        }
        return points.Count == 0 ? (points, "The file has no obstacles in it.") : (points, null);
    }

    /// <summary>SPLAT!'s ReadBearing: decimal degrees, or "degrees minutes seconds".</summary>
    static double? Bearing(string s)
    {
        var p = s.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var v = new double[p.Length];
        for (int i = 0; i < p.Length; i++)
            if (!double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return p.Length switch
        {
            1 => v[0],
            3 => Math.Sign(v[0] == 0 ? 1 : v[0]) * (Math.Abs(v[0]) + v[1] / 60 + v[2] / 3600),
            _ => null,
        };
    }

    /// <summary>
    /// Why this file can't be used for a job whose terrain is <paramref name="tiles"/>, or null.
    /// None of the obstacles in the job's area almost always means the longitudes were written
    /// east-negative (-77.2) instead of SPLAT!'s degrees west (77.2), so it is refused, not ignored.
    /// </summary>
    public static string? Problem(string path, IReadOnlyList<string> tiles, bool hd)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path)) return $"The obstacles file {path} doesn't exist.";
        var (points, why) = Parse(File.ReadAllText(path, Encoding.Latin1));
        if (why != null) return $"{name}: {why}";
        var inArea = points.Count(p => tiles.Contains(Terrain.TilesFor(new[] { (p.Lat, p.Lon) }, hd)[0]));
        if (inArea == 0)
            return $"{name}: none of its {points.Count} obstacle(s) is in this job's area. SPLAT! reads longitudes as degrees WEST " +
                   "(77.2 for 77.2° W, not -77.2), so check the signs.";
        return null;
    }
}
