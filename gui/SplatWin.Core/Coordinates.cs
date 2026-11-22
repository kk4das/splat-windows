using System.Globalization;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>
/// Reads a typed or pasted position in ordinary signs (north and east positive, so US longitudes
/// are negative). Accepts what people usually paste:
///   38.924216, -77.205793           (Google Maps)
///   38.924216 -77.205793
///   38°55'27.2"N 77°12'20.9"W       (deg-min-sec, with N/S/E/W)
///   38 55 27.2 N, 77 12 20.9 W      N38 55.45 W77 12.35
/// Anything ambiguous is rejected with a reason rather than guessed.
/// </summary>
public static partial class Coordinates
{
    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?|[NSEW]|,")]
    private static partial Regex TokenRx();

    sealed class Group
    {
        public readonly List<string> Numbers = new();
        public char? Hemisphere;
    }

    public static (double Lat, double Lon) Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Enter a latitude and a longitude, such as 38.9242, -77.2058.");
        string s = Normalize(text);
        var tokens = TokenRx().Matches(s).Select(m => m.Value).ToList();
        string leftover = TokenRx().Replace(s, " ").Trim();
        if (leftover.Length > 0)
            throw new FormatException($"\"{leftover.Split(' ')[0]}\" isn't part of a position. Use numbers, and N/S/E/W if you like.");

        var (a, b) = SplitInTwo(tokens);
        // "77.2 W 38.9 N": longitude first, but the letters make it clear.
        if (a.Hemisphere is 'E' or 'W' && b.Hemisphere is 'N' or 'S') (a, b) = (b, a);
        double lat = Value(a, latitude: true), lon = Value(b, latitude: false);
        if (lat < -90 || lat > 90) throw new FormatException($"Latitude {lat:0.######} is outside -90 to 90.");
        if (lon < -180 || lon > 180) throw new FormatException($"Longitude {lon:0.######} is outside -180 to 180.");
        return (lat, lon);
    }

    static string Normalize(string text)
    {
        var c = text.ToUpperInvariant().ToCharArray();
        for (int i = 0; i < c.Length; i++)
            c[i] = c[i] switch
            {
                '°' or 'º' or '˚' or '\'' or '′' or '’' or '‘' or '"' or '″' or '“' or '”' or ';' or '\t' or '\r' or '\n' => ' ',
                '−' or '–' => '-',   // typographic minus signs from web pages
                _ => c[i],
            };
        return new string(c);
    }

    static (Group, Group) SplitInTwo(List<string> tokens)
    {
        var groups = new List<Group>();
        int commas = tokens.Count(t => t == ",");
        if (commas > 1) throw new FormatException("Use one comma, between the latitude and the longitude (and a dot for decimals).");
        bool letters = tokens.Any(IsLetter);
        bool prefix = tokens.Count > 0 && IsLetter(tokens[0]);
        var cur = new Group();
        foreach (var t in tokens)
        {
            if (t == ",") { groups.Add(cur); cur = new Group(); continue; }
            if (IsLetter(t))
            {
                if (prefix && commas == 0 && (cur.Numbers.Count > 0 || cur.Hemisphere != null)) { groups.Add(cur); cur = new Group(); }
                if (cur.Hemisphere != null) throw new FormatException("Each of latitude and longitude takes one of N, S, E or W.");
                cur.Hemisphere = t[0];
                if (!prefix && commas == 0) { groups.Add(cur); cur = new Group(); }
                continue;
            }
            cur.Numbers.Add(t);
        }
        if (cur.Numbers.Count > 0 || cur.Hemisphere != null) groups.Add(cur);

        if (!letters && commas == 0 && groups.Count == 1)
        {
            // Numbers only, no comma: split in half (2, 4 or 6 numbers).
            var n = groups[0].Numbers;
            if (n.Count is not (2 or 4 or 6))
                throw new FormatException("Separate the latitude and the longitude with a comma, such as 38 55 27, -77 12 21.");
            var g1 = new Group(); var g2 = new Group();
            g1.Numbers.AddRange(n.Take(n.Count / 2)); g2.Numbers.AddRange(n.Skip(n.Count / 2));
            return (g1, g2);
        }
        if (groups.Count != 2 || groups.Any(g => g.Numbers.Count == 0))
            throw new FormatException("Enter both a latitude and a longitude, such as 38.9242, -77.2058.");
        return (groups[0], groups[1]);
    }

    static bool IsLetter(string t) => t.Length == 1 && char.IsLetter(t[0]);

    static double Value(Group g, bool latitude)
    {
        string what = latitude ? "latitude" : "longitude";
        if (g.Hemisphere is char h && (latitude ? h is 'E' or 'W' : h is 'N' or 'S'))
            throw new FormatException($"The {what} can't be {h}: use N/S for latitude and E/W for longitude.");
        var n = g.Numbers;
        if (n.Count > 3) throw new FormatException($"The {what} has too many numbers; use degrees, minutes and seconds at most.");
        bool negative = n[0].StartsWith('-');
        if (n.Skip(1).Any(x => x.StartsWith('-') || x.StartsWith('+')))
            throw new FormatException($"Only the degrees of the {what} can have a sign.");
        double deg = Math.Abs(double.Parse(n[0], CultureInfo.InvariantCulture));
        double min = n.Count > 1 ? double.Parse(n[1], CultureInfo.InvariantCulture) : 0;
        double sec = n.Count > 2 ? double.Parse(n[2], CultureInfo.InvariantCulture) : 0;
        if (n.Count > 1 && deg != Math.Floor(deg)) throw new FormatException($"With minutes given, the {what}'s degrees must be a whole number.");
        if (n.Count > 2 && min != Math.Floor(min)) throw new FormatException($"With seconds given, the {what}'s minutes must be a whole number.");
        if (min >= 60) throw new FormatException($"The {what}'s minutes must be less than 60.");
        if (sec >= 60) throw new FormatException($"The {what}'s seconds must be less than 60.");
        double v = deg + min / 60 + sec / 3600;
        if (g.Hemisphere is char hem)
        {
            if (negative) throw new FormatException($"Use either a minus sign or {hem} for the {what}, not both.");
            if (hem is 'S' or 'W') v = -v;
        }
        else if (negative) v = -v;
        return v;
    }

    public static string Format(double lat, double lon) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat:0.000000}, {lon:0.000000}");
}
