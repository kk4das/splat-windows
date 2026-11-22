using System.Text;

namespace SplatWin.Core;

/// <summary>A Maidenhead square: its locator and its edges in degrees (west longitude negative).</summary>
public readonly record struct GridSquare(string Locator, double South, double West, double North, double East)
{
    public double CenterLat => (South + North) / 2;
    public double CenterLon => (West + East) / 2;

    /// <summary>Size in miles at the square's own latitude, e.g. "about 2.9 × 4.5 mi".</summary>
    public string SizeText()
    {
        double h = (North - South) * 69.05;
        double w = (East - West) * 69.17 * Math.Cos(CenterLat * Math.PI / 180);
        return h >= 10 ? $"about {h:0} × {w:0} mi"
             : h >= 1 ? $"about {h:0.0} × {w:0.0} mi"
             : $"about {h * 1609.344:0} × {w * 1609.344:0} m";
    }
}

/// <summary>
/// Maidenhead locators of 4, 6, 8 or 10 characters (e.g. FM18jw), both ways.
/// Worked in whole units of the finest square (1/5760° of latitude, 1/2880° of longitude) so
/// that points exactly on an edge land in the same square every time.
/// </summary>
public static class Maidenhead
{
    // Size of each pair's step, in finest units. Latitude and longitude happen to share them:
    // field 10°/20°, square 1°/2°, subsquare 2.5'/5', extended 15"/30", 10-char 0.625"/1.25".
    static readonly long[] Step = { 57600, 5760, 240, 24, 1 };
    static readonly int[] Base = { 18, 10, 24, 10, 24 };
    const long Total = 1036800;   // 180° × 5760 = 360° × 2880
    const double LatUnits = 5760, LonUnits = 2880;

    public static string FromLatLon(double lat, double lon, int chars = 6)
    {
        if (chars is not (4 or 6 or 8 or 10)) throw new ArgumentOutOfRangeException(nameof(chars), "a locator has 4, 6, 8 or 10 characters");
        if (!(lat >= -90 && lat <= 90)) throw new ArgumentOutOfRangeException(nameof(lat), "latitude must be between -90 and 90");
        if (!(lon >= -180 && lon <= 180)) throw new ArgumentOutOfRangeException(nameof(lon), "longitude must be between -180 and 180");
        long x = Units(lon + 180, LonUnits), y = Units(lat + 90, LatUnits);
        var sb = new StringBuilder(chars);
        for (int i = 0; i < chars / 2; i++)
        {
            int a = (int)(x / Step[i]), b = (int)(y / Step[i]);
            x -= a * Step[i];
            y -= b * Step[i];
            sb.Append(Symbol(i, a)).Append(Symbol(i, b));
        }
        return sb.ToString();
    }

    static long Units(double deg, double perDeg)
    {
        long u = (long)Math.Floor(deg * perDeg + 1e-7);   // + tiny: 38.916666... is the edge, not just below it
        return Math.Clamp(u, 0, Total - 1);               // the north pole and 180° belong to the last square
    }

    static char Symbol(int pair, int v) => pair switch
    {
        0 => (char)('A' + v),
        1 or 3 => (char)('0' + v),
        _ => (char)('a' + v),
    };

    /// <summary>Parses a locator (any letter case). Throws FormatException with a plain-English reason.</summary>
    public static GridSquare Parse(string text)
    {
        string s = (text ?? "").Trim();
        if (s.Length is not (4 or 6 or 8 or 10))
            throw new FormatException("A grid square has 4, 6, 8 or 10 characters, such as FM18 or FM18jw.");
        long x = 0, y = 0;
        for (int i = 0; i < s.Length / 2; i++)
        {
            int a = Value(i, s[2 * i]), b = Value(i, s[2 * i + 1]);
            if (a < 0 || b < 0)
                throw new FormatException(i switch
                {
                    0 => "The first two characters must be letters from A to R, as in FM18.",
                    1 => "Characters 3 and 4 must be digits, as in FM18.",
                    2 => "Characters 5 and 6 must be letters from A to X, as in FM18jw.",
                    3 => "Characters 7 and 8 must be digits, as in FM18jw40.",
                    _ => "Characters 9 and 10 must be letters from A to X, as in FM18jw40xx.",
                });
            x += a * Step[i];
            y += b * Step[i];
        }
        long size = Step[s.Length / 2 - 1];
        string loc = Canonical(s);
        return new GridSquare(loc,
            South: y / LatUnits - 90, West: x / LonUnits - 180,
            North: (y + size) / LatUnits - 90, East: (x + size) / LonUnits - 180);
    }

    static int Value(int pair, char c)
    {
        c = char.ToUpperInvariant(c);
        return pair switch
        {
            0 => c >= 'A' && c <= 'R' ? c - 'A' : -1,
            1 or 3 => c >= '0' && c <= '9' ? c - '0' : -1,
            _ => c >= 'A' && c <= 'X' ? c - 'A' : -1,
        };
    }

    /// <summary>The usual way of writing a locator: FM18jw40xx.</summary>
    static string Canonical(string s)
    {
        var c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++) c[i] = i < 2 ? char.ToUpperInvariant(c[i]) : char.ToLowerInvariant(c[i]);
        return new string(c);
    }
}
