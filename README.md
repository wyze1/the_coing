# GIF Wallpaper

A tiny Android app that plays any GIF (or animated WebP) as a live wallpaper. Built for the Galaxy S25+ (works on Android 9+).

## Install
1. On your phone, open this repo's **Releases** page and download `GifWallpaper.apk` from the "latest" release.
2. Open the file. If asked, allow your browser/My Files to "Install unknown apps".

## Use
1. Open **GIF Wallpaper**, tap **Choose GIF**, pick a file.
2. Pick **Fill screen (crop)** or **Fit (no crop)**.
3. Tap **Set as wallpaper** → **Set wallpaper** → choose Home screen or Home and lock screens.

To change the GIF later, just open the app and choose a new one; the wallpaper updates right away.
The animation pauses whenever the wallpaper isn't visible, to save battery.

## Build yourself
`./gradlew assembleRelease` (needs the Android SDK). GitHub Actions builds it on every push.
