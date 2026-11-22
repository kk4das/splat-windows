using System.IO;
using System.Net.Http;
using System.Windows;
using SplatWin.Core;

namespace SplatWin.App;

public partial class App : Application
{
    /// <summary>One HTTP client for lookups, identifying the app as the services ask.</summary>
    public static readonly HttpClient Http = CreateHttp();

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
        int i = Array.IndexOf(e.Args, "--stations");
        if (i >= 0 && i + 1 < e.Args.Length) stations = Path.GetFullPath(e.Args[i + 1]);
        new MainWindow(e.Args.FirstOrDefault(a => a.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)), stations).Show();
    }
}
