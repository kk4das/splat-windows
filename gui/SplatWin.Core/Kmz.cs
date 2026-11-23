using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SplatWin.Core;

/// <summary>
/// Writes a .kmz (doc.kml + the map image in one zip) that opens in Google Earth Pro and on the
/// web. SPLAT!'s placemark icon is 'root://icons/palette-5.png', which exists only inside Google
/// Earth Pro; the web version rejects the whole file over it, so those Style blocks are dropped
/// (the standard pin is used). Same rules as scripts\ppm2png.ps1.
/// </summary>
public static class Kmz
{
    public static string WebSafeKml(string kml, string imageName, string? keyName = null)
    {
        string doc = Regex.Replace(kml, @"\s*<Style>.*?</Style>", "", RegexOptions.Singleline);
        doc = Regex.Replace(doc, @"(<GroundOverlay>.*?<href>)[^<]*(</href>)", m => m.Groups[1].Value + imageName + m.Groups[2].Value,
                            RegexOptions.Singleline);
        // SPLAT!'s colour key (signal maps): point it at the PNG inside the KMZ, or drop it if there is none.
        doc = keyName != null
            ? Regex.Replace(doc, @"(<ScreenOverlay>.*?<href>)[^<]*(</href>)", m => m.Groups[1].Value + keyName + m.Groups[2].Value, RegexOptions.Singleline)
            : Regex.Replace(doc, @"\s*<ScreenOverlay>.*?</ScreenOverlay>", "", RegexOptions.Singleline);
        XDocument.Parse(doc);                                                    // must still be valid XML
        if (doc.Contains("root://")) throw new InvalidDataException("KML still refers to a Google Earth Pro built-in icon");
        return doc;
    }

    /// <summary>Writes the KMZ via a temporary file, verifies it, then moves it into place.</summary>
    public static void Write(string kmzPath, string kml, string pngPath, string? keyPngPath = null)
    {
        string name = Path.GetFileName(pngPath);
        string? keyName = keyPngPath == null ? null : Path.GetFileName(keyPngPath);
        string doc = WebSafeKml(kml, name, keyName);
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
