using System.Globalization;
using System.Text;

namespace SplatWin.Core;

/// <summary>
/// Reads back what a map pixel means from SPLAT!'s color file (.scf signal strength, .dcf received
/// power, .lcf path loss), using the same band rules SPLAT! used to paint it (WritePPMSS, WritePPMDBM,
/// WritePPMLR). It works from the color only, so the answer is a band (or, with smooth colors, an
/// estimate), never SPLAT!'s exact number: the app labels it as approximate.
/// </summary>
public sealed class ColourKey
{
    public enum Kinds { Signal, Power, Loss }

    public Kinds Kind { get; }
    public IReadOnlyList<(int Level, byte R, byte G, byte B)> Bands { get; }
    public string Unit => Kind switch { Kinds.Signal => "dBµV/m", Kinds.Power => "dBm", _ => "dB" };

    ColourKey(Kinds kind, List<(int, byte, byte, byte)> bands) { Kind = kind; Bands = bands; }

    /// <summary>The color file's kind from its extension (.scf, .dcf, .lcf), or null.</summary>
    public static Kinds? KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".scf" => Kinds.Signal,
        ".dcf" => Kinds.Power,
        ".lcf" => Kinds.Loss,
        _ => null,
    };

    /// <summary>Parses "level: red, green, blue" lines (SPLAT!'s LoadSignalColors and friends), in file order.</summary>
    public static ColourKey Parse(string text, Kinds kind)
    {
        var bands = new List<(int, byte, byte, byte)>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.Length == 0) continue;
            var lv = line.Split(':');
            if (lv.Length != 2) continue;
            var rgb = lv[1].Split(',');
            if (rgb.Length != 3) continue;
            if (!int.TryParse(lv[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)) continue;
            byte C(string s) => (byte)Math.Clamp(int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0, 0, 255);
            bands.Add((level, C(rgb[0]), C(rgb[1]), C(rgb[2])));
            if (bands.Count == 32) break;   // SPLAT! reads at most 32 regions
        }
        if (bands.Count == 0) throw new InvalidDataException("the color file has no color levels");
        return new ColourKey(kind, bands);
    }

    public static ColourKey Load(string path) =>
        Parse(File.ReadAllText(path, Encoding.Latin1), KindOf(path) ?? throw new InvalidDataException($"{Path.GetFileName(path)} isn't a SPLAT! color file"));

    string Band(int z)
    {
        var L = Bands;
        string u = Unit;
        if (Kind == Kinds.Loss)
            return z == 0 ? $"{L[0].Level} {u} or less" : $"{L[z - 1].Level} to {L[z].Level} {u}";
        return z == 0 ? $"{L[0].Level} {u} or more" : $"{L[z].Level} to {L[z - 1].Level} {u}";
    }

    string Below() => Kind == Kinds.Loss
        ? $"no color: more than {Bands[^1].Level} dB of path loss"
        : $"no color: below {Bands[^1].Level} {Unit}, the lowest level in the key";

    /// <summary>What a pixel of this color means on the map. Null when the color isn't one SPLAT! paints for a value.</summary>
    public string? Describe(byte r, byte g, byte b, bool smooth)
    {
        if (r == g && g == b) return Below();                      // terrain shading (gray) or no shading (white)
        if (r == 0 && g == 0 && b == 170) return Below() + " (sea level)";
        for (int z = 0; z < Bands.Count; z++)
            if (Bands[z].R == r && Bands[z].G == g && Bands[z].B == b) return Band(z);
        if (smooth)
        {
            // SPLAT! blends linearly between a band's color (at its level) and the next band up's.
            for (int z = 1; z < Bands.Count; z++)
            {
                var lo = Bands[z]; var hi = Bands[z - 1];
                var ch = new (int C, int C0, int C1)[] { (r, lo.R, hi.R), (g, lo.G, hi.G), (b, lo.B, hi.B) };
                // The estimate comes from the channel that changes most between the two colors (the finest steps);
                // every channel must then sit on the blend at that point, within SPLAT!'s rounding (it rounds up).
                var best = ch.MaxBy(x => Math.Abs(x.C1 - x.C0));
                if (best.C1 == best.C0) continue;
                double tt = (best.C - best.C0) / (double)(best.C1 - best.C0);
                if (tt < -0.01 || tt > 1.01) continue;
                bool ok = ch.All(x => Math.Abs(x.C - (x.C0 + tt * (x.C1 - x.C0))) <= 1.5);
                if (ok)
                {
                    double value = lo.Level + Math.Clamp(tt, 0, 1) * (hi.Level - lo.Level);
                    return $"about {value.ToString("0", CultureInfo.InvariantCulture)} {Unit} ({Band(z)})";
                }
            }
        }
        return null;
    }
}
