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
    public string? ColourKeyPng { get; init; }           // SPLAT!'s colour key (signal maps), as PNG
    public int Width { get; init; }
    public int Height { get; init; }
}

public static class MapLoader
{
    public static LoadedMap Load(string kmlPath, string outDir)
    {
        string kmlText = File.ReadAllText(kmlPath);
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
            SavePng(src, originalPng);
        }
        else
        {
            src = ReadImage(img);
            originalPng = img;
        }
        string merc = Path.Combine(outDir, stem + "-mercator.png");
        SavePng(MercatorWarp.ToMercator(src, kml.North, kml.South), merc);
        // The colour key SPLAT! writes beside a signal map (<name>-ck.ppm), named in the KML's ScreenOverlay.
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
        Kmz.Write(kmz, kmlText, originalPng, keyPng);
        return new LoadedMap { KmlPath = kmlPath, Kml = kml, MercatorPng = merc, OriginalPng = originalPng, Kmz = kmz,
                               Width = src.Width, Height = src.Height, ColourKeyPng = keyPng };
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

    static void SavePng(RgbImage im, string path)
    {
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
}
