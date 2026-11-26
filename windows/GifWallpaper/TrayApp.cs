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
    private static readonly int[] Hues = { 0, 30, 60, 90, 120, 150, 180, -150, -120, -90, -60, -30 };
    private static readonly double[] Brights = { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2 };
    private static readonly double[] Sats = { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2 };
    private readonly ToolStripMenuItem _fitItem, _startupItem, _speedItem, _zoomItem, _flipHItem, _flipVItem, _hueItem,
        _brightItem, _satItem, _panItem;
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
        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold); // default (double-click) action
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Choose GIF or video…", null, (_, _) => ChooseFile());
        menu.Items.Add(_speedItem);
        menu.Items.Add(_zoomItem);
        _flipHItem = new ToolStripMenuItem("Flip horizontally", null, (_, _) => ToggleFlip(h: true))
            { Checked = _settings.FlipH };
        _flipVItem = new ToolStripMenuItem("Flip vertically", null, (_, _) => ToggleFlip(h: false))
            { Checked = _settings.FlipV };
        _hueItem = new ToolStripMenuItem("Hue");
        _hueItem.DropDownItems.Add("Shift +15°", null, (_, _) => SetHue(_settings.Hue + 15));
        _hueItem.DropDownItems.Add("Shift −15°", null, (_, _) => SetHue(_settings.Hue - 15));
        _hueItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (int deg in Hues)
        {
            var item = new ToolStripMenuItem(HueLabel(deg)) { Tag = deg };
            item.Click += (_, _) => SetHue(deg);
            _hueItem.DropDownItems.Add(item);
        }
        UpdateHueChecks();
        menu.Items.Add(_hueItem);
        _brightItem = new ToolStripMenuItem("Brightness");
        _brightItem.DropDownItems.Add("Brighter (+10%)", null, (_, _) => SetBrightness(_settings.Brightness + 0.1));
        _brightItem.DropDownItems.Add("Darker (−10%)", null, (_, _) => SetBrightness(_settings.Brightness - 0.1));
        _brightItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (double b in Brights)
        {
            var item = new ToolStripMenuItem(BrightLabel(b) + (b == 1 ? " (normal)" : "")) { Tag = b };
            item.Click += (_, _) => SetBrightness(b);
            _brightItem.DropDownItems.Add(item);
        }
        UpdateBrightChecks();
        menu.Items.Add(_brightItem);

        _satItem = new ToolStripMenuItem("Saturation");
        _satItem.DropDownItems.Add("More vivid (+10%)", null, (_, _) => SetSaturation(_settings.Saturation + 0.1));
        _satItem.DropDownItems.Add("Less vivid (−10%)", null, (_, _) => SetSaturation(_settings.Saturation - 0.1));
        _satItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (double s in Sats)
        {
            string extra = s == 0 ? " (black & white)" : s == 1 ? " (normal)" : "";
            var item = new ToolStripMenuItem(BrightLabel(s) + extra) { Tag = s };
            item.Click += (_, _) => SetSaturation(s);
            _satItem.DropDownItems.Add(item);
        }
        UpdateSatChecks();
        menu.Items.Add(_satItem);

        _panItem = new ToolStripMenuItem("Pan");
        _panItem.DropDownItems.Add("Move left", null, (_, _) => MovePan(-0.05, 0));
        _panItem.DropDownItems.Add("Move right", null, (_, _) => MovePan(0.05, 0));
        _panItem.DropDownItems.Add("Move up", null, (_, _) => MovePan(0, -0.05));
        _panItem.DropDownItems.Add("Move down", null, (_, _) => MovePan(0, 0.05));
        _panItem.DropDownItems.Add(new ToolStripSeparator());
        _panItem.DropDownItems.Add("Center", null, (_, _) => MovePan(-_settings.PanX, -_settings.PanY));
        _panItem.DropDown.Closing += (_, e) =>
        {
            // Keep the menu open while nudging, so several moves don't need re-opening it.
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };
        UpdatePanText();
        menu.Items.Add(_panItem);
        menu.Items.Add(_flipHItem);
        menu.Items.Add(_flipVItem);
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
        _tray.DoubleClick += (_, _) => OpenSettings();

        _watcher = new ExplorerWatcher(() => _ = RebuildAsync());
        SystemEvents.DisplaySettingsChanged += (_, _) => _ = RebuildAsync();
        _coverTimer.Tick += (_, _) => UpdatePausedState();
        _coverTimer.Start();

        if (_settings.MediaPath != null && File.Exists(_settings.MediaPath))
            _ = RebuildAsync();
        else
        {
            ChooseFile();
            OpenSettings();
        }
    }

    /// <summary>The app icon at the size the tray wants (sharp at any display scaling).</summary>
    private static Icon LoadTrayIcon() => AppIcon(SystemInformation.SmallIconSize.Width);

    /// <summary>The app icon at (about) the given pixel size.</summary>
    internal static Icon AppIcon(int size)
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
        return stream != null ? new Icon(stream, size, size) : SystemIcons.Application;
    }

    internal Settings Current => _settings;

    private SettingsForm? _settingsForm;

    /// <summary>Opens the settings window (or brings it to the front if already open).</summary>
    internal void OpenSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(this);
            _settingsForm.Show();
        }
        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    internal void ChooseFile()
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

    internal void SetSpeed(double sp)
    {
        sp = Math.Round(Math.Clamp(sp, 0.1, 5.0), 2);
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

    internal void SetZoom(double z)
    {
        z = Math.Round(Math.Clamp(z, 0.1, 10.0), 3);
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

    private static string BrightLabel(double b) => $"{Math.Round(b * 100)}%";

    internal void SetBrightness(double b)
    {
        b = Math.Round(Math.Clamp(b, 0, 3.0), 3);
        _settings.Brightness = b;
        _settings.Save();
        UpdateBrightChecks();
        foreach (var f in _forms) f.SetBrightness(b); // instant
    }

    private void UpdateBrightChecks()
    {
        foreach (var item in _brightItem.DropDownItems.OfType<ToolStripMenuItem>())
            if (item.Tag is double b) item.Checked = Math.Abs(b - _settings.Brightness) < 0.001;
        _brightItem.Text = $"Brightness ({BrightLabel(_settings.Brightness)})";
    }

    internal void SetSaturation(double s)
    {
        s = Math.Round(Math.Clamp(s, 0, 4.0), 3);
        _settings.Saturation = s;
        _settings.Save();
        UpdateSatChecks();
        foreach (var f in _forms) f.SetSaturation(s); // instant
    }

    private void UpdateSatChecks()
    {
        foreach (var item in _satItem.DropDownItems.OfType<ToolStripMenuItem>())
            if (item.Tag is double s) item.Checked = Math.Abs(s - _settings.Saturation) < 0.001;
        _satItem.Text = $"Saturation ({BrightLabel(_settings.Saturation)})";
    }

    internal void MovePan(double dx, double dy)
    {
        _settings.PanX = Math.Round(Math.Clamp(_settings.PanX + dx, -1, 1), 3);
        _settings.PanY = Math.Round(Math.Clamp(_settings.PanY + dy, -1, 1), 3);
        _settings.Save();
        UpdatePanText();
        foreach (var f in _forms) f.SetPan(_settings.PanX, _settings.PanY); // instant
    }

    private void UpdatePanText() =>
        _panItem.Text = _settings.PanX == 0 && _settings.PanY == 0
            ? "Pan (centered)"
            : $"Pan ({Math.Round(_settings.PanX * 100):+0;-0;0}%, {Math.Round(_settings.PanY * 100):+0;-0;0}%)";

    private static string HueLabel(int deg) => deg == 0 ? "Normal" : (deg > 0 ? "+" : "−") + Math.Abs(deg) + "°";

    internal void SetHue(int deg)
    {
        // Keep within -180..180 so it wraps around the color wheel.
        deg = ((deg + 180) % 360 + 360) % 360 - 180;
        _settings.Hue = deg;
        _settings.Save();
        UpdateHueChecks();
        foreach (var f in _forms) f.SetHue(deg); // instant
    }

    private void UpdateHueChecks()
    {
        foreach (var item in _hueItem.DropDownItems.OfType<ToolStripMenuItem>())
            if (item.Tag is int d) item.Checked = d == _settings.Hue || (Math.Abs(d) == 180 && Math.Abs(_settings.Hue) == 180);
        _hueItem.Text = $"Hue ({HueLabel(_settings.Hue)})";
    }

    private void ToggleFlip(bool h) =>
        SetFlip(h ? !_settings.FlipH : _settings.FlipH, h ? _settings.FlipV : !_settings.FlipV);

    internal void SetFlip(bool flipH, bool flipV)
    {
        _settings.FlipH = flipH;
        _settings.FlipV = flipV;
        _settings.Save();
        _flipHItem.Checked = _settings.FlipH;
        _flipVItem.Checked = _settings.FlipV;
        foreach (var f in _forms) f.SetFlip(_settings.FlipH, _settings.FlipV); // instant
    }

    private void ToggleFit() => SetFit(!_settings.Fit);

    internal void SetFit(bool fit)
    {
        _settings.Fit = fit;
        _fitItem.Checked = fit;
        _settings.Save();
        foreach (var f in _forms) f.SetFit(fit); // instant
    }

    internal static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) != null;
    }

    private void ToggleStartup() => SetStartup(!IsStartupEnabled());

    internal void SetStartup(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) SetStartupPath();
        else key.DeleteValue(RunName, false);
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
                await form.LoadAsync(_env, pageFolder, mediaName, _settings.Fit, _settings.Speed, _settings.Zoom,
                    _settings.FlipH, _settings.FlipV, _settings.Hue,
                    _settings.Brightness, _settings.Saturation, _settings.PanX, _settings.PanY);
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
        _settingsForm?.Close();
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
