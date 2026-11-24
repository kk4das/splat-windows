using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>A help page in the Contents list (ToString is what screen readers and the UI tests read).</summary>
public sealed record PageItem(string Title, string File) { public override string ToString() => Title; }
public sealed record IndexItem(string Term, string Target) { public override string ToString() => Term; }
public sealed record HitItem(string Title, string Snippet, string Target) { public override string ToString() => Title; }

/// <summary>
/// The help window (Stage G6): the pages shipped in the app's help folder, shown offline in WebView2,
/// with Contents, Index and Search built from the pages by <see cref="HelpLibrary"/>. Links to the web
/// open in the user's browser, never inside help.
/// </summary>
public partial class HelpWindow : Window
{
    const string HelpHost = "help.splat.local";
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "help");

    readonly HelpLibrary _lib;
    readonly List<IndexItem> _index;
    string? _pending;
    bool _ready, _navigating;

    public HelpWindow()
    {
        InitializeComponent();
        _lib = HelpLibrary.Load(Folder);
        ContentsList.ItemsSource = _lib.Pages.Select(p => new PageItem(p.Title, p.File)).ToList();
        _index = _lib.Index().Select(e => new IndexItem(e.Term, e.Target)).ToList();
        IndexList.ItemsSource = _index;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => Show(HelpTopics.Home)));
        Loaded += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            await Page.EnsureCoreWebView2Async(await App.WebEnvironmentAsync());
            var core = Page.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(HelpHost, Folder, CoreWebView2HostResourceAccessKind.Deny);
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenOutside(e.Uri); };
            core.NavigationStarting += (_, e) =>
            {
                // Help stays inside help; anything else (the web) opens in the user's browser.
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Host != HelpHost) { e.Cancel = true; OpenOutside(e.Uri); }
            };
            core.NavigationCompleted += async (_, _) => await AfterNavigationAsync();
            _ready = true;
            Navigate(_pending ?? HelpTopics.Home);
        }
        catch (Exception ex)
        {
            PageError.Text = $"Help couldn't start its page view ({ex.Message}). The pages are in {Folder} and open in any web browser.";
            PageError.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Shows a page ("page.html" or "page.html#place"), and brings the window forward.</summary>
    public void Show(string target, string? tab = null)
    {
        if (!_lib.Resolves(target)) target = HelpTopics.Home;
        if (tab == "index") HelpTabs.SelectedItem = IndexTab;
        else if (tab == "search") { HelpTabs.SelectedItem = SearchTab; SearchBox.Focus(); }
        else if (tab == "contents") HelpTabs.SelectedItem = ContentsTab;
        if (!IsVisible) base.Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_ready) Navigate(target); else _pending = target;
    }

    void Navigate(string target)
    {
        _navigating = true;
        Page.CoreWebView2.Navigate($"https://{HelpHost}/{target}");
        string file = target.Split('#')[0];
        ContentsList.SelectedItem = ((List<PageItem>)ContentsList.ItemsSource).FirstOrDefault(p => p.File == file);
        _navigating = false;
    }

    async Task AfterNavigationAsync()
    {
        var core = Page.CoreWebView2;
        if (core == null) return;
        var u = new Uri(core.Source);
        string target = u.AbsolutePath.TrimStart('/') + u.Fragment;
        string extra = "";
        if (target.StartsWith(HelpTopics.About, StringComparison.Ordinal))
        {
            string v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            v = v.Split('+')[0];
            string shown = await core.ExecuteScriptAsync($"document.getElementById('version').textContent = {JsonSerializer.Serialize(v)};");
            extra = $" (version {JsonSerializer.Deserialize<string>(shown)})";
        }
        // What is shown, for screen readers (and the UI tests).
        AutomationProperties.SetHelpText(Page, $"Help shows {target}: {core.DocumentTitle}{extra}");
    }

    static void OpenOutside(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    void ContentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_navigating && ContentsList.SelectedItem is PageItem p && _ready) Navigate(p.File);
    }

    void IndexFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        string f = IndexFilter.Text.Trim();
        IndexList.ItemsSource = f.Length == 0 ? _index : _index.Where(i => i.Term.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    void IndexList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IndexList.SelectedItem is IndexItem i && _ready) Navigate(i.Target);
    }

    void Search_Click(object sender, RoutedEventArgs e) => RunSearch();

    void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RunSearch(); }

    void RunSearch()
    {
        var hits = _lib.Search(SearchBox.Text).Select(h => new HitItem(h.Page.Title, h.Snippet, h.Target)).ToList();
        SearchResults.ItemsSource = hits;
        SearchHint.Text = SearchBox.Text.Trim().Length == 0 ? "Type something to search for."
                        : hits.Count == 0 ? $"No page has all of: {SearchBox.Text.Trim()}. Try fewer or other words, or the Index."
                        : $"{hits.Count} page{(hits.Count == 1 ? "" : "s")} found. The best match is first.";
        if (hits.Count > 0) SearchResults.SelectedIndex = 0;
    }

    void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is HitItem h && _ready) Navigate(h.Target);
    }
}
