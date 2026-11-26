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
    private readonly CheckBox _invert = new() { Text = "Invert colors (negative)", AutoSize = true };
    private readonly Label _panLabel = new() { AutoSize = true };
    private readonly CheckBox _breathe = new() { Text = "Make the picture breathe (slow zoom in and out)", AutoSize = true };
    private readonly ComboBox _breathMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly PointPicker _picker = new();
    private readonly RadioButton[] _placeButtons = new RadioButton[4];
    private readonly CheckBox _swirl = new() { Text = "Twist the picture into a swirl", AutoSize = true };
    private readonly ComboBox _swirlCenter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
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
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            ColumnCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(12, 12),
        };
        // name | slider | value | − | + | reset
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        for (int i = 0; i < 3; i++) root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // Scrolls if the window is taller than the screen.
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(root);
        Controls.Add(scroller);
        Load += (_, _) =>
        {
            var pref = root.GetPreferredSize(Size.Empty);
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = pref.Width + 24 + SystemInformation.VerticalScrollBarWidth;
            int h = Math.Min(pref.Height + 24, work.Height - 60);
            ClientSize = new Size(w, h);
            CenterToScreen();
        };

        // --- File
        var choose = new Button { Text = "Choose GIF or video…", AutoSize = true };
        choose.Click += async (_, _) =>
        {
            _app.ChooseFile();
            RefreshAll();
            await Task.Delay(1500); // give the new wallpaper a moment to load
            await LoadFrameAsync();
        };
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
        AddSlider(root, "Contrast", 0, 3, 1, log: false, step: 0.005,
            get: () => _app.Current.Contrast, set: v => _app.UpdateColor(s => s.Contrast = v), fmt: v => Pct(v));
        AddSlider(root, "Tint color", 0, 360, 30, log: false, step: 1,
            get: () => _app.Current.TintHue, set: v => _app.UpdateColor(s => s.TintHue = v),
            fmt: v => TintName(v) + " (" + Num(v) + "°)");
        AddSlider(root, "Tint strength", 0, 1, 0, log: false, step: 0.005,
            get: () => _app.Current.TintStrength, set: v => _app.UpdateColor(s => s.TintStrength = v),
            fmt: v => v == 0 ? "off" : Pct(v));
        AddSlider(root, "Color cycle", 0, 120, 0, log: false, step: 0.5,
            get: () => _app.Current.ColorCycle, set: v => _app.UpdateColor(s => s.ColorCycle = v),
            fmt: v => v == 0 ? "off" : "every " + Num(v) + " s");
        _invert.CheckedChanged += (_, _) => { if (!_loading) _app.UpdateColor(s => s.Invert = _invert.Checked); };
        AddFullRow(root, _invert);

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

        // --- Breathing
        AddFullRow(root, Heading("Breathing"));
        _breathe.CheckedChanged += (_, _) => { if (!_loading) _app.UpdateBreath(s => s.Breathe = _breathe.Checked); };
        AddFullRow(root, _breathe);
        AddSlider(root, "Depth", 0.01, 0.5, 0.08, log: false, step: 0.005,
            get: () => _app.Current.BreatheDepth, set: v => _app.UpdateBreath(s => s.BreatheDepth = v),
            fmt: v => "+" + Pct(v));
        AddSlider(root, "Length", 1, 30, 6, log: false, step: 0.1,
            get: () => _app.Current.BreathePeriod, set: v => _app.UpdateBreath(s => s.BreathePeriod = v),
            fmt: v => Num(v) + " s");

        var modeRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        modeRow.Controls.Add(new Label { Text = "Focus on", AutoSize = true, Margin = new Padding(3, 7, 12, 3) });
        _breathMode.Items.AddRange(Settings.BreatheModeNames);
        _breathMode.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _app.UpdateBreath(s => s.BreatheMode = _breathMode.SelectedIndex);
            _picker.ActiveIndex = _breathMode.SelectedIndex is >= 1 and <= 4 ? _breathMode.SelectedIndex - 1 : -1;
            _picker.Invalidate();
        };
        modeRow.Controls.Add(_breathMode);
        AddFullRow(root, modeRow);

        var placeRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        placeRow.Controls.Add(new Label { Text = "Click the picture to place point", AutoSize = true, Margin = new Padding(3, 7, 6, 3) });
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var rb = new RadioButton
            {
                Text = (i + 1).ToString(), AutoSize = true, Appearance = Appearance.Button,
                MinimumSize = new Size(32, 0), TextAlign = ContentAlignment.MiddleCenter, Checked = i == 0,
            };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { _picker.SelectedIndex = idx; _picker.Invalidate(); } };
            _placeButtons[i] = rb;
            placeRow.Controls.Add(rb);
        }
        var refreshPic = new Button { Text = "Refresh picture", AutoSize = true, Margin = new Padding(12, 3, 3, 3) };
        refreshPic.Click += async (_, _) => await LoadFrameAsync();
        placeRow.Controls.Add(refreshPic);
        AddFullRow(root, placeRow);

        _picker.GetPoints = () => _app.Current.Points.Length == 8 ? _app.Current.Points : new Settings().Points;
        _picker.PointPlaced += (i, x, y) =>
        {
            _app.UpdateBreath(s =>
            {
                if (s.Points.Length != 8) s.Points = new Settings().Points;
                s.Points[i * 2] = Math.Round(x, 4);
                s.Points[i * 2 + 1] = Math.Round(y, 4);
            });
            // move on to the next point, so placing all four is quick
            _placeButtons[(i + 1) % 4].Checked = true;
        };
        AddFullRow(root, _picker);

        // --- Swirl
        AddFullRow(root, Heading("Swirl"));
        _swirl.CheckedChanged += (_, _) => { if (!_loading) _app.UpdateSwirl(s => s.Swirl = _swirl.Checked); };
        AddFullRow(root, _swirl);
        AddSlider(root, "Depth", -3, 3, 1, log: false, step: 0.01,
            get: () => _app.Current.SwirlDepth, set: v => _app.UpdateSwirl(s => s.SwirlDepth = v),
            fmt: v => v == 0 ? "none" : Num(Math.Abs(v)) + (v > 0 ? " turns ⟲" : " turns ⟳"));
        AddSlider(root, "Size", 0.1, 2, 1, log: false, step: 0.01,
            get: () => _app.Current.SwirlSize, set: v => _app.UpdateSwirl(s => s.SwirlSize = v),
            fmt: v => Pct(v));
        AddSlider(root, "Motion", 0, 30, 0, log: false, step: 0.1,
            get: () => _app.Current.SwirlPeriod, set: v => _app.UpdateSwirl(s => s.SwirlPeriod = v),
            fmt: v => v == 0 ? "still" : "every " + Num(v) + " s");
        var swirlRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        swirlRow.Controls.Add(new Label { Text = "Swirl center", AutoSize = true, Margin = new Padding(3, 7, 12, 3) });
        _swirlCenter.Items.AddRange(Settings.SwirlCenterNames);
        _swirlCenter.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading) _app.UpdateSwirl(s => s.SwirlCenter = _swirlCenter.SelectedIndex);
        };
        swirlRow.Controls.Add(_swirlCenter);
        swirlRow.Controls.Add(new Label
        {
            Text = "(points are the breathing focal points above)", AutoSize = true,
            ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3),
        });
        AddFullRow(root, swirlRow);

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
        Shown += async (_, _) => await LoadFrameAsync();
        // Pick up changes made from the tray menu while this window was open.
        Activated += (_, _) => RefreshAll();
    }

    private async Task LoadFrameAsync()
    {
        var img = await _app.GetFrameAsync();
        if (img == null) return;
        _picker.Frame?.Dispose();
        _picker.Frame = img;
        _picker.Invalidate();
    }

    private void ResetAll()
    {
        _app.SetZoom(1);
        _app.SetSpeed(1);
        _app.SetHue(0);
        _app.SetBrightness(1);
        _app.SetSaturation(1);
        _app.UpdateColor(s => { s.Contrast = 1; s.TintStrength = 0; s.Invert = false; s.ColorCycle = 0; });
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
            _invert.Checked = s.Invert;
            _breathe.Checked = s.Breathe;
            _swirl.Checked = s.Swirl;
            _swirlCenter.SelectedIndex = Math.Clamp(s.SwirlCenter, 0, 4);
            _breathMode.SelectedIndex = Math.Clamp(s.BreatheMode, 0, 5);
            _picker.ActiveIndex = s.BreatheMode is >= 1 and <= 4 ? s.BreatheMode - 1 : -1;
            _picker.Invalidate();
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

    private static string TintName(double h)
    {
        h = ((h % 360) + 360) % 360;
        return h < 15 ? "red" : h < 45 ? "orange" : h < 70 ? "yellow" : h < 160 ? "green" : h < 200 ? "cyan"
            : h < 260 ? "blue" : h < 290 ? "purple" : h < 340 ? "pink" : "red";
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Pct(double v) => (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.#", CultureInfo.InvariantCulture);
}
