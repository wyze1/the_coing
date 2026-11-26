package com.gifwall

import android.app.Activity
import android.app.WallpaperManager
import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Intent
import android.graphics.ImageDecoder
import android.graphics.drawable.AnimatedImageDrawable
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.widget.Button
import android.widget.ImageView
import android.widget.RadioGroup
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import android.widget.VideoView
import java.io.File

class MainActivity : Activity() {

    private lateinit var preview: ImageView
    private lateinit var videoPreview: VideoView
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        preview = findViewById(R.id.preview)
        videoPreview = findViewById(R.id.videoPreview)
        status = findViewById(R.id.status)

        val group = findViewById<RadioGroup>(R.id.scaleGroup)
        group.check(if (Prefs.scale(this) == Prefs.SCALE_FIT) R.id.scaleFit else R.id.scaleFill)
        group.setOnCheckedChangeListener { _, id ->
            val mode = if (id == R.id.scaleFit) Prefs.SCALE_FIT else Prefs.SCALE_FILL
            Prefs.get(this).edit().putString(Prefs.KEY_SCALE, mode).apply()
        }

        buildSliders()
        setupPreviewDrag()

        listOf(R.id.flipH to Prefs.KEY_FLIP_H, R.id.flipV to Prefs.KEY_FLIP_V).forEach { (id, key) ->
            val sw = findViewById<Switch>(id)
            sw.isChecked = Prefs.get(this).getBoolean(key, false)
            sw.setOnCheckedChangeListener { _, on ->
                Prefs.get(this).edit().putBoolean(key, on).apply()
                refreshPreviewLook()
            }
        }
        // Pan needs the preview's size, which is known only after layout.
        findViewById<View>(R.id.previewFrame).post { refreshPreviewLook() }

        findViewById<Button>(R.id.photosButton).setOnClickListener { pickFromGooglePhotos() }
        findViewById<Button>(R.id.pickButton).setOnClickListener { pickFile() }
        findViewById<Button>(R.id.setButton).setOnClickListener { setWallpaper() }

