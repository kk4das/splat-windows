using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>
/// Turns a SPLAT! coverage .kml (and the .ppm or .png it refers to) into what the map page needs:
/// a Web-Mercator image plus its box and sites, and a .kmz for Google Earth.
/// </summary>
public sealed class LoadedMap
{
    public required string KmlPath { get; init; }
    public required KmlOverlay Kml { get; init; }
    public required string MercatorPng { get; init; }   // for the in-app map
    public required string OriginalPng { get; init; }   // lat/lon grid, for Google Earth
    public required string Kmz { get; init; }
    public string? ColourKeyPng { get; init; }           // SPLAT!'s color key (signal maps), as PNG
    public int Width { get; init; }
    public int Height { get; init; }
    public bool ClearWhite { get; init; }                // -ngs map: white (no shading) shown as clear
    public required RgbImage Source { get; init; }       // SPLAT!'s pixels, for the click readout
    public ColourKey? Key { get; init; }                 // the color file SPLAT! painted with, if found
    public bool LineOfSight { get; init; }

    /// <summary>
    /// What the map shows at a point, read from the color SPLAT! painted there (so a band, not
    /// SPLAT!'s exact number). Null outside the map, or when the map's color file isn't known.
    /// </summary>
    public string? Probe(double lat, double lon)
    {
        if (lat > Kml.North || lat < Kml.South || lon < Kml.West || lon > Kml.East) return null;
        int x = Math.Clamp((int)((lon - Kml.West) / (Kml.East - Kml.West) * Width), 0, Width - 1);
        int y = Math.Clamp((int)((Kml.North - lat) / (Kml.North - Kml.South) * Height), 0, Height - 1);
        int i = (y * Width + x) * 3;
        byte r = Source.Pixels[i], g = Source.Pixels[i + 1], b = Source.Pixels[i + 2];
        if (LineOfSight)
        {
            if (r == 0 && g == 255 && b == 0) return "line of sight to the transmitter";
            if ((r == g && g == b) || (r == 0 && g == 0 && b == 170)) return "no line of sight";
            return "a SPLAT! marking (site or label)";
        }
        if (Key == null) return null;
        return Key.Describe(r, g, b, smooth: true) ?? "a SPLAT! marking (site or label)";
    }
}

