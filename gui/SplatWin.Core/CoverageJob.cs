using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplatWin.Core;

/// <summary>What the coverage map shows. All but line of sight use the Longley-Rice/ITWOM model (-L).</summary>
public enum CoverageKind { FieldStrength, PowerDbm, PathLoss, LineOfSight }

/// <summary>
/// Options for a coverage map. Radius and receiver height are in miles and feet, or kilometres
/// and metres when Metric is on (as SPLAT! reads -R, -c and -L). OutputName names the map files;
/// CitiesFile (optional) labels towns, as -s does. NoShading leaves out SPLAT!'s grey terrain shading
/// (-ngs: those areas are white, which the app shows as clear). UdtFile is a user-defined terrain
/// file (-udt) copied into the job.
/// </summary>
public sealed record CoverageOptions(CoverageKind Kind = CoverageKind.FieldStrength, double Radius = 30, double RxHeight = 6,
                                     bool Metric = false, bool Hd = false, bool Smooth = false, PathModel Model = PathModel.Itwom,
                                     string OutputName = "coverage", string? CitiesFile = null, bool NoShading = false, string? UdtFile = null);

/// <summary>What a job folder records about itself (job.json), for the job history.</summary>
public sealed record JobRecord(string Kind, string Transmitter, DateTime Created, CoverageOptions? Coverage, RadioSettings? Radio,
                               string? MapKml, string? ColourKey, bool Finished, string? Note)
{
    /// <summary>The antenna pattern used, if any (what the .az/.el files in the folder came from).</summary>
    public string? Pattern { get; init; }
    public const string FileName = "job.json";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static JobRecord? Read(string folder)
    {
        try { return JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(Path.Combine(folder, FileName)), Json); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The finished coverage jobs under the jobs folder, newest first.</summary>
    public static List<(string Folder, JobRecord Job)> History(string jobsRoot)
    {
        if (!Directory.Exists(jobsRoot)) return new();
        return Directory.GetDirectories(jobsRoot)
            .Select(d => (Folder: d, Job: Read(d)))
            .Where(x => x.Job is { Kind: "coverage", Finished: true, MapKml: not null } && File.Exists(Path.Combine(x.Folder, x.Job.MapKml!)))
            .Select(x => (x.Folder, x.Job!))
            .OrderByDescending(x => x.Item2.Created)
            .ToList();
    }

    public string Describe()
    {
        var c = Coverage!;
        string kind = c.Kind switch
        {
            CoverageKind.FieldStrength => "signal strength",
            CoverageKind.PowerDbm => "received power",
            CoverageKind.PathLoss => "path loss",
            _ => "line of sight",
        };
        return $"{Created:yyyy-MM-dd HH:mm}  {Transmitter}, {c.Radius:0.##} {(c.Metric ? "km" : "mi")}, {kind}, RX {c.RxHeight:0.##} {(c.Metric ? "m" : "ft")}" +
               $"{(c.Hd ? ", HD" : "")}{(Pattern != null ? ", antenna pattern" : "")}{(c.UdtFile != null ? ", obstacles" : "")}";
    }
}

public sealed record CoverageResult(string Folder, string KmlPath, string? ColourKeyPath, string Output, int ExitCode);

/// <summary>
/// A coverage map job: writes the transmitter's .qth/.lrp into a new job folder, runs the unchanged
/// SPLAT! engine there once with -kml, and records the job in job.json. The map files are SPLAT!'s
/// own (&lt;name&gt;.ppm, &lt;name&gt;.kml, and &lt;name&gt;-ck.ppm, its colour key, for signal maps).
/// </summary>
public sealed class CoverageJob
{
    public Station Tx { get; }
    public RadioSettings Radio { get; }
    public CoverageOptions Options { get; }
    public string TerrainFolder { get; }
    public string EngineFolder { get; }
    public string Folder { get; }
    public string TxFile { get; }
    public DateTime Created { get; }

    string Exe => Path.Combine(EngineFolder, Options.Hd ? "splat-hd.exe" : "splat.exe");
    double RadiusMiles => Options.Metric ? Options.Radius / 1.609344 : Options.Radius;

    CoverageJob(Station tx, RadioSettings radio, CoverageOptions o, string terrain, string engine, string folder, DateTime created)
    {
        Tx = tx; Radio = radio.Clone(); Options = o; TerrainFolder = terrain; EngineFolder = engine; Folder = folder; Created = created;
        TxFile = PathJob.SafeName(tx.Name, "tx").ToLowerInvariant();   // lower case, as for path jobs
        // A path-loss map is what SPLAT! draws when the ERP is 0.
        if (o.Kind == CoverageKind.PathLoss) Radio.ErpWatts = 0;
    }

    public static List<string> Tiles(Station tx, CoverageOptions o) =>
        Terrain.TilesForCoverage(tx.Lat, tx.Lon, o.Metric ? o.Radius / 1.609344 : o.Radius, o.Hd);

    /// <summary>Why this job can't be made, apart from terrain (which the app fetches), or null.</summary>
    public static string? Problem(Station? tx, RadioSettings radio, CoverageOptions o, string engine)
    {
        if (tx == null) return "Choose the transmitter.";
        if (tx.Problem(Array.Empty<Station>()) is { } p) return $"{tx.Name}: {p}";
        if (tx.Name.IndexOfAny(new[] { ':', '?', '"', '<', '>', '|' }) >= 0)
            return $"SPLAT! can't name its files after \"{tx.Name.Trim()}\" (Windows doesn't allow : ? \" < > | in file names). Rename the station.";
        string unit = o.Metric ? "km" : "miles";
        double miles = o.Metric ? o.Radius / 1.609344 : o.Radius;
        double max = Terrain.MaxRadiusMiles(tx.Lat, o.Hd);
        if (!(o.Radius > 0)) return $"Enter the radius in {unit} (more than 0).";
        if (miles > max)
            return $"SPLAT!{(o.Hd ? " HD" : "")} can map at most about {(o.Metric ? Math.Floor(max * 1.609344) : max)} {unit} around {tx.Name} (it holds {(o.Hd ? Terrain.HdMaxTiles : Terrain.StandardMaxTiles)} terrain tiles); beyond that it cuts the map short. Use a smaller radius{(o.Hd ? " or standard terrain" : "")}.";
        if (!(o.RxHeight > 0 && o.RxHeight <= 3000)) return $"Enter the receiver antenna height above ground in {(o.Metric ? "metres" : "feet")}.";
        if (o.Kind != CoverageKind.LineOfSight)
        {
            var r = radio.Clone();
            if (o.Kind == CoverageKind.PathLoss && r.ErpWatts <= 0) r.ErpWatts = 1;   // ERP isn't used for a path-loss map
            if (r.Problem() is { } rp) return rp;
        }
        string exe = Path.Combine(engine, o.Hd ? "splat-hd.exe" : "splat.exe");
        if (!File.Exists(exe)) return $"The SPLAT! program wasn't found at {exe}.";
        if (o.CitiesFile != null && !File.Exists(o.CitiesFile)) return $"The cities file {o.CitiesFile} doesn't exist.";
        if (tx.Pattern?.Problem() is { } pp) return $"{tx.Name}'s antenna pattern: {pp}";
        if (o.UdtFile != null && UserTerrain.Problem(o.UdtFile, Tiles(tx, o), o.Hd) is { } up) return up;
        if (o.OutputName.Length == 0 || o.OutputName.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))) return "The map name may use only letters, digits, - and _.";
        return null;
    }

    /// <summary>Makes the job folder under <paramref name="jobsRoot"/> and writes the inputs. Terrain must already be there.</summary>
    public static CoverageJob Create(string jobsRoot, Station tx, RadioSettings radio, CoverageOptions o, string terrain, string engine, DateTime? when = null)
    {
        if (Problem(tx, radio, o, engine) is { } p) throw new InvalidOperationException(p);
        if (Terrain.Problem(terrain, Tiles(tx, o), o.Hd) is { } tp) throw new InvalidOperationException(tp);
        var created = when ?? DateTime.Now;
        string baseName = $"{created.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)} {PathJob.SafeName(tx.Name, "tx")} coverage";
        string folder = Path.Combine(jobsRoot, baseName);
        for (int n = 2; Directory.Exists(folder); n++) folder = Path.Combine(jobsRoot, $"{baseName} ({n})");
        Directory.CreateDirectory(folder);
        var job = new CoverageJob(tx, radio, o, Path.GetFullPath(terrain), Path.GetFullPath(engine), folder, created);
        File.WriteAllText(Path.Combine(folder, job.TxFile + ".qth"), QthFile.Format(tx.ToQth() with { Name = tx.Name.Trim() }), Encoding.ASCII);
        if (o.Kind != CoverageKind.LineOfSight) File.WriteAllText(Path.Combine(folder, job.TxFile + ".lrp"), LrpFile.Format(job.Radio), Encoding.ASCII);
        if (o.CitiesFile != null) File.Copy(o.CitiesFile, Path.Combine(folder, "cities.dat"));
        // The pattern shapes the signal, so it isn't used for line of sight (SPLAT! ignores it there).
        if (o.Kind != CoverageKind.LineOfSight) tx.Pattern?.WriteFor(folder, job.TxFile);
        if (o.UdtFile != null) File.Copy(o.UdtFile, Path.Combine(folder, UserTerrain.JobFileName));
        File.WriteAllText(Path.Combine(folder, PathJob.ReRunFile), job.ReRunScript(), Encoding.ASCII);
        job.Record(false, null, null, "running");
        return job;
    }

    void Record(bool finished, string? kml, string? key, string? note) =>
        File.WriteAllText(Path.Combine(Folder, JobRecord.FileName),
            new JobRecord("coverage", Tx.Name.Trim(), Created, Options, Options.Kind == CoverageKind.LineOfSight ? null : Radio, kml, key, finished, note)
            { Pattern = Options.Kind != CoverageKind.LineOfSight ? Tx.Pattern?.Source : null }.ToJson());

    public List<string> Args()
    {
        var ci = CultureInfo.InvariantCulture;
        var a = new List<string> { "-t", TxFile };
        a.AddRange(new[] { Options.Kind == CoverageKind.LineOfSight ? "-c" : "-L", Options.RxHeight.ToString("0.###", ci) });
        a.AddRange(new[] { "-R", Options.Radius.ToString("0.###", ci), "-d", TerrainFolder, "-o", Options.OutputName + ".ppm" });
        if (Options.CitiesFile != null) a.AddRange(new[] { "-s", "cities.dat" });
        if (Options.UdtFile != null) a.AddRange(new[] { "-udt", UserTerrain.JobFileName });
        if (Options.NoShading) a.Add("-ngs");
        a.Add("-kml");
        if (Options.Kind == CoverageKind.PowerDbm) a.Add("-dbm");
        if (Options.Smooth) a.Add("-sc");
        if (Options.Metric) a.Add("-metric");
        if (Options.Model == PathModel.LongleyRice) a.Add("-olditm");
        return a;
    }

    static string Quote(string s) => s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_.:\\/".Contains(c)) ? s : "\"" + s + "\"";

    string ReRunScript() =>
        "@echo off\r\n" +
        $"rem SPLAT! for Windows coverage job: {PathJob.SafeName(Tx.Name, "tx")}. Runs exactly the SPLAT! command the app ran, from this folder.\r\n" +
        "cd /d \"%~dp0\"\r\n" +
        $"{Quote(Exe)} {string.Join(" ", Args().Select(Quote))}\r\n";

    /// <summary>Runs SPLAT!. Progress gets SPLAT!'s console lines (e.g. "25% to  50% ...").</summary>
    public async Task<CoverageResult> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            WorkingDirectory = Folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Latin1, StandardErrorEncoding = Encoding.Latin1,
        };
        foreach (var x in Args()) psi.ArgumentList.Add(x);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        psi.Environment["PATH"] = string.Join(";", Path.Combine(win, "System32"), win);   // as for path jobs: only Windows
        var output = new StringBuilder();
        object gate = new();
        int code;
        using (var p = Process.Start(psi) ?? throw new InvalidOperationException("SPLAT! could not be started."))
        {
            void Line(string? s)
            {
                if (s == null) return;
                lock (gate) output.Append(s).Append('\n');
                string t = s.Trim();
                if (t.Length > 0) progress?.Report(t.Length > 90 ? t[..90] + "…" : t);
            }
            p.OutputDataReceived += (_, e) => Line(e.Data);
            p.ErrorDataReceived += (_, e) => Line(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            try { await p.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                p.WaitForExit();
                Record(false, null, null, "cancelled");
                throw;
            }
            p.WaitForExit();
            code = p.ExitCode;
        }
        string log;
        lock (gate) log = output.ToString();
        File.WriteAllText(Path.Combine(Folder, PathJob.LogFile),
            $"> {Exe} {string.Join(" ", Args().Select(Quote))}\r\n{log.Replace("\n", "\r\n")}\r\n(exit code {code})\r\n", Encoding.Latin1);
        string kml = Options.OutputName + ".kml", ppm = Options.OutputName + ".ppm", ck = Options.OutputName + "-ck.ppm";
        if (code != 0 || !File.Exists(Path.Combine(Folder, kml)) || !File.Exists(Path.Combine(Folder, ppm)))
        {
            Record(false, null, null, $"SPLAT! exit code {code}");
            var tail = string.Join("\n", log.Trim().Split('\n').TakeLast(6));
            throw new InvalidOperationException($"SPLAT! didn't write the map (exit code {code}). Its output is in {Path.Combine(Folder, PathJob.LogFile)}:\n{tail}");
        }
        string? key = File.Exists(Path.Combine(Folder, ck)) ? ck : null;
        Record(true, kml, key, null);
        return new CoverageResult(Folder, Path.Combine(Folder, kml), key == null ? null : Path.Combine(Folder, key), log, code);
    }
}
