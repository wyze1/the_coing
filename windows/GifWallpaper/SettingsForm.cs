using System.Globalization;

namespace GifWallpaper;

/// <summary>
/// A small window with sliders for every setting. Changes apply to the wallpaper immediately.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayApp _app;
    private readonly Label _fileLabel = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly RadioButton _fill = new() { Text = "Fill screen (crop)", AutoSize = true };
    private readonly RadioButton _fit = new() { Text = "Fit (no crop)", AutoSize = true };
    private readonly CheckBox _flipH = new() { Text = "Flip horizontally", AutoSize = true };
    private readonly CheckBox _flipV = new() { Text = "Flip vertically", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };
    private readonly Label _panLabel = new() { AutoSize = true };
    private readonly List<Action> _refreshers = new();
    private bool _loading;

    public SettingsForm(TrayApp app)
    {
        _app = app;
        Text = "GIF Wallpaper Settings";
        Icon = TrayApp.AppIcon(32);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            ColumnCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        // name | slider | value | − | + | reset
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        for (int i = 0; i < 3; i++) root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(root);

        // --- File
        var choose = new Button { Text = "Choose GIF or video…", AutoSize = true };
        choose.Click += (_, _) => { _app.ChooseFile(); RefreshAll(); };
        AddFullRow(root, Heading("Wallpaper file"));
        AddFullRow(root, _fileLabel);
        AddFullRow(root, choose);

        // --- Layout
        AddFullRow(root, Heading("Picture"));
        var fitRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        fitRow.Controls.Add(_fill);
        fitRow.Controls.Add(_fit);
        _fit.CheckedChanged += (_, _) => { if (!_loading) _app.SetFit(_fit.Checked); };
        AddFullRow(root, fitRow);

        AddSlider(root, "Zoom", 0.1, 10, 1, log: true, step: 0.001,
            get: () => _app.Current.Zoom, set: _app.SetZoom, fmt: v => Pct(v));
        AddSlider(root, "Speed", 0.1, 5, 1, log: true, step: 0.01,
            get: () => _app.Current.Speed, set: _app.SetSpeed, fmt: v => Num(v) + "×");
        AddSlider(root, "Hue", -180, 180, 0, log: false, step: 1,
            get: () => _app.Current.Hue, set: v => _app.SetHue((int)Math.Round(v)),
            fmt: v => v == 0 ? "normal" : (v > 0 ? "+" : "−") + Num(Math.Abs(v)) + "°");
        AddSlider(root, "Brightness", 0, 3, 1, log: false, step: 0.005,
            get: () => _app.Current.Brightness, set: _app.SetBrightness, fmt: v => Pct(v));
        AddSlider(root, "Saturation", 0, 4, 1, log: false, step: 0.005,
            get: () => _app.Current.Saturation, set: _app.SetSaturation,
            fmt: v => v == 0 ? "0% (B&W)" : Pct(v));

        // --- Pan: arrow buttons (hold to repeat) + center
        var panRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        panRow.Controls.Add(new Label { Text = "Pan", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
        foreach (var (text, dx, dy) in new[] { ("←", -0.01, 0.0), ("→", 0.01, 0.0), ("↑", 0.0, -0.01), ("↓", 0.0, 0.01) })
        {
            var b = SmallButton(text);
            HoldToRepeat(b, () => { _app.MovePan(dx, dy); RefreshPan(); });
            panRow.Controls.Add(b);
        }
        var center = SmallButton("Center");
        center.Click += (_, _) => { _app.MovePan(-_app.Current.PanX, -_app.Current.PanY); RefreshPan(); };
        panRow.Controls.Add(center);
        _panLabel.Margin = new Padding(12, 8, 3, 3);
        panRow.Controls.Add(_panLabel);
        AddFullRow(root, panRow);

        var flipRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        flipRow.Controls.Add(_flipH);
        flipRow.Controls.Add(_flipV);
        _flipH.CheckedChanged += (_, _) => { if (!_loading) _app.SetFlip(_flipH.Checked, _flipV.Checked); };
        _flipV.CheckedChanged += (_, _) => { if (!_loading) _app.SetFlip(_flipH.Checked, _flipV.Checked); };
        AddFullRow(root, flipRow);

        // --- App
        AddFullRow(root, Heading("App"));
        _startup.CheckedChanged += (_, _) => { if (!_loading) _app.SetStartup(_startup.Checked); };
        AddFullRow(root, _startup);

        var resetAll = new Button { Text = "Reset all adjustments", AutoSize = true };
        resetAll.Click += (_, _) => ResetAll();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(resetAll);
        buttons.Controls.Add(close);
        AddFullRow(root, buttons);
        CancelButton = close;

        RefreshAll();
        // Pick up changes made from the tray menu while this window was open.
        Activated += (_, _) => RefreshAll();
    }

    private void ResetAll()
    {
        _app.SetZoom(1);
        _app.SetSpeed(1);
        _app.SetHue(0);
        _app.SetBrightness(1);
        _app.SetSaturation(1);
        _app.MovePan(-_app.Current.PanX, -_app.Current.PanY);
        _app.SetFlip(false, false);
        RefreshAll();
    }

    private void RefreshAll()
    {
        _loading = true;
        try
        {
            var s = _app.Current;
            _fileLabel.Text = s.MediaPath ?? "(none chosen yet)";
            _fit.Checked = s.Fit;
            _fill.Checked = !s.Fit;
            _flipH.Checked = s.FlipH;
            _flipV.Checked = s.FlipV;
            _startup.Checked = TrayApp.IsStartupEnabled();
            foreach (var r in _refreshers) r();
            RefreshPan();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshPan()
    {
        var s = _app.Current;
        _panLabel.Text = s.PanX == 0 && s.PanY == 0
            ? "centered"
            : $"{Signed(s.PanX * 100)}% across, {Signed(s.PanY * 100)}% down";
    }

    /// <summary>
    /// One slider row: name, slider, value, −, +, Reset. With <paramref name="log"/> the slider
    /// is on a log scale (fine control near 100% plus a wide range).
    /// </summary>
    private void AddSlider(TableLayoutPanel root, string name, double min, double max, double def,
        bool log, double step, Func<double> get, Action<double> set, Func<double, string> fmt)
    {
        int ticks = log ? 1000 : (int)Math.Round((max - min) / step);
        double ValueAt(int p)
        {
            double raw = log ? min * Math.Pow(max / min, (double)p / ticks) : min + p * step;
            return Math.Clamp(Math.Round(raw / step) * step, min, max);
        }
        int TickOf(double v) => Math.Clamp(log
            ? (int)Math.Round(ticks * Math.Log(v / min) / Math.Log(max / min))
            : (int)Math.Round((v - min) / step), 0, ticks);

        var label = new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) };
        var bar = new TrackBar
        {
            Minimum = 0, Maximum = ticks, TickStyle = TickStyle.None,
            Dock = DockStyle.Fill, SmallChange = 1, LargeChange = Math.Max(1, ticks / 20),
        };
        var value = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        var minus = SmallButton("−");
        var plus = SmallButton("+");
        var reset = SmallButton("Reset");

        bar.ValueChanged += (_, _) =>
        {
            double v = ValueAt(bar.Value);
            value.Text = fmt(v);
            if (!_loading) set(v);
        };
        HoldToRepeat(minus, () => bar.Value = Math.Max(0, bar.Value - 1));
        HoldToRepeat(plus, () => bar.Value = Math.Min(ticks, bar.Value + 1));
        reset.Click += (_, _) => bar.Value = TickOf(def);

        _refreshers.Add(() =>
        {
            bar.Value = TickOf(get());
            value.Text = fmt(get());
        });

        int row = root.RowCount++;
        root.Controls.Add(label, 0, row);
        root.Controls.Add(bar, 1, row);
        root.Controls.Add(value, 2, row);
        root.Controls.Add(minus, 3, row);
        root.Controls.Add(plus, 4, row);
        root.Controls.Add(reset, 5, row);
    }

    private static void AddFullRow(TableLayoutPanel root, Control c)
    {
        int row = root.RowCount++;
        root.Controls.Add(c, 0, row);
        root.SetColumnSpan(c, 6);
    }

    private static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f),
        Margin = new Padding(3, 12, 3, 4),
    };

    private static Button SmallButton(string text) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(32, 0), Margin = new Padding(2, 3, 2, 3),
    };

    /// <summary>Runs the action on press, then repeatedly while the button is held.</summary>
    private static void HoldToRepeat(Button b, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) => { timer.Interval = 60; action(); };
        b.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            action();
            timer.Interval = 400;
            timer.Start();
        };
        b.MouseUp += (_, _) => timer.Stop();
        b.MouseLeave += (_, _) => timer.Stop();
        b.KeyDown += (_, e) => { if (e.KeyCode is Keys.Space or Keys.Enter) action(); };
        b.Disposed += (_, _) => timer.Dispose();
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Pct(double v) => (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.#", CultureInfo.InvariantCulture);
}
