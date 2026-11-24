using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>
/// Stage G8: a 3D view of a finished path job: the terrain along the path, both antennas, the line
/// of sight, the Fresnel zone and SPLAT!'s obstruction points. The numbers come from
/// <see cref="PathScene"/> (SPLAT!'s own arithmetic, checked against the job's files); the page
/// (web\path3d.html, three.js) only draws them. Its own window (decision G10), so it can sit beside the app.
/// </summary>
public partial class View3DWindow : Window
{
    const string Host = "view3d.splatwin.example";
    static double _lastWidth = 1.0;   // miles either side (decision G12), kept for this session

    readonly string _folder;
    PathJobSetup? _job;
    CancellationTokenSource? _cts;

    public View3DWindow(string jobFolder)
    {
        InitializeComponent();
        _folder = jobFolder;
        try
        {
            _job = PathJobSetup.FromJobFolder(jobFolder);
            Title = $"3D view: {_job.RxName} to {_job.TxName} - SPLAT! for Windows";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowError("This path can't be shown in 3D: " + ex.Message);
        }
        Loaded += async (_, _) => { if (_job != null) await InitAsync(); };
        Closed += (_, _) => _cts?.Cancel();
    }

    void ShowError(string text)
    {
        PageError.Text = text;
        PageError.Visibility = Visibility.Visible;
        AutomationProperties.SetHelpText(Page, "Problem: " + text);
    }

    async Task InitAsync()
    {
        try
        {
            await Page.EnsureCoreWebView2Async(await App.WebEnvironmentAsync());
            var core = Page.CoreWebView2;
            core.Settings.UserAgent += " " + MainWindow.AgentName;   // map-tile servers ask apps to identify themselves
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.Deny);
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenOutside(e.Uri); };
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Host != Host) { e.Cancel = true; OpenOutside(e.Uri); }
            };
            core.WebMessageReceived += OnMessage;
            core.Navigate($"https://{Host}/path3d.html");
        }
        catch (Exception ex)
        {
            ShowError("The 3D view couldn't start (" + ex.Message + "). It needs the Microsoft Edge WebView2 Runtime, which Windows 11 includes.");
        }
    }

    static void OpenOutside(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                await SendSceneAsync(_lastWidth);
                break;
            case "width":
                _lastWidth = Math.Clamp(root.GetProperty("miles").GetDouble(), 0.25, 5);
                await SendSceneAsync(_lastWidth);
                break;
            case "shown":   // what the page drew, for screen readers and the window test
                AutomationProperties.SetHelpText(Page, root.GetProperty("text").GetString() ?? "");
                break;
            case "help":
                App.ShowHelp("path-3d.html");
                break;
        }
    }

    async Task SendSceneAsync(double halfWidth)
    {
        if (_job is not { } job) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Post(new { type = "busy", text = "Reading the terrain…" });
        try
        {
            var extra = PathScene.StripTiles(job, halfWidth);
            var missing = Terrain.Missing(job.TerrainFolder, extra);
            if (missing.Count > 0)
            {
                Post(new { type = "busy", text = $"Fetching {missing.Count} terrain tile(s) for the sides of the strip…" });
                // A tile that can't be fetched (offline, say) only leaves that part of the strip out.
                try { await App.Fetcher(job.TerrainFolder).EnsureAsync(missing, job.Hd, null, cts.Token); }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { }
            }
            if (cts.IsCancellationRequested) return;
            string json = await Task.Run(() => PathScene.Load(job, extra).BuildJson(halfWidth), cts.Token);
            if (cts.IsCancellationRequested || Page.CoreWebView2 == null) return;
            Page.CoreWebView2.PostWebMessageAsJson("{\"type\":\"scene\",\"scene\":" + json + "}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!cts.IsCancellationRequested) Post(new { type = "error", text = "The 3D view couldn't be made: " + ex.Message });
        }
    }

    void Post(object message)
    {
        if (Page.CoreWebView2 != null) Page.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { App.ShowHelp("path-3d.html"); e.Handled = true; }
    }
}
