using System.Globalization;

namespace SplatWin.Core;

public enum HeightUnit { Feet, Meters }

/// <summary>A site as a .qth file holds it, but with ordinary signs: longitude east positive.</summary>
public sealed record QthSite(string Name, double Lat, double Lon, double Height, HeightUnit Unit);

/// <summary>
/// Reads and writes SPLAT! .qth files: name, latitude, longitude (WEST positive), antenna height
/// (feet, or meters if the line contains an M). Mirrors SPLAT!'s LoadQTH/ReadBearing, but where
/// SPLAT! would silently read something as 0 or cut it short, this refuses with a reason.
/// </summary>
public static class QthFile
{
    public const int MaxLine = 48;     // LoadQTH reads each line with fgets(string, 49, fd)
    const double FeetPerMeter = 3.28084;   // SPLAT!'s own factor

    public static QthSite Parse(string text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 4 || lines.Take(4).Any(string.IsNullOrWhiteSpace))
            throw new FormatException("A .qth file needs four lines: name, latitude, longitude and antenna height.");
        for (int i = 0; i < 4; i++)
            if (lines[i].Length > MaxLine)
                throw new FormatException($"Line {i + 1} is longer than {MaxLine} characters, which SPLAT! would cut short.");
        string name = lines[0].Trim();
        double lat = Bearing(lines[1], "latitude");
        if (lat < -90 || lat > 90) throw new FormatException($"Line 2: latitude {lat} is outside -90 to 90.");
        double west = Bearing(lines[2], "longitude");
        if (west < 0) west += 360;                       // as LoadQTH does
        double lon = -west;                              // to ordinary signs (east positive)
        if (lon <= -180) lon += 360;
        var (h, unit) = Height(lines[3]);
        return new QthSite(name, lat, lon == 0 ? 0 : lon, h, unit);
    }

    /// <summary>ReadBearing: decimal ("38.924216") or degrees minutes seconds ("38 55 27.18").</summary>
    static double Bearing(string line, string what)
    {
        if (line.TrimEnd().Length > 18)   // ReadBearing copies at most 18 characters
            throw new FormatException($"The {what} \"{line.Trim()}\" is longer than the 18 characters SPLAT! reads.");
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double v;
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { }
        else if (parts.Length == 3
                 && int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int d)
                 && int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int m)
                 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
        {
            v = Math.Abs(d) + Math.Abs(m) / 60.0 + Math.Abs(s) / 3600.0;
            if (d < 0 || m < 0 || s < 0 || parts[0].StartsWith('-')) v = -v;   // "-0 30 00" is negative too
        }
        else throw new FormatException($"The {what} \"{line.Trim()}\" isn't decimal degrees or \"degrees minutes seconds\"; SPLAT! would read it as 0.");
        if (double.IsNaN(v) || v > 360 || v < -360)
            throw new FormatException($"The {what} \"{line.Trim()}\" is out of range; SPLAT! would read it as 0.");
        return v;
    }

    static (double, HeightUnit) Height(string line)
    {
        string s = line.Trim();
        int m = s.IndexOfAny(new[] { 'm', 'M' });
        var unit = m >= 0 ? HeightUnit.Meters : HeightUnit.Feet;
        string num = (m >= 0 ? s[..m] : s).Trim();
        foreach (var suffix in new[] { "feet", "ft", "'" })   // "30 ft" is read as 30 feet by SPLAT! too
            if (unit == HeightUnit.Feet && num.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { num = num[..^suffix.Length].Trim(); break; }
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || double.IsNaN(h) || double.IsInfinity(h))
            throw new FormatException($"The antenna height \"{s}\" isn't a number of feet, or of meters (\"9.14 meters\").");
        if (h <= 0) throw new FormatException($"The antenna height \"{s}\" must be above 0.");
        return (h, unit);
    }

    /// <summary>Writes the file as SPLAT! wants it: west longitude positive, 0 to 360.</summary>
    public static string Format(QthSite s)
    {
        double west = -s.Lon;
        if (west < 0) west += 360;
        if (west >= 360) west -= 360;
        double lat = s.Lat;
        if (west == 0) west = 0;   // never print "-0"
        if (lat == 0) lat = 0;
        var ci = CultureInfo.InvariantCulture;
        string h = s.Height.ToString("0.######", ci) + (s.Unit == HeightUnit.Meters ? " meters" : "");
        return string.Join("\r\n", s.Name, lat.ToString("0.000000", ci), west.ToString("0.000000", ci), h) + "\r\n";
    }

    public static double ToFeet(double height, HeightUnit unit) => unit == HeightUnit.Meters ? height * FeetPerMeter : height;
}
