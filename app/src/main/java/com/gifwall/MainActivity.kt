package com.gifwall

import android.app.Activity
import android.app.WallpaperManager
import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Intent
import android.graphics.ImageDecoder
import android.graphics.drawable.AnimatedImageDrawable
import android.net.Uri
import android.os.Bundle
import android.widget.Button
import android.widget.ImageView
import android.widget.RadioGroup
import android.widget.TextView
import android.widget.Toast
import java.io.File

class MainActivity : Activity() {

    private lateinit var preview: ImageView
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        preview = findViewById(R.id.preview)
        status = findViewById(R.id.status)

        val group = findViewById<RadioGroup>(R.id.scaleGroup)
        group.check(if (Prefs.scale(this) == Prefs.SCALE_FIT) R.id.scaleFit else R.id.scaleFill)
        group.setOnCheckedChangeListener { _, id ->
            val mode = if (id == R.id.scaleFit) Prefs.SCALE_FIT else Prefs.SCALE_FILL
            Prefs.get(this).edit().putString(Prefs.KEY_SCALE, mode).apply()
            Prefs.bumpVersion(this)
        }

        findViewById<Button>(R.id.pickButton).setOnClickListener { pickGif() }
        findViewById<Button>(R.id.setButton).setOnClickListener { setWallpaper() }

        showPreview()
    }

    private fun pickGif() {
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "image/*"
            putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("image/gif", "image/webp"))
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
        if (requestCode == REQ_PICK && resultCode == RESULT_OK && uri != null) importGif(uri)
    }

    private fun importGif(uri: Uri) {
        status.text = "Importing…"
        Thread {
            val result = runCatching {
                val dest = Prefs.gifFile(this)
                val tmp = File(filesDir, "wallpaper.tmp")
                contentResolver.openInputStream(uri)!!.use { input ->
                    tmp.outputStream().use { input.copyTo(it) }
                }
                // Make sure it actually decodes before replacing the current one.
                ImageDecoder.decodeDrawable(ImageDecoder.createSource(tmp))
                if (!tmp.renameTo(dest)) error("Could not save file")
            }
            runOnUiThread {
                if (result.isSuccess) {
                    Prefs.bumpVersion(this)
                    showPreview()
                    toast("GIF saved. Tap \"Set as wallpaper\".")
                } else {
                    status.text = "Couldn't open that file: ${result.exceptionOrNull()?.message}"
                }
            }
        }.start()
    }

    private fun showPreview() {
        val file = Prefs.gifFile(this)
        if (!file.exists()) return
        runCatching {
            val d = ImageDecoder.decodeDrawable(ImageDecoder.createSource(file))
            preview.setImageDrawable(d)
            (d as? AnimatedImageDrawable)?.start()
            status.text = "Current GIF: ${file.length() / 1024} KB"
        }.onFailure { status.text = "Saved GIF couldn't be read; choose another." }
    }

    private fun setWallpaper() {
        if (!Prefs.gifFile(this).exists()) {
            toast("Choose a GIF first")
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
    }
}
