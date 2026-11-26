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

        // Zoom: 50%–300%, updates the wallpaper live while dragging.
        val zoomLabel = findViewById<TextView>(R.id.zoomLabel)
        val zoomBar = findViewById<SeekBar>(R.id.zoomBar)
        val minPct = (Prefs.ZOOM_MIN * 100).toInt()
        zoomBar.max = (Prefs.ZOOM_MAX * 100).toInt() - minPct
        val flipHSwitch = findViewById<Switch>(R.id.flipH)
        val flipVSwitch = findViewById<Switch>(R.id.flipV)
        fun showZoom(z: Float) {
            zoomLabel.text = "Zoom: ${(z * 100).toInt()}%"
            val sx = z * (if (flipHSwitch.isChecked) -1 else 1)
            val sy = z * (if (flipVSwitch.isChecked) -1 else 1)
            preview.scaleX = sx; preview.scaleY = sy
            videoPreview.scaleX = sx; videoPreview.scaleY = sy
        }
        flipHSwitch.isChecked = Prefs.flipH(this)
        flipVSwitch.isChecked = Prefs.flipV(this)
        flipHSwitch.setOnCheckedChangeListener { _, on ->
            Prefs.get(this).edit().putBoolean(Prefs.KEY_FLIP_H, on).apply()
            showZoom(Prefs.zoom(this))
        }
        flipVSwitch.setOnCheckedChangeListener { _, on ->
            Prefs.get(this).edit().putBoolean(Prefs.KEY_FLIP_V, on).apply()
            showZoom(Prefs.zoom(this))
        }
        zoomBar.progress = (Prefs.zoom(this) * 100).toInt() - minPct
        showZoom(Prefs.zoom(this))
        zoomBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar, progress: Int, fromUser: Boolean) {
                val z = (progress + minPct) / 100f
                showZoom(z)
                Prefs.get(this@MainActivity).edit().putFloat(Prefs.KEY_ZOOM, z).apply()
            }
            override fun onStartTrackingTouch(bar: SeekBar) {}
            override fun onStopTrackingTouch(bar: SeekBar) {}
        })
        findViewById<Button>(R.id.zoomReset).setOnClickListener { zoomBar.progress = 100 - minPct }

        val speedLabel = findViewById<TextView>(R.id.speedLabel)
        val speedBar = findViewById<SeekBar>(R.id.speedBar)
        val speeds = Prefs.SPEEDS
        speedBar.max = speeds.size - 1
        val current = Prefs.speed(this)
        speedBar.progress = speeds.indices.minByOrNull { kotlin.math.abs(speeds[it] - current) } ?: 3
        speedLabel.text = "Speed: ${Prefs.speedLabel(speeds[speedBar.progress])}"
        speedBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar, progress: Int, fromUser: Boolean) {
                speedLabel.text = "Speed: ${Prefs.speedLabel(speeds[progress])}"
            }
            override fun onStartTrackingTouch(bar: SeekBar) {}
            override fun onStopTrackingTouch(bar: SeekBar) {
                // Saved when the finger lifts, so the wallpaper reloads once, not on every step.
                Prefs.get(this@MainActivity).edit()
                    .putFloat(Prefs.KEY_SPEED, speeds[bar.progress]).apply()
                applyPreviewSpeed()
            }
        })

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
