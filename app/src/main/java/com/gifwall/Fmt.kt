package com.gifwall

/** Number formatting for slider labels: drops trailing zeros (1.50 -> "1.5", 2.00 -> "2"). */
object Fmt {
    fun num(v: Float, decimals: Int = 2): String {
        val s = String.format(java.util.Locale.US, "%.${decimals}f", v)
        return if (s.contains('.')) s.trimEnd('0').trimEnd('.') else s
    }
}
