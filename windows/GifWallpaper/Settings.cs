using System.Text.Json;

namespace GifWallpaper;

internal sealed class Settings
{
    public string? MediaPath { get; set; }
    public bool Fit { get; set; }
    public double Speed { get; set; } = 1.0;
    public double Zoom { get; set; } = 1.0;
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    public int Hue { get; set; }

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GifWallpaper");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
