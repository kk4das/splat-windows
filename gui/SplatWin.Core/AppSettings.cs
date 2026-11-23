using System.Text.Json;

namespace SplatWin.Core;

/// <summary>The app's own preferences (not stations): kept in %LOCALAPPDATA%\SPLATforWindows\settings.json.</summary>
public sealed class AppSettings
{
    public string? TerrainFolder { get; set; }

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
