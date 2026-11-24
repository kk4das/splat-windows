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
    public const string AgentName = "SPLATforWindows/0.7";   // identifies the app to map-tile servers

    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPLATforWindows");
    static readonly string MapCache = Path.Combine(DataDir, "map-cache");

    readonly string? _startupKml;
    LoadedMap? _map;
    bool _pageReady;
    /// <summary>What "Open in Google Earth Pro" opens: the last map or path shown.</summary>
    string? _earthFile;

    public MainWindow(string? startupKml, string stationsPath)
    {
        _startupKml = startupKml;
        InitializeComponent();
        LoadLibrary(stationsPath);
        UpdateQrzButton();
        OpacitySlider.Value = App.Settings.MapOpacity;
        // F1: help for what the window shows.
        CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Help, (_, _) => App.ShowHelp(ContextTopic())));
        Loaded += async (_, _) => await InitWebAsync();
        Loaded += (_, _) => Dispatcher.BeginInvoke(FirstRunSetup, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    void UpdateQrzButton() =>
        QrzButton.Content = App.QrzSaved() is { } q ? $"QRZ.com: {q.User}" : "QRZ.com: sign in…";

    void Qrz_Click(object sender, RoutedEventArgs e)
    {
        var w = new QrzSignInWindow { Owner = this };
        if (w.ShowDialog() == true && w.Outcome != null) Status.Text = w.Outcome;
        UpdateQrzButton();
        if (EditorPanel.Visibility == Visibility.Visible) ShowMethodHint();
    }

    async Task InitWebAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async(await App.WebEnvironmentAsync());
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
                Status.Text = _library == null ? "The stations file couldn't be read (see the left panel)."
                            : $"Ready. {_library.Stations.Count} station(s) in {_library.FilePath}.";
                PushStationsToMap();
                if (_startupKml != null) _ = ShowMapAsync(_startupKml);
                break;
            case "pin":
                PinMovedOnMap(doc.RootElement.GetProperty("lat").GetDouble(), doc.RootElement.GetProperty("lon").GetDouble());
                break;
            case "station-click":
                SelectByName(doc.RootElement.GetProperty("name").GetString() ?? "");
                break;
            case "overlay-loaded":
                if (_map != null)
                {
                    string where = $"{Path.GetFileName(_map.KmlPath)}: {_map.Width}×{_map.Height} map, " +
                                   $"{_map.Kml.South:0.###}° to {_map.Kml.North:0.###}° N, {_map.Kml.West:0.###}° to {_map.Kml.East:0.###}° E";
                    // A coverage run puts its own summary in the status line; the map's extent goes to screen readers (and the UI test).
                    if (CoveragePanel.Visibility != Visibility.Visible) Status.Text = where;
                    System.Windows.Automation.AutomationProperties.SetHelpText(Web, "Map shows " + where);
                }
                break;
            case "path-shown":   // the map drew the path; said for screen readers (and the UI test)
                System.Windows.Automation.AutomationProperties.SetHelpText(Web,
                    $"Map shows the path with {doc.RootElement.GetProperty("obstructions").GetInt32()} obstruction mark(s).");
                break;
            case "probe":   // a click on the map while a coverage map is shown
                {
                    double lat = doc.RootElement.GetProperty("lat").GetDouble(), lon = doc.RootElement.GetProperty("lon").GetDouble();
                    string? what = _map?.Probe(lat, lon);
                    string text = what == null
                        ? (_map == null ? "" : "Outside the coverage map, or its colour key isn't known.")
                        : $"{what} (read from the map's colour, so approximate)";
                    if (text.Length == 0) break;
                    _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.showProbe({lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                        $"{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {JsonSerializer.Serialize(text)})");
                    Status.Text = $"{lat:0.00000}, {lon:0.00000}: {text}";
                    System.Windows.Automation.AutomationProperties.SetItemStatus(Web, "Readout: " + text);
                    break;
                }
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
            // A map the app made knows whether it was drawn without terrain shading (its white is then shown clear).
            bool clear = SplatWin.Core.JobRecord.Read(Path.GetDirectoryName(Path.GetFullPath(kmlPath))!)?.Coverage?.NoShading == true;
            var pins = (_library?.Stations ?? new List<SplatWin.Core.Station>())
                .Select(s => new SplatWin.Core.KmlPin(s.Name.Trim(), s.Lat, s.Lon, $"Station: {s.Grid}, antenna {s.HeightText}")).ToList();
            var map = await Task.Run(() => MapLoader.Load(kmlPath, outDir, clear, pins));
            _map = map;
            _earthFile = map.Kmz;
            string rel = Path.GetRelativePath(MapCache, map.MercatorPng).Replace('\\', '/');
            string json = MapLoader.PageJson(map, $"https://{MapHost}/{Uri.EscapeDataString(rel).Replace("%2F", "/")}");
            await Web.CoreWebView2.ExecuteScriptAsync($"window.splat.showMap({json}); window.splat.setOpacity({OpacitySlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
            SetEarthFile(map.Kmz);
            OpacitySlider.IsEnabled = true;
            Title = $"{Path.GetFileName(kmlPath)} - SPLAT! for Windows (preview)";
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
        if (_earthFile == null) return;
        string? pro = GoogleEarthPro.Find();
        try
        {
            if (pro != null) Process.Start(new ProcessStartInfo(pro) { ArgumentList = { _earthFile }, UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(_earthFile) { UseShellExecute = true });   // whatever opens .kmz
            Status.Text = $"Opened {Path.GetFileName(_earthFile)} in {(pro != null ? "Google Earth Pro" : "the program Windows uses for .kmz files")}.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // Neither Pro nor any program for .kmz: the web version takes the same file.
            Status.Text = "Google Earth Pro isn't installed and no program opens .kmz files, so here are the steps for Google Earth on the web.";
            new EarthWebWindow(_earthFile) { Owner = this }.ShowDialog();
        }
    }

    void EarthWeb_Click(object sender, RoutedEventArgs e)
    {
        if (_earthFile != null) new EarthWebWindow(_earthFile) { Owner = this }.ShowDialog();
    }

    /// <summary>Makes a file the one the Google Earth buttons open.</summary>
    void SetEarthFile(string? kmz)
    {
        _earthFile = kmz;
        EarthButton.IsEnabled = EarthWebButton.IsEnabled = kmz != null;
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow { Owner = this };
        w.ShowDialog();
        UpdateQrzButton();
        ShowTerrain();
        OpacitySlider.Value = App.Settings.MapOpacity;
        if (w.Outcome != null) Status.Text = w.Outcome;
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
