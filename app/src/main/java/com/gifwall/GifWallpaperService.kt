package com.gifwall

import android.content.SharedPreferences
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.ImageDecoder
import android.graphics.Movie
import android.graphics.drawable.AnimatedImageDrawable
import android.graphics.drawable.Drawable
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
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

        private val ctx get() = this@GifWallpaperService
        private val handler = Handler(Looper.getMainLooper())
        private val prefs = Prefs.get(ctx)

        // GIF at normal speed: Android's AnimatedImageDrawable.
        private var drawable: Drawable? = null

        // GIF at a custom speed: Movie, which lets us choose the time of each frame.
        private var movie: Movie? = null
        private var movieTime = 0.0
        private var lastTick = 0L
        private var movieScale = 1f
        private var movieLeft = 0f
        private var movieTop = 0f
        private val movieTick = object : Runnable {
            override fun run() {
                drawFrame()
                if (visible && movie != null) handler.postDelayed(this, 20)
            }
        }

        // Videos (OpenGL, so they can be zoomed/fitted).
        private var video: VideoRenderer? = null
        private var isVideo = false

        private var hasSurface = false
        private var visible = false
        private var surfaceW = 0
        private var surfaceH = 0
        private var speed = 1f

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
            if (key == Prefs.KEY_SPEED && isVideo) {
                // Change video speed in place, without restarting it.
                speed = Prefs.speed(ctx)
                video?.setSpeed(speed)
                return
            }
            if (key == Prefs.KEY_HUE || key == Prefs.KEY_BRIGHTNESS) {
                applyHue()
                drawFrame()
                return
            }
            if (key == Prefs.KEY_ZOOM || key == Prefs.KEY_SCALE ||
                key == Prefs.KEY_FLIP_H || key == Prefs.KEY_FLIP_V
            ) {
                // Zoom / fit / flip change: just re-layout, so dragging the slider is smooth.
                video?.setLayout(
                    Prefs.scale(ctx) == Prefs.SCALE_FIT, Prefs.zoom(ctx), Prefs.flipH(ctx), Prefs.flipV(ctx),
                )
                updateBounds()
                drawFrame()
                return
            }
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
                video?.setVisible(true)
                if (movie != null) {
                    lastTick = SystemClock.uptimeMillis()
                    handler.removeCallbacks(movieTick)
                    handler.post(movieTick)
                }
                drawFrame()
            } else {
                // Pause when the screen is off or an app covers the wallpaper.
                anim?.stop()
                video?.setVisible(false)
                handler.removeCallbacksAndMessages(null)
            }
        }

        override fun onSurfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            surfaceW = width
            surfaceH = height
            video?.setSize(width, height)
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
            val file = Prefs.mediaFile(ctx)
            if (!file.exists()) return
            speed = Prefs.speed(ctx)
            isVideo = Prefs.isVideo(ctx)
            if (isVideo) {
                if (hasSurface) startVideo()
                return
            }

            if (speed != 1f) {
                moviePaint.colorFilter = Hue.filter(Prefs.hue(ctx), Prefs.brightness(ctx))
                movie = runCatching { Movie.decodeFile(file.path) }.getOrNull()
                    ?.takeIf { it.duration() > 0 && it.width() > 0 }
                if (movie != null) {
                    movieTime = 0.0
                    lastTick = SystemClock.uptimeMillis()
                    updateBounds()
                    if (visible) handler.post(movieTick)
                    return
                }
                // Not a GIF Movie can read (e.g. animated WebP): play at normal speed.
            }

            drawable = runCatching {
                ImageDecoder.decodeDrawable(ImageDecoder.createSource(file))
            }.getOrNull()?.also { d ->
                d.callback = this
                d.colorFilter = Hue.filter(Prefs.hue(ctx), Prefs.brightness(ctx))
                (d as? AnimatedImageDrawable)?.let {
                    it.repeatCount = AnimatedImageDrawable.REPEAT_INFINITE
                    if (visible) it.start()
                }
            }
            updateBounds()
        }

        private fun startVideo() {
            releasePlayer()
            val r = VideoRenderer(surfaceHolder.surface, Prefs.mediaFile(ctx))
            val frame = surfaceHolder.surfaceFrame
            r.start(
                if (surfaceW > 0) surfaceW else frame.width(),
                if (surfaceH > 0) surfaceH else frame.height(),
                Prefs.scale(ctx) == Prefs.SCALE_FIT, Prefs.zoom(ctx),
                Prefs.flipH(ctx), Prefs.flipV(ctx), speed,
            )
            r.setColor(Prefs.hue(ctx), Prefs.brightness(ctx))
            if (visible) r.setVisible(true)
            video = r
        }

        private fun releasePlayer() {
            video?.release()
            video = null
        }

        private fun release() {
            releasePlayer()
            isVideo = false
            movie = null
            handler.removeCallbacksAndMessages(null)
            drawable?.let {
                (it as? AnimatedImageDrawable)?.stop()
                it.callback = null
            }
            drawable = null
        }

        private fun updateBounds() {
            if (surfaceW == 0 || surfaceH == 0) return
            val fit = Prefs.scale(ctx) == Prefs.SCALE_FIT
            val zoom = Prefs.zoom(ctx)
            fun scaleFor(iw: Int, ih: Int): Float {
                val sx = surfaceW.toFloat() / iw
                val sy = surfaceH.toFloat() / ih
                return (if (fit) min(sx, sy) else max(sx, sy)) * zoom
            }

            movie?.let { m ->
                movieScale = scaleFor(m.width(), m.height())
                movieLeft = (surfaceW - m.width() * movieScale) / 2f
                movieTop = (surfaceH - m.height() * movieScale) / 2f
            }

            val d = drawable ?: return
            val iw = d.intrinsicWidth.takeIf { it > 0 } ?: surfaceW
            val ih = d.intrinsicHeight.takeIf { it > 0 } ?: surfaceH
            val s = scaleFor(iw, ih)
            val w = (iw * s).roundToInt()
            val h = (ih * s).roundToInt()
            val left = (surfaceW - w) / 2
            val top = (surfaceH - h) / 2
            d.setBounds(left, top, left + w, top + h)
        }

        private fun drawFrame() {
            // While a video is attached, OpenGL owns the surface.
            if (!visible || isVideo) return
            val holder = surfaceHolder
            val canvas = try { holder.lockCanvas() } catch (e: Exception) { null } ?: return
            try {
                canvas.drawColor(Color.BLACK)
                // Flip around the center of the screen.
                canvas.save()
                canvas.scale(
                    if (Prefs.flipH(ctx)) -1f else 1f, if (Prefs.flipV(ctx)) -1f else 1f,
                    surfaceW / 2f, surfaceH / 2f,
                )
                val m = movie
                if (m != null) drawMovie(canvas, m) else drawable?.draw(canvas)
                canvas.restore()
            } finally {
                try { holder.unlockCanvasAndPost(canvas) } catch (_: Exception) {}
            }
        }

        private val moviePaint = android.graphics.Paint(android.graphics.Paint.FILTER_BITMAP_FLAG)

        private fun applyHue() {
            val deg = Prefs.hue(ctx)
            val bright = Prefs.brightness(ctx)
            val f = Hue.filter(deg, bright)
            drawable?.colorFilter = f
            moviePaint.colorFilter = f
            video?.setColor(deg, bright)
        }

        private fun drawMovie(canvas: Canvas, m: Movie) {
            val now = SystemClock.uptimeMillis()
            movieTime = (movieTime + (now - lastTick) * speed) % m.duration()
            lastTick = now
            m.setTime(movieTime.toInt())
            canvas.save()
            canvas.translate(movieLeft, movieTop)
            canvas.scale(movieScale, movieScale)
            m.draw(canvas, 0f, 0f, moviePaint)
            canvas.restore()
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
