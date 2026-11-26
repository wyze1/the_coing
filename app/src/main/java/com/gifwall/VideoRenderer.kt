package com.gifwall

import android.graphics.SurfaceTexture
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.opengl.EGL14
import android.opengl.EGLConfig
import android.opengl.EGLContext
import android.opengl.EGLDisplay
import android.opengl.EGLSurface
import android.opengl.GLES11Ext
import android.opengl.GLES20
import android.os.Handler
import android.os.HandlerThread
import android.view.Surface
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlin.math.max
import kotlin.math.min

/**
 * Plays a video onto the wallpaper surface with OpenGL, so it can be zoomed,
 * fitted or cropped. All GL and MediaPlayer work happens on its own thread.
 */
class VideoRenderer(private val output: Surface, private val file: File) {

    private val thread = HandlerThread("video-wallpaper").apply { start() }
    private val h = Handler(thread.looper)

    private var display: EGLDisplay = EGL14.EGL_NO_DISPLAY
    private var context: EGLContext = EGL14.EGL_NO_CONTEXT
    private var eglSurface: EGLSurface = EGL14.EGL_NO_SURFACE
    private var program = 0
    private var texId = 0
    private var texture: SurfaceTexture? = null
    private var player: MediaPlayer? = null
    private var ready = false
    private var hasFrame = false

    private var viewW = 1
    private var viewH = 1
    private var videoW = 0
    private var videoH = 0
    private var visible = false
    private var fit = false
    private var zoom = 1f
    private var flipH = false
    private var flipV = false
    private var speed = 1f

    private val texMatrix = FloatArray(16)
    private val quad: FloatBuffer = ByteBuffer.allocateDirect(16 * 4)
        .order(ByteOrder.nativeOrder()).asFloatBuffer()

    fun start(
        width: Int, height: Int, fit: Boolean, zoom: Float, flipH: Boolean, flipV: Boolean, speed: Float,
    ) = h.post {
        viewW = max(1, width); viewH = max(1, height)
        this.fit = fit; this.zoom = zoom; this.speed = speed
        this.flipH = flipH; this.flipV = flipV
        runCatching {
            initEgl()
            initGl()
            initPlayer()
        }
    }

    fun setSize(width: Int, height: Int) = h.post {
        viewW = max(1, width); viewH = max(1, height)
        draw(updateTexture = false)
    }

    fun setLayout(fit: Boolean, zoom: Float, flipH: Boolean, flipV: Boolean) = h.post {
        this.fit = fit; this.zoom = zoom
        this.flipH = flipH; this.flipV = flipV
        draw(updateTexture = false)
    }

    fun setSpeed(speed: Float) = h.post {
        this.speed = speed
        if (visible) applySpeed()
    }

    fun setVisible(v: Boolean) = h.post {
        visible = v
        val p = player ?: return@post
        if (!ready) return@post
        runCatching {
            if (v) { p.start(); applySpeed() } else p.pause()
        }
    }

    /** Blocks until GL has let go of the surface, as the wallpaper framework requires. */
    fun release() {
        val done = CountDownLatch(1)
        h.post {
            runCatching { player?.let { it.reset(); it.release() } }
            player = null
            texture?.release()
            texture = null
            releaseEgl()
            done.countDown()
        }
        done.await(2, TimeUnit.SECONDS)
        thread.quitSafely()
    }

    private fun applySpeed() {
        val p = player ?: return
        runCatching { p.playbackParams = p.playbackParams.setSpeed(speed) }
    }

