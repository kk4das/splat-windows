using System.Globalization;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>A site as SPLAT! holds it: latitude, WEST longitude 0-360, antenna height in feet as a C float.</summary>
public readonly record struct SplatSite(double Lat, double Lon, float Alt);

/// <summary>A path as SPLAT!'s ReadPath makes it: points from source to destination, elevation in feet, distance in miles.</summary>
public sealed class SplatPath
{
    public required double[] Lat { get; init; }
    public required double[] Lon { get; init; }      // west, 0-360
    public required double[] Elevation { get; init; }
    public required double[] Distance { get; init; }
    public int Length { get; init; }
}

/// <summary>
/// SPLAT!'s geometry (splat.cpp 1.4.2), copied function for function and operation for operation, so
/// the 3D view and the Google Earth file use exactly SPLAT!'s numbers. Longitudes are degrees WEST
/// (0-360), heights feet, distances statute miles, as in SPLAT!. Stage G8.
/// </summary>
public static class SplatGeo
{
    public const double PI = 3.141592653589793, TWOPI = 6.283185307179586, HALFPI = 1.570796326794896;
    public const double DEG2RAD = 1.74532925199e-02;
    public const double EarthRadius = 20902230.97;   // EARTHRADIUS, feet (SPLAT!'s 4/3 Earth); the app never passes -m
    public const double MetersPerFoot = 0.3048, KmPerMile = 1.609344;

    static double Rint(double v) => Math.Round(v, MidpointRounding.ToEven);   // C rint(), default rounding mode

    public static double LonDiff(double lon1, double lon2)
    {
        double diff = lon1 - lon2;
        if (diff <= -180.0) diff += 360.0;
        if (diff >= 180.0) diff -= 360.0;
        return diff;
    }

    static double Arccos(double x, double y)
    {
        double result = 0.0;
        if (y > 0.0) result = Math.Acos(x / y);
        if (y < 0.0) result = PI + Math.Acos(x / y);
        return result;
    }

    public static double Distance(double lat1d, double lon1d, double lat2d, double lon2d)
    {
        double lat1 = lat1d * DEG2RAD, lon1 = lon1d * DEG2RAD, lat2 = lat2d * DEG2RAD, lon2 = lon2d * DEG2RAD;
        return 3959.0 * Math.Acos(Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos((lon1) - (lon2)));
    }

    public static double Distance(SplatSite a, SplatSite b) => Distance(a.Lat, a.Lon, b.Lat, b.Lon);

    public static double Azimuth(double srcLatD, double srcLonD, double dstLatD, double dstLonD)
    {
        double dest_lat = dstLatD * DEG2RAD, dest_lon = dstLonD * DEG2RAD, src_lat = srcLatD * DEG2RAD, src_lon = srcLonD * DEG2RAD;
        double beta = Math.Acos(Math.Sin(src_lat) * Math.Sin(dest_lat) + Math.Cos(src_lat) * Math.Cos(dest_lat) * Math.Cos(src_lon - dest_lon));
        double num = Math.Sin(dest_lat) - (Math.Sin(src_lat) * Math.Cos(beta));
        double den = Math.Cos(src_lat) * Math.Sin(beta);
        double fraction = num / den;
        if (fraction >= 1.0) fraction = 1.0;
        if (fraction <= -1.0) fraction = -1.0;
        double azimuth = Math.Acos(fraction);
        double diff = dest_lon - src_lon;
        if (diff <= -PI) diff += TWOPI;
        if (diff >= PI) diff -= TWOPI;
        if (diff > 0.0) azimuth = TWOPI - azimuth;
        return azimuth / DEG2RAD;
    }

    public static double Azimuth(SplatSite s, SplatSite d) => Azimuth(s.Lat, s.Lon, d.Lat, d.Lon);

