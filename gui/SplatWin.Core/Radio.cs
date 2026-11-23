using System.Globalization;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>
/// A transmitter's radio and ground settings: what SPLAT! reads from its .lrp file. Kept with the
/// transmitter station in the library. Frequency and ERP start at 0, meaning "not entered yet".
/// </summary>
public sealed class RadioSettings
{
    public double Dielectric { get; set; } = 15.0;       // SPLAT! documentation: average ground
    public double Conductivity { get; set; } = 0.005;
    public double Bending { get; set; } = 301.0;          // N-units
    public double FrequencyMHz { get; set; }
    public int Climate { get; set; } = 5;                 // Continental Temperate
    public int Polarization { get; set; } = 1;            // vertical, usual for VHF/UHF FM
    public double Situations { get; set; } = 0.50;
    public double Time { get; set; } = 0.90;
    public double ErpWatts { get; set; }

    public RadioSettings Clone() => (RadioSettings)MemberwiseClone();

    /// <summary>SPLAT!'s radio climate numbers (its own spelling in reports aside).</summary>
    public static readonly IReadOnlyList<string> ClimateNames = new[]
    {
        "1 Equatorial", "2 Continental subtropical", "3 Maritime subtropical", "4 Desert",
        "5 Continental temperate", "6 Maritime temperate, over land", "7 Maritime temperate, over sea",
    };

    /// <summary>The ground types in SPLAT!'s documentation: dielectric constant and conductivity (S/m).</summary>
    public static readonly IReadOnlyList<(string Name, double Dielectric, double Conductivity)> Grounds = new[]
    {
        ("Average ground", 15.0, 0.005),
        ("Poor ground", 4.0, 0.001),
        ("Good ground", 25.0, 0.020),
        ("Farmland, forest", 15.0, 0.005),
        ("Marshy land", 12.0, 0.007),
        ("Mountain, sand", 13.0, 0.002),
        ("City, industrial", 5.0, 0.001),
        ("Fresh water", 80.0, 0.010),
        ("Salt water", 80.0, 5.000),
    };

    /// <summary>Why SPLAT! can't use these settings, or null if it can.</summary>
    public string? Problem()
    {
        if (!(FrequencyMHz >= 20 && FrequencyMHz <= 20000)) return "Enter the frequency in MHz (SPLAT! works from 20 MHz to 20 GHz).";
        if (!(ErpWatts > 0 && ErpWatts <= 10_000_000)) return "Enter the transmitter's ERP in watts (more than 0).";
        if (!(Dielectric >= 1 && Dielectric <= 100)) return "The ground's dielectric constant should be between 1 and 100 (average ground is 15).";
        if (!(Conductivity > 0 && Conductivity <= 10)) return "The ground's conductivity should be more than 0 and at most 10 S/m (average ground is 0.005).";
        if (!(Bending >= 250 && Bending <= 400)) return "The atmospheric bending constant should be between 250 and 400 N-units (301 is usual).";
        if (Climate < 1 || Climate > 7) return "Choose a radio climate from 1 to 7.";
        if (Polarization != 0 && Polarization != 1) return "Choose horizontal or vertical polarization.";
        if (!(Situations >= 0.01 && Situations <= 0.99)) return "The fraction of situations should be between 1% and 99%.";
        if (!(Time >= 0.01 && Time <= 0.99)) return "The fraction of time should be between 1% and 99%.";
        return null;
    }
}

/// <summary>
/// Reads and writes SPLAT! .lrp files the way SPLAT!'s ReadLRParm reads them: one number per line
/// before any ';', in a fixed order, ERP optional (in watts, or dBm EIRP if the line says dBm).
/// Where SPLAT! would quietly misread a file, this refuses it with a reason.
/// </summary>
public static class LrpFile
{
    public const int MaxLine = 78;   // ReadLRParm reads lines with fgets(string, 80, fd)
    static readonly Regex Real = new(@"^\s*[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?", RegexOptions.CultureInvariant);
    static readonly Regex Int = new(@"^\s*[-+]?\d+", RegexOptions.CultureInvariant);
    static readonly string[] What =
    {
        "earth dielectric constant", "earth conductivity", "atmospheric bending constant", "frequency",
        "radio climate", "polarization", "fraction of situations", "fraction of time",
    };

    static string N(double v) => v.ToString("0.000###########", CultureInfo.InvariantCulture);

    public static string Format(RadioSettings r) => string.Join("\r\n",
        $"{N(r.Dielectric)}\t; Earth Dielectric Constant (Relative permittivity)",
        $"{N(r.Conductivity)}\t; Earth Conductivity (Siemens per meter)",
        $"{N(r.Bending)}\t; Atmospheric Bending Constant (N-Units)",
        $"{N(r.FrequencyMHz)}\t; Frequency in MHz (20 MHz to 20 GHz)",
        $"{r.Climate}\t; Radio Climate (1-7, 5 = Continental Temperate)",
        $"{r.Polarization}\t; Polarization (0 = Horizontal, 1 = Vertical)",
        $"{N(r.Situations)}\t; Fraction of Situations",
        $"{N(r.Time)}\t; Fraction of Time",
        $"{N(r.ErpWatts)}\t; Transmitter ERP in Watts",
        "",
        "Written by SPLAT! for Windows. See the SPLAT! documentation for the meaning of each value.") + "\r\n";

    public static RadioSettings Parse(string text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 8) throw new FormatException("A .lrp file needs at least eight lines of values; this one is shorter.");
        for (int i = 0; i < Math.Min(lines.Length, 9); i++)
            if (lines[i].Length > MaxLine)
                throw new FormatException($"Line {i + 1} is longer than {MaxLine} characters, which SPLAT! would split and misread.");
        string Value(int i) { string s = lines[i]; int c = s.IndexOf(';'); return c >= 0 ? s[..c] : s; }
        double D(int i)
        {
            var m = Real.Match(Value(i));
            if (!m.Success) throw new FormatException($"Line {i + 1} ({What[i]}) doesn't start with a number, so SPLAT! would stop reading the file there.");
            return double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        int I(int i)
        {
            var m = Int.Match(Value(i));
            if (!m.Success) throw new FormatException($"Line {i + 1} ({What[i]}) doesn't start with a whole number, so SPLAT! would stop reading the file there.");
            return int.Parse(m.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite, CultureInfo.InvariantCulture);
        }
        var r = new RadioSettings
        {
            Dielectric = D(0), Conductivity = D(1), Bending = D(2), FrequencyMHz = D(3),
            Climate = I(4), Polarization = I(5), Situations = D(6), Time = D(7), ErpWatts = 0,
        };
        if (lines.Length > 8)
        {
            var m = Real.Match(Value(8));
            if (m.Success)
            {
                double erp = double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                // As SPLAT! does: "dBm" anywhere on the line (even in the comment) means dBm EIRP.
                if (lines[8].Contains("dBm") || lines[8].Contains("dbm")) erp = Math.Pow(10.0, (erp - 32.14) / 10.0);
                r.ErpWatts = erp;
            }
        }
        return r;
    }
}
