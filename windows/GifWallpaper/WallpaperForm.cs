using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GifWallpaper;

/// <summary>One borderless window per monitor, sitting behind the desktop icons.</summary>
internal sealed class WallpaperForm : Form
{
    private const string Host = "app.gifwallpaper";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    public Screen TargetScreen { get; }
    private bool _paused;

    public WallpaperForm(Screen screen)
    {
        TargetScreen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        _web.DefaultBackgroundColor = Color.Black;
        Controls.Add(_web);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <param name="mediaName">File name of the media copy inside <paramref name="pageFolder"/>.</param>
    public async Task LoadAsync(CoreWebView2Environment env, string pageFolder, string mediaName, bool fit, double speed, double zoom, bool flipH, bool flipV, int hue, double brightness)
    {
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        // Page and media are served from the same folder, so the page may read the GIF's bytes.
        core.SetVirtualHostNameToFolderMapping(Host, pageFolder, CoreWebView2HostResourceAccessKind.Allow);

        string src = $"https://{Host}/{Uri.EscapeDataString(mediaName)}";
        bool video = !mediaName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                     && !mediaName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
        core.Navigate($"https://{Host}/index.html#src={Uri.EscapeDataString(src)}" +
                      $"&fit={(fit ? 1 : 0)}&video={(video ? 1 : 0)}&speed={Num(speed)}&zoom={Num(zoom)}" +
                      $"&fh={(flipH ? 1 : 0)}&fv={(flipV ? 1 : 0)}&hue={hue}&bright={Num(brightness)}");
    }

    public void SetFlip(bool h, bool v) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setFlip({(h ? "true" : "false")},{(v ? "true" : "false")})");

    public void SetBrightness(double b) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setBrightness({Num(b)})");

    public void SetHue(int degrees) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setHue({degrees})");

    public void SetZoom(double zoom) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setZoom({Num(zoom)})");

    public void SetSpeed(double speed) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setSpeed({Num(speed)})");

    private static string Num(double d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void SetPaused(bool paused)
    {
        if (paused == _paused || _web.CoreWebView2 == null) return;
        _paused = paused;
        _ = _web.CoreWebView2.ExecuteScriptAsync(paused ? "pauseMedia()" : "playMedia()");
    }

    public const string PageHtml = """
<!doctype html>
<html><head><meta charset="utf-8">
<style>
  html,body{margin:0;height:100%;background:#000;overflow:hidden}
  #m{width:100vw;height:100vh;display:block;object-fit:cover;transform-origin:center;transform:scale(calc(var(--zoom,1) * var(--fx,1)), calc(var(--zoom,1) * var(--fy,1)));filter:hue-rotate(var(--hue,0deg)) brightness(var(--bright,1))}
  body.fit #m{object-fit:contain}
</style></head>
<body>
<script>
  const p = new URLSearchParams(location.hash.slice(1));
  if (p.get('fit') === '1') document.body.classList.add('fit');
  const src = p.get('src');
  let speed = parseFloat(p.get('speed') || '1') || 1;
  let paused = false, resume = null;
  let el;

  function setZoom(z) { document.documentElement.style.setProperty('--zoom', z); }
  setZoom(parseFloat(p.get('zoom') || '1') || 1);
  function setFlip(h, v) {
    const s = document.documentElement.style;
    s.setProperty('--fx', h ? -1 : 1);
    s.setProperty('--fy', v ? -1 : 1);
  }
  setFlip(p.get('fh') === '1', p.get('fv') === '1');
  function setHue(deg) { document.documentElement.style.setProperty('--hue', deg + 'deg'); }
  setHue(parseInt(p.get('hue') || '0') || 0);
  function setBrightness(b) { document.documentElement.style.setProperty('--bright', b); }
  setBrightness(parseFloat(p.get('bright') || '1') || 1);
  function setSpeed(s) { speed = s; if (el && el.tagName === 'VIDEO') el.playbackRate = s; }
  function pauseMedia() { paused = true; if (el && el.tagName === 'VIDEO') el.pause(); }
  function playMedia() {
    paused = false;
    if (el && el.tagName === 'VIDEO') el.play();
    if (resume) { resume(); resume = null; }
  }

  function show(node) { node.id = 'm'; el = node; document.body.appendChild(node); }

  function plainImage() {
    const img = document.createElement('img');
    img.src = src;
    show(img);
  }

  // Draw GIF / animated WebP frames ourselves so the speed can be changed.
  async function animatedImage() {
    if (!('ImageDecoder' in window)) return plainImage();
    try {
      const resp = await fetch(src);
      const type = src.toLowerCase().endsWith('.webp') ? 'image/webp' : 'image/gif';
      const dec = new ImageDecoder({ data: resp.body, type });
      await dec.tracks.ready;
      await dec.completed;
      const count = dec.tracks.selectedTrack.frameCount;
      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d');
      show(canvas);
      let i = 0;
      for (;;) {
        if (paused) await new Promise(r => resume = r);
        const t0 = performance.now();
        const { image } = await dec.decode({ frameIndex: i });
        if (canvas.width !== image.displayWidth || canvas.height !== image.displayHeight) {
          canvas.width = image.displayWidth;
          canvas.height = image.displayHeight;
        }
        ctx.drawImage(image, 0, 0);
        let ms = (image.duration ?? 100000) / 1000;
        if (ms <= 10) ms = 100; // same rule browsers use for very short GIF delays
        image.close();
        if (count <= 1) return;
        i = (i + 1) % count;
        const wait = ms / speed - (performance.now() - t0);
        await new Promise(r => setTimeout(r, Math.max(0, wait)));
      }
    } catch (e) {
      if (!el) plainImage();
    }
  }

  if (p.get('video') === '1') {
    const v = document.createElement('video');
    v.muted = true; v.loop = true; v.autoplay = true; v.playsInline = true;
    v.src = src;
    v.playbackRate = speed;
    v.addEventListener('loadedmetadata', () => v.playbackRate = speed);
    show(v);
  } else {
    animatedImage();
  }
</script>
</body></html>
""";
}
