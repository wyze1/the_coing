using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GifWallpaper;

/// <summary>One borderless window per monitor, sitting behind the desktop icons.</summary>
internal sealed class WallpaperForm : Form
{
    private const string PageHost = "app.gifwallpaper";
    private const string MediaHost = "media.gifwallpaper";

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

    public async Task LoadAsync(CoreWebView2Environment env, string pageFolder, string mediaPath, bool fit)
    {
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(PageHost, pageFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping(MediaHost, Path.GetDirectoryName(mediaPath)!,
            CoreWebView2HostResourceAccessKind.Allow);

        string src = $"https://{MediaHost}/{Uri.EscapeDataString(Path.GetFileName(mediaPath))}";
        bool video = !mediaPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                     && !mediaPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
        core.Navigate($"https://{PageHost}/index.html#src={Uri.EscapeDataString(src)}" +
                      $"&fit={(fit ? 1 : 0)}&video={(video ? 1 : 0)}");
    }

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
  #m{width:100vw;height:100vh;display:block;object-fit:cover}
  body.fit #m{object-fit:contain}
</style></head>
<body>
<script>
  const p = new URLSearchParams(location.hash.slice(1));
  if (p.get('fit') === '1') document.body.classList.add('fit');
  let el;
  if (p.get('video') === '1') {
    el = document.createElement('video');
    el.muted = true; el.loop = true; el.autoplay = true; el.playsInline = true;
  } else {
    el = document.createElement('img');
  }
  el.id = 'm';
  el.src = p.get('src');
  document.body.appendChild(el);
  // GIFs can't be paused in an <img>, so hide them (stops rendering) instead.
  function pauseMedia(){ if (el.pause) el.pause(); else el.style.visibility='hidden'; }
  function playMedia(){ if (el.play) el.play(); else el.style.visibility='visible'; }
</script>
</body></html>
""";
}
