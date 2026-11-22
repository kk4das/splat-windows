using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace SplatWin.App;

public partial class MainWindow : Window
{
    const string AppHost = "app.splat.local";   // the map page (files shipped with the app)
    const string MapHost = "map.splat.local";   // the images made from the opened map
    public const string AgentName = "SPLATforWindows/0.1";   // identifies the app to map-tile servers

    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPLATforWindows");
    static readonly string MapCache = Path.Combine(DataDir, "map-cache");

    readonly string? _startupKml;
    LoadedMap? _map;
    bool _pageReady;

    public MainWindow(string? startupKml)
    {
        _startupKml = startupKml;
        InitializeComponent();
        Loaded += async (_, _) => await InitWebAsync();
    }

    async Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            core.Settings.UserAgent += " " + AgentName;   // OpenStreetMap's tile policy asks apps to identify themselves
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            string web = Path.Combine(AppContext.BaseDirectory, "web");
            Directory.CreateDirectory(MapCache);
            core.SetVirtualHostNameToFolderMapping(AppHost, web, CoreWebView2HostResourceAccessKind.Deny);
            core.SetVirtualHostNameToFolderMapping(MapHost, MapCache, CoreWebView2HostResourceAccessKind.Allow);
            // Links in map attributions open in the user's browser, not inside the app.
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
            core.WebMessageReceived += OnPageMessage;
            core.Navigate($"https://{AppHost}/index.html");
        }
        catch (Exception ex)
        {
            Status.Text = "The map view could not start: " + ex.Message +
                          " (it needs the Microsoft Edge WebView2 Runtime, which Windows 11 includes).";
        }
    }

    void OnPageMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        switch (doc.RootElement.GetProperty("type").GetString())
        {
            case "ready":
                _pageReady = true;
                Status.Text = "Ready. Open a SPLAT! coverage map (.kml).";
                if (_startupKml != null) _ = ShowMapAsync(_startupKml);
                break;
            case "overlay-loaded":
                if (_map != null)
                    Status.Text = $"{Path.GetFileName(_map.KmlPath)}: {_map.Width}×{_map.Height} map, " +
                                  $"{_map.Kml.South:0.###}° to {_map.Kml.North:0.###}° N, {_map.Kml.West:0.###}° to {_map.Kml.East:0.###}° E";
                break;
            case "error":
                Status.Text = "Map page: " + doc.RootElement.GetProperty("text").GetString();
                break;
        }
    }

    async Task ShowMapAsync(string kmlPath)
    {
        if (!_pageReady) return;
        Status.Text = $"Preparing {Path.GetFileName(kmlPath)}…";
        OpenButton.IsEnabled = false;
        try
        {
            // Each map gets its own cache folder so the page never shows a stale image.
            string outDir = Path.Combine(MapCache, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var map = await Task.Run(() => MapLoader.Load(kmlPath, outDir));
            _map = map;
            string rel = Path.GetRelativePath(MapCache, map.MercatorPng).Replace('\\', '/');
            string json = MapLoader.PageJson(map, $"https://{MapHost}/{Uri.EscapeDataString(rel).Replace("%2F", "/")}");
            await Web.CoreWebView2.ExecuteScriptAsync($"window.splat.showMap({json}); window.splat.setOpacity({OpacitySlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
            EarthButton.IsEnabled = OpacitySlider.IsEnabled = true;
            Title = $"{Path.GetFileName(kmlPath)} - SPLAT! for Windows (G0 preview)";
        }
        catch (Exception ex)
        {
            Status.Text = $"Could not open {Path.GetFileName(kmlPath)}: {ex.Message}";
        }
        finally { OpenButton.IsEnabled = true; }
    }

    async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open a SPLAT! coverage map",
            Filter = "SPLAT! coverage map (*.kml)|*.kml|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == true) await ShowMapAsync(dlg.FileName);
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_pageReady && Web.CoreWebView2 != null)
            _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.setOpacity({e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    void Earth_Click(object sender, RoutedEventArgs e)
    {
        if (_map == null) return;
        string? pro = GoogleEarthPro.Find();
        try
        {
            if (pro != null) Process.Start(new ProcessStartInfo(pro) { ArgumentList = { _map.Kmz }, UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(_map.Kmz) { UseShellExecute = true });   // whatever opens .kmz
            Status.Text = $"Opened {Path.GetFileName(_map.Kmz)} in {(pro != null ? "Google Earth Pro" : "the program Windows uses for .kmz files")}.";
        }
        catch (Exception ex)
        {
            Status.Text = "Google Earth Pro was not found, and no program is set to open .kmz files. " +
                          $"The file is {_map.Kmz} (it also opens on earth.google.com via Projects → Import). ({ex.Message})";
        }
    }

    static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }
}

static class GoogleEarthPro
{
    public static string? Find()
    {
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string p = Path.Combine(Environment.GetFolderPath(pf), "Google", "Google Earth Pro", "client", "googleearth.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
