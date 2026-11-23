namespace GifWallpaper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one copy at a time.
        using var mutex = new Mutex(true, "GifWallpaper.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
