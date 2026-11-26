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
    const val KEY_HUE = "hue_deg" // float; older versions stored whole degrees under "hue"
    private const val KEY_HUE_OLD = "hue"
    const val KEY_BRIGHTNESS = "brightness"
    const val BRIGHT_MIN = 0f
    const val BRIGHT_MAX = 3f
    const val SAT_MAX = 4f
    const val PAN_MAX = 1f
    const val SPEED_MIN = 0.1f
    const val SPEED_MAX = 5f
    const val KEY_SATURATION = "saturation"
    const val KEY_PAN_X = "pan_x"
    const val KEY_PAN_Y = "pan_y"
    const val KEY_BREATHE = "breathe"
    const val KEY_BREATHE_DEPTH = "breathe_depth"
    const val KEY_BREATHE_PERIOD = "breathe_period"
    const val KEY_BREATHE_MODE = "breathe_mode"
    const val KEY_CONTRAST = "contrast"
    const val KEY_TINT_HUE = "tint_hue"
    const val KEY_TINT_STRENGTH = "tint_strength"
    const val KEY_INVERT = "invert"
    const val KEY_COLOR_CYCLE = "color_cycle"
    const val KEY_SWIRL = "swirl"
    const val KEY_SWIRL_DEPTH = "swirl_depth"
    const val KEY_SWIRL_SIZE = "swirl_size"
    const val KEY_SWIRL_CENTER = "swirl_center"
    const val KEY_SWIRL_PERIOD = "swirl_period"
    fun keyPointX(i: Int) = "point${i + 1}_x"
    fun keyPointY(i: Int) = "point${i + 1}_y"

    /** Keys that only change how the picture is drawn (applied live, no reload). */
    val LIVE_KEYS = setOf(
        KEY_SCALE, KEY_ZOOM, KEY_FLIP_H, KEY_FLIP_V, KEY_PAN_X, KEY_PAN_Y,
        KEY_HUE, KEY_BRIGHTNESS, KEY_SATURATION,
        KEY_BREATHE, KEY_BREATHE_DEPTH, KEY_BREATHE_PERIOD, KEY_BREATHE_MODE,
        KEY_CONTRAST, KEY_TINT_HUE, KEY_TINT_STRENGTH, KEY_INVERT, KEY_COLOR_CYCLE,
        KEY_SWIRL, KEY_SWIRL_DEPTH, KEY_SWIRL_SIZE, KEY_SWIRL_CENTER, KEY_SWIRL_PERIOD,
    ) + (0 until 4).flatMap { listOf(keyPointX(it), keyPointY(it)) }
    const val ZOOM_MIN = 0.1f
    const val ZOOM_MAX = 10f
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

    /** Hue shift in degrees, -180..180. */
    fun hue(context: Context): Float {
        val p = get(context)
        val v = if (p.contains(KEY_HUE)) p.getFloat(KEY_HUE, 0f)
        else runCatching { p.getInt(KEY_HUE_OLD, 0).toFloat() }.getOrDefault(0f)
        return v.coerceIn(-180f, 180f)
    }

    fun brightness(context: Context) =
        get(context).getFloat(KEY_BRIGHTNESS, 1f).coerceIn(BRIGHT_MIN, BRIGHT_MAX)

    fun saturation(context: Context) = get(context).getFloat(KEY_SATURATION, 1f).coerceIn(0f, SAT_MAX)

    /** Pan as a fraction of the screen size, -1..1 (0 = centered). */
    fun panX(context: Context) = get(context).getFloat(KEY_PAN_X, 0f).coerceIn(-PAN_MAX, PAN_MAX)
    fun panY(context: Context) = get(context).getFloat(KEY_PAN_Y, 0f).coerceIn(-PAN_MAX, PAN_MAX)

    /** All color settings as one filter (at time 0 — for the in-app preview). */
    fun colorFilter(context: Context) = ColorFx.filter(ColorFx.config(context), 0L)

    fun zoom(context: Context): Float =
        get(context).getFloat(KEY_ZOOM, 1f).coerceIn(ZOOM_MIN, ZOOM_MAX)

    fun speed(context: Context): Float =
        get(context).getFloat(KEY_SPEED, 1f).coerceIn(SPEED_MIN, SPEED_MAX)

    /** Tell any running wallpaper engine to reload. */
    fun bumpVersion(context: Context) {
        val p = get(context)
        p.edit().putLong(KEY_VERSION, p.getLong(KEY_VERSION, 0) + 1).apply()
    }
}
