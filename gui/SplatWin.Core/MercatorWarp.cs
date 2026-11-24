namespace SplatWin.Core;

/// <summary>
/// SPLAT! maps are a plain latitude/longitude grid: every row covers the same span of latitude.
/// Web maps (Leaflet, OpenStreetMap) use the Web Mercator projection, where rows further from the
/// equator are stretched. Laying SPLAT!'s image on a web map unchanged puts the middle of a
/// 2-degree map about half a mile out at 39 N, so the rows are re-sampled first.
///
/// Columns need no change: longitude is linear in both projections. Rows are re-sampled by
/// nearest neighbor, so every output pixel is an exact SPLAT! color (the color key still
/// reads correctly). Google Earth uses the latitude/longitude grid itself, so KMZ files keep the
/// original image.
/// </summary>
public static class MercatorWarp
{
    /// <summary>Web Mercator y for a latitude (radians-based; only differences matter).</summary>
    public static double MercatorY(double latDeg) => Math.Log(Math.Tan(Math.PI / 4 + latDeg * Math.PI / 360));

    public static double LatFromMercatorY(double y) => (2 * Math.Atan(Math.Exp(y)) - Math.PI / 2) * 180 / Math.PI;

    /// <summary>
    /// Which source row (0 = north edge) supplies output row <paramref name="outRow"/> when an image
    /// covering <paramref name="north"/>..<paramref name="south"/> is redrawn in Mercator.
    /// </summary>
    public static int SourceRow(int outRow, int outHeight, int srcHeight, double north, double south)
    {
        double yN = MercatorY(north), yS = MercatorY(south);
        double y = yN - (outRow + 0.5) / outHeight * (yN - yS);       // center of the output row
        double lat = LatFromMercatorY(y);
        int r = (int)Math.Floor((north - lat) / (north - south) * srcHeight);
        return Math.Clamp(r, 0, srcHeight - 1);
    }

    /// <summary>Returns the image re-sampled to Web Mercator rows (same width and height).</summary>
    public static RgbImage ToMercator(RgbImage src, double north, double south)
    {
        int w = src.Width, h = src.Height, stride = w * 3;
        var dst = new byte[src.Pixels.Length];
        for (int j = 0; j < h; j++)
            Buffer.BlockCopy(src.Pixels, SourceRow(j, h, h, north, south) * stride, dst, j * stride, stride);
        return new RgbImage(w, h, dst);
    }
}
