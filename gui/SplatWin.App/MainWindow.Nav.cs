using System.Windows;
using System.Windows.Controls;

namespace SplatWin.App;

// The left panel's views (owner's request after G5): the four buttons at the top of the panel are
// always there, the current view's button is highlighted, and each switches straight to its view.
// Path analysis and coverage maps have "Back to stations" at the top; while a job runs, the views
// stay put (Cancel is next to Run).
public partial class MainWindow
{
    enum View { Stations, Editor, Path, Coverage }
    View _view = View.Stations;

    bool Busy => _covCts != null || _pathCts != null;

    /// <summary>Shows one view of the left panel, tidying up the one being left.</summary>
    void ShowView(View v)
    {
        if (_view == View.Editor && v != View.Editor)
        {
            ClearPin();
            if (_pageReady) _ = Web.CoreWebView2.ExecuteScriptAsync("window.splat.setEditing(false)");
        }
        if (_view == View.Path && v != View.Path)
        {
            ShowResults(false);
            if (_pageReady) _ = Web.CoreWebView2.ExecuteScriptAsync("window.splat.clearPath()");
        }
        ListPanel.Visibility = v == View.Stations ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = v == View.Editor ? Visibility.Visible : Visibility.Collapsed;
        PathPanel.Visibility = v == View.Path ? Visibility.Visible : Visibility.Collapsed;
        CoveragePanel.Visibility = v == View.Coverage ? Visibility.Visible : Visibility.Collapsed;
        _view = v;
        var accent = (Style)FindResource("AccentButtonStyle");
        foreach (var (b, which) in new (Button, View)[] { (AddButton, View.Editor), (PathButton, View.Path), (CoverageButton, View.Coverage) })
        {
            if (which == v) b.Style = accent;
            else b.ClearValue(StyleProperty);   // back to the theme's own button style (Style = null would drop it)
        }
        BackButton.Visibility = v == View.Stations ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Content = v == View.Editor ? "Back to stations (nothing saved)" : "Back to stations";
        BackButton.ToolTip = v switch
        {
            View.Editor => "Leave the editor without saving this station",
            View.Path => "Back to the stations list (this clears the path from the map)",
            _ => "Back to the stations list (the coverage map stays on the map)",
        };
        foreach (var sv in new[] { PathPanel, CoveragePanel, EditorPanel }) sv.ScrollToTop();
    }

    /// <summary>False (and says why) while a path analysis or a coverage map is running.</summary>
    bool CanSwitch()
    {
        if (!Busy) return true;
        Status.Text = _covCts != null ? "A coverage map is being made. Wait for it, or click Cancel next to Run."
                                      : "A path analysis is running. Wait for it, or click Cancel next to Analyze.";
        return false;
    }

    /// <summary>While a job runs: Run/Analyze off, Cancel shown, and no switching views.</summary>
    void SetBusy(bool path, bool on)
    {
        if (path) { AnalyseButton.IsEnabled = !on; PathCancelButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        else { CovRunButton.IsEnabled = !on; CovCancelButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        foreach (var b in new[] { AddButton, ImportButton, PathButton, CoverageButton, BackButton }) b.IsEnabled = !on;
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        bool editing = _view == View.Editor;
        ShowView(View.Stations);
        Status.Text = editing ? "Nothing was changed." : $"{_library?.Stations.Count ?? 0} station(s).";
    }

    void PathCancel_Click(object sender, RoutedEventArgs e) => _pathCts?.Cancel();
    void CovCancel_Click(object sender, RoutedEventArgs e) => _covCts?.Cancel();
}
