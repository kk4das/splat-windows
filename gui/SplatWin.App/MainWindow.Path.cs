using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SplatWin.Core;

namespace SplatWin.App;

// Path analysis (Stage G3): pick a transmitter and a receiver, run the unchanged SPLAT! engine,
// show its numbers (copied from its report), its graphs (drawn here from its data) and the path on the map.
public partial class MainWindow
{
    PathResult? _pathResult;
    CancellationTokenSource? _pathCts;
    /// <summary>The obstacles (.udt) file chosen for paths and maps, or null.</summary>
    string? _udtFile;
    (double Dielectric, double Conductivity)? _customGround;

    void PathOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null || !CanSwitch()) return;
        if (_view == View.Path) return;   // already showing: keep what's typed
        if (_library.Stations.Count < 2) { Status.Text = "Add at least two stations first: a transmitter and a receiver."; return; }
        if (ClimateBox.Items.Count == 0) foreach (var c in RadioSettings.ClimateNames) ClimateBox.Items.Add(c);
        var list = _library.Stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var tx = Selected ?? list.FirstOrDefault(s => s.Radio != null) ?? list[0];
        TxBox.ItemsSource = list;
        RxBox.ItemsSource = list;
        RxBox.SelectedItem = RxBox.SelectedItem is Station r && list.Contains(r) && r != tx ? r : list.First(s => s != tx);
        TxBox.SelectedItem = tx;
        LoadRadio(tx);
        ShowTerrain();
        PathError.Text = "";
        ShowView(View.Path);
        Status.Text = "Choose the transmitter and the receiver, check the radio settings, then click Analyze.";
    }

    void TxBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxBox.SelectedItem is Station tx) LoadRadio(tx);
    }

    void Swap_Click(object sender, RoutedEventArgs e)
    {
        var (t, r) = (TxBox.SelectedItem, RxBox.SelectedItem);
        RxBox.SelectedItem = t;
        TxBox.SelectedItem = r;
    }

    void UnitsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClutterLabel != null) ClutterLabel.Text = UnitsBox.SelectedIndex == 1 ? "Ground clutter (m)" : "Ground clutter (ft)";
    }

    static string N(double v) => v.ToString("0.######", CultureInfo.CurrentCulture);

    void LoadRadio(Station tx)
    {
        var r = tx.Radio ?? new RadioSettings();
        RadioHint.Text = tx.Radio != null
            ? $"Saved with {tx.Name}. Changes are saved with it when you click Analyze."
            : $"Not set for {tx.Name} yet. Enter them (or load a .lrp file); they are saved with {tx.Name} when you click Analyze.";
        RadioHint.Text += tx.Pattern != null ? $" Antenna pattern: {tx.Pattern.Describe()}." : " Antenna: the same in all directions (add a pattern with Edit…).";
        FillRadio(r);
    }

    void FillRadio(RadioSettings r)
    {
        FreqBox.Text = r.FrequencyMHz > 0 ? N(r.FrequencyMHz) : "";
        ErpBox.Text = r.ErpWatts > 0 ? N(r.ErpWatts) : "";
        PolBox.SelectedIndex = r.Polarization == 0 ? 0 : 1;
        ClimateBox.SelectedIndex = r.Climate is >= 1 and <= 7 ? r.Climate - 1 : 4;
        SitBox.Text = N(r.Situations * 100);
        TimeBox.Text = N(r.Time * 100);
        BendBox.Text = N(r.Bending);
        GroundBox.Items.Clear();
        foreach (var g in RadioSettings.Grounds) GroundBox.Items.Add($"{g.Name} ({N(g.Dielectric)}, {N(g.Conductivity)} S/m)");
        int match = RadioSettings.Grounds.ToList().FindIndex(g => g.Dielectric == r.Dielectric && g.Conductivity == r.Conductivity);
        _customGround = null;
        if (match < 0)
        {
            _customGround = (r.Dielectric, r.Conductivity);
            GroundBox.Items.Add($"As loaded ({N(r.Dielectric)}, {N(r.Conductivity)} S/m)");
            match = GroundBox.Items.Count - 1;
        }
        GroundBox.SelectedIndex = match;
    }

    static double? Read(TextBox box)
    {
        string t = box.Text.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
        return null;
    }

    /// <summary>The settings as typed, or a reason they can't be read.</summary>
    (RadioSettings?, PathOptions?, string?) ReadPathForm()
    {
        string? bad = null;
        double Get(TextBox b, string what) { var v = Read(b); if (v == null) bad ??= $"Enter the {what} as a number."; return v ?? 0; }
        var r = new RadioSettings
        {
            FrequencyMHz = Get(FreqBox, "frequency in MHz"),
            ErpWatts = Get(ErpBox, "ERP in watts"),
            Polarization = PolBox.SelectedIndex == 0 ? 0 : 1,
            Climate = ClimateBox.SelectedIndex + 1,
            Situations = Get(SitBox, "fraction of situations (%)") / 100,
            Time = Get(TimeBox, "fraction of time (%)") / 100,
            Bending = Get(BendBox, "atmospheric bending constant"),
        };
        if (_customGround is { } cg && GroundBox.SelectedIndex == GroundBox.Items.Count - 1) (r.Dielectric, r.Conductivity) = cg;
        else (_, r.Dielectric, r.Conductivity) = RadioSettings.Grounds[Math.Max(0, GroundBox.SelectedIndex)];
        var o = new PathOptions(ModelBox.SelectedIndex == 1 ? PathModel.LongleyRice : PathModel.Itwom, UnitsBox.SelectedIndex == 1,
                                Get(FzBox, "Fresnel zone clearance (%)"), Get(ClutterBox, "ground clutter height"), HdBox.IsChecked == true, _udtFile);
        return bad != null ? (null, null, bad) : (r, o, null);
    }

    void Lrp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Load radio settings from a SPLAT! .lrp file", Filter = "SPLAT! radio settings (*.lrp)|*.lrp|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            FillRadio(LrpFile.Parse(File.ReadAllText(dlg.FileName)));
            PathError.Text = "";
            Status.Text = $"Loaded the radio settings from {Path.GetFileName(dlg.FileName)}. They are saved with the transmitter when you click Analyze.";
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            PathError.Text = $"{Path.GetFileName(dlg.FileName)} couldn't be used: {ex.Message}";
        }
    }

    void ShowTerrain()
    {
        string f = App.TerrainFolder;
        int n = Directory.Exists(f) ? Directory.GetFiles(f, "*.sdf").Length : 0;
        string text = $"{f} ({n} .sdf file{(n == 1 ? "" : "s")}). Missing tiles are downloaded automatically.";
        TerrainText.Text = text;
        TerrainText.ToolTip = f;
        if (CovTerrainText != null) { CovTerrainText.Text = text; CovTerrainText.ToolTip = f; }
    }

    void Terrain_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the folder that holds your SPLAT! terrain (.sdf) files" };
        if (Directory.Exists(App.TerrainFolder)) dlg.InitialDirectory = App.TerrainFolder;
        if (dlg.ShowDialog(this) != true) return;
        try { App.SetTerrainFolder(dlg.FolderName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { PathError.Text = "The choice couldn't be saved: " + ex.Message; }
        ShowTerrain();
    }

    async void Analyse_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        PathError.Text = "";
        var tx = TxBox.SelectedItem as Station;
        var rx = RxBox.SelectedItem as Station;
        var (radio, options, bad) = ReadPathForm();
        if (bad != null) { PathError.Text = bad; return; }
        if (PathJob.Problem(tx, rx, radio!, options!, App.TerrainFolder, App.EngineDir, checkTerrain: false) is { } p) { PathError.Text = p; return; }
        var tiles = PathJob.TilesFor(tx!, rx!, options!);
        if (tiles.Count > (options!.Hd ? Terrain.HdMaxTiles : Terrain.StandardMaxTiles)) { PathError.Text = Terrain.Problem(App.TerrainFolder, tiles, options.Hd); return; }
        _pathCts = new CancellationTokenSource();
        SetBusy(path: true, on: true);
        string? terrainError;
        try { terrainError = await EnsureTerrainAsync(tiles, options.Hd, _pathCts.Token); }
        catch (OperationCanceledException) { Status.Text = "Canceled while fetching terrain; nothing was run."; terrainError = ""; }
        finally { _pathCts.Dispose(); _pathCts = null; SetBusy(path: true, on: false); ShowTerrain(); }
        if (terrainError != null) { if (terrainError.Length > 0) PathError.Text = terrainError; return; }
        if (Terrain.Problem(App.TerrainFolder, tiles, options.Hd) is { } tp) { PathError.Text = tp; return; }

        // The radio settings belong to the transmitter: save them with it.
        var before = tx!.Radio;
        if (before == null || JsonSerializer.Serialize(before) != JsonSerializer.Serialize(radio))
        {
            tx.Radio = radio!.Clone();
            try { _library.Save(); }
            catch (Exception ex) { tx.Radio = before; PathError.Text = "The radio settings couldn't be saved with the station: " + ex.Message; return; }
        }

        PathJob job;
        try { job = PathJob.Create(App.JobsDir, tx, rx!, radio!, options!, App.TerrainFolder, App.EngineDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { PathError.Text = "The job folder couldn't be made: " + ex.Message; return; }

        _pathCts = new CancellationTokenSource();
        SetBusy(path: true, on: true);
        var progress = new Progress<string>(s => Status.Text = s);
        try
        {
            var r = await Task.Run(() => job.RunAsync(progress, _pathCts.Token));
            ShowPathResult(r);
            Status.Text = $"Path analyzed: {r.Summary.TxName} to {r.Summary.RxName}. Job folder: {r.Folder}" +
                          (r.GraphErrors.Count > 0 ? $" ({r.GraphErrors.Count} graph(s) couldn't be drawn; see their tabs)" : "") +
                          (job.KmzNote != null ? ". " + job.KmzNote : "");
        }
        catch (OperationCanceledException) { Status.Text = "Canceled. The unfinished job folder is " + job.Folder; }
        catch (Exception ex) { PathError.Text = ex.Message; Status.Text = "The path analysis didn't finish. Job folder: " + job.Folder; }
        finally
        {
            _pathCts.Dispose(); _pathCts = null;
            SetBusy(path: true, on: false);
        }
    }

    /// <summary>
    /// Fetches and converts any terrain tiles the job needs that the terrain folder lacks. Returns
    /// null when every tile is there (or known to be open water), else why not.
    /// </summary>
    async Task<string?> EnsureTerrainAsync(IReadOnlyList<string> tiles, bool hd, CancellationToken ct)
    {
        var missing = Terrain.Missing(App.TerrainFolder, tiles);
        if (missing.Count == 0) return null;
        Status.Text = $"Fetching {missing.Count} terrain tile(s) into {App.TerrainFolder}…";
        try
        {
            var progress = new Progress<string>(s => Status.Text = s);
            var done = await Task.Run(() => App.Fetcher(App.TerrainFolder).EnsureAsync(missing, hd, progress, ct), ct);
            var bad = done.Where(d => !d.Ok).ToList();
            _lastTerrainNote = string.Join("; ", done.Select(d => $"{d.Tile}: {d.What}"));
            if (bad.Count > 0)
                return $"Terrain couldn't be fetched, so nothing was run: " + string.Join("; ", bad.Select(b => $"{b.Tile}: {b.What}"));
            Status.Text = $"Terrain ready: {_lastTerrainNote}.";
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            return "Terrain couldn't be fetched, so nothing was run: " + ex.Message;
        }
    }

    string? _lastTerrainNote;

    void ShowResults(bool on)
    {
        ResultSplitRow.Height = new GridLength(on ? 5 : 0);
        ResultRow.Height = on ? new GridLength(1.1, GridUnitType.Star) : new GridLength(0);
        ResultTabs.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) SummaryBox.Visibility = Visibility.Collapsed;
    }

    void ShowPathResult(PathResult r)
    {
        _pathResult = r;
        var s = r.Summary;
        SumTitle.Text = $"{s.TxName} to {s.RxName}: {s.Distance}, azimuth {s.AzimuthToRx}";
        SumLos.Text = s.LineOfSightClear
            ? "Line of sight: clear of terrain."
            : $"Line of sight: blocked by terrain at {s.Obstructions.Count} point(s), marked red on the map.";
        var rows = new List<string>();
        void Row(string label, string? v) { if (v != null) rows.Add($"{label,-17}{v}"); }
        Row($"Signal at {Short(s.RxName)}:", s.SignalPower);
        Row("Field strength:", s.FieldStrength);
        Row("Path loss:", s.PathLoss == null ? null : $"{s.PathLoss} ({s.Model})");
        Row("Free-space loss:", s.FreeSpaceLoss);
        Row("Terrain loss:", s.Attenuation);
        Row("Mode:", s.Mode);
        Row("Frequency:", s.Frequency);
        Row("TX ERP:", s.Erp);
        SumNumbers.Text = string.Join("\n", rows);
        bool warn = s.ErrorNumber is > 0;
        SumWarning.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        SumWarning.Text = warn ? $"{s.Model} error number {s.ErrorNumber}. {s.ErrorNote}" : "";
        SumClearance.Text = string.Join("\n", s.Clearance);
        SummaryBox.Visibility = Visibility.Visible;

        foreach (var (kind, view, err) in new[] { (GraphKind.Profile, GraphProfile, ErrProfile), (GraphKind.Elevation, GraphElevation, ErrElevation),
                                                  (GraphKind.Height, GraphHeight, ErrHeight), (GraphKind.Loss, GraphLoss, ErrLoss) })
        {
            view.Data = r.Graphs.TryGetValue(kind, out var g) ? g : null;
            err.Text = r.GraphErrors.TryGetValue(kind, out var m) ? $"The {PathJob.Describe(kind)} graph couldn't be drawn: {m}" : "";
            err.Visibility = err.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ReportBox.Text = r.ReportText.Replace("\r\n", "\n").Replace("\n", "\r\n");
        SetEarthFile(r.KmzPath);
        SumEarth.Text = r.KmzPath != null ? "Open in Google Earth Pro (top of the window) shows both stations, the path and its line-of-sight part." : "";
        ShowResults(true);
        if (ResultTabs.SelectedIndex < 0) ResultTabs.SelectedIndex = 0;

        if (_pageReady && TxBox.SelectedItem is Station tx && RxBox.SelectedItem is Station rx)
        {
            var msg = new
            {
                tx = new { name = tx.Name, lat = tx.Lat, lon = tx.Lon },
                rx = new { name = rx.Name, lat = rx.Lat, lon = rx.Lon },
                clear = s.LineOfSightClear,
                obstructions = s.Obstructions.Select(o => new { lat = o.Lat, lon = o.Lon, label = $"Obstruction: {o.Height}, {o.Distance} from {s.RxName}" }),
            };
            _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.showPath({JsonSerializer.Serialize(msg)})");
        }
    }

    void Udt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose a SPLAT! user terrain file of obstacles (.udt)", Filter = "SPLAT! user terrain (*.udt)|*.udt|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        var (pts, why) = UserTerrain.Parse(File.ReadAllText(dlg.FileName, System.Text.Encoding.Latin1));
        if (why != null) { (PathPanel.Visibility == Visibility.Visible ? PathError : CovError).Text = $"{Path.GetFileName(dlg.FileName)} couldn't be used: {why}"; return; }
        SetUdt(dlg.FileName, $"{Path.GetFileName(dlg.FileName)} ({pts.Count} obstacle{(pts.Count == 1 ? "" : "s")})");
    }

    void UdtClear_Click(object sender, RoutedEventArgs e) => SetUdt(null, "None");

    void SetUdt(string? file, string text)
    {
        _udtFile = file;
        UdtText.Text = CovUdtText.Text = text;
        UdtText.ToolTip = CovUdtText.ToolTip = file;
        UdtClearButton.IsEnabled = CovUdtClearButton.IsEnabled = file != null;
    }

    static string Short(string name) => name.Length > 8 ? name[..8] + "…" : name;

    /// <summary>Stage G8: the 3D view of the path shown, in its own window (decision G10), beside this one.</summary>
    void View3D_Click(object sender, RoutedEventArgs e)
    {
        if (_pathResult is not { } r || !Directory.Exists(r.Folder)) { Status.Text = "Analyze a path first; the 3D view shows the path you analyzed."; return; }
        var w = new View3DWindow(r.Folder);
        w.Show();
    }

    void JobFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_pathResult is { } r && Directory.Exists(r.Folder))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { r.Folder }, UseShellExecute = false });
    }
}