    /// <summary>ElevationAngle: degrees of elevation of the destination seen from the source (earth radius EARTHRADIUS).</summary>
    public static double ElevationAngle(SplatSite source, SplatSite destination, Func<double, double, double> elevation)
    {
        double a = elevation(destination.Lat, destination.Lon) + destination.Alt + EarthRadius;
        double b = elevation(source.Lat, source.Lon) + source.Alt + EarthRadius;
        double dx = 5280.0 * Distance(source, destination);
        return (180.0 * (Math.Acos(((b * b) + (dx * dx) - (a * a)) / (2.0 * b * dx))) / PI) - 90.0;
    }

    /// <summary>SPLAT!'s ARRAYSIZE for this build (build.ps1: standard 64 pages, HD 16 pages).</summary>
    public static int ArraySize(bool hd) => hd ? 57713 : 76810;

    /// <summary>ReadPath: points along the great circle from source to destination, one per terrain sample.</summary>
    public static SplatPath ReadPath(SplatSite source, SplatSite destination, bool hd, Func<double, double, double> elevation)
    {
        int arraysize = ArraySize(hd);
        double ppd = hd ? 3600.0 : 1200.0;
        var pLat = new double[arraysize + 1]; var pLon = new double[arraysize + 1];
        var pEl = new double[arraysize + 1]; var pDist = new double[arraysize + 1];
        int c;
        double azimuth, distance, lat1, lon1, beta, den, num, lat2, lon2, total_distance, dx, dy, path_length,
               miles_per_sample = 0.0, samples_per_radian = 68755.0;

        lat1 = source.Lat * DEG2RAD;
        lon1 = source.Lon * DEG2RAD;
        lat2 = destination.Lat * DEG2RAD;
        lon2 = destination.Lon * DEG2RAD;
        if (ppd == 1200.0) samples_per_radian = 68755.0;
        if (ppd == 3600.0) samples_per_radian = 206265.0;
        azimuth = Azimuth(source, destination) * DEG2RAD;
        total_distance = Distance(source, destination);

        if (total_distance > (30.0 / ppd))
        {
            dx = samples_per_radian * Math.Acos(Math.Cos(lon1 - lon2));
            dy = samples_per_radian * Math.Acos(Math.Cos(lat1 - lat2));
            path_length = Math.Sqrt((dx * dx) + (dy * dy));
            miles_per_sample = total_distance / path_length;
        }
        else
        {
            c = 0;
            total_distance = 0.0;
            lat1 = lat1 / DEG2RAD;
            lon1 = lon1 / DEG2RAD;
            pLat[c] = lat1; pLon[c] = lon1; pEl[c] = elevation(source.Lat, source.Lon); pDist[c] = 0.0;
        }

        for (distance = 0.0, c = 0; (total_distance != 0.0 && distance <= total_distance && c < arraysize); c++, distance = miles_per_sample * (double)c)
        {
            beta = distance / 3959.0;
            lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(beta) + Math.Cos(azimuth) * Math.Sin(beta) * Math.Cos(lat1));
            num = Math.Cos(beta) - (Math.Sin(lat1) * Math.Sin(lat2));
            den = Math.Cos(lat1) * Math.Cos(lat2);

            if (azimuth == 0.0 && (beta > HALFPI - lat1)) lon2 = lon1 + PI;
            else if (azimuth == HALFPI && (beta > HALFPI + lat1)) lon2 = lon1 + PI;
            else if (Math.Abs(num / den) > 1.0) lon2 = lon1;
            else
            {
                if ((PI - azimuth) >= 0.0) lon2 = lon1 - Arccos(num, den);
                else lon2 = lon1 + Arccos(num, den);
            }
            while (lon2 < 0.0) lon2 += TWOPI;
            while (lon2 > TWOPI) lon2 -= TWOPI;
            lat2 = lat2 / DEG2RAD;
            lon2 = lon2 / DEG2RAD;
            pLat[c] = lat2; pLon[c] = lon2; pEl[c] = elevation(lat2, lon2); pDist[c] = distance;
        }

