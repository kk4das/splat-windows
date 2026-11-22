using System.IO;
using System.Windows;

namespace SplatWin.App;

public partial class App : Application
{
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
        new MainWindow(e.Args.FirstOrDefault(a => a.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))).Show();
    }
}
