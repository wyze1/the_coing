namespace GifWallpaper;

/// <summary>
/// Shows a still of the wallpaper picture with the 4 numbered breathing focal points.
/// Clicking places the selected point there.
/// </summary>
internal sealed class PointPicker : Control
{
    public Image? Frame { get; set; }
    public Func<double[]> GetPoints { get; set; } = () => new double[8];
    public int ActiveIndex { get; set; } = -1;    // point used for breathing (highlighted)
    public int SelectedIndex { get; set; }        // point a click will place
    public event Action<int, double, double>? PointPlaced;

    public PointPicker()
    {
        DoubleBuffered = true;
        Size = new Size(420, 236);
        Cursor = Cursors.Cross;
        BackColor = Color.FromArgb(32, 32, 32);
    }

    /// <summary>Where the picture is drawn inside the control (fitted, centered).</summary>
    private RectangleF ImageRect()
    {
        if (Frame == null) return new RectangleF(0, 0, Width, Height);
        float k = Math.Min((float)Width / Frame.Width, (float)Height / Frame.Height);
        float w = Frame.Width * k, h = Frame.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = ImageRect();
        if (Frame != null) g.DrawImage(Frame, r);
        else
            TextRenderer.DrawText(g, "No picture yet — choose a file, then click Refresh picture.", Font,
                ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

        var pts = GetPoints();
        float rad = 11 * DeviceDpi / 96f;
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        for (int i = 0; i < 4; i++)
        {
            float x = r.X + (float)pts[i * 2] * r.Width, y = r.Y + (float)pts[i * 2 + 1] * r.Height;
            var fill = i == SelectedIndex ? Color.FromArgb(255, 170, 0)
                : i == ActiveIndex ? Color.FromArgb(255, 92, 158)
                : Color.FromArgb(190, 60, 60, 60);
            using var b = new SolidBrush(fill);
            using var pen = new Pen(Color.White, 2);
            g.FillEllipse(b, x - rad, y - rad, rad * 2, rad * 2);
            g.DrawEllipse(pen, x - rad, y - rad, rad * 2, rad * 2);
            TextRenderer.DrawText(g, (i + 1).ToString(), font, new Rectangle((int)(x - rad), (int)(y - rad), (int)(rad * 2), (int)(rad * 2)),
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var r = ImageRect();
        double fx = Math.Clamp((e.X - r.X) / r.Width, 0, 1);
        double fy = Math.Clamp((e.Y - r.Y) / r.Height, 0, 1);
        PointPlaced?.Invoke(SelectedIndex, fx, fy);
        Invalidate();
    }
}
