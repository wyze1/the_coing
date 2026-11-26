package com.gifwall

import android.content.Context
import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter

/**
 * All color adjustments combined into one affine color transform:
 *     out.rgb = M * in.rgb + offset          (values 0..1)
 * Applied in this order: hue (+ cycling) -> saturation -> brightness -> contrast -> tint -> invert.
 * The Windows app computes exactly the same matrix, so both look the same.
 */
object ColorFx {
    data class Config(
        val hue: Float,          // degrees
        val brightness: Float,   // 1 = unchanged
        val saturation: Float,   // 1 = unchanged, 0 = black & white
        val contrast: Float,     // 1 = unchanged
        val tintHue: Float,      // degrees on the color wheel
        val tintStrength: Float, // 0..1
        val invert: Boolean,
        val cycleMs: Float,      // 0 = off, else ms per full trip around the color wheel
    ) {
        val cycling get() = cycleMs > 0f
        val isIdentity get() = !cycling && hue == 0f && brightness == 1f && saturation == 1f &&
            contrast == 1f && (tintStrength == 0f) && !invert
    }

    fun config(ctx: Context): Config {
        val p = Prefs.get(ctx)
        return Config(
            hue = Prefs.hue(ctx),
            brightness = Prefs.brightness(ctx),
            saturation = Prefs.saturation(ctx),
            contrast = p.getFloat(Prefs.KEY_CONTRAST, 1f).coerceIn(0f, 3f),
            tintHue = p.getFloat(Prefs.KEY_TINT_HUE, 30f).coerceIn(0f, 360f),
            tintStrength = p.getFloat(Prefs.KEY_TINT_STRENGTH, 0f).coerceIn(0f, 1f),
            invert = p.getBoolean(Prefs.KEY_INVERT, false),
            cycleMs = p.getFloat(Prefs.KEY_COLOR_CYCLE, 0f).coerceIn(0f, 120f) * 1000f,
        )
    }

    /** Returns 12 floats: the 3x3 matrix (row-major) followed by the 3 offsets. */
    fun affine(c: Config, nowMs: Long): FloatArray {
        val hue = if (c.cycling) c.hue + 360f * ((nowMs % c.cycleMs.toLong()) / c.cycleMs) else c.hue
        // hue, saturation, brightness (the existing combined matrix)
        var m = Hue.rows(hue, c.brightness, c.saturation)
        var o = floatArrayOf(0f, 0f, 0f)
        // contrast around mid-grey
        val k = c.contrast
        m = FloatArray(9) { m[it] * k }
        o = FloatArray(3) { o[it] * k + 0.5f * (1 - k) }
        // tint: blend toward (brightness of the pixel) x (tint color)
        if (c.tintStrength > 0f) {
            val t = hsvToRgb(c.tintHue)
            val s = c.tintStrength
            val lum = floatArrayOf(0.2126f, 0.7152f, 0.0722f)
            val tm = FloatArray(9) { i ->
                val r = i / 3
                val col = i % 3
                (if (r == col) 1 - s else 0f) + s * t[r] * lum[col]
            }
            m = mul(tm, m)
            o = FloatArray(3) { r -> tm[r * 3] * o[0] + tm[r * 3 + 1] * o[1] + tm[r * 3 + 2] * o[2] }
        }
        if (c.invert) {
            m = FloatArray(9) { -m[it] }
            o = FloatArray(3) { 1 - o[it] }
        }
        return m + o
    }

    /** Android color filter (offsets are in 0..255 there), or null when nothing changes. */
    fun filter(c: Config, nowMs: Long): ColorMatrixColorFilter? {
        if (c.isIdentity) return null
        val a = affine(c, nowMs)
        return ColorMatrixColorFilter(ColorMatrix(floatArrayOf(
            a[0], a[1], a[2], 0f, a[9] * 255f,
            a[3], a[4], a[5], 0f, a[10] * 255f,
            a[6], a[7], a[8], 0f, a[11] * 255f,
            0f, 0f, 0f, 1f, 0f,
        )))
    }

    /** Fully saturated color at a point on the color wheel, as r,g,b 0..1. */
    fun hsvToRgb(hueDeg: Float): FloatArray {
        val hsv = floatArrayOf(((hueDeg % 360f) + 360f) % 360f, 1f, 1f)
        val c = android.graphics.Color.HSVToColor(hsv)
        return floatArrayOf(
            android.graphics.Color.red(c) / 255f,
            android.graphics.Color.green(c) / 255f,
            android.graphics.Color.blue(c) / 255f,
        )
    }

    fun tintName(h: Float): String = when (((h % 360f) + 360f) % 360f) {
        in 0f..15f -> "red"
        in 15f..45f -> "orange"
        in 45f..70f -> "yellow"
        in 70f..160f -> "green"
        in 160f..200f -> "cyan"
        in 200f..260f -> "blue"
        in 260f..290f -> "purple"
        in 290f..340f -> "pink"
        else -> "red"
    }

    private fun mul(a: FloatArray, b: FloatArray) = FloatArray(9) { i ->
        val r = i / 3
        val c = i % 3
        a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c]
    }
}
