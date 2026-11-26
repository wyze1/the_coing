using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace GifWallpaper;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "GifWallpaper";

    private readonly Settings _settings = Settings.Load();
    private readonly NotifyIcon _tray;
    private static readonly double[] Speeds = { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3 };
    private static readonly double[] Zooms = { 0.5, 0.75, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3 };
    private readonly ToolStripMenuItem _fitItem, _startupItem, _speedItem, _zoomItem;
    private readonly List<WallpaperForm> _forms = new();
    private readonly System.Windows.Forms.Timer _coverTimer = new() { Interval = 1500 };
    private readonly ExplorerWatcher _watcher;
    private CoreWebView2Environment? _env;
    private bool _rebuilding, _pending;

    private readonly Control _ui = new();
    private RegisteredWaitHandle? _quitWait;

    public TrayApp(EventWaitHandle quitSignal)
    {
        // A newer copy that starts up signals this event: exit so it can take over.
        _ui.CreateControl();
        _quitWait = ThreadPool.RegisterWaitForSingleObject(quitSignal,
            (_, _) => _ui.BeginInvoke(new Action(ExitApp)), null, Timeout.Infinite, executeOnlyOnce: true);

        // If "Start with Windows" points at another copy of the app (e.g. an older download),
        // point it at this one.
        if (IsStartupEnabled()) SetStartupPath();

        _fitItem = new ToolStripMenuItem("Fit (no crop)", null, (_, _) => ToggleFit())
            { Checked = _settings.Fit };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
            { Checked = IsStartupEnabled() };

        _speedItem = new ToolStripMenuItem("Speed");
        foreach (double sp in Speeds)
        {
            var item = new ToolStripMenuItem(SpeedLabel(sp)) { Tag = sp };
            item.Click += (_, _) => SetSpeed(sp);
            _speedItem.DropDownItems.Add(item);
        }
        UpdateSpeedChecks();

        _zoomItem = new ToolStripMenuItem("Zoom");
        _zoomItem.DropDownItems.Add("Zoom in", null, (_, _) => StepZoom(+1));
        _zoomItem.DropDownItems.Add("Zoom out", null, (_, _) => StepZoom(-1));
        _zoomItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (double z in Zooms)
        {
            var item = new ToolStripMenuItem(ZoomLabel(z) + (z == 1 ? " (normal)" : "")) { Tag = z };
            item.Click += (_, _) => SetZoom(z);
            _zoomItem.DropDownItems.Add(item);
        }
        UpdateZoomChecks();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Choose GIF or video…", null, (_, _) => ChooseFile());
        menu.Items.Add(_speedItem);
        menu.Items.Add(_zoomItem);
        menu.Items.Add(_fitItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "GIF Wallpaper",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ChooseFile();

        _watcher = new ExplorerWatcher(() => _ = RebuildAsync());
        SystemEvents.DisplaySettingsChanged += (_, _) => _ = RebuildAsync();
        _coverTimer.Tick += (_, _) => UpdatePausedState();
        _coverTimer.Start();

        if (_settings.MediaPath != null && File.Exists(_settings.MediaPath))
            _ = RebuildAsync();
        else
            ChooseFile();
    }

    /// <summary>The app icon at the size the tray wants (sharp at any display scaling).</summary>
    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
        return stream != null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    private void ChooseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a GIF or video for your wallpaper",
            Filter = "GIFs and videos|*.gif;*.webp;*.mp4;*.webm;*.mov;*.m4v|All files|*.*",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.MediaPath = dlg.FileName;
        _settings.Save();
        _ = RebuildAsync();
    }

    private static string SpeedLabel(double sp) =>
        sp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×" + (sp == 1 ? " (normal)" : "");

    private void SetSpeed(double sp)
    {
        _settings.Speed = sp;
        _settings.Save();
        UpdateSpeedChecks();
        foreach (var f in _forms) f.SetSpeed(sp); // takes effect immediately, no reload
    }

    private void UpdateSpeedChecks()
    {
        foreach (ToolStripMenuItem item in _speedItem.DropDownItems)
            item.Checked = Math.Abs((double)item.Tag! - _settings.Speed) < 0.001;
        _speedItem.Text = $"Speed ({SpeedLabel(_settings.Speed).Replace(" (normal)", "")})";
    }

    private static string ZoomLabel(double z) => $"{Math.Round(z * 100)}%";

    private void SetZoom(double z)
    {
        _settings.Zoom = z;
        _settings.Save();
        UpdateZoomChecks();
        foreach (var f in _forms) f.SetZoom(z); // instant, no reload
    }

    private void StepZoom(int dir)
    {
        // Move to the next preset above/below the current zoom.
        double cur = _settings.Zoom;
        double next = dir > 0
            ? Zooms.FirstOrDefault(z => z > cur + 0.001, Zooms[^1])
            : Zooms.LastOrDefault(z => z < cur - 0.001, Zooms[0]);
        SetZoom(next);
    }

    private void UpdateZoomChecks()
    {
        foreach (var item in _zoomItem.DropDownItems.OfType<ToolStripMenuItem>())
            if (item.Tag is double z) item.Checked = Math.Abs(z - _settings.Zoom) < 0.001;
        _zoomItem.Text = $"Zoom ({ZoomLabel(_settings.Zoom)})";
    }

    private void ToggleFit()
    {
        _settings.Fit = !_settings.Fit;
        _fitItem.Checked = _settings.Fit;
        _settings.Save();
        _ = RebuildAsync();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) != null;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsStartupEnabled()) key.DeleteValue(RunName, false);
        else SetStartupPath();
        _startupItem.Checked = IsStartupEnabled();
    }

    private static void SetStartupPath()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        string want = $"\"{Environment.ProcessPath}\"";
        if (key.GetValue(RunName) as string != want) key.SetValue(RunName, want);
    }

    private async Task RebuildAsync()
    {
        if (_rebuilding) { _pending = true; return; }
        _rebuilding = true;
        try
        {
            CloseForms();
            string? path = _settings.MediaPath;
            if (path == null || !File.Exists(path)) return;

            await Task.Delay(300); // let Explorer settle after a restart / display change
            if (!Desktop.Attach())
            {
                _tray.ShowBalloonTip(5000, "GIF Wallpaper", "Couldn't find the desktop window.", ToolTipIcon.Error);
                return;
            }

            string pageFolder = Path.Combine(Settings.Folder, "page");
            Directory.CreateDirectory(pageFolder);
            File.WriteAllText(Path.Combine(pageFolder, "index.html"), WallpaperForm.PageHtml);
            string mediaName;
            try
            {
                mediaName = await Task.Run(() => CopyMedia(path, pageFolder));
            }
            catch (Exception ex)
            {
                _tray.ShowBalloonTip(5000, "GIF Wallpaper", "Couldn't read that file: " + ex.Message, ToolTipIcon.Error);
                return;
            }

            try
            {
                _env ??= await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(Settings.Folder, "WebView2"),
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            }
            catch (Exception)
            {
                MessageBox.Show("GIF Wallpaper needs the Microsoft Edge WebView2 Runtime.\n" +
                                "Get it from https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "GIF Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (var screen in Screen.AllScreens)
            {
                var form = new WallpaperForm(screen);
                form.Show();
                Desktop.Place(form.Handle, screen.Bounds);
                _forms.Add(form);
                await form.LoadAsync(_env, pageFolder, mediaName, _settings.Fit, _settings.Speed, _settings.Zoom);
            }
        }
        finally
        {
            _rebuilding = false;
            if (_pending) { _pending = false; _ = RebuildAsync(); }
        }
    }

    /// <summary>
    /// Copies the chosen file next to the page (skipped if already copied), so both are served
    /// from one place. Returns the copy's file name.
    /// </summary>
    private static string CopyMedia(string source, string pageFolder)
    {
        var info = new FileInfo(source);
        string name = "media-" + (info.Length ^ info.LastWriteTimeUtc.Ticks).ToString("x") +
                      info.Extension.ToLowerInvariant();
        string dest = Path.Combine(pageFolder, name);
        if (!File.Exists(dest))
        {
            foreach (var old in Directory.GetFiles(pageFolder, "media-*")) File.Delete(old);
            File.Copy(source, dest + ".tmp", true);
            File.Move(dest + ".tmp", dest, true);
        }
        return name;
    }

    private void CloseForms()
    {
        foreach (var f in _forms) f.Close();
        _forms.Clear();
    }

    /// <summary>Pause a monitor's wallpaper while a maximized or full-screen app covers it.</summary>
    private void UpdatePausedState()
    {
        IntPtr fg = GetForegroundWindow();
        Rectangle? covered = null;
        if (fg != IntPtr.Zero && !IsShellWindow(fg) && GetWindowRect(fg, out RECT r))
        {
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            var screen = Screen.FromHandle(fg);
            if (IsZoomed(fg) || rect.Contains(screen.Bounds)) covered = screen.Bounds;
        }
        foreach (var f in _forms)
            f.SetPaused(covered == f.TargetScreen.Bounds);
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        string c = sb.ToString();
        return c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private void ExitApp()
    {
        _coverTimer.Stop();
        CloseForms();
        Desktop.RefreshStaticWallpaper();
        _tray.Visible = false;
        _tray.Dispose();
        _watcher.DestroyHandle();
        _quitWait?.Unregister(null);
        ExitThread();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
}

/// <summary>Hidden window that notices when Explorer restarts, so the wallpaper can re-attach.</summary>
internal sealed class ExplorerWatcher : NativeWindow
{
    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _onRestart;

    public ExplorerWatcher(Action onRestart)
    {
        _onRestart = onRestart;
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TaskbarCreated) _onRestart();
        base.WndProc(ref m);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string msg);
}
