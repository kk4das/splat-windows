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

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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
        // Test hooks: "--qrz-url <url>" talks to a stand-in QRZ service, and "--qrz-credential <name>"
        // keeps the test sign-in in its own Credential Manager entry, away from the real one.
        if (Arg(e.Args, "--qrz-url") is { } qu) QrzService = new Uri(qu);
        if (Arg(e.Args, "--qrz-credential") is { } qc) QrzTarget = qc;
        new MainWindow(e.Args.FirstOrDefault(a => a.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)), stations).Show();
    }
}
