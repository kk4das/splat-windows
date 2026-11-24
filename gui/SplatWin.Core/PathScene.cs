using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SplatWin.Core;

/// <summary>What SPLAT! was asked to do in a path job folder, read back from run-again.cmd and the files SPLAT! read.</summary>
public sealed record PathJobSetup(string Folder, string TxName, string RxName, SplatSite Tx, SplatSite Rx, double FrequencyMhz,
                                  bool Metric, double FresnelClearance, double ClutterFeet, bool Hd, string TerrainFolder,
                                  string? UdtFile, IReadOnlyList<string> SplatTiles)
{
    /// <summary>Reads the job folder: the first SPLAT! command in run-again.cmd, the .qth files and the transmitter's .lrp.</summary>
    public static PathJobSetup FromJobFolder(string folder)
    {
        string script = Path.Combine(folder, PathJob.ReRunFile);
        if (!File.Exists(script)) throw new FileNotFoundException("This job folder has no run-again.cmd, so it isn't known how SPLAT! was run.", script);
        var line = File.ReadAllLines(script).FirstOrDefault(l => l.Contains("splat", StringComparison.OrdinalIgnoreCase) && l.Contains(" -t "))
                   ?? throw new InvalidDataException("run-again.cmd has no SPLAT! command in it.");
        var tok = Tokens(line);
        bool hd = Path.GetFileName(tok[0]).Equals("splat-hd.exe", StringComparison.OrdinalIgnoreCase);
        string? Arg(string flag) { int i = tok.IndexOf(flag); return i >= 0 && i + 1 < tok.Count && !tok[i + 1].StartsWith('-') ? tok[i + 1] : null; }
        string txFile = Arg("-t") ?? throw new InvalidDataException("run-again.cmd doesn't name the transmitter (-t).");
        string rxFile = Arg("-r") ?? throw new InvalidDataException("run-again.cmd doesn't name the receiver (-r).");
        string terrain = Arg("-d") ?? throw new InvalidDataException("run-again.cmd doesn't name the terrain folder (-d).");
        bool metric = tok.Contains("-metric");
        double fz = 0.6;
        if (Arg("-fz") is { } fzs && SplatGeo.LeadingNumber(fzs, out double fzv)) { fz = fzv < 0 || fzv > 100 ? 60 : fzv; fz /= 100.0; }
        double clutter = 0;
        if (Arg("-gc") is { } gcs && SplatGeo.LeadingNumber(gcs, out double gcv)) clutter = Math.Max(0, gcv);
        if (metric) clutter /= SplatGeo.MetersPerFoot;   // SPLAT!: meters --> feet
        string? udt = Arg("-udt") is { } u ? Path.Combine(folder, u) : null;

        string Qth(string f) => Path.Combine(folder, f.EndsWith(".qth", StringComparison.OrdinalIgnoreCase) ? f : f + ".qth");
        var (txName, tx) = SplatGeo.LoadQth(Qth(txFile));
        var (rxName, rx) = SplatGeo.LoadQth(Qth(rxFile));
        double frq = 0;
        string lrp = Path.Combine(folder, Path.GetFileNameWithoutExtension(txFile) + ".lrp");
        if (File.Exists(lrp))
        {
            var l = File.ReadAllLines(lrp, Encoding.Latin1);
            if (l.Length > 3) SplatGeo.LeadingNumber(l[3].Split(';')[0], out frq);
        }
        var tiles = Terrain.TilesFor(new[] { (tx.Lat, SplatGeo.East(tx.Lon)), (rx.Lat, SplatGeo.East(rx.Lon)) }, hd);
        return new PathJobSetup(folder, txName, rxName, tx, rx, frq, metric, fz, clutter, hd, terrain, udt, tiles);
    }

    static List<string> Tokens(string line)
    {
        var list = new List<string>(); var sb = new StringBuilder(); bool q = false, any = false;
        foreach (char c in line)
        {
            if (c == '"') { q = !q; any = true; continue; }
            if (c == ' ' && !q) { if (any || sb.Length > 0) list.Add(sb.ToString()); sb.Clear(); any = false; continue; }
            sb.Append(c);
        }
        if (any || sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
}

/// <summary>A point SPLAT! lists as an obstruction, recomputed exactly as ObstructionAnalysis finds it.</summary>
public sealed record ObstructionPoint(double Lat, double West, double MilesFromRx, double FeetAmsl, string ReportLine);

/// <summary>One check of the 3D view's numbers against what SPLAT! wrote in the job folder.</summary>
public sealed record SceneCheck(string What, bool Ok, string Detail);

/// <summary>
/// The 3D path view's numbers (stage G8). Along the path everything is SPLAT!'s own arithmetic
/// (GraphHeight, ObstructionAnalysis), so it can be checked against the files SPLAT! wrote; either
/// side of the path the same terrain lookup and curvature correction are applied. Heights are
/// "normalized" as SPLAT!'s -H graph does: Earth's curvature taken out, so the line of sight is a
/// straight line from one antenna top to the other.
/// </summary>
public sealed class PathScene
{
    public PathJobSetup Job { get; }
    public SdfTerrain Terrain { get; }
    readonly Func<double, double, double> _el;
    readonly SplatPath _path;               // ReadPath(RX, TX), as GraphHeight reads it
    readonly double _b, _refangle, _nm, _nb, _lambda, _d;
    const double R = SplatGeo.EarthRadius;

    PathScene(PathJobSetup job, SdfTerrain terrain)
    {
        Job = job; Terrain = terrain; _el = terrain.Elevation;
        _path = SplatGeo.ReadPath(job.Rx, job.Tx, job.Hd, _el);
        _refangle = SplatGeo.ElevationAngle(job.Rx, job.Tx, _el);
        _b = _el(job.Rx.Lat, job.Rx.Lon) + job.Rx.Alt + R;
        double ed = _el(job.Rx.Lat, job.Rx.Lon), es = _el(job.Tx.Lat, job.Tx.Lon);
        _nb = -job.Rx.Alt - ed;
        _nm = (-job.Tx.Alt - es - _nb) / (_path.Distance[_path.Length - 1]);
        _lambda = 9.8425e8 / (job.FrequencyMhz * 1e6);
        _d = 5280.0 * _path.Distance[_path.Length - 1];
    }

    /// <summary>Loads the job's terrain (and the strip's, if given) and its user terrain, as SPLAT! did.</summary>
    public static PathScene Load(PathJobSetup job, IEnumerable<string>? extraTiles = null)
    {
        var tiles = job.SplatTiles.Concat(extraTiles ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var terrain = SdfTerrain.Load(job.TerrainFolder, tiles, job.Hd);
        var lost = terrain.Missing.Intersect(job.SplatTiles, StringComparer.OrdinalIgnoreCase).ToList();
        if (lost.Count > 0) throw new FileNotFoundException($"The terrain this path was analyzed with is no longer in {job.TerrainFolder} ({string.Join(", ", lost)}). Run the path again.");
        if (job.UdtFile != null) terrain.ApplyUserTerrain(job.UdtFile, job.SplatTiles);
        return new PathScene(job, terrain);
    }

    public double LengthMiles => _path.Distance[_path.Length - 1];
    public double RxGround => _el(Job.Rx.Lat, Job.Rx.Lon);
    public double TxGround => _el(Job.Tx.Lat, Job.Tx.Lon);
    public double RxTop => RxGround + Job.Rx.Alt;
    public double TxTop => TxGround + Job.Tx.Alt;

    // GraphHeight's c: distance from Earth's center to the line of sight, at angle cangle from the receiver.
    double LosRadius(double cangle) => _b * Math.Sin(_refangle * SplatGeo.DEG2RAD + SplatGeo.HALFPI) / Math.Sin(SplatGeo.HALFPI - _refangle * SplatGeo.DEG2RAD - cangle);
    double Cangle(double lat, double west) => 5280.0 * SplatGeo.Distance(Job.Rx.Lat, Job.Rx.Lon, lat, west) / R;
    double Norm(double miles) => -(_nm * miles) - _nb;   // the -H graph's r: line of sight, antenna top to antenna top

    // ---------- SPLAT!'s graph data, recomputed (for the checks) ----------

    static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
    string Row(double miles, double feet) => Job.Metric ? $"{F(SplatGeo.KmPerMile * miles)}\t{F(SplatGeo.MetersPerFoot * feet)}" : $"{F(miles)}\t{F(feet)}";

    /// <summary>GraphTerrain's profile.gp (the -p graph): elevation along ReadPath(RX, TX).</summary>
    public List<string> TerrainProfileGp()
    {
        var l = new List<string>();
        for (int x = 0; x < _path.Length; x++) l.Add(Row(_path.Distance[x], _path.Elevation[x]));
        return l;
    }

    /// <summary>GraphHeight's data files (the -h graph, not normalized, with the Fresnel zone): file name to lines.</summary>
    public Dictionary<string, List<string>> HeightGraphGp()
    {
        var files = new[] { "profile.gp", "reference.gp", "curvature.gp", "fresnel.gp", "fresnel_pt_6.gp" }.ToDictionary(f => f, _ => new List<string>());
        bool fres = Job.FrequencyMhz >= 20.0 && Job.FrequencyMhz <= 20000.0;
        for (int x = 0; x < _path.Length - 1; x++)
        {
            double terrain = _el(_path.Lat[x], _path.Lon[x]);
            if (x == 0) terrain += Job.Rx.Alt;
            double a = terrain + R;
            double c = LosRadius(Cangle(_path.Lat[x], _path.Lon[x]));
            double height = a - c, f_zone = 0, fpt6 = 0, r = 0.0;
            if (fres)
            {
                double d1 = 5280.0 * _path.Distance[x];
                f_zone = -1.0 * Math.Sqrt(_lambda * d1 * (_d - d1) / _d);
                fpt6 = f_zone * Job.FresnelClearance;
            }
            files["profile.gp"].Add(Row(_path.Distance[x], height));
            files["reference.gp"].Add(Row(_path.Distance[x], r));
            files["curvature.gp"].Add(Row(_path.Distance[x], height - terrain));
            if (fres) { files["fresnel.gp"].Add(Row(_path.Distance[x], f_zone)); files["fresnel_pt_6.gp"].Add(Row(_path.Distance[x], fpt6)); }
        }
        double last = _path.Distance[_path.Length - 1];
        files["profile.gp"].Add(Row(last, 0.0));
        files["reference.gp"].Add(Row(last, 0.0));
        if (fres) { files["fresnel.gp"].Add(Row(last, 0.0)); files["fresnel_pt_6.gp"].Add(Row(last, 0.0)); }
        else { files.Remove("fresnel.gp"); files.Remove("fresnel_pt_6.gp"); }
        return files;
    }

    /// <summary>ObstructionAnalysis, line-of-sight part: the points SPLAT! lists, in its order (from the receiver outward).</summary>
    public List<ObstructionPoint> Obstructions()
    {
        var tx = Job.Tx; var rx = Job.Rx;
        var path = SplatGeo.ReadPath(tx, rx, Job.Hd, _el);
        double h_r = _el(rx.Lat, rx.Lon) + rx.Alt + R;
        double h_t = _el(tx.Lat, tx.Lon) + tx.Alt + R;
        double d_tx = 5280.0 * SplatGeo.Distance(rx, tx);
        double cos_tx_angle = ((h_r * h_r) + (d_tx * d_tx) - (h_t * h_t)) / (2.0 * h_r * d_tx);
        var list = new List<ObstructionPoint>();
        var ci = CultureInfo.InvariantCulture;
        for (int x = path.Length - 1; x > 0; x--)
        {
            double lat = path.Lat[x], lon = path.Lon[x];
            double h_x = _el(lat, lon) + R + Job.ClutterFeet;
            double d_x = 5280.0 * SplatGeo.Distance(rx.Lat, rx.Lon, lat, lon);
            double cos_test_angle = ((h_r * h_r) + (d_x * d_x) - (h_x * h_x)) / (2.0 * h_r * d_x);
            if (cos_tx_angle > cos_test_angle)
            {
                string ns = lat >= 0.0 ? "N" : "S";
                double la = lat >= 0.0 ? lat : -lat;
                string text = Job.Metric
                    ? string.Format(ci, "   {0,8:F4} {1},{2,9:F4} W, {3,5:F2} kilometers, {4,6:F2} meters AMSL", la, ns, lon, SplatGeo.KmPerMile * (d_x / 5280.0), SplatGeo.MetersPerFoot * (h_x - R))
                    : string.Format(ci, "   {0,8:F4} {1},{2,9:F4} W, {3,5:F2} miles, {4,6:F2} feet AMSL", la, ns, lon, d_x / 5280.0, h_x - R);
                list.Add(new ObstructionPoint(lat, lon, d_x / 5280.0, h_x - R, text));
            }
            while (cos_tx_angle > cos_test_angle)
            {
                h_r += 1;
                cos_test_angle = ((h_r * h_r) + (d_x * d_x) - (h_x * h_x)) / (2.0 * h_r * d_x);
                cos_tx_angle = ((h_r * h_r) + (d_tx * d_tx) - (h_t * h_t)) / (2.0 * h_r * d_tx);
            }
        }
        return list;
    }

    /// <summary>
    /// The checks: this code's terrain, curvature, Fresnel zones and obstruction list against the files
    /// SPLAT! wrote in the job folder. Graph data must agree to 0.001 (feet, or meters with -metric):
    /// a thousand times finer than anything the view can show, and a hundredth of a foot is caught. Most
    /// values are identical; the rest differ in the last printed digit (as Windows and Linux SPLAT! do, D12)
    /// or, rarely, by a few millionths of a foot within a fraction of a mile of the receiver, where SPLAT!'s
    /// arc-cosine distance is very sensitive to the math library's last bits. The counts are reported.
    /// The obstruction list must be identical, character for character.
    /// </summary>
    public const double GraphTolerance = 0.001;

    public List<SceneCheck> Verify()
    {
        var checks = new List<SceneCheck>();
        void Compare(string what, string file, List<string> mine)
        {
            if (!File.Exists(file)) { checks.Add(new SceneCheck(what, false, $"{file} is missing")); return; }
            var theirs = File.ReadAllLines(file, Encoding.Latin1).Where(l => l.Length > 0).ToList();
            if (theirs.Count != mine.Count) { checks.Add(new SceneCheck(what, false, $"{mine.Count} lines here, {theirs.Count} in SPLAT!'s file")); return; }
            int exact = 0, lastDigit = 0, small = 0;
            double worst = 0;
            string? bad = null;
            for (int i = 0; i < mine.Count; i++)
            {
                if (mine[i] == theirs[i]) { exact++; continue; }
                var a = mine[i].Split('\t'); var b = theirs[i].Split('\t');
                double diff = a.Length != b.Length ? double.PositiveInfinity
                            : a.Zip(b).Max(p => Math.Abs(double.Parse(p.First, CultureInfo.InvariantCulture) - double.Parse(p.Second, CultureInfo.InvariantCulture)));
                worst = Math.Max(worst, diff);
                if (diff <= 1.0000001e-6) lastDigit++;
                else if (diff <= GraphTolerance) small++;
                else bad ??= $"line {i + 1}: here \"{mine[i]}\", SPLAT! \"{theirs[i]}\"";
            }
            string counts = $"{mine.Count} points: {exact} identical, {lastDigit} differ in the last printed digit" +
                            (small > 0 ? $", {small} by up to {worst.ToString("0.000000", CultureInfo.InvariantCulture)}" : "");
            checks.Add(new SceneCheck(what, bad == null, bad ?? counts));
        }
        Compare("terrain along the path (graph-profile\\profile.gp)", Path.Combine(Job.Folder, "graph-profile", "profile.gp"), TerrainProfileGp());
        foreach (var (name, lines) in HeightGraphGp())
            Compare($"height graph {name} (curvature, line of sight, Fresnel)", Path.Combine(Job.Folder, "graph-height", name), lines);

        string? report = Directory.GetFiles(Job.Folder, "*-to-*.txt").FirstOrDefault();
        if (report == null) checks.Add(new SceneCheck("obstruction list", false, "the path report is missing"));
        else
        {
            var theirs = File.ReadAllLines(report, Encoding.Latin1).Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s+\d+\.\d{4} [NS],\s*\d+\.\d{4} W, ")).ToList();
            var mine = Obstructions().Select(o => o.ReportLine).ToList();
            int firstDiff = Enumerable.Range(0, Math.Min(mine.Count, theirs.Count)).FirstOrDefault(i => mine[i] != theirs[i], -1);
            bool ok = mine.Count == theirs.Count && firstDiff < 0;
            checks.Add(new SceneCheck("obstruction list (the report's lines, character for character)", ok,
                ok ? $"{mine.Count} point(s), identical" : $"{mine.Count} here, {theirs.Count} in the report" + (firstDiff >= 0 ? $"; first difference: \"{mine[firstDiff]}\" vs \"{theirs[firstDiff]}\"" : "")));
            var text = File.ReadAllText(report, Encoding.Latin1);
            foreach (var (who, top) in new[] { (Job.TxName, TxTop), (Job.RxName, RxTop) })
            {
                string want = Job.Metric ? (SplatGeo.MetersPerFoot * top).ToString("F2", CultureInfo.InvariantCulture) + " meters AMSL"
                                         : top.ToString("F2", CultureInfo.InvariantCulture) + " feet AMSL";
                checks.Add(new SceneCheck($"{who} antenna top ({want}) as in the report", text.Contains(" / " + want), want));
            }
        }
        return checks;
    }

    // ---------- the 3D strip ----------

    /// <summary>The terrain tiles a strip this wide (miles either side) needs, beyond the job's own.</summary>
    public static List<string> StripTiles(PathJobSetup job, double halfWidthMiles)
    {
        double pad = halfWidthMiles / 69.0 + 0.01;
        double s = Math.Min(job.Tx.Lat, job.Rx.Lat) - pad, n = Math.Max(job.Tx.Lat, job.Rx.Lat) + pad;
        double coslat = Math.Max(0.2, Math.Cos(Math.Max(Math.Abs(s), Math.Abs(n)) * Math.PI / 180));
        double wpad = halfWidthMiles / (69.0 * coslat) + 0.01;
        double w0 = Math.Min(job.Tx.Lon, job.Rx.Lon), w1 = Math.Max(job.Tx.Lon, job.Rx.Lon);
        if (w1 - w0 > 180) (w0, w1) = (w1, w0 + 360);
        return SdfTerrain.TilesForBox(s, n, w0 - wpad, w1 + wpad, job.Hd);
    }

    /// <summary>Builds what the 3D page draws, as JSON. At most about maxVertices terrain points: long or HD paths are thinned evenly.</summary>
    public string BuildJson(double halfWidthMiles, int maxVertices = 300_000)
    {
        int L = _path.Length;
        double ds = L > 1 ? _path.Distance[1] : 0.01;
        int m = Math.Max(1, (int)Math.Ceiling(halfWidthMiles / ds));
        int k = 1;
        while ((L / k + 2) * (2 * (m / k + 1) + 1) > maxVertices) k++;
        var rows = new List<int>();
        for (int i = 0; i < L; i += k) rows.Add(i);
        if (rows[^1] != L - 1) rows.Add(L - 1);
        int half = (int)Math.Ceiling((double)m / k);
        var cross = Enumerable.Range(-half, 2 * half + 1).Select(j => j * k * ds).ToArray();

        int nr = rows.Count, nc = cross.Length;
        var z = new float[nr * nc]; var latOff = new float[nr * nc]; var lonOff = new float[nr * nc];
        double lat0 = Job.Rx.Lat, lon0 = SplatGeo.East(Job.Rx.Lon);
        double south = 90, north = -90, west = 180, east = -180;
        int noData = 0;
        for (int ri = 0; ri < nr; ri++)
        {
            int i = rows[ri];
            double plat = _path.Lat[i], plon = _path.Lon[i];
            double bearing = i < L - 1 ? SplatGeo.Azimuth(plat, plon, Job.Tx.Lat, Job.Tx.Lon) : SplatGeo.Azimuth(_path.Lat[i - 1], _path.Lon[i - 1], Job.Tx.Lat, Job.Tx.Lon);
            double c = LosRadius(Cangle(plat, plon)), r = Norm(_path.Distance[i]);
            for (int ci = 0; ci < nc; ci++)
            {
                double t = cross[ci];
                var (la, lo) = t == 0 ? (plat, plon) : SplatGeo.Destination(plat, plon, bearing + 90, t);
                double e = _el(la, lo);
                int idx = ri * nc + ci;
                if (double.IsNaN(e)) { z[idx] = float.NaN; noData++; }
                else { double tf = 5280.0 * t; z[idx] = (float)(e + R - c + r - tf * tf / (2 * R)); }
                double le = SplatGeo.East(lo);
                latOff[idx] = (float)(la - lat0); lonOff[idx] = (float)(le - lon0);
                south = Math.Min(south, la); north = Math.Max(north, la); west = Math.Min(west, le); east = Math.Max(east, le);
            }
        }

        // The ground right under the path, at full resolution, and where it rises above the line of sight.
        var profS = new double[L]; var profZ = new double[L]; var profH = new double[L];
        for (int i = 0; i < L; i++)
        {
            double e = _el(_path.Lat[i], _path.Lon[i]);
            double h = e + R - LosRadius(Cangle(_path.Lat[i], _path.Lon[i]));
            profS[i] = _path.Distance[i]; profH[i] = h; profZ[i] = h + Norm(_path.Distance[i]);
        }
        profH[0] = -Job.Rx.Alt; profH[L - 1] = -Job.Tx.Alt;   // under each antenna, by definition
        var fresnel = rows.Select(i => { double d1 = 5280.0 * _path.Distance[i]; return Math.Sqrt(Math.Max(0, _lambda * d1 * (_d - d1) / _d)); }).ToArray();

        var obs = Obstructions();
        var obsOut = obs.Select(o =>
        {
            double e = _el(o.Lat, o.West);
            double zz = e + R - LosRadius(Cangle(o.Lat, o.West)) + Norm(o.MilesFromRx);
            return new { s = o.MilesFromRx, z = zz, amsl = o.FeetAmsl, line = o.ReportLine.Trim() };
        }).ToArray();

        var notes = new List<string>();
        if (Job.ClutterFeet > 0)
            notes.Add($"SPLAT! raised the ground by {Len(Job.ClutterFeet)} for ground clutter when it looked for obstructions; the view shows the bare ground.");
        if (Job.UdtFile != null) notes.Add("Includes your user-defined terrain (obstacles file), as SPLAT! used it.");
        if (noData > 0) notes.Add($"No terrain was available for part of the strip ({string.Join(", ", Terrain.Missing)}); that part is left out.");

        var payload = new
        {
            version = 1,
            rx = new { name = Job.RxName, ground = RxGround, top = RxTop, alt = (double)Job.Rx.Alt },
            tx = new { name = Job.TxName, ground = TxGround, top = TxTop, alt = (double)Job.Tx.Alt },
            metric = Job.Metric, lengthMiles = LengthMiles, halfWidthMiles, thinned = k,
            frequencyMhz = Job.FrequencyMhz, fresnelClearance = Job.FresnelClearance,
            rows = rows.Select(i => _path.Distance[i]).ToArray(), cross, fresnel,
            z = B64(z), latOff = B64(latOff), lonOff = B64(lonOff), lat0, lon0,
            bbox = new { south, north, west, east },
            profile = new { s = profS, z = profZ, h = profH },
            obstructions = obsOut,
            summary = Summary(obs, profS, profH),
            notes,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    }

    static string B64(float[] a) { var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); return Convert.ToBase64String(b); }

    string Len(double feet) => Job.Metric ? $"{feet * SplatGeo.MetersPerFoot:0} m" : $"{feet:0} ft";
    string Dist(double miles) => Job.Metric ? $"{miles * SplatGeo.KmPerMile:0.00} km" : $"{miles:0.00} mi";

    /// <summary>One plain sentence: SPLAT!'s verdict (its obstruction list) and where the ground rises into the line of sight or Fresnel zone.</summary>
    public string Summary(List<ObstructionPoint> obs, double[] s, double[] h)
    {
        var ci = CultureInfo.InvariantCulture;
        if (obs.Count > 0)
        {
            double near = obs.Min(o => o.MilesFromRx), far = obs.Max(o => o.MilesFromRx);
            double top = h.Skip(1).Take(h.Length - 2).DefaultIfEmpty(0).Max();
            string where = near == far ? $"{Dist(near)} from {Job.RxName}" : $"{Dist(near)} to {Dist(far)} from {Job.RxName}";
            return string.Format(ci, "Blocked: SPLAT! finds terrain in the way {0}. Along the path the ground rises up to {1} above the line of sight.",
                                 where, Len(Math.Max(0, top)));
        }
        double worst = double.MaxValue; int at = -1;
        for (int i = 1; i < s.Length - 1; i++)
        {
            double d1 = 5280.0 * s[i];
            double zone = Job.FresnelClearance * Math.Sqrt(Math.Max(0, _lambda * d1 * (_d - d1) / _d));
            double clearance = -h[i] - zone;   // how far the ground is below the zone's lower edge
            if (clearance < worst) { worst = clearance; at = i; }
        }
        string pct = (Job.FresnelClearance * 100).ToString("0.#", ci) + " %";
        if (at > 0 && worst < 0)
            return $"Line of sight clear, but the ground reaches into the {pct} Fresnel zone {Dist(s[at])} from {Job.RxName} (by up to {Len(-worst)}).";
        return $"Clear: the line of sight and the {pct} Fresnel zone are clear of the terrain.";
    }

    // ---------- Google Earth ----------

    /// <summary>
    /// The additions to the path's Google Earth file: the true line of sight (each point at SPLAT!'s
    /// line-of-sight height above sea level, which includes its 4/3-Earth allowance for the
    /// atmosphere's bending), both antennas as masts, SPLAT!'s obstruction points, and views from
    /// each antenna. Altitudes in meters.
    /// </summary>
    public (string Folder, string RxCamera) KmlExtras()
    {
        var ci = CultureInfo.InvariantCulture;
        string E(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        string Coord(double lat, double west, double feet) => string.Format(ci, "{0:0.000000},{1:0.000000},{2:0.00}", SplatGeo.East(west), lat, feet * SplatGeo.MetersPerFoot);
        var sb = new StringBuilder();
        sb.Append("<Folder>\n<name>3D path (SPLAT! for Windows)</name>\n<open>1</open>\n");
        sb.Append("<description>The line of sight at its true height, the antennas, and the points where SPLAT! finds terrain in the way. Google Earth's terrain differs slightly from SPLAT!'s, so the line can look a few feet off the ground.</description>\n");
        // the line of sight
        int L = _path.Length, step = Math.Max(1, L / 200);
        sb.Append("<Placemark>\n<name>Line of sight (true height)</name>\n<Style><LineStyle><color>ff00ffff</color><width>3</width></LineStyle></Style>\n");
        sb.Append("<LineString>\n<altitudeMode>absolute</altitudeMode>\n<coordinates>\n");
        for (int i = 0; i < L; i += step)
            sb.Append(Coord(_path.Lat[i], _path.Lon[i], i == 0 ? RxTop : LosRadius(Cangle(_path.Lat[i], _path.Lon[i])) - R)).Append('\n');
        sb.Append(Coord(Job.Tx.Lat, Job.Tx.Lon, TxTop)).Append('\n');
        sb.Append("</coordinates>\n</LineString>\n</Placemark>\n");
        // masts
        foreach (var (name, site, top) in new[] { (Job.TxName, Job.Tx, TxTop), (Job.RxName, Job.Rx, RxTop) })
            sb.Append("<Placemark>\n<name>").Append(E($"{name} antenna, {Len(top)} above sea level")).Append("</name>\n")
              .Append("<Style><IconStyle><scale>0.6</scale></IconStyle><LineStyle><color>ffffffff</color><width>2</width></LineStyle></Style>\n")
              .Append("<Point>\n<extrude>1</extrude>\n<altitudeMode>absolute</altitudeMode>\n<coordinates>")
              .Append(Coord(site.Lat, site.Lon, top)).Append("</coordinates>\n</Point>\n</Placemark>\n");
        // obstructions
        var obs = Obstructions();
        if (obs.Count > 0)
        {
            sb.Append("<Folder>\n<name>").Append(E($"Terrain in the way ({obs.Count} point{(obs.Count == 1 ? "" : "s")} SPLAT! lists)")).Append("</name>\n");
            foreach (var o in obs)
                sb.Append("<Placemark>\n<name>").Append(E($"{Dist(o.MilesFromRx)} from {Job.RxName}, ground {Len(o.FeetAmsl)} above sea level")).Append("</name>\n")
                  .Append("<description>").Append(E("SPLAT!: " + o.ReportLine.Trim())).Append("</description>\n")
                  .Append("<Style><IconStyle><color>ff0000ff</color></IconStyle></Style>\n<Point>\n<coordinates>")
                  .Append(Coord(o.Lat, o.West, 0)).Append("</coordinates>\n</Point>\n</Placemark>\n");
            sb.Append("</Folder>\n");
        }
        // views from each antenna (double-click the folder to fly there)
        string rxCam = Camera(Job.Rx, RxTop, SplatGeo.Azimuth(Job.Rx, Job.Tx), SplatGeo.ElevationAngle(Job.Rx, Job.Tx, _el));
        string txCam = Camera(Job.Tx, TxTop, SplatGeo.Azimuth(Job.Tx, Job.Rx), SplatGeo.ElevationAngle(Job.Tx, Job.Rx, _el));
        sb.Append("<Folder>\n<name>").Append(E($"View from {Job.RxName}'s antenna")).Append("</name>\n").Append(rxCam).Append("</Folder>\n");
        sb.Append("<Folder>\n<name>").Append(E($"View from {Job.TxName}'s antenna")).Append("</name>\n").Append(txCam).Append("</Folder>\n");
        sb.Append("</Folder>\n");
        return (sb.ToString(), rxCam);
    }

    static string Camera(SplatSite at, double topFeet, double heading, double elevationDeg)
    {
        var ci = CultureInfo.InvariantCulture;
        return string.Format(ci, "<Camera>\n<longitude>{0:0.000000}</longitude>\n<latitude>{1:0.000000}</latitude>\n<altitude>{2:0.0}</altitude>\n" +
                                 "<heading>{3:0.00}</heading>\n<tilt>{4:0.00}</tilt>\n<roll>0</roll>\n<altitudeMode>absolute</altitudeMode>\n</Camera>\n",
                             SplatGeo.East(at.Lon), at.Lat, topFeet * SplatGeo.MetersPerFoot + 2.0, heading, Math.Clamp(90.0 + elevationDeg, 0, 180));
    }
}
