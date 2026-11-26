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
    public async Task LoadAsync(CoreWebView2Environment env, string pageFolder, string mediaName, Settings s)
    {
        bool fit = s.Fit, flipH = s.FlipH, flipV = s.FlipV;
        double speed = s.Speed, zoom = s.Zoom, brightness = s.Brightness, saturation = s.Saturation;
        double panX = s.PanX, panY = s.PanY;
        int hue = s.Hue;
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
                      $"&fh={(flipH ? 1 : 0)}&fv={(flipV ? 1 : 0)}&color={Uri.EscapeDataString(s.ColorJson())}" +
                      $"&sat={Num(saturation)}&px={Num(panX)}&py={Num(panY)}" +
                      $"&breath={Uri.EscapeDataString(s.BreathJson())}" +
                      $"&swirl={Uri.EscapeDataString(s.SwirlJson())}");
    }

    public void SetSwirl(Settings s) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setSwirl({s.SwirlJson()})");

    public void SetBreath(Settings s) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setBreath({s.BreathJson()})");

    /// <summary>A still frame of the wallpaper's picture (without zoom/colors), for placing focal points.</summary>
    public async Task<Image?> GetFrameAsync()
    {
        if (_web.CoreWebView2 == null) return null;
        try
        {
            string json = await _web.CoreWebView2.ExecuteScriptAsync("getFrame()");
            string? dataUrl = System.Text.Json.JsonSerializer.Deserialize<string?>(json);
            if (string.IsNullOrEmpty(dataUrl)) return null;
            byte[] bytes = Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(Image.FromStream(ms));
        }
        catch
        {
            return null;
        }
    }

    public void SetFit(bool fit) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setFit({(fit ? "true" : "false")})");

    public void SetFlip(bool h, bool v) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setFlip({(h ? "true" : "false")},{(v ? "true" : "false")})");

    /// <summary>Hue, brightness, saturation, contrast, tint, invert and color cycling, all at once.</summary>
    public void SetColor(Settings s) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setColor({s.ColorJson()})");

    public void SetPan(double x, double y) =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync($"setPan({Num(x)},{Num(y)})");

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
  #w{position:fixed;inset:0;transform-origin:0 0;will-change:transform}
  #g{position:absolute;left:0;top:0;display:none}
  #m,#g{width:100vw;height:100vh;display:block;object-fit:cover;transform-origin:center;transform:translate(calc(var(--px,0) * 100vw), calc(var(--py,0) * 100vh)) scale(calc(var(--zoom,1) * var(--fx,1)), calc(var(--zoom,1) * var(--fy,1)))}
  body.cm #m,body.cm #g{filter:url(#cm)}
  body.fit #m,body.fit #g{object-fit:contain}
