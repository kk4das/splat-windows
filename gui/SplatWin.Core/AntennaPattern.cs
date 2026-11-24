using System.Globalization;
using System.Text;

namespace SplatWin.Core;

/// <summary>
/// A transmitter's antenna pattern: SPLAT!'s .az file (azimuth pattern) and, optionally, its .el
/// file (elevation pattern), kept with the station as text. SPLAT! reads them by name: for a
/// transmitter site file k4hta.qth it looks for k4hta.az and k4hta.el next to it (LoadPAT), so a job
/// writes them beside the .qth. The files themselves are passed to SPLAT! unchanged.
/// </summary>
public sealed class AntennaPattern
{
    /// <summary>The .az file's text: a rotation line, then "azimuth amplitude" lines.</summary>
    public string Az { get; set; } = "";
    /// <summary>The .el file's text, or null: a "tilt tilt-azimuth" line, then "elevation amplitude" lines.</summary>
    public string? El { get; set; }
    /// <summary>Where it came from, e.g. "k4hta_pat.az and .el".</summary>
    public string Source { get; set; } = "";

    public AntennaPattern Clone() => new() { Az = Az, El = El, Source = Source };

    public string Describe() => El != null ? $"azimuth and elevation pattern ({Source})" : $"azimuth pattern ({Source})";

    /// <summary>Reads a .az file and the .el file with the same name beside it, if there is one.
    /// <paramref name="path"/> may name either file.</summary>
    public static AntennaPattern Load(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string az = Path.Combine(dir, stem + ".az"), el = Path.Combine(dir, stem + ".el");
        if (!File.Exists(az)) throw new InvalidDataException($"There is no azimuth pattern file {stem}.az beside {Path.GetFileName(path)}; SPLAT! needs the .az file (the .el file is optional).");
        var p = new AntennaPattern
        {
            Az = File.ReadAllText(az, Encoding.Latin1),
            El = File.Exists(el) ? File.ReadAllText(el, Encoding.Latin1) : null,
        };
        p.Source = p.El != null ? $"{stem}.az and {stem}.el" : $"{stem}.az";
        if (p.Problem() is { } why) throw new InvalidDataException(why);
        return p;
    }

    /// <summary>Writes &lt;baseName&gt;.az (and .el) into <paramref name="dir"/>, as SPLAT! expects.</summary>
    public void WriteFor(string dir, string baseName)
    {
        File.WriteAllText(Path.Combine(dir, baseName + ".az"), Az, Encoding.Latin1);
        if (El != null) File.WriteAllText(Path.Combine(dir, baseName + ".el"), El, Encoding.Latin1);
    }

    /// <summary>Why SPLAT! would misread this pattern, or null. Checks SPLAT!'s documented format.</summary>
    public string? Problem()
    {
        if (Check(Az, ".az", 1, 0, 360) is { } a) return a;
        if (El != null && Check(El, ".el", 2, -10, 90) is { } e) return e;
        return null;
    }

    // SPLAT! reads at most 253 characters a line, strips anything after ';', takes the first line's
    // numbers as settings, then "angle amplitude" pairs. Amplitudes are normalised field values (0 to 1).
    static string? Check(string text, string kind, int headerNumbers, double minAngle, double maxAngle)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int n = 0, pairs = 0;
        bool header = true;
        foreach (var raw in lines)
        {
            n++;
            if (raw.Length > 250) return $"The {kind} file's line {n} is too long for SPLAT! (at most 250 characters).";
            string line = raw.Split(';')[0].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<double>();
            foreach (var p in parts)
            {
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return $"The {kind} file's line {n} isn't numbers: \"{raw.Trim()}\".";
                nums.Add(v);
            }
            if (header)
            {
                if (nums.Count < headerNumbers)
                    return kind == ".az" ? "The .az file's first line should be the pattern's rotation in degrees."
                                         : "The .el file's first line should be the mechanical tilt and the tilt's azimuth, in degrees.";
                header = false;
                continue;
            }
            if (nums.Count < 2) return $"The {kind} file's line {n} should be an angle and an amplitude: \"{raw.Trim()}\".";
            if (nums[0] < minAngle || nums[0] > maxAngle)
                return $"The {kind} file's line {n} has angle {nums[0].ToString(CultureInfo.InvariantCulture)}; SPLAT! uses {minAngle:0} to {maxAngle:0} degrees.";
            if (nums[1] < 0 || nums[1] > 1)
                return $"The {kind} file's line {n} has amplitude {nums[1].ToString(CultureInfo.InvariantCulture)}; SPLAT! expects normalised values from 0 to 1.";
            pairs++;
        }
        if (header) return $"The {kind} file is empty.";
        if (pairs < 2) return $"The {kind} file has {pairs} pattern value(s); a pattern needs at least two.";
        return null;
    }
}
