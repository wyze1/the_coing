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
    /** Combined matrix for CSS `hue-rotate() brightness() saturate()` (applied in that order). */
    fun rows(degrees: Float, brightness: Float = 1f, saturation: Float = 1f): FloatArray {
        val h = hueRows(degrees)
        val s = saturation
        val sat = floatArrayOf(
            0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s,
            0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s,
            0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s,
        )
        // sat * hue, times brightness
        return FloatArray(9) { i ->
            val r = i / 3
            val c = i % 3
            (sat[r * 3] * h[c] + sat[r * 3 + 1] * h[3 + c] + sat[r * 3 + 2] * h[6 + c]) * brightness
        }
    }

    private fun hueRows(degrees: Float): FloatArray {
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
    fun columns(degrees: Float, brightness: Float = 1f, saturation: Float = 1f): FloatArray {
        val r = rows(degrees, brightness, saturation)
        return FloatArray(9) { i -> r[(i % 3) * 3 + i / 3] }
    }

    /** Color filter for Canvas / drawables, or null when there is no shift. */
    fun filter(degrees: Float, brightness: Float = 1f, saturation: Float = 1f): ColorMatrixColorFilter? {
        if (degrees == 0f && brightness == 1f && saturation == 1f) return null
        val r = rows(degrees, brightness, saturation)
        return ColorMatrixColorFilter(ColorMatrix(floatArrayOf(
            r[0], r[1], r[2], 0f, 0f,
            r[3], r[4], r[5], 0f, 0f,
            r[6], r[7], r[8], 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
        )))
    }

    fun brightnessLabel(b: Float) = "Brightness: ${Math.round(b * 100)}%"

    fun label(degrees: Float) =
        if (degrees == 0f) "Hue: normal" else "Hue: ${if (degrees > 0) "+" else ""}${Fmt.num(degrees)}°"
}
