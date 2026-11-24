using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SplatWin.App;

/// <summary>
/// The steps for opening a map or path in Google Earth on the web (when Google Earth Pro isn't
/// installed, or by choice). The app makes the same web-safe .kmz for both; it never uploads it.
/// </summary>
public partial class EarthWebWindow : Window
{
    public const string WebAddress = "https://earth.google.com/web/";
    readonly string _file;

    public EarthWebWindow(string kmzFile)
    {
        InitializeComponent();
        _file = kmzFile;
        FileBox.Text = kmzFile;
        StepsText.Text =
            "1. Click Open earth.google.com below; it opens in your web browser. Sign in to Google if it asks.\n" +
            "2. In Google Earth, open Projects (in the menu on the left), then New project, then Import KML file from computer.\n" +
            "3. Choose this file (Copy its path, then paste it into the file box; or Show the file):";
    }

    void Web_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(WebAddress) { UseShellExecute = true });
        DoneText.Text = "Opened earth.google.com in your browser.";
    }

    void Show_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_file)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + _file }, UseShellExecute = false });
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_file); DoneText.Text = "The file's path is on the clipboard."; }
        catch (System.Runtime.InteropServices.ExternalException) { DoneText.Text = "The clipboard is busy; select the path above and copy it."; }
    }
}
