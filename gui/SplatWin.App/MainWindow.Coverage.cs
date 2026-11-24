using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SplatWin.Core;

namespace SplatWin.App;

/// <summary>One finished coverage job in the "Earlier maps" list.</summary>
public sealed record HistoryItem(string Folder, JobRecord Job)
{
    public string Label => Job.Describe();
    public override string ToString() => Label;   // what screen readers (and UI tests) read
}

// Coverage maps (Stage G4): pick a transmitter, a radius and a receiver height, fetch any missing
// terrain, run the unchanged SPLAT! engine with -kml, and show its map over the street/terrain map.
public partial class MainWindow
{
    CancellationTokenSource? _covCts;
    bool _covDefaultsApplied;
    CoverageResult? _covResult;
    string? _covFolder;

    void CoverageOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        if (_library.Stations.Count == 0) { Status.Text = "Add the transmitter as a station first."; return; }
        var list = _library.Stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        CovTxBox.ItemsSource = list;
        CovTxBox.SelectedItem = Selected ?? list.FirstOrDefault(s => s.Radio != null) ?? list[0];
        CovError.Text = "";
        if (!_covDefaultsApplied)   // the Settings window's defaults, once per session (then the panel keeps what you typed)
        {
            _covDefaultsApplied = true;
            var s = App.Settings;
            CovMetricBox.IsChecked = s.Metric;
            CovMetric_Click(this, null!);
            CovRadiusBox.Text = N(s.CoverageRadius);
            CovRxBox.Text = N(s.CoverageRxHeight);
            CovShadingBox.IsChecked = s.TerrainShading;
        }
        ShowTerrain();
        CovKindBox_SelectionChanged(this, null!);
        RefreshHistory();
        ListPanel.Visibility = Visibility.Collapsed;
        CoveragePanel.Visibility = Visibility.Visible;
        Status.Text = "Choose the transmitter, what to map, the radius and the receiver height, then click Run.";
    }

    void CovClose_Click(object sender, RoutedEventArgs e)
    {
        if (_covCts != null) { _covCts.Cancel(); return; }   // while running, this is Cancel
        CoveragePanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
    }

    void CovTxBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CovTxBox.SelectedItem is not Station tx) return;
        var r = tx.Radio;
        CovFreqBox.Text = r is { FrequencyMHz: > 0 } ? N(r.FrequencyMHz) : "";
        CovErpBox.Text = r is { ErpWatts: > 0 } ? N(r.ErpWatts) : "";
        CovRadioHint.Text = r != null
            ? $"{tx.Name}'s radio settings (saved with it). Changes are saved when you click Run."
            : $"No radio settings for {tx.Name} yet: enter the frequency and ERP (the rest are SPLAT!'s documented defaults; change them in Analyse a path).";
        CovRadioHint.Text += tx.Pattern != null ? $" Antenna pattern: {tx.Pattern.Describe()}." : "";
    }

    void CovKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CovKindHint == null) return;
        CovKindHint.Text = (CoverageKind)Math.Max(0, CovKindBox.SelectedIndex) switch
        {
            CoverageKind.FieldStrength => "The signal a receiver would get, in dBµV/m, from the transmitter's ERP. SPLAT!'s colour key comes with the map.",
            CoverageKind.PowerDbm => "The power a receiver's antenna would deliver, in dBm (into a dipole). SPLAT!'s colour key comes with the map.",
            CoverageKind.PathLoss => "The loss between the antennas, in dB. The ERP isn't used.",
            _ => "Where a receiver antenna at this height can see the transmitter's antenna, in green. No radio settings needed.",
        };
    }

    void CovMetric_Click(object sender, RoutedEventArgs e)
    {
        bool m = CovMetricBox.IsChecked == true;
        CovRadiusLabel.Text = m ? "Radius (km)" : "Radius (miles)";
        CovRxLabel.Text = m ? "Receiver antenna (m)" : "Receiver antenna (ft)";
    }

    void RefreshHistory()
    {
        var items = JobRecord.History(App.JobsDir).Select(h => new HistoryItem(h.Folder, h.Job)).ToList();
        CovHistory.ItemsSource = items;
        CovHistoryHint.Text = items.Count == 0 ? "None yet. Each map you run is kept in its own job folder and listed here."
                                               : "Click one to show it again (from its job folder; nothing is re-run).";
    }

    async void CovRun_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        CovError.Text = "";
        var tx = CovTxBox.SelectedItem as Station;
        var kind = (CoverageKind)Math.Max(0, CovKindBox.SelectedIndex);
        if (Read(CovRadiusBox) is not double radius) { CovError.Text = "Enter the radius as a number."; return; }
        if (Read(CovRxBox) is not double rxh) { CovError.Text = "Enter the receiver antenna height as a number."; return; }
        var radio = tx?.Radio?.Clone() ?? new RadioSettings();
        if (kind != CoverageKind.LineOfSight)
        {
            if (Read(CovFreqBox) is not double f) { CovError.Text = "Enter the frequency in MHz as a number."; return; }
            radio.FrequencyMHz = f;
            if (kind != CoverageKind.PathLoss)
            {
                if (Read(CovErpBox) is not double erp) { CovError.Text = "Enter the ERP in watts as a number."; return; }
                radio.ErpWatts = erp;
            }
        }
        var o = new CoverageOptions(kind, radius, rxh, CovMetricBox.IsChecked == true, CovHdBox.IsChecked == true,
                                    CovSmoothBox.IsChecked == true, CovModelBox.SelectedIndex == 1 ? PathModel.LongleyRice : PathModel.Itwom,
                                    NoShading: CovShadingBox.IsChecked != true, UdtFile: _udtFile);
        if (CoverageJob.Problem(tx, radio, o, App.EngineDir) is { } p) { CovError.Text = p; return; }

        // The radio settings belong to the transmitter: save them with it (not for line of sight, which doesn't use them).
        if (kind != CoverageKind.LineOfSight && radio.Problem() == null)
        {
            var before = tx!.Radio;
            if (before == null || JsonSerializer.Serialize(before) != JsonSerializer.Serialize(radio))
            {
                tx.Radio = radio.Clone();
                try { _library.Save(); }
                catch (Exception ex) { tx.Radio = before; CovError.Text = "The radio settings couldn't be saved with the station: " + ex.Message; return; }
            }
        }

        _covCts = new CancellationTokenSource();
        CovRunButton.IsEnabled = false;
        CovCloseButton.Content = "Cancel";
        CoverageJob? job = null;
        _lastTerrainNote = null;
        try
        {
            var tiles = CoverageJob.Tiles(tx!, o);
            if (await EnsureTerrainAsync(tiles, o.Hd, _covCts.Token) is { } terr) { CovError.Text = terr; return; }
            ShowTerrain();
            job = CoverageJob.Create(App.JobsDir, tx!, radio, o, App.TerrainFolder, App.EngineDir);
            string name = tx!.Name;
            var progress = new Progress<string>(s => Status.Text = $"{name} coverage: {s}");
            var sw = Stopwatch.StartNew();
            var r = await Task.Run(() => job.RunAsync(progress, _covCts.Token));
            _covResult = r;
            _covFolder = r.Folder;
            await ShowMapAsync(r.KmlPath);
            var rec = JobRecord.Read(r.Folder);
            ShowCoverageResult(rec, r.Folder, $"Made in {sw.Elapsed.TotalSeconds:0} s." + (_lastTerrainNote != null ? $" Terrain fetched: {_lastTerrainNote}." : ""));
            RefreshHistory();
        }
        catch (OperationCanceledException)
        {
            Status.Text = job != null ? "Cancelled. The unfinished job folder is " + job.Folder : "Cancelled.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            CovError.Text = ex.Message;
            Status.Text = "The coverage map didn't finish." + (job != null ? " Job folder: " + job.Folder : "");
        }
        finally
        {
            _covCts.Dispose(); _covCts = null;
            CovRunButton.IsEnabled = true;
            CovCloseButton.Content = "Close";
        }
    }

    void ShowCoverageResult(JobRecord? rec, string folder, string note)
    {
        CovResultText.Text = rec?.Coverage != null ? rec.Describe() : Path.GetFileName(folder);
        CovResultNote.Text = note;
        CovKeyImage.Source = null;
        if (_map?.ColourKeyPng is { } key && File.Exists(key))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(key); bmp.EndInit();
            CovKeyImage.Source = bmp;
            CovKeyText.Text = "SPLAT!'s colour key for this map. Click the map to read the value at a point.";
        }
        else CovKeyText.Text = rec?.Coverage?.Kind == CoverageKind.LineOfSight
            ? $"Green: a receiver antenna at {rec.Coverage.RxHeight:0.##} {(rec.Coverage.Metric ? "m" : "ft")} has line of sight to {rec.Transmitter}."
            : "";
        CovResultBox.Visibility = Visibility.Visible;
        _covFolder = folder;
        Status.Text = $"Coverage map shown: {CovResultText.Text}. Job folder: {folder}";
    }

    async void CovHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_covCts != null || CovHistory.SelectedItem is not HistoryItem h) return;
        await ShowMapAsync(Path.Combine(h.Folder, h.Job.MapKml!));
        ShowCoverageResult(h.Job, h.Folder, "From the job history: shown from its job folder, not re-run.");
    }

    void CovFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_covFolder is { } f && Directory.Exists(f))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { f }, UseShellExecute = false });
    }
}
