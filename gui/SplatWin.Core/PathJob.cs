using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SplatWin.Core;

public enum PathModel { Itwom, LongleyRice }

/// <summary>The graphs the app draws. Each is a separate SPLAT! run in its own folder, because
/// SPLAT! reuses the same data file names (profile.gp, ...) for every graph.</summary>
public enum GraphKind { Profile, Elevation, Height, Loss }

/// <summary>Options for a path analysis. Clutter is in meters when Metric is on (as SPLAT! reads -gc).
/// UdtFile is a user-defined terrain file (-udt) to copy into the job; Kml asks SPLAT! for its path KML (-kml, report run only).</summary>
public sealed record PathOptions(PathModel Model = PathModel.Itwom, bool Metric = false, double FresnelPercent = 60,
                                 double Clutter = 0, bool Hd = false, string? UdtFile = null, bool Kml = true);

public sealed record SplatRun(string Folder, IReadOnlyList<string> Args, int ExitCode, string Output);

/// <summary>What a finished path job produced. A graph that couldn't be read has an error text instead.</summary>
public sealed record PathResult(string Folder, string ReportPath, string ReportText, PathSummary Summary,
                                IReadOnlyDictionary<GraphKind, GraphData> Graphs,
                                IReadOnlyDictionary<GraphKind, string> GraphErrors, IReadOnlyList<SplatRun> Runs)
{
    /// <summary>The Google Earth file made from SPLAT!'s path KML (null when -kml wasn't asked for).</summary>
    public string? KmzPath { get; init; }
}

/// <summary>
/// A point-to-point job. It writes the stations' .qth files and the transmitter's .lrp into a new
/// job folder, runs the unchanged SPLAT! engine there (once for the report, then once per graph),
/// and reads what SPLAT! wrote. The exact commands are saved in run-again.cmd in the folder.
/// </summary>
public sealed class PathJob
{
    public const string ReRunFile = "run-again.cmd";
    public const string LogFile = "splat-log.txt";
    static readonly char[] BadForSplat = { ':', '?', '"', '<', '>', '|' };

    public static readonly IReadOnlyDictionary<GraphKind, (string Flag, string File, string Folder)> GraphRuns =
        new Dictionary<GraphKind, (string, string, string)>
        {
            [GraphKind.Profile] = ("-p", "profile.png", "graph-profile"),
            [GraphKind.Elevation] = ("-e", "elevation.png", "graph-elevation"),
            [GraphKind.Height] = ("-h", "height.png", "graph-height"),
            [GraphKind.Loss] = ("-l", "loss.png", "graph-loss"),
        };

    public Station Tx { get; }
    public Station Rx { get; }
    public RadioSettings Radio { get; }
    public PathOptions Options { get; }
    public string TerrainFolder { get; }
    public string EngineFolder { get; }
    public string Folder { get; }
    public string TxFile { get; }
    public string RxFile { get; }
    public IReadOnlyList<string> Tiles { get; }

    string Exe => Path.Combine(EngineFolder, Options.Hd ? "splat-hd.exe" : "splat.exe");

    PathJob(Station tx, Station rx, RadioSettings radio, PathOptions o, string terrain, string engine, string folder, IReadOnlyList<string> tiles)
    {
        Tx = tx; Rx = rx; Radio = radio.Clone(); Options = o; TerrainFolder = terrain; EngineFolder = engine; Folder = folder; Tiles = tiles;
        // Site files are lower case, as in SPLAT!'s documentation (k4hta.qth): SPLAT! names some of
        // its outputs after them (k4hta.scf, and inside it), so the app's match the command line's.
        TxFile = SafeName(tx.Name, "tx").ToLowerInvariant();
        RxFile = SafeName(rx.Name, "rx").ToLowerInvariant();
        if (string.Equals(TxFile, RxFile, StringComparison.OrdinalIgnoreCase)) RxFile += "_rx";
    }

    /// <summary>A .qth base name SPLAT! handles: letters, digits, '-' and '_' only (SPLAT! cuts .lrp names at the first '.').</summary>
    public static string SafeName(string name, string fallback)
    {
        string s = new string(name.Trim().Select(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' ? c : '_').ToArray());
        return s.Trim('_').Length == 0 ? fallback : s;
    }

    /// <summary>SPLAT!'s report file name: "tx-to-rx.txt" with space, \, * and / made '_' (PathReport).</summary>
    public static string ReportName(string tx, string rx)
    {
        var sb = new StringBuilder($"{tx}-to-{rx}.txt");
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] is ' ' or (char)17 or '\\' or '*' or '/') sb[i] = '_';
        return sb.ToString();
    }

