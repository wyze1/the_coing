<img src="art/icon.png" width="96" align="right">

# GIF Wallpaper

A tiny Android app that plays any GIF, animated WebP, or MP4 video as a live wallpaper. Built for the Galaxy S25+ (works on Android 9+).

## Install
1. On your phone, open this repo's **Releases** page and download `GifWallpaper.apk` from the "latest" release.
2. Open the file. If asked, allow your browser/My Files to "Install unknown apps".

## Use
1. Open **GIF Wallpaper**, tap **Choose from Google Photos** (or **Choose from files**) and pick a GIF or video. Videos play muted and on loop.
2. Pick **Fill screen (crop)** or **Fit (no crop)**, then adjust the sliders: **Zoom** (10%–1000%), **Hue** (−180° to +180°), **Brightness** (0%–300%), **Saturation** (0% = black & white, up to 400%), and **Speed** (0.1×–5×). Drag the preview to move (pan) the picture; **Center** resets it, plus the **Flip ↔ / Flip ↕** switches. Each slider has **− / +** buttons for fine steps (hold to repeat) and a reset button.
3. **Breathing** (optional): turn it on to make the picture slowly zoom in and out. Set **Depth** and **Length**, then choose **Focus on**: Center, one of 4 focal points, or **Cycle through points** (each breath moves to the next). To place a point, tap **Set 1–4**, then tap the spot on the preview.
4. **Swirl** (optional): twists the picture around a point. **Depth** = turns of twist (negative = other direction), **Size** = how far it reaches, **Motion** = twist back and forth every N seconds (0 = still), **Swirl center** = picture center or one of the 4 focal points.
5. Tap **Set as wallpaper** → **Set wallpaper** → choose Home screen or Home and lock screens.

To change the GIF later, just open the app and choose a new one; the wallpaper updates right away.
The animation pauses whenever the wallpaper isn't visible, to save battery.

## Build yourself
`./gradlew assembleRelease` (needs the Android SDK). GitHub Actions builds it on every push.

# Windows 11 version

A tray app that plays a GIF or video (MP4, WebM, MOV) behind your desktop icons, on every monitor.

1. Download `GifWallpaper.exe` from the "latest" release and run it. If SmartScreen warns you, click **More info → Run anyway** (the app isn't code-signed).
2. Pick a GIF or video. It starts playing behind your icons.
3. **Double-click the tray icon** (near the clock) to open **Settings**: choose a file, Fill/Fit, sliders for **Zoom** (10%–1000%), **Speed** (0.1×–5×), **Hue**, **Brightness** (0%–300%) and **Saturation** (0%–400%), **Contrast**, **Tint color/strength**, **Color cycle** with − / + / Reset buttons, **Invert colors**, **Pan** arrows, **Flip**, **Breathing** (depth, length, focus mode, and 4 focal points you place by clicking a still of your wallpaper), **Swirl** (depth, size, motion, center), and **Start with Windows**. Everything applies instantly. The right-click tray menu has the same options for quick access.

The animation pauses on any monitor where a maximized or full-screen app covers it. Exiting restores your normal wallpaper.
Source is in `windows/GifWallpaper` (C#/.NET 8 + WebView2).
