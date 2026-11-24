using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>
/// The first-run setup (owner decision G9, 2026-11-23): welcome, terrain folder, call-sign lookups,
/// then "Add my first station". It opens once, on a first start with no stations; it can be skipped,
/// and opened again from Help > Setup…. It changes nothing the user doesn't choose.
/// </summary>
public partial class SetupWindow : Window
{
    int _page = 1;
    const int Pages = 4;

    /// <summary>True when the user finished with "Add my first station" (the main window then opens the editor).</summary>
    public bool AddStation { get; private set; }

    public SetupWindow()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Help, (_, _) => App.ShowHelp(HelpTopics.Wizard)));
        ShowPage();
    }

    void ShowPage()
    {
        var pages = new[] { Page1, Page2, Page3, Page4 };
        for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i + 1 == _page ? Visibility.Visible : Visibility.Collapsed;
        StepText.Text = $"Step {_page} of {Pages}";
        BackButton.Visibility = _page > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _page == Pages ? "Add my first station" : "Next";
        SkipButton.Content = _page == Pages ? "Finish without a station" : "Skip setup";
        if (_page == 2) ShowTerrain();
        if (_page == 3) ShowQrz();
    }

    void ShowTerrain()
    {
        string f = App.TerrainFolder;
        bool own = string.Equals(Path.GetFullPath(f), Path.GetFullPath(App.DefaultTerrainFolder), StringComparison.OrdinalIgnoreCase);
        TerrainText.Text = f + (own ? "  (the app's own folder)" : "");
        TerrainDefaultButton.IsEnabled = !own;
    }

    void ShowQrz() =>
        QrzText.Text = App.QrzSaved() is { } q ? $"Signed in to QRZ.com as {q.User}." : "Not signed in: US call signs are looked up on callook.info.";

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < Pages) { _page++; ShowPage(); return; }
        AddStation = true;
        Finish();
    }

    void Back_Click(object sender, RoutedEventArgs e) { if (_page > 1) { _page--; ShowPage(); } }

    void Skip_Click(object sender, RoutedEventArgs e) => Finish();

    void Finish()
    {
        App.MarkSetupDone();
        DialogResult = true;
    }

    void TerrainChange_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the folder for SPLAT! terrain (.sdf) files" };
        if (dlg.ShowDialog(this) != true) return;
        try { App.SetTerrainFolder(dlg.FolderName); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TerrainText.Text = "The choice couldn't be saved: " + ex.Message; return; }
        ShowTerrain();
    }

    void TerrainDefault_Click(object sender, RoutedEventArgs e)
    {
        try { App.SetTerrainFolder(null); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TerrainText.Text = "The choice couldn't be saved: " + ex.Message; return; }
        ShowTerrain();
    }

    void Qrz_Click(object sender, RoutedEventArgs e)
    {
        new QrzSignInWindow { Owner = this }.ShowDialog();
        ShowQrz();
    }
}
