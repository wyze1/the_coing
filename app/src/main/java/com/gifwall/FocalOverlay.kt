package com.gifwall

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.View

/** Draws the 4 numbered breathing focal points on top of the preview. Doesn't take touches. */
class FocalOverlay(context: Context) : View(context) {

    /** Returns where point i is on this view, or null if it can't be shown. */
    var positionOf: (Int) -> Pair<Float, Float>? = { null }
    /** Point currently used for breathing (-1 = none / center). */
    var activeIndex = -1
    /** Point waiting to be placed by a tap (-1 = none). */
    var placingIndex = -1
    var showPoints = false

    private val density = resources.displayMetrics.density
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val ring = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 2 * density
        color = Color.WHITE
    }
    private val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textAlign = Paint.Align.CENTER
        textSize = 12 * density
        isFakeBoldText = true
    }

    init {
        isClickable = false
        isFocusable = false
    }

    override fun onDraw(canvas: Canvas) {
        if (!showPoints) return
        val r = 11 * density
        for (i in 0 until 4) {
            val (x, y) = positionOf(i) ?: continue
            fill.color = when (i) {
                placingIndex -> Color.rgb(255, 170, 0)
                activeIndex -> Color.rgb(255, 92, 158)
                else -> Color.argb(170, 60, 60, 60)
            }
            canvas.drawCircle(x, y, r, fill)
            canvas.drawCircle(x, y, r, ring)
            canvas.drawText("${i + 1}", x, y + text.textSize / 3, text)
        }
    }
}
