package com.gifwall

import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter
import kotlin.math.cos
import kotlin.math.sin

/**
 * Hue rotation + brightness, using the same math as CSS `hue-rotate()` and `brightness()`,
 * so phone and PC match. Brightness is a simple multiplier (1 = unchanged).
 */
object Hue {
    /** 3x3 matrix, row-major: out.rgb = M * in.rgb */
    fun rows(degrees: Int, brightness: Float = 1f): FloatArray =
        hueRows(degrees).map { it * brightness }.toFloatArray()

    private fun hueRows(degrees: Int): FloatArray {
        val a = Math.toRadians(degrees.toDouble())
        val c = cos(a).toFloat()
        val s = sin(a).toFloat()
        return floatArrayOf(
            0.213f + c * 0.787f - s * 0.213f, 0.715f - c * 0.715f - s * 0.715f, 0.072f - c * 0.072f + s * 0.928f,
            0.213f - c * 0.213f + s * 0.143f, 0.715f + c * 0.285f + s * 0.140f, 0.072f - c * 0.072f - s * 0.283f,
            0.213f - c * 0.213f - s * 0.787f, 0.715f - c * 0.715f + s * 0.715f, 0.072f + c * 0.928f + s * 0.072f,
        )
    }

    /** Column-major copy for OpenGL. */
    fun columns(degrees: Int, brightness: Float = 1f): FloatArray {
        val r = rows(degrees, brightness)
        return FloatArray(9) { i -> r[(i % 3) * 3 + i / 3] }
    }

    /** Color filter for Canvas / drawables, or null when there is no shift. */
    fun filter(degrees: Int, brightness: Float = 1f): ColorMatrixColorFilter? {
        if (degrees == 0 && brightness == 1f) return null
        val r = rows(degrees, brightness)
        return ColorMatrixColorFilter(ColorMatrix(floatArrayOf(
            r[0], r[1], r[2], 0f, 0f,
            r[3], r[4], r[5], 0f, 0f,
            r[6], r[7], r[8], 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
        )))
    }

    fun brightnessLabel(b: Float) = "Brightness: ${Math.round(b * 100)}%"

    fun label(degrees: Int) = if (degrees == 0) "Hue: normal" else "Hue: ${if (degrees > 0) "+" else ""}$degrees°"
}
