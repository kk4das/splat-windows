using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplatWin.Core;

/// <summary>A station in the library. Latitude/longitude use ordinary signs (west negative).</summary>
public sealed class Station
{
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double AntennaHeight { get; set; }
    public HeightUnit HeightUnit { get; set; } = HeightUnit.Feet;
    /// <summary>How the position was found, e.g. "Grid FM18jw, moved on map".</summary>
    public string Source { get; set; } = "";
    public DateTime Updated { get; set; }
    /// <summary>Radio settings (the .lrp values) when this station is used as a transmitter; null until first used.</summary>
    public RadioSettings? Radio { get; set; }
    /// <summary>The antenna's radiation pattern (SPLAT!'s .az/.el files), used when this station transmits; null for none (omnidirectional).</summary>
    public AntennaPattern? Pattern { get; set; }

    [JsonIgnore] public double HeightFeet => QthFile.ToFeet(AntennaHeight, HeightUnit);
    [JsonIgnore] public string Grid => Maidenhead.FromLatLon(Lat, Lon, 8);
    [JsonIgnore] public string HeightText => $"{AntennaHeight:0.##} {(HeightUnit == HeightUnit.Meters ? "m" : "ft")}";

    public override string ToString() => Name;   // what screen readers (and UI tests) read in the list

    public QthSite ToQth() => new(Name, Lat, Lon, AntennaHeight, HeightUnit);

    public static Station FromQth(QthSite q, string source) => new()
    {
        Name = q.Name, Lat = q.Lat, Lon = q.Lon, AntennaHeight = q.Height, HeightUnit = q.Unit,
        Source = source, Updated = DateTime.Now,
    };

    /// <summary>
    /// Why this station can't be saved, or null if it can. <paramref name="others"/> are the
    /// library's other stations (names must be unique: they become file names in jobs).
    /// </summary>
    public string? Problem(IEnumerable<Station> others)
    {
        string n = Name.Trim();
        if (n.Length == 0) return "Give the station a name, such as its call sign.";
        if (n.Length > QthFile.MaxLine) return $"The name can have at most {QthFile.MaxLine} characters (SPLAT!'s limit).";
        if (n.Any(c => c < 32 || c > 126)) return "Use plain letters, digits, spaces and punctuation in the name (SPLAT! can't show other characters).";
        if (others.Any(o => !ReferenceEquals(o, this) && string.Equals(o.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
            return $"There is already a station called {n}.";
        if (!(Lat >= -90 && Lat <= 90) || !(Lon >= -180 && Lon <= 180)) return "Place the station on the map first.";
        if (!(AntennaHeight > 0) || double.IsInfinity(AntennaHeight)) return "Enter the antenna height above ground (more than 0).";
        if (HeightFeet > 10000) return "That antenna height is over 10,000 ft. Check the number and the ft/m choice.";
        return null;
    }
}

/// <summary>
/// The stations library: one JSON file (by default Documents\SPLAT\stations.json).
/// Saving writes a temporary file and then swaps it in, keeping the previous version as .bak,
/// so a crash mid-save never loses the library.
/// </summary>
public sealed class StationLibrary
{
    public const string FormatName = "SPLATforWindows.stations";
    public string FilePath { get; }
    public List<Station> Stations { get; } = new();

    StationLibrary(string path) { FilePath = path; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    sealed class FileModel
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public List<Station> Stations { get; set; } = new();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SPLAT", "stations.json");

    /// <summary>A missing file is an empty library. A damaged one throws; it is never overwritten.</summary>
    public static StationLibrary Load(string path)
    {
        var lib = new StationLibrary(path);
        if (!File.Exists(path)) return lib;
        FileModel? m;
        try { m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), Json); }
        catch (JsonException ex) { throw new InvalidDataException($"The stations file {path} is damaged ({ex.Message}). It has been left as it is.", ex); }
        if (m == null || m.Format != FormatName)
            throw new InvalidDataException($"{path} is not a SPLAT! for Windows stations file. It has been left as it is.");
        if (m.Version > 1)
            throw new InvalidDataException($"{path} was written by a newer version of SPLAT! for Windows. It has been left as it is.");
        lib.Stations.AddRange(m.Stations);
        return lib;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        string part = FilePath + ".part";
        var model = new FileModel { Stations = Stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList() };
        File.WriteAllText(part, JsonSerializer.Serialize(model, Json));
        // Read it back before replacing the real file.
        var check = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(part), Json);
        if (check == null || check.Stations.Count != Stations.Count)
        {
            File.Delete(part);
            throw new IOException("The stations file could not be written correctly; the previous version is unchanged.");
        }
        if (File.Exists(FilePath)) File.Replace(part, FilePath, FilePath + ".bak");
        else File.Move(part, FilePath);
    }

    public Station? Find(string name) =>
        Stations.FirstOrDefault(s => string.Equals(s.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A file name for exporting a station as .qth: the name with unsafe characters replaced.</summary>
    public static string QthFileName(string stationName)
    {
        var bad = Path.GetInvalidFileNameChars();
        string n = new string(stationName.Trim().Select(c => bad.Contains(c) ? '-' : c).ToArray());
        return (n.Length == 0 ? "station" : n) + ".qth";
    }
}