</style></head>
<body>
<script>
  const p = new URLSearchParams(location.hash.slice(1));
  // Current layout, kept so breathing can find where a spot of the picture is on screen.
  const look = { fit: false, zoom: 1, fx: 1, fy: 1, px: 0, py: 0 };
  const wrap = document.createElement('div');
  wrap.id = 'w';
  document.body.appendChild(wrap);
  function setFit(f) { look.fit = f; document.body.classList.toggle('fit', f); }
  setFit(p.get('fit') === '1');
  const src = p.get('src');
  let speed = parseFloat(p.get('speed') || '1') || 1;
  let paused = false, resume = null;
  let el;

  function setZoom(z) { look.zoom = z; document.documentElement.style.setProperty('--zoom', z); }
  setZoom(parseFloat(p.get('zoom') || '1') || 1);
  function setFlip(h, v) {
    look.fx = h ? -1 : 1; look.fy = v ? -1 : 1;
    const s = document.documentElement.style;
    s.setProperty('--fx', h ? -1 : 1);
    s.setProperty('--fy', v ? -1 : 1);
  }
  setFlip(p.get('fh') === '1', p.get('fv') === '1');
  // ---- Colors: one SVG color matrix = hue (+cycling) -> saturation -> brightness -> contrast -> tint -> invert.
  // Same math as the Android app (ColorFx.kt), so both look the same.
  let color = { hue: 0, bright: 1, sat: 1, contrast: 1, tintHue: 30, tintStrength: 0, invert: false, cycle: 0 };
  try { const c = p.get('color'); if (c) color = JSON.parse(c); } catch (e) {}
  const svgNS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(svgNS, 'svg');
  svg.setAttribute('width', '0'); svg.setAttribute('height', '0');
  svg.style.position = 'absolute';
  svg.innerHTML = '<filter id="cm" color-interpolation-filters="sRGB"><feColorMatrix id="cmm" type="matrix" ' +
    'values="1 0 0 0 0 0 1 0 0 0 0 0 1 0 0 0 0 0 1 0"/></filter>';
  document.body.appendChild(svg);
  const cmm = svg.querySelector('#cmm');
  function hueRows(deg) {
    const a = deg * Math.PI / 180, c = Math.cos(a), s = Math.sin(a);
    return [0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928,
            0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283,
            0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072];
  }
  function mul(a, b) {
    const m = [];
    for (let r = 0; r < 3; r++) for (let c = 0; c < 3; c++)
      m.push(a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c]);
    return m;
  }
  function hsv(h) { // fully saturated color at hue h, r/g/b 0..1
    h = ((h % 360) + 360) % 360;
    const f = n => { const k = (n + h / 60) % 6; return 1 - Math.max(0, Math.min(k, 4 - k, 1)); };
    return [f(5), f(3), f(1)];
  }
  function affine(c, now) {
    const hue = c.cycle > 0 ? c.hue + 360 * ((now / 1000 / c.cycle) % 1) : c.hue;
    const s = c.sat;
    const sat = [0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
                 0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
                 0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s];
    let m = mul(sat, hueRows(hue)).map(v => v * c.bright);
    let o = [0, 0, 0];
    const k = c.contrast;
    m = m.map(v => v * k); o = o.map(v => v * k + 0.5 * (1 - k));
    if (c.tintStrength > 0) {
      const t = hsv(c.tintHue), st = c.tintStrength, lum = [0.2126, 0.7152, 0.0722];
      const tm = [];
      for (let r = 0; r < 3; r++) for (let col = 0; col < 3; col++) tm.push((r === col ? 1 - st : 0) + st * t[r] * lum[col]);
      m = mul(tm, m);
      o = [0, 1, 2].map(r => tm[r * 3] * o[0] + tm[r * 3 + 1] * o[1] + tm[r * 3 + 2] * o[2]);
    }
    if (c.invert) { m = m.map(v => -v); o = o.map(v => 1 - v); }
    return [m, o];
  }
  function isIdentity(c) {
    return !(c.cycle > 0) && c.hue === 0 && c.bright === 1 && c.sat === 1 && c.contrast === 1 && !(c.tintStrength > 0) && !c.invert;
  }
  function applyColor(now) {
    if (isIdentity(color)) { document.body.classList.remove('cm'); return; }
    const [m, o] = affine(color, now);
    cmm.setAttribute('values',
      `${m[0]} ${m[1]} ${m[2]} 0 ${o[0]} ${m[3]} ${m[4]} ${m[5]} 0 ${o[1]} ${m[6]} ${m[7]} ${m[8]} 0 ${o[2]} 0 0 0 1 0`);
    document.body.classList.add('cm');
  }
  function setColor(c) { color = c; applyColor(performance.now()); }
  applyColor(performance.now());
  function colorTick(now) {
    requestAnimationFrame(colorTick);
    if (color.cycle > 0 && !paused) applyColor(now);
  }
  requestAnimationFrame(colorTick);
  function setPan(x, y) {
    look.px = x; look.py = y;
    const s = document.documentElement.style;
    s.setProperty('--px', x);
    s.setProperty('--py', y);
  }
  setPan(parseFloat(p.get('px') || '0') || 0, parseFloat(p.get('py') || '0') || 0);
  function setSpeed(s) { speed = s; if (el && el.tagName === 'VIDEO') el.playbackRate = s; }
  function pauseMedia() { paused = true; if (el && el.tagName === 'VIDEO') el.pause(); }
  function playMedia() {
    paused = false;
    if (el && el.tagName === 'VIDEO') el.play();
    if (resume) { resume(); resume = null; }
  }

  function show(node) { node.id = 'm'; el = node; wrap.appendChild(node); }

  // ---- Breathing: slow zoom in/out around a focal point (spots are fractions of the picture).
  let breath = { on: false, depth: 0.08, period: 6, mode: 0, pts: [[.3,.3],[.7,.3],[.3,.7],[.7,.7]] };
  try { const b = p.get('breath'); if (b) breath = JSON.parse(b); } catch (e) {}
  function setBreath(b) { breath = b; if (!b.on) wrap.style.transform = ''; }
  function mediaSize() {
    if (!el) return null;
    const w = el.videoWidth || el.naturalWidth || el.width;
    const h = el.videoHeight || el.naturalHeight || el.height;
    return w && h ? [w, h] : null;
  }
  function imageToScreen(fx, fy) {
    const bw = innerWidth, bh = innerHeight, sz = mediaSize();
    if (!sz) return [bw / 2, bh / 2];
    const c = look.fit ? Math.min(bw / sz[0], bh / sz[1]) : Math.max(bw / sz[0], bh / sz[1]);
    const cw = sz[0] * c, ch = sz[1] * c;
    const x = (bw - cw) / 2 + fx * cw, y = (bh - ch) / 2 + fy * ch;
    // same transform as #m: pan, then zoom/flip around the screen center
    return [bw / 2 + look.px * bw + look.zoom * look.fx * (x - bw / 2),
            bh / 2 + look.py * bh + look.zoom * look.fy * (y - bh / 2)];
  }
  function breathTick(now) {
    if (breath.on && !paused) {
      const ph = now / (breath.period * 1000), cyc = Math.floor(ph), fr = ph - cyc;
      const s = 1 + breath.depth * (1 - Math.cos(2 * Math.PI * fr)) / 2;
      // in cycle mode the point changes exactly when the zoom is back to normal
      const pt = breath.mode === 0 ? [0.5, 0.5] : breath.mode === 5 ? breath.pts[cyc % 4] : breath.pts[breath.mode - 1];
      const [x, y] = imageToScreen(pt[0], pt[1]);
      wrap.style.transform = `translate(${x}px,${y}px) scale(${s}) translate(${-x}px,${-y}px)`;
    }
    requestAnimationFrame(breathTick);
  }
  requestAnimationFrame(breathTick);

  // ---- Swirl: a WebGL canvas drawn on top of the picture, twisting it around a point.
  // It has the same size rules, filters and transforms as the picture, so everything else still applies.
  let swirl = { on: false, depth: 1, size: 1, cx: 0.5, cy: 0.5, period: 0 };
  try { const s = p.get('swirl'); if (s) swirl = JSON.parse(s); } catch (e) {}
  function setSwirl(s) { swirl = s; }
  let gl = null, glCanvas = null, glLoc = null;
  function initGL() {
    glCanvas = document.createElement('canvas');
    glCanvas.id = 'g';
    wrap.appendChild(glCanvas);
    gl = glCanvas.getContext('webgl', { alpha: false, antialias: false });
    if (!gl) return false;
    const vs = `attribute vec2 aPos; varying vec2 vImg;
      void main() { gl_Position = vec4(aPos, 0.0, 1.0); vImg = vec2((aPos.x + 1.0) / 2.0, (1.0 - aPos.y) / 2.0); }`;
    const fs = `precision highp float;
      uniform sampler2D t; uniform vec4 sw; uniform float aspect; varying vec2 vImg;
      void main() {
        vec2 img = vImg;
        vec2 d = (img - sw.xy) * vec2(aspect, 1.0);
        float r = length(d);
        if (sw.z > 0.0 && r < sw.z) {
          float k = 1.0 - r / sw.z;
          float a = -sw.w * k * k;
          float c = cos(a), s = sin(a);
          d = vec2(d.x * c - d.y * s, d.x * s + d.y * c);
          img = sw.xy + d / vec2(aspect, 1.0);
        }
        if (img.x < 0.0 || img.x > 1.0 || img.y < 0.0 || img.y > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); return; }
        gl_FragColor = texture2D(t, img);
      }`;
    const prog = gl.createProgram();
    for (const [type, src] of [[gl.VERTEX_SHADER, vs], [gl.FRAGMENT_SHADER, fs]]) {
      const sh = gl.createShader(type);
      gl.shaderSource(sh, src);
      gl.compileShader(sh);
      gl.attachShader(prog, sh);
    }
    gl.linkProgram(prog);
    gl.useProgram(prog);
    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
    const aPos = gl.getAttribLocation(prog, 'aPos');
    gl.enableVertexAttribArray(aPos);
    gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    glLoc = { sw: gl.getUniformLocation(prog, 'sw'), aspect: gl.getUniformLocation(prog, 'aspect') };
    return true;
  }
  function swirlTick(now) {
    requestAnimationFrame(swirlTick);
    const sz = mediaSize();
    const ready = el && sz && (el.tagName !== 'VIDEO' || el.readyState >= 2) && (el.tagName !== 'IMG' || el.complete);
    if (!swirl.on || swirl.depth === 0 || !ready) { if (glCanvas) glCanvas.style.display = 'none'; return; }
    if (paused) return;
    if (!gl && !initGL()) return;
    const k = Math.min(1, 2048 / Math.max(sz[0], sz[1]));
    const w = Math.round(sz[0] * k), h = Math.round(sz[1] * k);
    if (glCanvas.width !== w || glCanvas.height !== h) { glCanvas.width = w; glCanvas.height = h; }
    gl.viewport(0, 0, w, h);
    try { gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, el); } catch (e) { return; }
    const aspect = sz[0] / sz[1];
    const motion = swirl.period > 0 ? Math.sin(2 * Math.PI * now / (swirl.period * 1000)) : 1;
    gl.uniform4f(glLoc.sw, swirl.cx, swirl.cy, swirl.size * Math.min(aspect, 1) * 0.5, swirl.depth * motion * 2 * Math.PI);
    gl.uniform1f(glLoc.aspect, aspect);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    glCanvas.style.display = 'block';
  }
  requestAnimationFrame(swirlTick);

  // Still frame of the raw picture (for placing focal points in Settings).
  function getFrame() {
    const sz = mediaSize();
    if (!sz) return null;
    const k = Math.min(1, 640 / Math.max(sz[0], sz[1]));
    const c = document.createElement('canvas');
    c.width = Math.round(sz[0] * k); c.height = Math.round(sz[1] * k);
    c.getContext('2d').drawImage(el, 0, 0, c.width, c.height);
    return c.toDataURL('image/jpeg', 0.85);
  }

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
