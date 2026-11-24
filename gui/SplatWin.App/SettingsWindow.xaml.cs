using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SplatWin.App;

/// <summary>
/// The app's preferences (G5): terrain folder, defaults for new coverage maps, map opacity, and
/// where QRZ.com, Google Earth and the job folders stand. Saved in settings.json; the QRZ password
/// stays in Windows Credential Manager (its own window).
/// </summary>
public partial class SettingsWindow : Window
{
    public string? Outcome { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        var s = App.Settings;
        UnitsBox.SelectedIndex = s.Metric ? 1 : 0;
        RadiusBox.Text = s.CoverageRadius.ToString("0.###", CultureInfo.CurrentCulture);
        RxBox.Text = s.CoverageRxHeight.ToString("0.###", CultureInfo.CurrentCulture);
        ShadingBox.IsChecked = s.TerrainShading;
        OpacitySlider.Value = s.MapOpacity;
        ShowTerrain();
        ShowQrz();
        string? pro = GoogleEarthPro.Find();
        EarthText.Text = pro != null
            ? $"Google Earth Pro is installed ({pro}). Open in Google Earth Pro uses it."
            : "Google Earth Pro isn't installed. Google Earth on the web… shows how to open the same files in a browser.";
        JobsText.Text = App.JobsDir;
    }

    void ShowTerrain()
    {
        string f = App.TerrainFolder;
        bool own = string.Equals(Path.GetFullPath(f), Path.GetFullPath(App.DefaultTerrainFolder), StringComparison.OrdinalIgnoreCase);
        int n = Directory.Exists(f) ? Directory.GetFiles(f, "*.sdf").Length : 0;
        TerrainText.Text = $"{f}{(own ? " (the app's own folder)" : "")}: {n} .sdf file{(n == 1 ? "" : "s")}.";
        TerrainDefaultButton.IsEnabled = !own;
    }

    void ShowQrz() => QrzText.Text = App.QrzSaved() is { } q ? $"Signed in as {q.User}." : "Not signed in (US call signs are looked up on callook.info).";

    void Units_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RadiusLabel == null) return;
        bool m = UnitsBox.SelectedIndex == 1;
        RadiusLabel.Text = m ? "Radius (km)" : "Radius (miles)";
        RxLabel.Text = m ? "Receiver antenna (m)" : "Receiver antenna (ft)";
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText != null) OpacityText.Text = $"{e.NewValue * 100:0}%";
    }

    void TerrainChange_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the folder that holds your SPLAT! terrain (.sdf) files" };
        if (Directory.Exists(App.TerrainFolder)) dlg.InitialDirectory = App.TerrainFolder;
        if (dlg.ShowDialog(this) != true) return;
        try { App.SetTerrainFolder(dlg.FolderName); Outcome = "Terrain folder: " + dlg.FolderName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorText.Text = "The choice couldn't be saved: " + ex.Message; }
        ShowTerrain();
    }

    void TerrainDefault_Click(object sender, RoutedEventArgs e)
    {
        try { App.SetTerrainFolder(null); Outcome = "Terrain folder: the app's own, " + App.DefaultTerrainFolder; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorText.Text = "The choice couldn't be saved: " + ex.Message; }
        ShowTerrain();
    }

    void Qrz_Click(object sender, RoutedEventArgs e)
    {
        var w = new QrzSignInWindow { Owner = this };
        if (w.ShowDialog() == true && w.Outcome != null) Outcome = w.Outcome;
        ShowQrz();
    }

    void Jobs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.JobsDir);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { App.JobsDir }, UseShellExecute = false });
    }

    static double? Read(TextBox box)
    {
        string t = box.Text.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out double v)
            || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        return null;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (Read(RadiusBox) is not double radius) { ErrorText.Text = "Enter the radius as a number."; return; }
        if (Read(RxBox) is not double rx) { ErrorText.Text = "Enter the receiver antenna height as a number."; return; }
        var s = App.Settings;
        var before = (s.CoverageRadius, s.CoverageRxHeight, s.Metric, s.TerrainShading, s.MapOpacity);
        s.CoverageRadius = radius; s.CoverageRxHeight = rx; s.Metric = UnitsBox.SelectedIndex == 1;
        s.TerrainShading = ShadingBox.IsChecked == true; s.MapOpacity = Math.Round(OpacitySlider.Value, 2);
        if (s.Problem() is { } p)
        {
            (s.CoverageRadius, s.CoverageRxHeight, s.Metric, s.TerrainShading, s.MapOpacity) = before;
            ErrorText.Text = p; return;
        }
        try { s.Save(App.SettingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (s.CoverageRadius, s.CoverageRxHeight, s.Metric, s.TerrainShading, s.MapOpacity) = before;
            ErrorText.Text = "The settings couldn't be saved: " + ex.Message; return;
        }
        Outcome = "Settings saved. New coverage maps start with them (from the next time the coverage panel opens in a new session).";
        DialogResult = true;
    }
}
