using System.IO;
using System.Net.Http;
using System.Windows;
using SplatWin.Core;

namespace SplatWin.App;

public partial class App : Application
{
    /// <summary>One HTTP client for lookups, identifying the app as the services ask.</summary>
    public static readonly HttpClient Http = CreateHttp();

    /// <summary>The QRZ service and the Credential Manager entry; tests point these elsewhere.</summary>
    public static Uri QrzService { get; private set; } = Qrz.DefaultService;
    public static string QrzTarget { get; private set; } = WinCredentials.QrzTarget;
    static Qrz.Client? _qrz;

    /// <summary>The saved QRZ sign-in, or null. Read fresh each time; the password stays in Credential Manager.</summary>
    public static (string User, string Password)? QrzSaved()
    {
        try { return WinCredentials.Read(QrzTarget); }
        catch { return null; }
    }

    /// <summary>The QRZ client; it signs in with the saved password when it needs a session.</summary>
    public static Qrz.Client QrzClient => _qrz ??= new Qrz.Client(Http, QrzSaved, QrzService);

    public static void ResetQrz() { _qrz?.Forget(); _qrz = null; }

    // ---- path analysis (G3) ----
    /// <summary>The unchanged SPLAT! programs, shipped in the app's engine folder (from dist\).</summary>
    public static string EngineDir { get; private set; } = Path.Combine(AppContext.BaseDirectory, "engine");
    /// <summary>Path jobs: Documents\SPLAT\Jobs\&lt;date&gt; &lt;tx&gt; to &lt;rx&gt;\.</summary>
    public static string JobsDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SPLAT", "Jobs");
    /// <summary>The app's preferences; "--settings <file>" (tests) uses another file, so the real one is never touched.</summary>
    public static string SettingsPath { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPLATforWindows", "settings.json");
    public static AppSettings Settings { get; private set; } = AppSettings.Load(SettingsPath);
    /// <summary>Set by --terrain (tests): used instead of the saved choice, and never saved.</summary>
    public static string? TerrainOverride { get; private set; }

    /// <summary>Where terrain is kept and fetched to when none has been chosen: the app's own local data
    /// (not Documents, which OneDrive may sync).</summary>
    public static readonly string DefaultTerrainFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPLATforWindows", "Terrain");

    public static string TerrainFolder => TerrainOverride ?? Settings.TerrainFolder ?? DefaultTerrainFolder;

    /// <summary>Terrain sources (runbook D7); tests point these at a stand-in with --terrain-std-url / --terrain-hd-url.</summary>
    public static Uri TerrainStdSource { get; private set; } = TerrainFetcher.DefaultStandardSource;
    public static Uri TerrainHdSource { get; private set; } = TerrainFetcher.DefaultHdSource;

    /// <summary>Downloads of terrain can take minutes; the fetcher sets its own 10-minute limit per file.</summary>
    public static readonly HttpClient TerrainHttp = CreateTerrainHttp();

    static HttpClient CreateTerrainHttp()
    {
        var h = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(SplatWin.App.MainWindow.AgentName);
        return h;
    }

    public static TerrainFetcher Fetcher(string folder) => new(TerrainHttp, folder, EngineDir, TerrainStdSource, TerrainHdSource);

    /// <summary>Saves the terrain folder choice; null goes back to the app's own folder.</summary>
    public static void SetTerrainFolder(string? folder)
    {
        if (TerrainOverride != null) { TerrainOverride = folder ?? DefaultTerrainFolder; return; }
        Settings.TerrainFolder = folder;
        Settings.Save(SettingsPath);
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    sealed record JobSpec(Station Tx, Station Rx, RadioSettings Radio, PathModel Model, bool Metric, double FresnelPercent, double Clutter, bool Hd,
                          string? UdtFile = null);

    static int HeadlessPathJob(string spec)
    {
        try
        {
            var o = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var s = System.Text.Json.JsonSerializer.Deserialize<JobSpec>(File.ReadAllText(spec), o) ?? throw new InvalidDataException("empty job spec");
            var job = PathJob.Create(JobsDir, s.Tx, s.Rx, s.Radio, new PathOptions(s.Model, s.Metric, s.FresnelPercent, s.Clutter, s.Hd, s.UdtFile),
                                     TerrainFolder ?? "", EngineDir);
            var r = Task.Run(() => job.RunAsync()).GetAwaiter().GetResult();   // off the UI thread, or the awaits would deadlock
            File.WriteAllText(spec + ".out", r.Folder + "\n" + string.Join("\n", r.Runs.Select(x => $"{Path.GetRelativePath(r.Folder, x.Folder)}\t{x.ExitCode}")));
            return r.GraphErrors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            File.WriteAllText(spec + ".err", ex.ToString());
            return 1;
        }
    }

    sealed record CoverageSpec(Station Tx, RadioSettings Radio, CoverageOptions Options);

    static int HeadlessCoverageJob(string spec)
    {
        try
        {
            var o = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var s = System.Text.Json.JsonSerializer.Deserialize<CoverageSpec>(File.ReadAllText(spec), o) ?? throw new InvalidDataException("empty job spec");
            var tiles = CoverageJob.Tiles(s.Tx, s.Options);
            var fetched = Task.Run(() => Fetcher(TerrainFolder).EnsureAsync(tiles, s.Options.Hd)).GetAwaiter().GetResult();
            if (fetched.FirstOrDefault(f => !f.Ok) is { } bad) throw new IOException($"terrain {bad.Tile}: {bad.What}");
            var job = CoverageJob.Create(JobsDir, s.Tx, s.Radio, s.Options, TerrainFolder, EngineDir);
            var r = Task.Run(() => job.RunAsync()).GetAwaiter().GetResult();
            File.WriteAllLines(spec + ".out", new[] { r.Folder, r.ExitCode.ToString() }.Concat(fetched.Select(f => f.Tile + "\t" + f.What)));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(spec + ".err", ex.ToString());
            return 1;
        }
    }

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(SplatWin.App.MainWindow.AgentName);
        return h;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Test hook: "--export <map.kml> <folder>" writes the map page's inputs (the Mercator PNG,
        // overlay.json, the KMZ) without opening a window, so the page can be checked in a browser.
        if (e.Args.Length == 3 && e.Args[0] == "--export")
        {
            try
            {
                var m = MapLoader.Load(e.Args[1], e.Args[2]);
                File.WriteAllText(Path.Combine(e.Args[2], "overlay.json"), MapLoader.PageJson(m, Path.GetFileName(m.MercatorPng)));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(e.Args[2], "export-error.txt"), ex.ToString());
                Shutdown(1);
            }
            return;
        }
        // "--stations <file>" uses another stations library (for testing); the default is Documents\SPLAT\stations.json.
        string stations = StationLibrary.DefaultPath;
        if (Arg(e.Args, "--stations") is { } st) stations = Path.GetFullPath(st);
        if (Arg(e.Args, "--settings") is { } se) { SettingsPath = Path.GetFullPath(se); Settings = AppSettings.Load(SettingsPath); }
        // Test hooks: "--qrz-url <url>" talks to a stand-in QRZ service, and "--qrz-credential <name>"
        // keeps the test sign-in in its own Credential Manager entry, away from the real one.
        if (Arg(e.Args, "--qrz-url") is { } qu) QrzService = new Uri(qu);
        if (Arg(e.Args, "--qrz-credential") is { } qc) QrzTarget = qc;
        // Path analysis: "--engine <dir>" (the SPLAT! programs), "--terrain <dir>" (terrain for this
        // run only, not saved), "--jobs <dir>" (where job folders go).
        if (Arg(e.Args, "--engine") is { } en) EngineDir = Path.GetFullPath(en);
        if (Arg(e.Args, "--terrain") is { } te) TerrainOverride = Path.GetFullPath(te);
        if (Arg(e.Args, "--jobs") is { } jo) JobsDir = Path.GetFullPath(jo);
        if (Arg(e.Args, "--terrain-std-url") is { } ts) TerrainStdSource = new Uri(ts);
        if (Arg(e.Args, "--terrain-hd-url") is { } th) TerrainHdSource = new Uri(th);
        // Test hook: "--coverage-job <spec.json>" fetches terrain and runs one coverage job as the window
        // does, without a window; the job folder goes to <spec.json>.out (or the error to .err).
        if (Arg(e.Args, "--coverage-job") is { } cspec)
        {
            Shutdown(HeadlessCoverageJob(cspec));
            return;
        }
        // Test hook: "--path-job <spec.json>" runs one path job exactly as the window does, without a
        // window, and writes the job folder's path to <spec.json>.out (or the error to <spec.json>.err).
        if (Arg(e.Args, "--path-job") is { } spec)
        {
            Shutdown(HeadlessPathJob(spec));
            return;
        }
        new MainWindow(e.Args.FirstOrDefault(a => a.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)), stations).Show();
    }
}