public static class MapLoader
{
    /// <param name="clearWhite">For a map made without terrain shading (-ngs): show its white as clear, in the app and in Google Earth.</param>
    /// <param name="pins">The user's stations to add to the Google Earth file.</param>
    public static LoadedMap Load(string kmlPath, string outDir, bool clearWhite = false, IEnumerable<KmlPin>? pins = null)
    {
        string kmlText = Kmz.ReadSplatKml(kmlPath);
        var kml = KmlOverlay.Parse(kmlText);
        if (string.IsNullOrEmpty(kml.ImageHref)) throw new InvalidDataException("the KML does not name a map image");
        string dir = Path.GetDirectoryName(Path.GetFullPath(kmlPath))!;
        string img = Path.Combine(dir, kml.ImageHref);
        if (!File.Exists(img)) throw new FileNotFoundException($"the map image {kml.ImageHref} is not next to the KML", img);

        string stem = Path.GetFileNameWithoutExtension(img);
        Directory.CreateDirectory(outDir);
        RgbImage src;
        string originalPng;
        if (img.EndsWith(".ppm", StringComparison.OrdinalIgnoreCase))
        {
            src = RgbImage.ReadPpm(File.ReadAllBytes(img));
            originalPng = Path.Combine(outDir, stem + ".png");
            SavePng(src, originalPng, clearWhite);
        }
        else
        {
            src = ReadImage(img);
            originalPng = img;
            if (clearWhite) { originalPng = Path.Combine(outDir, stem + ".png"); SavePng(src, originalPng, true); }
        }
        string merc = Path.Combine(outDir, stem + "-mercator.png");
        SavePng(MercatorWarp.ToMercator(src, kml.North, kml.South), merc, clearWhite);
        // The color key SPLAT! writes beside a signal map (<name>-ck.ppm), named in the KML's ScreenOverlay.
        string? keyPng = null;
        var ck = System.Text.RegularExpressions.Regex.Match(kmlText, @"<ScreenOverlay>.*?<href>([^<]*)</href>", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (ck.Success && Path.GetFileName(ck.Groups[1].Value.Trim()) is { Length: > 0 } ckName && File.Exists(Path.Combine(dir, ckName)))
        {
            string ckPath = Path.Combine(dir, ckName);
            keyPng = Path.Combine(outDir, Path.GetFileNameWithoutExtension(ckName) + ".png");
            if (ckPath.EndsWith(".ppm", StringComparison.OrdinalIgnoreCase)) SavePng(RgbImage.ReadPpm(File.ReadAllBytes(ckPath)), keyPng);
            else File.Copy(ckPath, keyPng, true);
        }
        string kmz = Path.Combine(outDir, stem + ".kmz");
        // Stations on the map, apart from one SPLAT! already marks (same name, within about 100 m).
        var onMap = pins?.Where(p => p.Lat <= kml.North && p.Lat >= kml.South && p.Lon >= kml.West && p.Lon <= kml.East)
                         .Where(p => !kml.Placemarks.Any(m => string.Equals(m.Name, p.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                                                             && Math.Abs(m.Lat - p.Lat) < 0.001 && Math.Abs(m.Lon - p.Lon) < 0.0013))
                         .ToList();
        Kmz.Write(kmz, kmlText, originalPng, keyPng, onMap);

        // Which color file SPLAT! used: its overlay's name says what the map shows; the file is beside the KML.
        var name = System.Text.RegularExpressions.Regex.Match(kmlText, @"<GroundOverlay>.*?<name>([^<]*)</name>",
                                                              System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        bool los = name.Contains("Line-of-Sight", StringComparison.OrdinalIgnoreCase);
        string? ext = name.Contains("Power Level") ? ".dcf" : name.Contains("Signal Strength") ? ".scf" : name.Contains("Path Loss") ? ".lcf" : null;
        ColourKey? key = null;
        if (ext != null && Directory.GetFiles(dir, "*" + ext) is { Length: 1 } cf)   // exactly one, or it's unclear which
        {
            try { key = ColourKey.Load(cf[0]); } catch (InvalidDataException) { }
        }
        return new LoadedMap { KmlPath = kmlPath, Kml = kml, MercatorPng = merc, OriginalPng = originalPng, Kmz = kmz,
                               Width = src.Width, Height = src.Height, ColourKeyPng = keyPng, ClearWhite = clearWhite,
                               Source = src, Key = key, LineOfSight = los };
    }

    /// <summary>The JSON the map page's splat.showMap() takes.</summary>
    public static string PageJson(LoadedMap m, string imageUrl) => JsonSerializer.Serialize(new
    {
        image = imageUrl, north = m.Kml.North, south = m.Kml.South, east = m.Kml.East, west = m.Kml.West,
        placemarks = m.Kml.Placemarks.Select(p => new { name = p.Name, lat = p.Lat, lon = p.Lon }),
    });

    static RgbImage ReadImage(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var rgb = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
        int w = rgb.PixelWidth, h = rgb.PixelHeight;
        var px = new byte[w * h * 3];
        rgb.CopyPixels(px, w * 3, 0);
        return new RgbImage(w, h, px);
    }

    /// <summary>Saves a PNG and checks it decodes back to the same pixels. With clearWhite, pure white
    /// (255,255,255) becomes fully clear and everything else stays opaque.</summary>
    static void SavePng(RgbImage im, string path, bool clearWhite = false)
    {
        if (clearWhite) { SavePngClearWhite(im, path); return; }
        var bmp = BitmapSource.Create(im.Width, im.Height, 96, 96, PixelFormats.Rgb24, null, im.Pixels, im.Width * 3);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        string part = path + ".part";
        using (var fs = File.Create(part)) enc.Save(fs);
        // Verify: the PNG must decode back to exactly these pixels (PNG is lossless).
        var back = ReadImage(part);
        if (back.Width != im.Width || back.Height != im.Height || !back.Pixels.AsSpan().SequenceEqual(im.Pixels))
        {
            File.Delete(part);
            throw new InvalidDataException($"PNG check failed for {Path.GetFileName(path)}");
        }
        File.Move(part, path, overwrite: true);
    }

    static void SavePngClearWhite(RgbImage im, string path)
    {
        int n = im.Width * im.Height;
        var px = new byte[n * 4];   // BGRA
        for (int i = 0; i < n; i++)
        {
            byte r = im.Pixels[i * 3], g = im.Pixels[i * 3 + 1], b = im.Pixels[i * 3 + 2];
            px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r;
            px[i * 4 + 3] = (byte)(r == 255 && g == 255 && b == 255 ? 0 : 255);
        }
        var bmp = BitmapSource.Create(im.Width, im.Height, 96, 96, PixelFormats.Bgra32, null, px, im.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        string part = path + ".part";
        using (var fs = File.Create(part)) enc.Save(fs);
        var frame = BitmapDecoder.Create(new Uri(Path.GetFullPath(part)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var back = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var got = new byte[n * 4];
        back.CopyPixels(got, im.Width * 4, 0);
        if (back.PixelWidth != im.Width || back.PixelHeight != im.Height || !got.AsSpan().SequenceEqual(px))
        {
            File.Delete(part);
            throw new InvalidDataException($"PNG check failed for {Path.GetFileName(path)}");
        }
        File.Move(part, path, overwrite: true);
    }
}