    /// <summary>Why this job can't run, or null.</summary>
    public static string? Problem(Station? tx, Station? rx, RadioSettings radio, PathOptions o, string? terrain, string engine, bool checkTerrain = true)
    {
        if (tx == null || rx == null) return "Choose a transmitter and a receiver.";
        if (ReferenceEquals(tx, rx) || (tx.Lat == rx.Lat && tx.Lon == rx.Lon)) return "The transmitter and the receiver are at the same place. Choose two different stations.";
        foreach (var s in new[] { tx, rx })
        {
            if (s.Problem(Array.Empty<Station>()) is { } p) return $"{s.Name}: {p}";
            if (s.Name.IndexOfAny(BadForSplat) >= 0)
                return $"SPLAT! can't name its report after \"{s.Name.Trim()}\" (Windows doesn't allow : ? \" < > | in file names). Rename the station.";
        }
        if (radio.Problem() is { } rp) return rp;
        if (tx.Pattern?.Problem() is { } pp) return $"{tx.Name}'s antenna pattern: {pp}";
        if (o.UdtFile != null && UserTerrain.Problem(o.UdtFile, TilesFor(tx, rx, o), o.Hd) is { } up) return up;
        if (!(o.FresnelPercent >= 1 && o.FresnelPercent <= 100)) return "The Fresnel zone clearance should be between 1% and 100% (60% is usual).";
        if (!(o.Clutter >= 0 && o.Clutter <= 1000)) return "The ground clutter height should be between 0 and 1000.";
        string exe = Path.Combine(engine, o.Hd ? "splat-hd.exe" : "splat.exe");
        if (!File.Exists(exe)) return $"The SPLAT! program wasn't found at {exe}.";
        if (terrain != null && terrain.Length > 200) return "The terrain folder's path is too long for SPLAT! (at most 200 characters). Move the terrain to a shorter path.";
        return checkTerrain ? Terrain.Problem(terrain, TilesFor(tx, rx, o), o.Hd) : null;
    }

    public static List<string> TilesFor(Station tx, Station rx, PathOptions o) => Terrain.TilesFor(new[] { (tx.Lat, tx.Lon), (rx.Lat, rx.Lon) }, o.Hd);

    /// <summary>Checks the job, makes its folder under <paramref name="jobsRoot"/> and writes the inputs.</summary>
    public static PathJob Create(string jobsRoot, Station tx, Station rx, RadioSettings radio, PathOptions o, string terrain, string engine, DateTime? when = null)
    {
        if (Problem(tx, rx, radio, o, terrain, engine) is { } p) throw new InvalidOperationException(p);
        var tiles = Terrain.TilesFor(new[] { (tx.Lat, tx.Lon), (rx.Lat, rx.Lon) }, o.Hd);
        string stamp = (when ?? DateTime.Now).ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        string baseName = $"{stamp} {SafeName(tx.Name, "tx")} to {SafeName(rx.Name, "rx")}";
        string folder = Path.Combine(jobsRoot, baseName);
        for (int n = 2; Directory.Exists(folder); n++) folder = Path.Combine(jobsRoot, $"{baseName} ({n})");
        Directory.CreateDirectory(folder);
        var job = new PathJob(tx, rx, radio, o, Path.GetFullPath(terrain), Path.GetFullPath(engine), folder, tiles);
        job.WriteInputs(folder);
        foreach (var g in GraphRuns.Values) job.WriteInputs(Directory.CreateDirectory(Path.Combine(folder, g.Folder)).FullName);
        File.WriteAllText(Path.Combine(folder, ReRunFile), job.ReRunScript(), Encoding.ASCII);
        return job;
    }

