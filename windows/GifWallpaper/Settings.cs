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
    public double Brightness { get; set; } = 1.0;
    public double Saturation { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    // Breathing: slow zoom in/out around a focal point (image fractions 0..1).
    public bool Breathe { get; set; }
    public double BreatheDepth { get; set; } = 0.08;   // +8% zoom at the peak
    public double BreathePeriod { get; set; } = 6;     // seconds per breath
    public int BreatheMode { get; set; }               // 0 center, 1-4 that point, 5 cycle
    public double[] Points { get; set; } = { 0.3, 0.3, 0.7, 0.3, 0.3, 0.7, 0.7, 0.7 };

    // Extra color effects.
    public double Contrast { get; set; } = 1.0;
    public double TintHue { get; set; } = 30;        // degrees on the color wheel
    public double TintStrength { get; set; }          // 0..1
    public bool Invert { get; set; }
    public double ColorCycle { get; set; }            // 0 = off, else seconds per full rainbow

    /// <summary>All color settings as JSON for the wallpaper page.</summary>
    public string ColorJson() => JsonSerializer.Serialize(new
    {
        hue = (double)Hue,
        bright = Brightness,
        sat = Saturation,
        contrast = Math.Clamp(Contrast, 0, 3),
        tintHue = TintHue,
        tintStrength = Math.Clamp(TintStrength, 0, 1),
        invert = Invert,
        cycle = Math.Clamp(ColorCycle, 0, 120),
    });

    // Swirl / twirl warp around a center point.
    public bool Swirl { get; set; }
    public double SwirlDepth { get; set; } = 1;    // turns at the center; negative = other way
    public double SwirlSize { get; set; } = 1;     // 1 = half the picture's shorter side
    public int SwirlCenter { get; set; }           // 0 picture center, 1-4 focal point
    public double SwirlPeriod { get; set; }        // 0 = still, else seconds per back-and-forth

    public static readonly string[] SwirlCenterNames =
        { "Center of picture", "Point 1", "Point 2", "Point 3", "Point 4" };

    public string SwirlJson()
    {
        var pts = Points.Length == 8 ? Points : new Settings().Points;
        int c = Math.Clamp(SwirlCenter, 0, 4);
        return JsonSerializer.Serialize(new
        {
            on = Swirl,
            depth = Math.Clamp(SwirlDepth, -3, 3),
            size = Math.Clamp(SwirlSize, 0.1, 2),
            cx = c == 0 ? 0.5 : pts[(c - 1) * 2],
            cy = c == 0 ? 0.5 : pts[(c - 1) * 2 + 1],
            period = Math.Clamp(SwirlPeriod, 0, 30),
        });
    }

    public static readonly string[] BreatheModeNames =
        { "Center", "Point 1", "Point 2", "Point 3", "Point 4", "Cycle through points" };

    /// <summary>Breathing settings as JSON for the wallpaper page.</summary>
    public string BreathJson()
    {
        var pts = Points.Length == 8 ? Points : new Settings().Points;
        return JsonSerializer.Serialize(new
        {
            on = Breathe,
            depth = Math.Clamp(BreatheDepth, 0.01, 0.5),
            period = Math.Clamp(BreathePeriod, 1, 30),
            mode = Math.Clamp(BreatheMode, 0, 5),
            pts = new[] { new[] { pts[0], pts[1] }, new[] { pts[2], pts[3] }, new[] { pts[4], pts[5] }, new[] { pts[6], pts[7] } },
        });
    }

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
