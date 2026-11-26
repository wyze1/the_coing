namespace GifWallpaper;

internal static class Program
{
    public const string QuitEventName = "GifWallpaper.Quit";

    [STAThread]
    private static void Main()
    {
        // Only one copy at a time. If another copy is already running (for example an older
        // version that started with Windows), ask it to exit and take over.
        using var mutex = new Mutex(false, "GifWallpaper.SingleInstance");
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        if (!TryAcquire(mutex, 0))
        {
            quit.Set();
            if (!TryAcquire(mutex, 10_000)) return; // other copy didn't exit (very old version)
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp(quit));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool TryAcquire(Mutex m, int timeoutMs)
    {
        try { return m.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; } // previous copy crashed; we own it now
    }
}
