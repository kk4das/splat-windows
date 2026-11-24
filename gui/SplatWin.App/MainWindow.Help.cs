using System.Windows;
using SplatWin.Core;

namespace SplatWin.App;

// Help and the first-run setup (Stage G6): the Help menu, F1, the "What does this mean?" buttons,
// and the setup that opens once on a first start with no stations (owner decision G9).
public partial class MainWindow
{
    bool _setupFollowUp;   // the setup's "Add my first station": offer a first map once it's saved

    /// <summary>Where F1 goes for what the window shows.</summary>
    string ContextTopic() => _view switch
    {
        View.Editor => HelpTopics.AddStation,
        View.Path => HelpTopics.Path,
        View.Coverage => HelpTopics.Coverage,
        _ => HelpTopics.Stations,
    };

    void HelpMenu_Click(object sender, RoutedEventArgs e)
    {
        HelpMenu.PlacementTarget = HelpButton;
        HelpMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        HelpMenu.IsOpen = true;
    }

    void HelpContents_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.Home, "contents");
    void HelpIndex_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.Home, "index");
    void HelpSearch_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.Home, "search");
    void HelpStart_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.GettingStarted, "contents");
    void HelpTrouble_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.Troubleshooting, "contents");
    void HelpAbout_Click(object sender, RoutedEventArgs e) => App.ShowHelp(HelpTopics.About, "contents");

    /// <summary>A "What does this mean?" button: its Tag is the help place.</summary>
    void HelpLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string target }) App.ShowHelp(target, "contents");
    }

    void HelpSetup_Click(object sender, RoutedEventArgs e) => RunSetup();

    /// <summary>On a first start with no stations, the setup opens once. With stations already there it isn't needed.</summary>
    void FirstRunSetup()
    {
        if (App.Settings.SetupDone || _library == null) return;
        if (_library.Stations.Count > 0) { App.MarkSetupDone(); return; }
        RunSetup();
    }

    void RunSetup()
    {
        if (!CanSwitch()) return;
        var w = new SetupWindow { Owner = this };
        w.ShowDialog();
        if (w.AddStation)
        {
            _setupFollowUp = true;
            OpenEditor(null);
            Status.Text = "Find your station, put the pin on the antenna, give its height, and click Save station.";
        }
    }

    /// <summary>After the setup's first station is saved: offer a first coverage map for it.</summary>
    void AfterStationSaved(Station saved)
    {
        if (!_setupFollowUp) return;
        _setupFollowUp = false;
        var answer = MessageBox.Show(this,
            $"{saved.Name} is saved. Make a first coverage map for it now?\n\nYou'll need the transmitter's frequency (MHz) and ERP (watts).",
            "SPLAT! for Windows", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;
        CoverageOpen_Click(this, new RoutedEventArgs());
        CovTxBox.SelectedItem = saved;
    }
}
