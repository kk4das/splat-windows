using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SplatWin.Core;

namespace SplatWin.App;

// The stations panel: the library list, and the editor where every way of finding a place
// ends with a pin on the map that the user checks (a call sign gives a mailing address, and a
// grid square only an area).
public partial class MainWindow
{
    StationLibrary? _library;
    Station? _editing;            // null while adding a new station
    double? _pinLat, _pinLon;
    string? _pinSource;           // how the pin's position was found
    bool _pinMoved;               // moved by hand after it was found

    enum Method { CallSign, Grid, Coordinates, Map }
    Method CurrentMethod => (Method)Math.Max(0, MethodTabs.SelectedIndex);

    void LoadLibrary(string path)
    {
        try
        {
            _library = StationLibrary.Load(path);
            RefreshList();
        }
        catch (Exception ex)
        {
            _library = null;
            AddButton.IsEnabled = ImportButton.IsEnabled = false;
            EmptyHint.Text = ex.Message + " Close the app, fix or move the file, and start it again.";
        }
    }

    void RefreshList(Station? select = null)
    {
        if (_library == null) return;
        StationList.ItemsSource = null;
        StationList.ItemsSource = _library.Stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        EmptyHint.Visibility = _library.Stations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (select != null) { StationList.SelectedItem = select; StationList.ScrollIntoView(select); }
        PushStationsToMap();
    }

    void PushStationsToMap()
    {
        if (!_pageReady || _library == null) return;
        var list = _library.Stations.Select(s => new { name = s.Name, lat = s.Lat, lon = s.Lon });
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.setStations({JsonSerializer.Serialize(list)})");
    }

    Station? Selected => StationList.SelectedItem as Station;