        showPreview()
    }

    override fun onResume() {
        super.onResume()
        if (videoPreview.visibility == View.VISIBLE) videoPreview.start()
    }

    /** Opens Google Photos directly (includes cloud-only items, no settings needed). */
    private fun pickFromGooglePhotos() {
        val attempts = listOf(
            Intent(Intent.ACTION_GET_CONTENT).setType("*/*")
                .putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("image/*", "video/*")),
            Intent(Intent.ACTION_PICK).setType("image/* video/*"),
            Intent(Intent.ACTION_GET_CONTENT).setType("image/*"),
        )
        for (intent in attempts) {
            intent.setPackage(GOOGLE_PHOTOS)
            try {
                startActivityForResult(intent, REQ_PICK)
                return
            } catch (_: ActivityNotFoundException) {
            }
        }
        toast("Google Photos isn't installed. Opening the file picker instead.")
        pickFile()
    }

    private fun pickFile() {
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
            putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("image/gif", "image/webp", "video/*"))
        }
        try {
            startActivityForResult(intent, REQ_PICK)
        } catch (e: ActivityNotFoundException) {
            toast("No file picker available")
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val uri = data?.data
        if (requestCode == REQ_PICK && resultCode == RESULT_OK && uri != null) importMedia(uri)
    }

    private fun importMedia(uri: Uri) {
        status.text = "Importing…"
        val mime = contentResolver.getType(uri) ?: ""
        Thread {
            val result = runCatching {
                val dest = Prefs.mediaFile(this)
                val tmp = File(filesDir, "wallpaper.tmp")
                contentResolver.openInputStream(uri)!!.use { input ->
                    tmp.outputStream().use { input.copyTo(it) }
                }
                val isVideo = mime.startsWith("video/") || hasVideoTrack(tmp)
                var animated = true
                if (!isVideo) {
                    // Make sure it decodes before replacing the current one.
                    val d = ImageDecoder.decodeDrawable(ImageDecoder.createSource(tmp))
                    animated = d is AnimatedImageDrawable
                }
                if (!tmp.renameTo(dest)) error("Could not save file")
                Prefs.get(this).edit()
                    .putString(Prefs.KEY_TYPE, if (isVideo) Prefs.TYPE_VIDEO else Prefs.TYPE_IMAGE)
                    .commit()
                animated
            }
            runOnUiThread {
                if (result.isSuccess) {
                    Prefs.bumpVersion(this)
                    showPreview()
                    if (result.getOrNull() == true) {
                        toast("Saved. Tap \"Set as wallpaper\".")
                    } else {
                        toast("Saved, but that image isn't animated, so it will be a still wallpaper.")
                    }
                } else {
                    status.text = "Couldn't open that file: ${result.exceptionOrNull()?.message}"
                }
            }
        }.start()
    }

    private fun hasVideoTrack(file: File): Boolean = runCatching {
        val r = MediaMetadataRetriever()
        try {
            r.setDataSource(file.path)
            r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_HAS_VIDEO) == "yes"
        } finally {
            r.release()
        }
    }.getOrDefault(false)

    private fun showPreview() {
        val file = Prefs.mediaFile(this)
        if (!file.exists()) return
        val sizeText = "${file.length() / 1024} KB"
        if (Prefs.isVideo(this)) {
            preview.setImageDrawable(null)
            videoPreview.visibility = View.VISIBLE
            videoPreview.setOnPreparedListener { mp ->
                mp.isLooping = true
                mp.setVolume(0f, 0f)
                previewPlayer = mp
                videoPreview.start()
                applyPreviewSpeed()
            }
            videoPreview.setOnErrorListener { _, _, _ ->
                status.text = "Video saved, but the preview can't play it."
                true
            }
            videoPreview.setVideoPath(file.path)
            status.text = "Current video: $sizeText"
        } else {
            previewPlayer = null
            videoPreview.stopPlayback()
            videoPreview.visibility = View.GONE
            runCatching {
                val d = ImageDecoder.decodeDrawable(ImageDecoder.createSource(file))
                preview.setImageDrawable(d)
                (d as? AnimatedImageDrawable)?.start()
                status.text = "Current GIF: $sizeText"
            }.onFailure { status.text = "Saved file couldn't be read; choose another." }
        }
    }


    private fun buildSliders() {
        addSlider("Zoom", Prefs.ZOOM_MIN, Prefs.ZOOM_MAX, 0.001f, 1f, log = true,
            get = { Prefs.zoom(this) },
            save = { putFloat(Prefs.KEY_ZOOM, it) },
            label = { "Zoom: ${Fmt.num(it * 100, 1)}%" })
        addSlider("Hue", -180f, 180f, 0.5f, 0f,
            get = { Prefs.hue(this) },
            save = { putFloat(Prefs.KEY_HUE, it) },
            label = { Hue.label(it) })
        addSlider("Brightness", Prefs.BRIGHT_MIN, Prefs.BRIGHT_MAX, 0.005f, 1f,
            get = { Prefs.brightness(this) },
            save = { putFloat(Prefs.KEY_BRIGHTNESS, it) },
            label = { "Brightness: ${Fmt.num(it * 100, 1)}%" })
        addSlider("Saturation", 0f, Prefs.SAT_MAX, 0.005f, 1f,
            get = { Prefs.saturation(this) },
            save = { putFloat(Prefs.KEY_SATURATION, it) },
            label = {
                if (it == 0f) "Saturation: 0% (black & white)" else "Saturation: ${Fmt.num(it * 100, 1)}%"
            })
        // Speed is saved when you let go (changing it can restart the GIF decoder).
        addSlider("Speed", Prefs.SPEED_MIN, Prefs.SPEED_MAX, 0.01f, 1f, log = true, live = false,
            get = { Prefs.speed(this) },
            save = { putFloat(Prefs.KEY_SPEED, it) },
            label = { "Speed: ${Fmt.num(it)}×" + if (it == 1f) " (normal)" else "" },
            onSaved = { applyPreviewSpeed() })
    }

    /**
     * Adds a row (label, −, +, reset) and a slider for one setting.
     * [step] is the value change per slider tick (and per − / + tap). With [log], ticks are spread
     * evenly on a log scale, so there's fine control near 100% as well as a wide range.
     */
    private fun addSlider(
        @Suppress("UNUSED_PARAMETER") name: String,
        min: Float, max: Float, step: Float, default: Float,
        log: Boolean = false,
        live: Boolean = true,
        resetText: String = "Reset",
        get: () -> Float,
        save: android.content.SharedPreferences.Editor.(Float) -> Unit,
        label: (Float) -> String,
        onSaved: () -> Unit = {},
    ): SeekBar {
        val ticks = if (log) 1000 else Math.round((max - min) / step)
        fun valueAt(p: Int): Float {
            val raw = if (log) (min * Math.pow((max / min).toDouble(), p.toDouble() / ticks)).toFloat()
            else min + p * step
            val perUnit = Math.round(1f / step).toFloat() // e.g. step 0.005 -> 200
            return (Math.round(raw * perUnit) / perUnit).coerceIn(min, max) // snap to the step size
        }
        fun progressOf(v: Float): Int =
            if (log) Math.round(ticks * Math.log((v / min).toDouble()) / Math.log((max / min).toDouble())).toInt()
            else Math.round((v - min) / step)

        val container = findViewById<android.widget.LinearLayout>(R.id.sliders)
        val row = android.widget.LinearLayout(this).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            gravity = android.view.Gravity.CENTER_VERTICAL
            setPadding(0, dp(8), 0, 0)
        }
        val text = TextView(this)
        row.addView(text, android.widget.LinearLayout.LayoutParams(0, -2, 1f))
        fun smallButton(t: String) = Button(this, null, android.R.attr.buttonStyleSmall).apply {
            this.text = t
            minWidth = dp(44); minimumWidth = dp(44)
            row.addView(this)
        }
        val minus = smallButton("−")
        val plus = smallButton("+")
        val reset = smallButton(resetText)
        val bar = SeekBar(this)
        container.addView(row)
        container.addView(bar)

        bar.max = ticks
        bar.progress = progressOf(get()).coerceIn(0, ticks)
        text.text = label(valueAt(bar.progress))

        fun store(v: Float) {
            Prefs.get(this).edit().apply { save(v) }.apply()
            refreshPreviewLook()
            onSaved()
        }
        bar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(b: SeekBar, progress: Int, fromUser: Boolean) {
                val v = valueAt(progress)
                text.text = label(v)
                // Buttons / reset / preview-drag change it in code (fromUser = false): save right away.
                if (live || !fromUser) store(v)
            }
            override fun onStartTrackingTouch(b: SeekBar) {}
            override fun onStopTrackingTouch(b: SeekBar) {
                if (!live) store(valueAt(b.progress))
            }
        })
        // − / + : one tick per tap; hold to repeat.
        fun repeatOnHold(btn: Button, delta: Int) {
            val handler = android.os.Handler(mainLooper)
            val repeat = object : Runnable {
                override fun run() {
                    bar.progress = (bar.progress + delta).coerceIn(0, ticks)
                    handler.postDelayed(this, 60)
                }
            }
            btn.setOnTouchListener { v, e ->
                when (e.actionMasked) {
                    android.view.MotionEvent.ACTION_DOWN -> {
                        bar.progress = (bar.progress + delta).coerceIn(0, ticks)
                        handler.postDelayed(repeat, 400)
                        v.isPressed = true
                    }
                    android.view.MotionEvent.ACTION_UP, android.view.MotionEvent.ACTION_CANCEL -> {
                        handler.removeCallbacks(repeat)
                        v.isPressed = false
                    }
                }
                true
            }
        }
        repeatOnHold(minus, -1)
        repeatOnHold(plus, +1)
        reset.setOnClickListener { bar.progress = progressOf(default).coerceIn(0, ticks) }
        return bar
    }

    private fun dp(v: Int) = Math.round(v * resources.displayMetrics.density)

    /** Dragging the preview pans the picture; the Center button resets it. */
    @android.annotation.SuppressLint("ClickableViewAccessibility")
    private fun setupPreviewDrag() {
        val frame = findViewById<View>(R.id.previewFrame)
        var lastX = 0f
        var lastY = 0f
        fun setPan(x: Float, y: Float) {
            Prefs.get(this).edit()
                .putFloat(Prefs.KEY_PAN_X, x.coerceIn(-Prefs.PAN_MAX, Prefs.PAN_MAX))
                .putFloat(Prefs.KEY_PAN_Y, y.coerceIn(-Prefs.PAN_MAX, Prefs.PAN_MAX))
                .apply()
            refreshPreviewLook()
        }
        frame.setOnTouchListener { v, e ->
            when (e.actionMasked) {
                android.view.MotionEvent.ACTION_DOWN -> {
                    lastX = e.x; lastY = e.y
                    v.parent.requestDisallowInterceptTouchEvent(true) // don't scroll the page
                }
                android.view.MotionEvent.ACTION_MOVE -> {
                    // Finger movement as a fraction of the preview = pan as a fraction of the screen.
                    setPan(
                        Prefs.panX(this) + (e.x - lastX) / v.width,
                        Prefs.panY(this) + (e.y - lastY) / v.height,
                    )
                    lastX = e.x; lastY = e.y
                }
            }
            true
        }
        findViewById<Button>(R.id.panCenter).setOnClickListener { setPan(0f, 0f) }
    }

    /** Makes the in-app preview match zoom, flip, pan and colors. */
    private fun refreshPreviewLook() {
        val z = Prefs.zoom(this)
        val sx = z * (if (Prefs.flipH(this)) -1 else 1)
        val sy = z * (if (Prefs.flipV(this)) -1 else 1)
        val frame = findViewById<View>(R.id.previewFrame)
        for (v in listOf<View>(preview, videoPreview)) {
            v.scaleX = sx; v.scaleY = sy
            v.translationX = Prefs.panX(this) * frame.width
            v.translationY = Prefs.panY(this) * frame.height
        }
        preview.colorFilter = Prefs.colorFilter(this)
    }

    private var previewPlayer: android.media.MediaPlayer? = null

    /** The video preview follows the speed setting (the GIF preview always plays at 1×). */
    private fun applyPreviewSpeed() {
        val mp = previewPlayer ?: return
        if (videoPreview.visibility != View.VISIBLE) return
        runCatching { mp.playbackParams = mp.playbackParams.setSpeed(Prefs.speed(this)) }
    }

    private fun setWallpaper() {
        if (!Prefs.mediaFile(this).exists()) {
            toast("Choose a GIF or video first")
            return
        }
        val component = ComponentName(this, GifWallpaperService::class.java)
        val intent = Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER)
            .putExtra(WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT, component)
        try {
            startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            try {
                startActivity(Intent(WallpaperManager.ACTION_LIVE_WALLPAPER_CHOOSER))
                toast("Pick \"GIF Wallpaper\" from the list")
            } catch (e2: ActivityNotFoundException) {
                toast("This device has no live wallpaper picker")
            }
        }
    }

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_LONG).show()

    companion object {
        private const val REQ_PICK = 1
        private const val GOOGLE_PHOTOS = "com.google.android.apps.photos"
    }
}
