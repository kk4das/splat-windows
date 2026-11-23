using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>An obstruction SPLAT! found, with ordinary signs (east positive). Distance and height are SPLAT!'s own text.</summary>
public sealed record Obstruction(double Lat, double Lon, string Distance, string Height);

/// <summary>
/// The headline numbers of a SPLAT! path report (name-to-name.txt). Every value is the report's
/// own text, copied, never recomputed or reformatted, so the panel shows exactly the CLI's numbers.
/// A value SPLAT! didn't print (for example signal levels without an ERP) is null.
/// </summary>
public sealed record PathSummary(
    string TxName, string RxName,
    string? Distance, string? AzimuthToRx, string? AzimuthToTx,
    string? TxGround, string? RxGround, string? TxAntenna, string? RxAntenna,
    string? Model, string? Frequency, string? Erp,
    string? FreeSpaceLoss, string? PathLoss, string? Attenuation,
    string? FieldStrength, string? SignalPower,
    string? Mode, int? ErrorNumber, string? ErrorNote,
    IReadOnlyList<Obstruction> Obstructions, bool LineOfSightClear, IReadOnlyList<string> Clearance)
{
    static readonly Regex Obs = new(@"^\s*(\d+\.\d+) ([NS]),\s*(\d+\.\d+) W,\s*(.+?),\s*(.+ AMSL)$", RegexOptions.CultureInvariant);

    public static PathSummary Load(string reportPath) => Parse(File.ReadAllText(reportPath, Encoding.Latin1));

    public static PathSummary Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (!lines.Any(l => l.Contains("Path Analysis ]==--")))
            throw new InvalidDataException("This isn't a SPLAT! path report.");
        string? tx = null, rx = null;
        var f = new Dictionary<string, string>();
        string section = "";
        int lastDashes = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.StartsWith("-----")) { lastDashes = i; continue; }
            if (l.StartsWith("Transmitter site: ")) { tx = l[18..]; section = "tx"; continue; }
            if (l.StartsWith("Receiver site: ")) { rx = l[15..]; section = "rx"; continue; }
            if (l.Contains("Parameters Used In This Analysis")) { f["model"] = l[..l.IndexOf(" Parameters", StringComparison.Ordinal)]; section = "lr"; continue; }
            if (l.StartsWith("Summary For The Link")) { section = "sum"; continue; }
            int c = l.IndexOf(": ", StringComparison.Ordinal);
            if (c <= 0) continue;
            string key = l[..c], val = l[(c + 2)..];
            string k = section switch
            {
                "tx" or "rx" when key == "Ground elevation" => section + ".ground",
                "tx" or "rx" when key == "Antenna height" => section + ".antenna",
                "tx" when key.StartsWith("Distance to ") => "distance",
                "tx" when key.StartsWith("Azimuth to ") => "az.rx",
                "rx" when key.StartsWith("Azimuth to ") => "az.tx",
                "lr" when key == "Frequency" => "freq",
                "lr" when key == "Transmitter ERP" => "erp",
                "sum" when key == "Free space path loss" => "fspl",
                "sum" when key.EndsWith(" path loss") => "loss",
                "sum" when key == "Attenuation due to terrain shielding" => "atten",
                "sum" when key.StartsWith("Field strength at ") => "field",
                "sum" when key.StartsWith("Signal power level at ") => "power",
                "sum" when key == "Mode of propagation" => "mode",
                "sum" when key.EndsWith(" error number") => "err",
                _ => "",
            };
            if (k.Length > 0 && !f.ContainsKey(k)) f[k] = val;
        }
        if (tx == null || rx == null) throw new InvalidDataException("The path report doesn't name both sites.");

        int? errNum = null; string? errNote = null;
        if (f.TryGetValue("err", out var e))
        {
            var m = Regex.Match(e, @"^(\d+)");
            if (m.Success) errNum = int.Parse(m.Value, CultureInfo.InvariantCulture);
            int at = Array.FindIndex(lines, l => l.Contains(" error number: "));
            var note = lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("-----")).Select(l => l.Trim()).Where(l => l.Length > 0);
            errNote = string.Join(" ", note);
            if (errNote.Length == 0) errNote = null;
        }

        // After the last line of dashes: the obstruction list and the clearance sentences.
        var obstructions = new List<Obstruction>();
        var clearance = new List<string>();
        var para = new StringBuilder();
        void Flush() { if (para.Length > 0) { clearance.Add(para.ToString()); para.Clear(); } }
        bool clear = false;
        foreach (var l in lines.Skip(lastDashes + 1))
        {
            var m = Obs.Match(l);
            if (m.Success)
            {
                double lat = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * (m.Groups[2].Value == "S" ? -1 : 1);
                double west = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                double lon = west > 180 ? 360 - west : -west;
                obstructions.Add(new Obstruction(lat, lon == 0 ? 0 : lon, m.Groups[4].Value.Trim(), m.Groups[5].Value.Trim()));
                continue;
            }
            string t = l.Trim();
            if (t.Length == 0) { Flush(); continue; }
            if (t.Contains("detected obstructions at:")) continue;
            if (t.StartsWith("No obstructions to LOS path")) clear = true;
            para.Append(para.Length > 0 ? " " : "").Append(t);
        }
        Flush();

        string? G(string k) => f.TryGetValue(k, out var v) ? v : null;
        return new PathSummary(tx, rx, G("distance"), G("az.rx"), G("az.tx"), G("tx.ground"), G("rx.ground"),
            G("tx.antenna"), G("rx.antenna"), G("model"), G("freq"), G("erp"), G("fspl"), G("loss"), G("atten"),
            G("field"), G("power"), G("mode"), errNum, errNote, obstructions, clear, clearance);
    }
}
