using System.Text.Json;

namespace SplatWin.Core;

/// <summary>The app's own preferences (not stations): kept in %LOCALAPPDATA%\SPLATforWindows\settings.json.</summary>
public sealed class AppSettings
{
    public string? TerrainFolder { get; set; }

    /// <summary>The first-run setup has been finished or skipped (G6), or wasn't needed (stations already there).</summary>
    public bool SetupDone { get; set; }

    // Defaults for new coverage maps (Settings window, G5). Distances in miles and feet unless Metric.
    public double CoverageRadius { get; set; } = 30;
    public double CoverageRxHeight { get; set; } = 6;
    public bool Metric { get; set; }
    public bool TerrainShading { get; set; } = true;
    /// <summary>How opaque coverage maps are drawn over the background map (0 to 1).</summary>
    public double MapOpacity { get; set; } = 0.65;

    /// <summary>Why these values can't be saved, or null.</summary>
    public string? Problem()
    {
        if (!(CoverageRadius > 0 && CoverageRadius <= 1000)) return "The coverage radius should be more than 0 (and at most 1000).";
        if (!(CoverageRxHeight > 0 && CoverageRxHeight <= 3000)) return "The receiver antenna height should be more than 0.";
        if (!(MapOpacity >= 0.1 && MapOpacity <= 1)) return "The map opacity should be between 10% and 100%.";
        return null;
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>A missing or damaged file gives the defaults; settings are never worth refusing to start over.</summary>
    public static AppSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string part = path + ".part";
        File.WriteAllText(part, JsonSerializer.Serialize(this, Json));
        File.Move(part, path, overwrite: true);
    }
}