    private fun initPlayer() {
        val st = SurfaceTexture(texId)
        st.setOnFrameAvailableListener({ draw(updateTexture = true) }, h)
        texture = st
        val surface = Surface(st)
        player = MediaPlayer().apply {
            setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE)
                    .build()
            )
            setSurface(surface)
            surface.release() // the player keeps its own reference
            setDataSource(file.path)
            isLooping = true
            setVolume(0f, 0f)
            setOnVideoSizeChangedListener { _, w, hgt ->
                videoW = w; videoH = hgt
                draw(updateTexture = false)
            }
            setOnPreparedListener {
                ready = true
                if (visible) { it.start(); applySpeed() }
            }
            setOnErrorListener { _, _, _ -> true }
            prepareAsync()
        }
    }

    private fun draw(updateTexture: Boolean) {
        if (eglSurface == EGL14.EGL_NO_SURFACE) return
        val st = texture ?: return
        if (updateTexture) {
            runCatching { st.updateTexImage(); st.getTransformMatrix(texMatrix) }
            hasFrame = true
        }
        GLES20.glViewport(0, 0, viewW, viewH)
        GLES20.glClearColor(0f, 0f, 0f, 1f)
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT)
        if (hasFrame && videoW > 0 && videoH > 0) {
            val sx = viewW.toFloat() / videoW
            val sy = viewH.toFloat() / videoH
            val s = (if (fit) min(sx, sy) else max(sx, sy)) * zoom
            // Half-size in GL units (screen = -1..1); a negative size mirrors the picture.
            val hw = videoW * s / viewW * (if (flipH) -1f else 1f)
            val hh = videoH * s / viewH * (if (flipV) -1f else 1f)
            quad.clear()
            // x, y, u, v for a triangle strip
            quad.put(floatArrayOf(
                -hw, -hh, 0f, 0f,
                hw, -hh, 1f, 0f,
                -hw, hh, 0f, 1f,
                hw, hh, 1f, 1f,
            ))
            GLES20.glUseProgram(program)
            val aPos = GLES20.glGetAttribLocation(program, "aPos")
            val aTex = GLES20.glGetAttribLocation(program, "aTex")
            quad.position(0)
            GLES20.glVertexAttribPointer(aPos, 2, GLES20.GL_FLOAT, false, 16, quad)
            GLES20.glEnableVertexAttribArray(aPos)
            quad.position(2)
            GLES20.glVertexAttribPointer(aTex, 2, GLES20.GL_FLOAT, false, 16, quad)
            GLES20.glEnableVertexAttribArray(aTex)
            GLES20.glUniformMatrix4fv(GLES20.glGetUniformLocation(program, "uTex"), 1, false, texMatrix, 0)
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
            GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, texId)
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)
        }
        EGL14.eglSwapBuffers(display, eglSurface)
    }

    private fun initEgl() {
        display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY)
        val ver = IntArray(2)
        EGL14.eglInitialize(display, ver, 0, ver, 1)
        val attribs = intArrayOf(
            EGL14.EGL_RED_SIZE, 8, EGL14.EGL_GREEN_SIZE, 8, EGL14.EGL_BLUE_SIZE, 8,
            EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
            EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
            EGL14.EGL_NONE,
        )
        val configs = arrayOfNulls<EGLConfig>(1)
        val num = IntArray(1)
        EGL14.eglChooseConfig(display, attribs, 0, configs, 0, 1, num, 0)
        val config = configs[0] ?: error("No EGL config")
        context = EGL14.eglCreateContext(
            display, config, EGL14.EGL_NO_CONTEXT,
            intArrayOf(EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE), 0
        )
        eglSurface = EGL14.eglCreateWindowSurface(display, config, output, intArrayOf(EGL14.EGL_NONE), 0)
        if (eglSurface == EGL14.EGL_NO_SURFACE) error("No EGL surface")
        EGL14.eglMakeCurrent(display, eglSurface, eglSurface, context)
    }

    private fun releaseEgl() {
        if (display == EGL14.EGL_NO_DISPLAY) return
        EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT)
        if (eglSurface != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(display, eglSurface)
        if (context != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(display, context)
        EGL14.eglTerminate(display)
        eglSurface = EGL14.EGL_NO_SURFACE
        context = EGL14.EGL_NO_CONTEXT
        display = EGL14.EGL_NO_DISPLAY
    }

    private fun initGl() {
        val vs = """
            attribute vec2 aPos;
            attribute vec4 aTex;
            uniform mat4 uTex;
            varying vec2 vTex;
            void main() {
                gl_Position = vec4(aPos, 0.0, 1.0);
                vTex = (uTex * aTex).xy;
            }
        """
        val fs = """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            uniform samplerExternalOES sTex;
            varying vec2 vTex;
            void main() { gl_FragColor = texture2D(sTex, vTex); }
        """
        program = GLES20.glCreateProgram()
        GLES20.glAttachShader(program, compile(GLES20.GL_VERTEX_SHADER, vs))
        GLES20.glAttachShader(program, compile(GLES20.GL_FRAGMENT_SHADER, fs))
        GLES20.glLinkProgram(program)

        val ids = IntArray(1)
        GLES20.glGenTextures(1, ids, 0)
        texId = ids[0]
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, texId)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)
    }

    private fun compile(type: Int, src: String): Int {
        val s = GLES20.glCreateShader(type)
        GLES20.glShaderSource(s, src.trimIndent())
        GLES20.glCompileShader(s)
        return s
    }
}
