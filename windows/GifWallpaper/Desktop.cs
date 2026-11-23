using System.Runtime.InteropServices;

namespace GifWallpaper;

/// <summary>
/// Finds the desktop window that sits behind the icons and places our windows inside it.
/// Handles both the classic layout and the Windows 11 24H2+ layout.
/// </summary>
internal static class Desktop
{
    public static IntPtr Parent { get; private set; }
    private static IntPtr _shellView;
    private static bool _raised; // Windows 11 24H2+ layout

    public static bool Attach()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return false;

        // Ask Explorer to create the WorkerW window behind the icons.
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        _shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (_shellView != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero)
        {
            // 24H2+: icons and WorkerW are both children of Progman.
            _raised = true;
            Parent = progman;
            return true;
        }

        _raised = false;
        IntPtr workerW = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                workerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        Parent = workerW;
        return workerW != IntPtr.Zero;
    }

    /// <summary>Puts a window behind the icons, covering the given screen area (physical pixels).</summary>
    public static void Place(IntPtr hwnd, Rectangle screenBounds)
    {
        if (_raised)
        {
            // Layered style is required for the window to render under the icons on 24H2+.
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        }

        SetParent(hwnd, Parent);

        GetWindowRect(Parent, out RECT pr);
        int x = screenBounds.X - pr.Left;
        int y = screenBounds.Y - pr.Top;
        IntPtr after = _raised ? _shellView : IntPtr.Zero; // just below the icons
        SetWindowPos(hwnd, after, x, y, screenBounds.Width, screenBounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Re-applies the normal wallpaper so no leftover frame stays on screen.</summary>
    public static void RefreshStaticWallpaper()
    {
        var sb = new System.Text.StringBuilder(520);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, sb, SPIF_SENDCHANGE);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const uint SPI_GETDESKWALLPAPER = 0x73, SPI_SETDESKWALLPAPER = 0x14, SPIF_SENDCHANGE = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint param, System.Text.StringBuilder? pv, uint winIni);
}
