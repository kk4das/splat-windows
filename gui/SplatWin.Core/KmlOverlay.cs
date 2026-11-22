using System.Globalization;
using System.Xml.Linq;

namespace SplatWin.Core;

/// <summary>A point marked in a SPLAT! KML (a transmitter or receiver site).</summary>
public sealed record KmlPlacemark(string Name, double Lat, double Lon);

/// <summary>
/// What SPLAT! writes in a coverage-map .kml: the map image it refers to, the box it covers
/// (ordinary signed degrees, west negative) and the site placemarks.
/// </summary>
public sealed record KmlOverlay(string? ImageHref, double North, double South, double East, double West,
                                IReadOnlyList<KmlPlacemark> Placemarks)
{
    public static KmlOverlay Parse(string kmlText)
    {
        var doc = XDocument.Parse(kmlText);
        // SPLAT! uses the old KML 2.1 namespace; match on local names so any namespace works.
        IEnumerable<XElement> All(XContainer c, string name) => c.Descendants().Where(e => e.Name.LocalName == name);
        XElement? One(XContainer c, string name) => All(c, name).FirstOrDefault();
        double Num(XContainer c, string name)
        {
            var e = One(c, name) ?? throw new FormatException($"KML has no <{name}>");
            return double.Parse(e.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        var overlay = One(doc, "GroundOverlay") ?? throw new FormatException("KML has no <GroundOverlay> (is it a coverage-map KML?)");
        var box = One(overlay, "LatLonBox") ?? throw new FormatException("KML has no <LatLonBox>");
        string? href = One(One(overlay, "Icon") ?? overlay, "href")?.Value.Trim();
        double n = Num(box, "north"), s = Num(box, "south"), e = Num(box, "east"), w = Num(box, "west");
        if (!(n > s) || !(e > w) || n > 90 || s < -90) throw new FormatException($"KML box is not valid: N{n} S{s} E{e} W{w}");

        var marks = new List<KmlPlacemark>();
        foreach (var pm in All(doc, "Placemark"))
        {
            var coords = One(pm, "coordinates")?.Value.Trim();
            if (string.IsNullOrEmpty(coords)) continue;
            var parts = coords.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Split(',');
            if (parts.Length < 2) continue;
            marks.Add(new KmlPlacemark(One(pm, "name")?.Value.Trim() ?? "",
                double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[0], CultureInfo.InvariantCulture)));
        }
        return new KmlOverlay(href, n, s, e, w, marks);
    }
}
