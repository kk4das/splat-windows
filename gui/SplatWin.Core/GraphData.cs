using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

public sealed record GraphSeries(string Title, string File, bool RightAxis, IReadOnlyList<(double X, double Y)> Points);

/// <summary>
/// One SPLAT! graph, read from the gnuplot command file (splat.gp) and data files (*.gp) that
/// SPLAT! keeps with -gpsav. The app draws it itself, so gnuplot is not needed. Only the commands
/// SPLAT! writes are understood; anything else is refused rather than guessed.
/// </summary>
public sealed record GraphData(
    string Title, string XLabel, string YLabel,
    (double Min, double Max)? XRange, (double Min, double Max)? YRange, (double Min, double Max)? Y2Range,
    IReadOnlyList<GraphSeries> Series)
{
    static readonly Encoding Latin1 = Encoding.Latin1;   // SPLAT! writes "set encoding iso_8859_1" and a 176 (°)
    static readonly Regex Range = new(@"^set (x|y|y2)range \[\s*(\S+)\s+to\s+(\S+)\s*\]$", RegexOptions.CultureInvariant);
    static readonly Regex Label = new(@"^set (title|xlabel|ylabel) ""(.*)""$", RegexOptions.CultureInvariant);
    static readonly Regex Plot = new(@"\G\s*""([^""]+)""\s+(?:axes\s+(x1y1|x1y2)\s+)?title\s+""([^""]*)""\s+with\s+lines\s*(,|$)", RegexOptions.CultureInvariant);

    public static GraphData Load(string folder)
    {
        string cmd = Path.Combine(folder, "splat.gp");
        if (!File.Exists(cmd)) throw new FileNotFoundException("SPLAT! didn't write the graph's command file (splat.gp).", cmd);
        return Parse(File.ReadAllText(cmd, Latin1), f =>
        {
            if (f.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || f.Contains("..")) throw new InvalidDataException($"The graph refers to the file \"{f}\" outside its folder.");
            return File.ReadAllText(Path.Combine(folder, f), Latin1);
        });
    }

    public static GraphData Parse(string commands, Func<string, string> readData)
    {
        string title = "", xl = "", yl = "";
        (double, double)? xr = null, yr = null, y2r = null;
        List<GraphSeries>? series = null;
        foreach (var raw in commands.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            Match m;
            if ((m = Range.Match(line)).Success)
            {
                var r = (Num(m.Groups[2].Value, line), Num(m.Groups[3].Value, line));
                if (m.Groups[1].Value == "x") xr = r; else if (m.Groups[1].Value == "y") yr = r; else y2r = r;
            }
            else if ((m = Label.Match(line)).Success)
            {
                string t = m.Groups[2].Value.Replace("\\n", "\n");
                if (m.Groups[1].Value == "title") title = t; else if (m.Groups[1].Value == "xlabel") xl = t; else yl = t;
            }
            else if (line.StartsWith("plot "))
            {
                series = new();
                string rest = line[5..];
                int pos = 0;
                while (pos < rest.Length)
                {
                    m = Plot.Match(rest, pos);
                    if (!m.Success) throw new InvalidDataException($"The graph's plot command isn't one SPLAT! writes: {line}");
                    series.Add(new GraphSeries(m.Groups[3].Value, m.Groups[1].Value, m.Groups[2].Value == "x1y2", ReadPoints(m.Groups[1].Value, readData(m.Groups[1].Value))));
                    pos = m.Index + m.Length;
                    if (m.Groups[4].Value != ",") break;
                }
                if (pos < rest.Length) throw new InvalidDataException($"The graph's plot command isn't one SPLAT! writes: {line}");
            }
            else if (line == "set grid" || line.StartsWith("set encoding ") || line.StartsWith("set term ") || line.StartsWith("set output ")) { }
            else throw new InvalidDataException($"The graph has a command the app doesn't know: {line}");
        }
        if (series == null || series.Count == 0) throw new InvalidDataException("The graph's command file has no plot command.");
        return new GraphData(title, xl, yl, xr, yr, y2r, series);
    }

    static double Num(string s, string line) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v
        : throw new InvalidDataException($"The graph has a range that isn't a number: {line}");

    static List<(double, double)> ReadPoints(string name, string text)
    {
        var pts = new List<(double, double)>();
        int n = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2
                || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.IsFinite(x) || !double.IsFinite(y))
                throw new InvalidDataException($"{name}, line {n}, isn't two numbers: \"{line}\"");
            pts.Add((x, y));
        }
        if (pts.Count == 0) throw new InvalidDataException($"{name} has no data.");
        return pts;
    }
}