        if (c < arraysize)
        {
            pLat[c] = destination.Lat; pLon[c] = destination.Lon;
            pEl[c] = elevation(destination.Lat, destination.Lon); pDist[c] = total_distance;
            c++;
        }
        int length = c < arraysize ? c : arraysize - 1;
        return new SplatPath { Lat = pLat, Lon = pLon, Elevation = pEl, Distance = pDist, Length = length };
    }

    /// <summary>The point at a distance (miles) and bearing (degrees true) from a point, on SPLAT!'s 3959-mile sphere. West longitudes.</summary>
    public static (double Lat, double Lon) Destination(double latD, double westD, double bearingD, double miles)
    {
        double lat1 = latD * DEG2RAD, lonE = -westD * DEG2RAD, th = bearingD * DEG2RAD, d = miles / 3959.0;
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(th));
        double lon2E = lonE + Math.Atan2(Math.Sin(th) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        double west = -lon2E / DEG2RAD;
        while (west < 0) west += 360;
        while (west >= 360) west -= 360;
        return (lat2 / DEG2RAD, west);
    }

    /// <summary>West longitude (0-360) to ordinary east-positive (-180..180].</summary>
    public static double East(double west) { double e = west > 180 ? 360 - west : -west; return e == 0 ? 0 : e; }

    // ---- SPLAT!'s text readers ----

    static readonly Regex Number = new(@"^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", RegexOptions.CultureInvariant);

    /// <summary>C atof / sscanf("%lf"): the leading number, or 0 (and false) when there isn't one.</summary>
    public static bool LeadingNumber(string s, out double v)
    {
        var m = Number.Match(s);
        v = m.Success ? double.Parse(m.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) : 0.0;
        return m.Success;
    }

    /// <summary>ReadBearing: decimal ("38.924216") or degrees minutes seconds ("38 55 27"); anything else 0.
    /// Pass the line as fgets() gives it, with its newline: SPLAT!'s space handling depends on it.</summary>
    public static double ReadBearing(string input)
    {
        var s = new System.Text.StringBuilder();
        int length = input.Length;
        for (int a = 0; a < length && a < 18; a++)
        {
            char ca = input[a], next = a + 1 < length ? input[a + 1] : '\0';
            if ((ca != ' ' && ca != '\n') || (ca == ' ' && next != ' ' && next != '\n' && s.Length != 0))
                s.Append(ca);
        }
        string str = s.ToString();
        int spaces = str.Count(ch => ch == ' ');
        double bearing = 0.0;
        if (spaces == 0) LeadingNumber(str, out bearing);
        if (spaces == 2)
        {
            var p = str.Split(' ');
            if (int.TryParse(p[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int degrees)
                && int.TryParse(p[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int minutes))
            {
                LeadingNumber(p[2], out double seconds);
                bearing = Math.Abs((double)degrees);
                bearing += Math.Abs(((double)minutes) / 60.0);
                bearing += Math.Abs(seconds / 3600.0);
                if ((degrees < 0) || (minutes < 0) || (seconds < 0.0)) bearing = -bearing;
            }
        }
        if (bearing > 360.0 || bearing < -360.0) bearing = 0.0;
        return bearing;
    }

    /// <summary>LoadQTH: name, latitude, WEST longitude (0-360) and antenna height in feet, as SPLAT! reads them.</summary>
    public static (string Name, SplatSite Site) LoadQth(string path)
    {
        var lines = File.ReadAllText(path, System.Text.Encoding.Latin1).Replace("\r\n", "\n").Split('\n');
        string L(int i) => i < lines.Length ? (lines[i].Length > 47 ? lines[i][..48] : lines[i] + "\n") : "";   // fgets(string, 49)
        string name = L(0).TrimEnd('\n', '\r');
        double lat = ReadBearing(L(1));
        double lon = ReadBearing(L(2));
        if (lon < 0.0) lon += 360.0;
        string h = L(3);
        int m = h.IndexOfAny(new[] { 'M', 'm' });
        string num = m >= 0 ? h[..m] : h;
        LeadingNumber(num, out double hv);
        float alt = (float)hv;                       // sscanf("%f") into a float
        if (m >= 0) alt = (float)(alt * 3.28084);    // tempsite.alt*=3.28084
        return (name, new SplatSite(lat, lon, alt));
    }
}
