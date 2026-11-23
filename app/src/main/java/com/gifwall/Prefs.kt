package com.gifwall

import android.content.Context
import android.content.SharedPreferences
import java.io.File

object Prefs {
    const val NAME = "gif_wallpaper"
    const val KEY_VERSION = "version"
    const val KEY_SCALE = "scale"
    const val SCALE_FILL = "fill"
    const val SCALE_FIT = "fit"

    fun get(context: Context): SharedPreferences =
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    fun gifFile(context: Context): File = File(context.filesDir, "wallpaper.gif")

    fun scale(context: Context): String =
        get(context).getString(KEY_SCALE, SCALE_FILL) ?: SCALE_FILL

    /** Tell any running wallpaper engine to reload. */
    fun bumpVersion(context: Context) {
        val p = get(context)
        p.edit().putLong(KEY_VERSION, p.getLong(KEY_VERSION, 0) + 1).apply()
    }
}
