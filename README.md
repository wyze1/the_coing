<img src="art/icon.png" width="96" align="right">

# GIF Wallpaper

A tiny Android app that plays any GIF, animated WebP, or MP4 video as a live wallpaper. Built for the Galaxy S25+ (works on Android 9+).

## Install
1. On your phone, open this repo's **Releases** page and download `GifWallpaper.apk` from the "latest" release.
2. Open the file. If asked, allow your browser/My Files to "Install unknown apps".

## Use
1. Open **GIF Wallpaper**, tap **Choose from Google Photos** (or **Choose from files**) and pick a GIF or video. Videos play muted and on loop.
2. Pick **Fill screen (crop)** or **Fit (no crop)**, drag **Zoom** (50% to 300%, with **Reset zoom**), drag **Hue** (shift all colors, −180° to +180°) **Brightness** (20% to 200%) and **Saturation** (0% = black & white, up to 200%), **Pan** (sliders, or drag the preview), use the **Flip ↔ / Flip ↕** switches, and **Speed** (0.25× to 3×).
3. Tap **Set as wallpaper** → **Set wallpaper** → choose Home screen or Home and lock screens.

To change the GIF later, just open the app and choose a new one; the wallpaper updates right away.
The animation pauses whenever the wallpaper isn't visible, to save battery.

## Build yourself
`./gradlew assembleRelease` (needs the Android SDK). GitHub Actions builds it on every push.

# Windows 11 version

A tray app that plays a GIF or video (MP4, WebM, MOV) behind your desktop icons, on every monitor.

1. Download `GifWallpaper.exe` from the "latest" release and run it. If SmartScreen warns you, click **More info → Run anyway** (the app isn't code-signed).
2. Pick a GIF or video. It starts playing behind your icons.
3. Right-click the tray icon (near the clock) for: **Choose GIF or video…**, **Speed** (0.25× to 3×), **Zoom** (50% to 300%, plus Zoom in / Zoom out), **Hue**, **Brightness**, **Saturation**, **Pan**, **Flip horizontally**, **Flip vertically**, **Fit (no crop)**, **Start with Windows**, **Exit**.

The animation pauses on any monitor where a maximized or full-screen app covers it. Exiting restores your normal wallpaper.
Source is in `windows/GifWallpaper` (C#/.NET 8 + WebView2).
