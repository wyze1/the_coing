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
            Prefs.bumpVersion(this)
        }

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
                videoPreview.start()
            }
            videoPreview.setOnErrorListener { _, _, _ ->
                status.text = "Video saved, but the preview can't play it."
                true
            }
            videoPreview.setVideoPath(file.path)
            status.text = "Current video: $sizeText"
        } else {
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