    void WriteInputs(string dir)
    {
        File.WriteAllText(Path.Combine(dir, TxFile + ".qth"), QthFile.Format(Tx.ToQth() with { Name = Tx.Name.Trim() }), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, RxFile + ".qth"), QthFile.Format(Rx.ToQth() with { Name = Rx.Name.Trim() }), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, TxFile + ".lrp"), LrpFile.Format(Radio), Encoding.ASCII);
        Tx.Pattern?.WriteFor(dir, TxFile);
        if (Options.UdtFile != null) File.Copy(Options.UdtFile, Path.Combine(dir, UserTerrain.JobFileName), overwrite: true);
    }

    /// <summary>SPLAT!'s path KML name: the report's, ending .kml.</summary>
    public string KmlName => Path.ChangeExtension(ReportName(Tx.Name.Trim(), Rx.Name.Trim()), ".kml");

    /// <summary>The SPLAT! arguments for the report run (graph = null) or one graph run.</summary>
    public List<string> Args(GraphKind? graph)
    {
        var a = new List<string> { "-t", TxFile, "-r", RxFile, "-d", TerrainFolder };
        if (Options.Model == PathModel.LongleyRice) a.Add("-olditm");
        if (Options.Metric) a.Add("-metric");
        if (Options.FresnelPercent != 60) a.AddRange(new[] { "-fz", Options.FresnelPercent.ToString("0.###", CultureInfo.InvariantCulture) });
        if (Options.Clutter > 0) a.AddRange(new[] { "-gc", Options.Clutter.ToString("0.###", CultureInfo.InvariantCulture) });
        if (Options.UdtFile != null) a.AddRange(new[] { "-udt", UserTerrain.JobFileName });
        if (graph == null && Options.Kml) a.Add("-kml");
        if (graph is { } g) a.AddRange(new[] { GraphRuns[g].Flag, GraphRuns[g].File, "-gpsav" });
        return a;
    }

    static string Quote(string s) => s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_.:\\/".Contains(c)) ? s : "\"" + s + "\"";

    string ReRunScript()
    {
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append($"rem SPLAT! for Windows path job: {SafeName(Tx.Name, "tx")} to {SafeName(Rx.Name, "rx")}.\r\n");
        sb.Append("rem Runs exactly the SPLAT! commands the app ran, from this folder. Graph pictures need gnuplot on PATH;\r\n");
        sb.Append("rem without it SPLAT! still writes the graph data (*.gp), which is what the app draws.\r\n");
        sb.Append("cd /d \"%~dp0\"\r\n");
        sb.Append($"{Quote(Exe)} {string.Join(" ", Args(null).Select(Quote))}\r\n");
        foreach (var (kind, g) in GraphRuns)
            sb.Append($"pushd {g.Folder} && {Quote(Exe)} {string.Join(" ", Args(kind).Select(Quote))} & popd\r\n");
        return sb.ToString();
    }

    /// <summary>Runs SPLAT! for the report and each graph, then reads the results.</summary>
    public async Task<PathResult> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var runs = new List<SplatRun>();
        progress?.Report("Running SPLAT! for the path report…");
        runs.Add(await RunSplatAsync(Folder, Args(null), ct));
        WriteLog(runs);
        string report = Path.Combine(Folder, ReportName(Tx.Name.Trim(), Rx.Name.Trim()));
        if (runs[0].ExitCode != 0 || !File.Exists(report))
            throw new InvalidOperationException($"SPLAT! didn't write the path report (exit code {runs[0].ExitCode}). Its output is in {Path.Combine(Folder, LogFile)}:\n{Tail(runs[0].Output)}");
        string text = File.ReadAllText(report, Encoding.Latin1);
        var summary = PathSummary.Parse(text);
        string? kmz = null;
        if (Options.Kml)
        {
            string kml = Path.Combine(Folder, KmlName);
            if (!File.Exists(kml)) throw new InvalidOperationException($"SPLAT! didn't write its path KML ({KmlName}). Its output is in {Path.Combine(Folder, LogFile)}.");
            kmz = Path.ChangeExtension(kml, ".kmz");
            Kmz.WritePath(kmz, Kmz.ReadSplatKml(kml));
        }

        var graphs = new Dictionary<GraphKind, GraphData>();
        var errors = new Dictionary<GraphKind, string>();
        foreach (var (kind, g) in GraphRuns)
        {
            progress?.Report($"Running SPLAT! for the {Describe(kind)} graph…");
            string dir = Path.Combine(Folder, g.Folder);
            var r = await RunSplatAsync(dir, Args(kind), ct);
            runs.Add(r);
            WriteLog(runs);
            try
            {
                if (r.ExitCode != 0) throw new InvalidOperationException($"SPLAT! stopped with exit code {r.ExitCode}: {Tail(r.Output)}");
                graphs[kind] = GraphData.Load(dir);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
            {
                errors[kind] = ex.Message;
            }
        }
        return new PathResult(Folder, report, text, summary, graphs, errors, runs) { KmzPath = kmz };
    }

    public static string Describe(GraphKind k) => k switch
    {
        GraphKind.Profile => "terrain profile",
        GraphKind.Elevation => "elevation angle",
        GraphKind.Height => "height and Fresnel zone",
        _ => "path loss",
    };

    static string Tail(string s)
    {
        var lines = s.Replace("\r\n", "\n").Trim().Split('\n');
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 6)));
    }

    void WriteLog(List<SplatRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            sb.Append($"> {Path.GetRelativePath(Folder, r.Folder)}: {Exe} {string.Join(" ", r.Args.Select(Quote))}\r\n");
            sb.Append(r.Output.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            sb.Append($"\r\n(exit code {r.ExitCode})\r\n\r\n");
        }
        File.WriteAllText(Path.Combine(Folder, LogFile), sb.ToString(), Encoding.Latin1);
    }

    async Task<SplatRun> RunSplatAsync(string dir, List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Latin1, StandardErrorEncoding = Encoding.Latin1,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Only Windows itself on PATH (SPLAT! is started by its full path): the result never depends on
        // what else this PC has, and gnuplot never runs, even from dist\ with its gnuplot.cmd shim.
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        psi.Environment["PATH"] = string.Join(";", Path.Combine(win, "System32"), win);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("SPLAT! could not be started.");
        var output = new StringBuilder();
        object gate = new();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.Append(e.Data).Append('\n'); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) output.Append(e.Data).Append('\n'); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("SPLAT! took more than 5 minutes on a path and was stopped.");
        }
        p.WaitForExit();   // flushes the output readers
        lock (gate) return new SplatRun(dir, args, p.ExitCode, output.ToString());
    }
}
