package com.gifwall

import android.content.Context
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.floor

/**
 * "Breathing": the picture slowly zooms in and out around a focal point.
 * Focal points are stored as fractions of the image (0..1), so they stay on the same
 * spot of the picture whatever the zoom / pan / flip settings are.
 */
object Breath {
    const val MODE_CENTER = 0
    // 1..4 = always that point
    const val MODE_CYCLE = 5

    val MODE_NAMES = listOf("Center", "Point 1", "Point 2", "Point 3", "Point 4", "Cycle through points")

    val DEFAULT_POINTS = listOf(0.3f to 0.3f, 0.7f to 0.3f, 0.3f to 0.7f, 0.7f to 0.7f)

    data class Config(
        val on: Boolean,
        val depth: Float,      // extra zoom at the peak, e.g. 0.08 = +8%
        val periodMs: Float,   // one full breath (in and out)
        val mode: Int,
        val points: List<Pair<Float, Float>>,
    )

    /** Scale (1 = normal) and focal point (image fractions) at a moment in time. */
    data class State(val scale: Float, val fx: Float, val fy: Float)

    fun config(ctx: Context): Config {
        val p = Prefs.get(ctx)
        return Config(
            on = p.getBoolean(Prefs.KEY_BREATHE, false),
            depth = p.getFloat(Prefs.KEY_BREATHE_DEPTH, 0.08f).coerceIn(0.01f, 0.5f),
            periodMs = p.getFloat(Prefs.KEY_BREATHE_PERIOD, 6f).coerceIn(1f, 30f) * 1000f,
            mode = p.getInt(Prefs.KEY_BREATHE_MODE, MODE_CENTER).coerceIn(0, MODE_CYCLE),
            points = (0 until 4).map { point(ctx, it) },
        )
    }

    fun point(ctx: Context, i: Int): Pair<Float, Float> {
        val p = Prefs.get(ctx)
        return p.getFloat(Prefs.keyPointX(i), DEFAULT_POINTS[i].first) to
            p.getFloat(Prefs.keyPointY(i), DEFAULT_POINTS[i].second)
    }

    fun at(cfg: Config, nowMs: Long): State? {
        if (!cfg.on) return null
        val phase = nowMs / cfg.periodMs
        val cycle = floor(phase).toLong()
        val frac = phase - cycle
        // Smooth in-and-out: 1 at the start/end of each breath, 1 + depth in the middle.
        val scale = 1f + cfg.depth * (1f - cos(2 * PI * frac).toFloat()) / 2f
        val (fx, fy) = when (cfg.mode) {
            MODE_CENTER -> 0.5f to 0.5f
            MODE_CYCLE -> cfg.points[(cycle % 4).toInt()] // changes exactly when scale is back to 1
            else -> cfg.points[cfg.mode - 1]
        }
        return State(scale, fx, fy)
    }
}
