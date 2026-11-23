package com.gifwall

import android.content.SharedPreferences
import android.graphics.Color
import android.graphics.ImageDecoder
import android.graphics.drawable.AnimatedImageDrawable
import android.graphics.drawable.Drawable
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.os.Handler
import android.os.Looper
import android.service.wallpaper.WallpaperService
import android.view.SurfaceHolder
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

class GifWallpaperService : WallpaperService() {

    override fun onCreateEngine(): Engine = GifEngine()

    inner class GifEngine : Engine(),
        SharedPreferences.OnSharedPreferenceChangeListener,
        Drawable.Callback {

        private val handler = Handler(Looper.getMainLooper())
        private val prefs = Prefs.get(this@GifWallpaperService)
        private var drawable: Drawable? = null
        private var player: MediaPlayer? = null
        private var isVideo = false
        private var hasSurface = false
        private var playerReady = false
        private var visible = false
        private var surfaceW = 0
        private var surfaceH = 0

        override fun onCreate(surfaceHolder: SurfaceHolder) {
            super.onCreate(surfaceHolder)
            prefs.registerOnSharedPreferenceChangeListener(this)
            load()
        }

        override fun onDestroy() {
            prefs.unregisterOnSharedPreferenceChangeListener(this)
            release()
            super.onDestroy()
        }

        override fun onSharedPreferenceChanged(p: SharedPreferences?, key: String?) {
            load()
            drawFrame()
        }

        override fun onSurfaceCreated(holder: SurfaceHolder) {
            super.onSurfaceCreated(holder)
            hasSurface = true
            if (isVideo) startVideo()
        }

        override fun onVisibilityChanged(isVisible: Boolean) {
            visible = isVisible
            val anim = drawable as? AnimatedImageDrawable
            if (isVisible) {
                anim?.start()
                if (playerReady) runCatching { player?.start() }
                drawFrame()
            } else {
                // Pause when the screen is off or an app covers the wallpaper.
                anim?.stop()
                if (playerReady) runCatching { player?.pause() }
                handler.removeCallbacksAndMessages(null)
            }
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            surfaceW = width
            surfaceH = height
            updateBounds()
            drawFrame()
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder) {
            visible = false
            hasSurface = false
            releasePlayer()
            (drawable as? AnimatedImageDrawable)?.stop()
            handler.removeCallbacksAndMessages(null)
            super.onSurfaceDestroyed(holder)
        }

        private fun load() {
            release()
            val ctx = this@GifWallpaperService
            val file = Prefs.mediaFile(ctx)
            if (!file.exists()) return
            isVideo = Prefs.isVideo(ctx)
            if (isVideo) {
                if (hasSurface) startVideo()
                return
            }
            drawable = runCatching {
                ImageDecoder.decodeDrawable(ImageDecoder.createSource(file))
            }.getOrNull()?.also { d ->
                d.callback = this
                (d as? AnimatedImageDrawable)?.let {
                    it.repeatCount = AnimatedImageDrawable.REPEAT_INFINITE
                    if (visible) it.start()
                }
            }
            updateBounds()
        }

        private fun startVideo() {
            releasePlayer()
            val file = Prefs.mediaFile(this@GifWallpaperService)
            player = runCatching {
                MediaPlayer().apply {
                    setAudioAttributes(
                        AudioAttributes.Builder()
                            .setUsage(AudioAttributes.USAGE_MEDIA)
                            .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE)
                            .build()
                    )
                    setSurface(surfaceHolder.surface)
                    setDataSource(file.path)
                    isLooping = true
                    setVolume(0f, 0f)
                    // Videos always fill the screen (cropping the edges).
                    setVideoScalingMode(MediaPlayer.VIDEO_SCALING_MODE_SCALE_TO_FIT_WITH_CROPPING)
                    setOnPreparedListener {
                        playerReady = true
                        if (visible) it.start()
                    }
                    setOnErrorListener { _, _, _ -> true }
                    prepareAsync()
                }
            }.getOrNull()
        }

        private fun releasePlayer() {
            player?.let { runCatching { it.reset() }; it.release() }
            player = null
            playerReady = false
        }

        private fun release() {
            releasePlayer()
            isVideo = false
            handler.removeCallbacksAndMessages(null)
            drawable?.let {
                (it as? AnimatedImageDrawable)?.stop()
                it.callback = null
            }
            drawable = null
        }

        private fun updateBounds() {
            val d = drawable ?: return
            if (surfaceW == 0 || surfaceH == 0) return
            val iw = d.intrinsicWidth.takeIf { it > 0 } ?: surfaceW
            val ih = d.intrinsicHeight.takeIf { it > 0 } ?: surfaceH
            val sx = surfaceW.toFloat() / iw
            val sy = surfaceH.toFloat() / ih
            val fit = Prefs.scale(this@GifWallpaperService) == Prefs.SCALE_FIT
            val s = if (fit) min(sx, sy) else max(sx, sy)
            val w = (iw * s).roundToInt()
            val h = (ih * s).roundToInt()
            val left = (surfaceW - w) / 2
            val top = (surfaceH - h) / 2
            d.setBounds(left, top, left + w, top + h)
        }

        private fun drawFrame() {
            // While a video is attached, MediaPlayer owns the surface.
            if (!visible || isVideo) return
            val holder = surfaceHolder
            val canvas = try { holder.lockCanvas() } catch (e: Exception) { null } ?: return
            try {
                canvas.drawColor(Color.BLACK)
                drawable?.draw(canvas)
            } finally {
                try { holder.unlockCanvasAndPost(canvas) } catch (_: Exception) {}
            }
        }

        // Drawable.Callback — the animated drawable asks us to redraw on each new frame.
        override fun invalidateDrawable(who: Drawable) {
            if (who === drawable) drawFrame()
        }

        override fun scheduleDrawable(who: Drawable, what: Runnable, `when`: Long) {
            if (visible) handler.postAtTime(what, who, `when`)
        }

        override fun unscheduleDrawable(who: Drawable, what: Runnable) {
            handler.removeCallbacks(what, who)
        }
    }
}
