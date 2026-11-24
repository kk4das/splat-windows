using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SplatWin.Core;

/// <summary>
/// Writes a .kmz (doc.kml + the map image in one zip) that opens in Google Earth Pro and on the
/// web. SPLAT!'s placemark icon is 'root://icons/palette-5.png', which exists only inside Google
/// Earth Pro; the web version rejects the whole file over it, so those Style blocks are dropped
/// (the standard pin is used). Same rules as scripts\ppm2png.ps1. Other styles (the path lines'
/// colours) are kept.
/// </summary>
public sealed record KmlPin(string Name, double Lat, double Lon, string Description);

public static class Kmz
{
    /// <summary>
    /// Reads a KML SPLAT! wrote. SPLAT! declares UTF-8 but writes the degree sign as one Latin-1 byte
    /// (0xB0) in point-to-point KML descriptions, which Google Earth shows as '�'. Text that isn't
    /// valid UTF-8 is read as Latin-1, so the KMZ (always UTF-8) shows '°'.
    /// </summary>
    public static string ReadSplatKml(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(b); }
    }

    public static string WebSafeKml(string kml, string imageName, string? keyName = null, IEnumerable<KmlPin>? pins = null)
    {
        // Only the styles that use Google Earth Pro's built-in icon (the placemarks'); one <Style> at a time.
        string doc = Regex.Replace(kml, @"\s*<Style>(?:(?!</Style>).)*?root://(?:(?!</Style>).)*?</Style>", "", RegexOptions.Singleline);
        doc = Regex.Replace(doc, @"(<GroundOverlay>.*?<href>)[^<]*(</href>)", m => m.Groups[1].Value + imageName + m.Groups[2].Value,
                            RegexOptions.Singleline);
        // SPLAT!'s colour key (signal maps): point it at the PNG inside the KMZ, or drop it if there is none.
        doc = keyName != null
            ? Regex.Replace(doc, @"(<ScreenOverlay>.*?<href>)[^<]*(</href>)", m => m.Groups[1].Value + keyName + m.Groups[2].Value, RegexOptions.Singleline)
            : Regex.Replace(doc, @"\s*<ScreenOverlay>.*?</ScreenOverlay>", "", RegexOptions.Singleline);
        if (pins?.ToList() is { Count: > 0 } list)
        {
            // The user's own stations, in a folder of their own, before SPLAT!'s folder closes.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("\n  <Folder>\n    <name>Stations</name>\n");
            foreach (var p in list)
                sb.Append("    <Placemark>\n      <name>").Append(SecurityElement(p.Name)).Append("</name>\n      <description>")
                  .Append(SecurityElement(p.Description)).Append("</description>\n      <Point>\n        <coordinates>")
                  .Append(p.Lon.ToString("0.000000", ci)).Append(',').Append(p.Lat.ToString("0.000000", ci))
                  .Append(",0</coordinates>\n      </Point>\n    </Placemark>\n");
            sb.Append("  </Folder>\n");
            int at = doc.LastIndexOf("</Folder>", StringComparison.Ordinal);
            if (at < 0) throw new InvalidDataException("the KML has no folder to add the stations to");
            doc = doc.Insert(at, sb.ToString());
        }
        XDocument.Parse(doc);                                                    // must still be valid XML
        if (doc.Contains("root://")) throw new InvalidDataException("KML still refers to a Google Earth Pro built-in icon");
        return doc;
    }

    static string SecurityElement(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    /// <summary>A point-to-point KMZ: SPLAT!'s path KML (stations, the path and the line-of-sight path), web-safe, no image.</summary>
    public static void WritePath(string kmzPath, string kml)
    {
        string doc = WebSafeKml(kml, "", null);
        string part = kmzPath + ".part";
        File.Delete(part);
        try
        {
            using (var z = ZipFile.Open(part, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("doc.kml").Open(), new UTF8Encoding(false))) w.Write(doc);
            using (var r = ZipFile.OpenRead(part))
                if (r.GetEntry("doc.kml") is null) throw new InvalidDataException("KMZ check failed: doc.kml is missing");
            File.Move(part, kmzPath, overwrite: true);
        }
        finally { File.Delete(part); }
    }

    /// <summary>Writes the KMZ via a temporary file, verifies it, then moves it into place.</summary>
    public static void Write(string kmzPath, string kml, string pngPath, string? keyPngPath = null, IEnumerable<KmlPin>? pins = null)
    {
        string name = Path.GetFileName(pngPath);
        string? keyName = keyPngPath == null ? null : Path.GetFileName(keyPngPath);
        string doc = WebSafeKml(kml, name, keyName, pins);
        string part = kmzPath + ".part";
        File.Delete(part);
        try
        {
            using (var z = ZipFile.Open(part, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("doc.kml").Open(), new UTF8Encoding(false))) w.Write(doc);
                z.CreateEntryFromFile(pngPath, name);
                if (keyPngPath != null) z.CreateEntryFromFile(keyPngPath, keyName!);
            }
            using (var r = ZipFile.OpenRead(part))
            {
                var img = r.GetEntry(name);
                var key = keyPngPath == null ? null : r.GetEntry(keyName!);
                if (r.GetEntry("doc.kml") is null || img is null || img.Length != new FileInfo(pngPath).Length
                    || (keyPngPath != null && (key is null || key.Length != new FileInfo(keyPngPath).Length)))
                    throw new InvalidDataException("KMZ check failed: doc.kml or the image is missing or incomplete");
            }
            File.Move(part, kmzPath, overwrite: true);
        }
        finally { File.Delete(part); }
    }
}