    void StationList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        EditButton.IsEnabled = DeleteButton.IsEnabled = ExportButton.IsEnabled = Selected != null;
        if (Selected is { } s && _pageReady)
            _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.focusStation({Num(s.Lat)}, {Num(s.Lon)})");
    }

    void SelectByName(string name)
    {
        if (_library?.Find(name) is { } s && ListPanel.Visibility == Visibility.Visible) StationList.SelectedItem = s;
    }

    // ---- the editor ----

    void Add_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } s) OpenEditor(s);
    }

    void OpenEditor(Station? s)
    {
        if (_library == null) return;
        _editing = s;
        EditorTitle.Text = s == null ? "New station" : "Edit " + s.Name;
        FindBox.Text = "";
        NoteBox.Visibility = Visibility.Collapsed;
        EditorError.Text = "";
        NameBox.Text = s?.Name ?? "";
        HeightBox.Text = s == null ? "" : s.AntennaHeight.ToString("0.##", CultureInfo.InvariantCulture);
        UnitBox.SelectedIndex = s?.HeightUnit == HeightUnit.Meters ? 1 : 0;
        MethodTabs.SelectedIndex = 0;
        ShowMethodHint();
        ClearPin();
        ListPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        if (_pageReady) _ = Web.CoreWebView2.ExecuteScriptAsync("window.splat.setEditing(true)");
        if (s != null)
        {
            SetPin(s.Lat, s.Lon, null, s.Source, zoom: true);
            ConfirmBox.IsChecked = true;   // it was confirmed when it was saved; moving the pin clears this
        }
        FindBox.Focus();
    }

    void CloseEditor()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
        ClearPin();
        if (_pageReady) _ = Web.CoreWebView2.ExecuteScriptAsync("window.splat.setEditing(false)");
    }

    void Cancel_Click(object sender, RoutedEventArgs e) { CloseEditor(); Status.Text = "Nothing was changed."; }

    void Method_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != MethodTabs) return;   // ignore bubbling from inner controls
        ShowMethodHint();
    }

    void ShowMethodHint()
    {
        FindRow.Visibility = CurrentMethod == Method.Map ? Visibility.Collapsed : Visibility.Visible;
        MethodHint.Text = CurrentMethod switch
        {
            Method.CallSign => "A US call sign, such as KK4DAS. Looked up on callook.info (free FCC licence data).",
            Method.Grid => "A Maidenhead grid square of 4 to 10 characters, such as FM18jw. The square is drawn on the map.",
            Method.Coordinates => "Decimal degrees as Google Maps shows them (38.9242, -77.2058), or degrees-minutes-seconds (38°55'27\"N 77°12'21\"W).",
            _ => "Click the map where the antenna is. Zoom in first to be precise.",
        };
    }

    void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Find_Click(sender, e); }
    }

    async void Find_Click(object sender, RoutedEventArgs e)
    {
        EditorError.Text = "";
        string text = FindBox.Text.Trim();
        try
        {
            switch (CurrentMethod)
            {
                case Method.CallSign:
                {
                    FindButton.IsEnabled = false;
                    Status.Text = "Looking up " + text.ToUpperInvariant() + " on callook.info…";
                    CallookResult r;
                    try { r = await Callook.LookupAsync(App.Http, text); }
                    finally { FindButton.IsEnabled = true; }
                    SetPin(r.Lat, r.Lon, null, $"callook.info: {r.Callsign} mailing address", zoom: true);
                    if (NameBox.Text.Trim().Length == 0) NameBox.Text = r.Callsign;
                    ShowNote($"{r.Callsign}, {r.Name}. callook.info puts it at the licence's mailing address" +
                             (r.Address.Length > 0 ? $" ({r.Address})" : "") +
                             ". That is often not where the antenna is; for a club or repeater it is usually the trustee's address. Drag the pin to the antenna.");
                    Status.Text = $"Found {r.Callsign} on callook.info.";
                    break;
                }
                case Method.Grid:
                {
                    var g = Maidenhead.Parse(text);
                    SetPin(g.CenterLat, g.CenterLon, g, $"Grid {g.Locator}", zoom: true);
                    string advice = g.Locator.Length <= 6 ? " That's too coarse for a VHF path: drag the pin to the antenna."
                                  : g.Locator.Length == 8 ? " Check that the pin is on the antenna." : "";
                    ShowNote($"{g.Locator} is a square {g.SizeText()}. The pin is at its centre.{advice}");
                    Status.Text = $"Grid square {g.Locator}.";
                    break;
                }
                case Method.Coordinates:
                {
                    var (lat, lon) = Coordinates.Parse(text);
                    SetPin(lat, lon, null, "Typed coordinates", zoom: true);
                    if (lon > 0) ShowNote("This longitude is EAST (positive). Longitudes in the Americas are negative (west), as in -77.2058.");
                    else NoteBox.Visibility = Visibility.Collapsed;
                    Status.Text = "Placed at " + Coordinates.Format(lat, lon) + ".";
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or LookupException)
        {
            EditorError.Text = ex.Message;
            Status.Text = "";
        }
    }

    void ShowNote(string text)
    {
        NoteText.Text = text;
        NoteBox.Visibility = Visibility.Visible;
    }

    /// <summary>Places the pin from a lookup. Moving it by hand afterwards is reported by the page.</summary>
    void SetPin(double lat, double lon, GridSquare? square, string source, bool zoom)
    {
        _pinLat = lat; _pinLon = lon; _pinSource = source; _pinMoved = false;
        ConfirmBox.IsChecked = false;
        ConfirmBox.IsEnabled = true;
        ShowPinText();
        if (!_pageReady) return;
        string rect = square is { } g ? $"[[{Num(g.South)},{Num(g.West)}],[{Num(g.North)},{Num(g.East)}]]" : "null";
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.splat.setPin({Num(lat)}, {Num(lon)}, {rect}, {(zoom ? "true" : "false")})");
    }

    /// <summary>The user dragged the pin or clicked the map.</summary>
    void PinMovedOnMap(double lat, double lon)
    {
        if (EditorPanel.Visibility != Visibility.Visible) return;
        if (_pinSource == null) _pinSource = "Map click";
        else _pinMoved = true;
        _pinLat = lat; _pinLon = lon;
        ConfirmBox.IsEnabled = true;
        ConfirmBox.IsChecked = false;
        ShowPinText();
    }

    void ClearPin()
    {
        _pinLat = _pinLon = null; _pinSource = null; _pinMoved = false;
        ConfirmBox.IsChecked = false;
        ConfirmBox.IsEnabled = false;
        PinText.Text = "No pin yet.";
        if (_pageReady) _ = Web.CoreWebView2.ExecuteScriptAsync("window.splat.clearPin()");
    }

    void ShowPinText()
    {
        if (_pinLat is not double lat || _pinLon is not double lon) return;
        PinText.Text = $"{Coordinates.Format(lat, lon)}\nGrid {Maidenhead.FromLatLon(lat, lon, 8)}";
    }

    string FinalSource()
    {
        string s = _pinSource ?? "";
        if (_pinMoved && s != "Map click" && !s.EndsWith(", moved on map", StringComparison.Ordinal)) s += ", moved on map";
        return s;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        EditorError.Text = "";
        if (_pinLat is not double lat || _pinLon is not double lon)
        { EditorError.Text = "Find the place first (or click the map), so there is a pin."; return; }
        if (ConfirmBox.IsChecked != true)
        { EditorError.Text = "Check that the pin is on the antenna (drag it if not), then tick \"The pin is on the antenna site\"."; return; }
        string ht = HeightBox.Text.Trim();
        if (!double.TryParse(ht, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)
            && !double.TryParse(ht, NumberStyles.Float, CultureInfo.CurrentCulture, out h))
        { EditorError.Text = "Enter the antenna height above ground as a number, such as 30."; return; }

        var candidate = new Station
        {
            Name = NameBox.Text.Trim(), Lat = lat, Lon = lon, AntennaHeight = h,
            HeightUnit = UnitBox.SelectedIndex == 1 ? HeightUnit.Meters : HeightUnit.Feet,
            Source = FinalSource(), Updated = DateTime.Now,
        };
        if (candidate.Problem(_library.Stations.Where(x => !ReferenceEquals(x, _editing))) is { } problem)
        { EditorError.Text = problem; return; }

        var before = _library.Stations.ToList();
        Station saved;
        if (_editing != null)
        {
            int i = _library.Stations.IndexOf(_editing);
            _library.Stations[i] = candidate;
            saved = candidate;
        }
        else { _library.Stations.Add(candidate); saved = candidate; }
        try { _library.Save(); }
        catch (Exception ex)
        {
            _library.Stations.Clear(); _library.Stations.AddRange(before);
            EditorError.Text = "The station couldn't be saved: " + ex.Message;
            return;
        }
        CloseEditor();
        RefreshList(saved);
        Status.Text = $"Saved {saved.Name} ({saved.Grid}, antenna {saved.HeightText}) to {_library.FilePath}.";
    }

    // ---- delete, import, export ----

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null || Selected is not { } s) return;
        if (MessageBox.Show(this, $"Delete the station {s.Name}?", "SPLAT! for Windows", MessageBoxButton.YesNo,
                            MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _library.Stations.Remove(s);
        try { _library.Save(); }
        catch (Exception ex)
        {
            _library.Stations.Add(s);
            Status.Text = "The station couldn't be deleted: " + ex.Message;
            return;
        }
        RefreshList();
        Status.Text = $"Deleted {s.Name}. The previous list is kept as {Path.GetFileName(_library.FilePath)}.bak.";
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Import SPLAT! site files",
            Filter = "SPLAT! site files (*.qth)|*.qth|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        var added = new List<Station>();
        var problems = new List<string>();
        foreach (string f in dlg.FileNames)
        {
            string file = Path.GetFileName(f);
            try
            {
                var st = Station.FromQth(QthFile.Parse(File.ReadAllText(f)), "Imported from " + file);
                if (_library.Find(st.Name) is { } existing)
                {
                    var answer = MessageBox.Show(this, $"There is already a station called {existing.Name}. Replace it with the one in {file}?",
                        "SPLAT! for Windows", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
                    if (answer == MessageBoxResult.Cancel) break;
                    if (answer != MessageBoxResult.Yes) continue;
                    _library.Stations.Remove(existing);
                }
                if (st.Problem(_library.Stations) is { } p) { problems.Add($"{file}: {p}"); continue; }
                _library.Stations.Add(st);
                added.Add(st);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file}: {ex.Message}");
            }
        }
        if (added.Count > 0)
        {
            try { _library.Save(); }
            catch (Exception ex)
            {
                _library.Stations.Clear();
                _library.Stations.AddRange(StationLibrary.Load(_library.FilePath).Stations);
                Status.Text = "The imported stations couldn't be saved: " + ex.Message;
                RefreshList();
                return;
            }
        }
        RefreshList(added.LastOrDefault());
        string summary = added.Count == 0 ? "No stations imported."
            : $"Imported {string.Join(", ", added.Select(s => $"{s.Name} ({s.HeightText})"))}. A .qth height is in feet unless the file says meters; check them.";
        Status.Text = summary;
        if (problems.Count > 0)
            MessageBox.Show(this, summary + "\n\nNot imported:\n" + string.Join("\n", problems), "SPLAT! for Windows",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        var dlg = new SaveFileDialog
        {
            Title = $"Export {s.Name} as a SPLAT! site file",
            Filter = "SPLAT! site files (*.qth)|*.qth",
            FileName = StationLibrary.QthFileName(s.Name),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, QthFile.Format(s.ToQth()));
            Status.Text = $"Wrote {dlg.FileName} (longitude written west-positive, as SPLAT! expects).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"Couldn't write {dlg.FileName}: {ex.Message}";
        }
    }

    static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
