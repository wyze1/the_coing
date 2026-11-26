package com.gifwall

import android.content.Context
import android.content.SharedPreferences
import java.io.File

object Prefs {
    const val NAME = "gif_wallpaper"
    const val KEY_VERSION = "version"
    const val KEY_SCALE = "scale"
    const val KEY_TYPE = "type"
    const val KEY_SPEED = "speed"
    const val KEY_ZOOM = "zoom"
    const val KEY_FLIP_H = "flip_h"
    const val KEY_FLIP_V = "flip_v"
    const val ZOOM_MIN = 0.5f
    const val ZOOM_MAX = 3f

    /** Speed choices shown on the slider. */
    val SPEEDS = floatArrayOf(0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f)
    const val SCALE_FILL = "fill"
    const val SCALE_FIT = "fit"
    const val TYPE_IMAGE = "image"
    const val TYPE_VIDEO = "video"

    fun get(context: Context): SharedPreferences =
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** The saved wallpaper file (GIF/WebP image or a video, see [type]). */
    fun mediaFile(context: Context): File = File(context.filesDir, "wallpaper.gif")

    fun type(context: Context): String =
        get(context).getString(KEY_TYPE, TYPE_IMAGE) ?: TYPE_IMAGE

    fun isVideo(context: Context) = type(context) == TYPE_VIDEO

    fun scale(context: Context): String =
        get(context).getString(KEY_SCALE, SCALE_FILL) ?: SCALE_FILL

    fun flipH(context: Context) = get(context).getBoolean(KEY_FLIP_H, false)
    fun flipV(context: Context) = get(context).getBoolean(KEY_FLIP_V, false)

    fun zoom(context: Context): Float =
        get(context).getFloat(KEY_ZOOM, 1f).coerceIn(ZOOM_MIN, ZOOM_MAX)

    fun speed(context: Context): Float = get(context).getFloat(KEY_SPEED, 1f)

    fun speedLabel(speed: Float): String =
        (if (speed % 1f == 0f) speed.toInt().toString() else speed.toString()) + "×"

    /** Tell any running wallpaper engine to reload. */
    fun bumpVersion(context: Context) {
        val p = get(context)
        p.edit().putLong(KEY_VERSION, p.getLong(KEY_VERSION, 0) + 1).apply()
    }
}
