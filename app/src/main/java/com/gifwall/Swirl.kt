package com.gifwall

import android.content.Context
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Swirl / twirl warp: twists the picture around a center point, strongest in the middle
 * and fading to nothing at the edge of the swirl. Worked out in image space (fractions 0..1),
 * so it stays on the same spot of the picture whatever the zoom / pan / flip settings are.
 */
object Swirl {
    val CENTER_NAMES = listOf("Center of picture", "Point 1", "Point 2", "Point 3", "Point 4")

    data class Config(
        val on: Boolean,
        val depth: Float,     // turns of twist at the center; negative = other direction
        val size: Float,      // radius; 1 = half the picture's shorter side
        val center: Int,      // 0 = picture center, 1..4 = focal point
        val periodMs: Float,  // 0 = still, otherwise twists back and forth
        val cx: Float,
        val cy: Float,
    ) {
        val animated get() = on && periodMs > 0f && depth != 0f
    }

    /** Twist angle at the center (radians), plus center and radius in image fractions. */
    data class State(val angle: Float, val cx: Float, val cy: Float, val size: Float)

    fun config(ctx: Context): Config {
        val p = Prefs.get(ctx)
        val center = p.getInt(Prefs.KEY_SWIRL_CENTER, 0).coerceIn(0, 4)
        val (cx, cy) = if (center == 0) 0.5f to 0.5f else Breath.point(ctx, center - 1)
        return Config(
            on = p.getBoolean(Prefs.KEY_SWIRL, false),
            depth = p.getFloat(Prefs.KEY_SWIRL_DEPTH, 1f).coerceIn(-3f, 3f),
            size = p.getFloat(Prefs.KEY_SWIRL_SIZE, 1f).coerceIn(0.1f, 2f),
            center = center,
            periodMs = p.getFloat(Prefs.KEY_SWIRL_PERIOD, 0f).coerceIn(0f, 30f) * 1000f,
            cx = cx, cy = cy,
        )
    }

    fun at(cfg: Config, nowMs: Long): State? {
        if (!cfg.on || cfg.depth == 0f) return null
        val k = if (cfg.periodMs > 0f) sin(2 * PI * (nowMs / cfg.periodMs)).toFloat() else 1f
        return State((cfg.depth * k * 2 * PI).toFloat(), cfg.cx, cfg.cy, cfg.size)
    }

    /**
     * Where image point (u, v) ends up after the swirl (forward warp), in image fractions.
     * [aspect] = image width / height. The GPU shaders do the exact inverse of this.
     */
    fun warp(s: State, aspect: Float, u: Float, v: Float, out: FloatArray) {
        val dx = (u - s.cx) * aspect
        val dy = v - s.cy
        val r = sqrt(dx * dx + dy * dy)
        val radius = s.size * min(aspect, 1f) * 0.5f
        if (r >= radius || radius <= 0f) {
            out[0] = u; out[1] = v
            return
        }
        val t = 1f - r / radius
        val a = s.angle * t * t
        val c = cos(a)
        val sn = sin(a)
        out[0] = s.cx + (dx * c - dy * sn) / aspect
        out[1] = s.cy + (dx * sn + dy * c)
    }
}
