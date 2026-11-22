namespace SplatWin.Core;

/// <summary>A plain 24-bit image: rows top to bottom, 3 bytes (R, G, B) per pixel, no padding.</summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("image size must be positive");
        if (pixels.Length != checked(width * height * 3)) throw new ArgumentException("pixel buffer size does not match the image size");
        Width = width; Height = height; Pixels = pixels;
    }

    /// <summary>Reads a binary (P6) PPM as SPLAT! writes it. Rejects other formats and truncated files.</summary>
    public static RgbImage ReadPpm(byte[] b)
    {
        if (b.Length < 2 || b[0] != (byte)'P' || b[1] != (byte)'6') throw new FormatException("not a binary (P6) PPM");
        int i = 2;
        int w = Token(b, ref i), h = Token(b, ref i), max = Token(b, ref i);
        i++;                                   // exactly one whitespace byte before the pixel data
        if (max != 255) throw new FormatException($"unsupported PPM maxval {max}");
        long need = (long)w * h * 3;
        if (b.Length - i < need) throw new FormatException("PPM is truncated");
        var px = new byte[need];
        Array.Copy(b, i, px, 0, need);
        return new RgbImage(w, h, px);
    }

    static int Token(byte[] b, ref int i)
    {
        while (true)
        {
            while (i < b.Length && char.IsWhiteSpace((char)b[i])) i++;
            if (i < b.Length && b[i] == (byte)'#') { while (i < b.Length && b[i] != 10) i++; continue; }
            break;
        }
        int v = 0, n = 0;
        while (i < b.Length && b[i] >= (byte)'0' && b[i] <= (byte)'9') { v = checked(v * 10 + (b[i] - 48)); i++; n++; }
        if (n == 0) throw new FormatException("bad PPM header");
        return v;
    }
}
