package com.lazyasf.remote.ui

import android.graphics.RenderEffect
import android.graphics.RuntimeShader
import android.graphics.Shader
import android.os.Build
import androidx.annotation.RequiresApi
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asComposeRenderEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

// ============================================================================
//  «Liquid Glass»: живой фон из цветных пятен + стеклянные панели, которые
//  показывают этот же фон под собой — размытым, чуть увеличенным и
//  преломлённым у краёв (AGSL-шейдер на Android 13+, просто размытие на 12).
// ============================================================================

object Palette {
    val Base1 = Color(0xFF0B0B1A)
    val Base2 = Color(0xFF16112E)
    val Violet = Color(0xFF7C5CFF)
    val Pink = Color(0xFFFF4D9A)
    val Cyan = Color(0xFF22D3EE)
    val Orange = Color(0xFFFF8A3D)
    val Blue = Color(0xFF3B82F6)
    val Text = Color(0xFFF5F5FA)
    val TextDim = Color(0xB3F5F5FA)
    val Ok = Color(0xFF4ADE80)
    val Fail = Color(0xFFFF6B6B)

    val YouTube = Color(0xFFFF3B3B)
    val Boosty = Color(0xFFFF7A1A)
    val Kino = Color(0xFF8B5CF6)
    val Taplink = Color(0xFF14C8A0)
}

private data class Blob(val color: Color, val x: Float, val y: Float, val r: Float, val sx: Float, val sy: Float, val phase: Float)

private val blobs = listOf(
    Blob(Palette.Violet, 0.20f, 0.18f, 0.55f, 0.11f, 0.07f, 0.0f),
    Blob(Palette.Pink, 0.85f, 0.32f, 0.50f, 0.08f, 0.10f, 1.7f),
    Blob(Palette.Cyan, 0.15f, 0.70f, 0.45f, 0.09f, 0.06f, 3.1f),
    Blob(Palette.Orange, 0.80f, 0.85f, 0.42f, 0.07f, 0.09f, 4.4f),
    Blob(Palette.Blue, 0.50f, 0.50f, 0.40f, 0.05f, 0.12f, 2.2f),
)

/** Рисует фон так, будто начало этого DrawScope находится в точке [origin] корневого экрана. */
fun DrawScope.drawAurora(t: Float, root: Size, origin: Offset, zoom: Float = 1f) {
    if (root.width <= 0f) return
    val pivot = Offset(size.width / 2f, size.height / 2f)
    scale(zoom, pivot) {
        translate(-origin.x, -origin.y) {
            drawRect(Brush.verticalGradient(listOf(Palette.Base2, Palette.Base1, Palette.Base2), 0f, root.height), size = root)
            val m = min(root.width, root.height)
            for (b in blobs) {
                val cx = (b.x + 0.16f * sin(t * b.sx * 6.28f + b.phase)) * root.width
                val cy = (b.y + 0.10f * cos(t * b.sy * 6.28f + b.phase)) * root.height
                val r = b.r * m * (1f + 0.12f * sin(t * 0.3f + b.phase))
                drawCircle(
                    Brush.radialGradient(listOf(b.color.copy(alpha = 0.55f), b.color.copy(alpha = 0f)), Offset(cx, cy), r),
                    radius = r, center = Offset(cx, cy),
                )
            }
        }
    }
}

class Backdrop(val time: State<Float>) {
    var rootSize by mutableStateOf(IntSize.Zero)
}

val LocalBackdrop = staticCompositionLocalOf<Backdrop> { error("Нет AuroraRoot") }

@Composable
fun AuroraRoot(content: @Composable () -> Unit) {
    val transition = rememberInfiniteTransition(label = "aurora")
    // время в секундах
    val time = transition.animateFloat(0f, 10_000f, infiniteRepeatable(tween(10_000_000, easing = LinearEasing)), label = "t")
    val backdrop = remember { Backdrop(time) }
    Box(
        Modifier
            .fillMaxSize()
            .onSizeChanged { backdrop.rootSize = it }
            .drawBehind {
                drawAurora(time.value, Size(backdrop.rootSize.width.toFloat(), backdrop.rootSize.height.toFloat()), Offset.Zero)
            }
    ) {
        CompositionLocalProvider(LocalBackdrop provides backdrop) { content() }
    }
}

// ---------------------------------------------------------------- шейдер

@RequiresApi(33)
private object Refraction {
    const val SRC = """
        uniform shader content;
        uniform float2 size;
        uniform float radius;
        uniform float depth;

        half4 main(float2 coord) {
            float2 hs = size * 0.5;
            float2 p = coord - hs;
            float r = min(radius, min(hs.x, hs.y));
            float2 q = abs(p) - (hs - r);
            float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            float band = max(r * 1.3, 24.0);
            float e = clamp(1.0 + d / band, 0.0, 1.0);
            e = e * e * e;
            float2 n = normalize(p + float2(0.001, 0.001));
            float2 uv = coord - n * e * depth;
            half4 c = content.eval(uv);
            half4 cr = content.eval(uv - n * e * depth * 0.18);
            half4 cb = content.eval(uv + n * e * depth * 0.18);
            return half4(cr.r, c.g, cb.b, c.a);
        }
    """

    fun effect(w: Float, h: Float, radius: Float, depth: Float, blur: Float): RenderEffect? = try {
        val shader = RuntimeShader(SRC)
        shader.setFloatUniform("size", w, h)
        shader.setFloatUniform("radius", radius)
        shader.setFloatUniform("depth", depth)
        val refract = RenderEffect.createRuntimeShaderEffect(shader, "content")
        if (blur > 0f) RenderEffect.createChainEffect(refract, RenderEffect.createBlurEffect(blur, blur, Shader.TileMode.CLAMP))
        else refract
    } catch (_: Throwable) {
        null
    }
}

private fun glassRenderEffect(w: Float, h: Float, radius: Float, blur: Float): RenderEffect? = when {
    w <= 0f || h <= 0f -> null
    Build.VERSION.SDK_INT >= 33 -> Refraction.effect(w, h, radius, depth = min(w, h) * 0.10f, blur = blur)
        ?: RenderEffect.createBlurEffect(blur, blur, Shader.TileMode.CLAMP)
    Build.VERSION.SDK_INT >= 31 -> RenderEffect.createBlurEffect(blur, blur, Shader.TileMode.CLAMP)
    else -> null
}

// ---------------------------------------------------------------- стекло

/**
 * Стеклянная панель. [tint] — цветное свечение внутри (для брендовых кнопок),
 * [glow] 0..1 — подсветка (нажатие/активность).
 */
@Composable
fun GlassSurface(
    modifier: Modifier = Modifier,
    radius: Dp = 28.dp,
    tint: Color = Color.White,
    tintStrength: Float = 0f,
    glow: Float = 0f,
    blur: Dp = 22.dp,
    contentAlignment: Alignment = Alignment.TopStart,
    content: @Composable BoxScope.() -> Unit = {},
) {
    val backdrop = LocalBackdrop.current
    val density = LocalDensity.current
    val radiusPx = with(density) { radius.toPx() }
    val blurPx = with(density) { blur.toPx() }
    val shape = RoundedCornerShape(radius)
    var origin by remember { mutableStateOf(Offset.Zero) }
    val legacy = Build.VERSION.SDK_INT < 31

    Box(
        modifier
            .onGloballyPositioned { origin = it.positionInRoot() }
            .clip(shape),
        contentAlignment = contentAlignment,
    ) {
        // 1. фон «под стеклом»
        Canvas(
            Modifier
                .matchParentSize()
                .graphicsLayer {
                    renderEffect = glassRenderEffect(size.width, size.height, radiusPx, blurPx)?.asComposeRenderEffect()
                }
        ) {
            val root = backdrop.rootSize
            drawAurora(backdrop.time.value, Size(root.width.toFloat(), root.height.toFloat()), origin, zoom = 1.12f)
        }
        // 2. заливка, цветное свечение, блики
        Box(
            Modifier
                .matchParentSize()
                .drawWithContent {
                    val base = if (legacy) 0.16f else 0.07f
                    drawRect(Brush.verticalGradient(listOf(Color.White.copy(alpha = base + 0.06f + glow * 0.10f), Color.White.copy(alpha = base + glow * 0.06f))))
                    if (tintStrength > 0f) {
                        drawRect(
                            Brush.radialGradient(
                                listOf(tint.copy(alpha = 0.55f * tintStrength + glow * 0.2f), tint.copy(alpha = 0.10f * tintStrength)),
                                center = Offset(size.width * 0.15f, size.height * 0.5f),
                                radius = size.maxDimension * 0.9f,
                            )
                        )
                    }
                    // верхний блик — мягкий, без видимых краёв
                    drawRect(
                        Brush.verticalGradient(
                            listOf(Color.White.copy(alpha = 0.16f + glow * 0.08f), Color.White.copy(alpha = 0.04f), Color.Transparent),
                            0f, min(size.height * 0.55f, radiusPx * 3f),
                        )
                    )
                    // нижний отсвет
                    drawRect(Brush.verticalGradient(listOf(Color.Transparent, Color.White.copy(alpha = 0.06f)), size.height * 0.7f, size.height))
                    drawContent()
                }
                .border(
                    1.2.dp,
                    Brush.linearGradient(
                        listOf(Color.White.copy(alpha = 0.65f), Color.White.copy(alpha = 0.08f), Color.White.copy(alpha = 0.06f), Color.White.copy(alpha = 0.35f)),
                        start = Offset.Zero, end = Offset.Infinite,
                    ),
                    shape,
                )
        )
        content()
    }
}

/** Стеклянная кнопка с пружинящим нажатием. */
@Composable
fun GlassButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    radius: Dp = 28.dp,
    tint: Color = Color.White,
    tintStrength: Float = 0f,
    enabled: Boolean = true,
    onLongClick: (() -> Unit)? = null,
    contentAlignment: Alignment = Alignment.Center,
    content: @Composable BoxScope.() -> Unit,
) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.94f else 1f, spring(dampingRatio = 0.45f, stiffness = 500f), label = "scale")
    val glow by animateFloatAsState(if (pressed) 1f else 0f, label = "glow")
    val click = if (onLongClick != null) Modifier.combinedClickableCompat(interaction, enabled, onClick, onLongClick)
    else Modifier.clickable(interaction, indication = null, enabled = enabled, onClick = onClick)
    GlassSurface(
        modifier
            .graphicsLayer { scaleX = scale; scaleY = scale; alpha = if (enabled) 1f else 0.5f }
            .then(click),
        radius = radius, tint = tint, tintStrength = tintStrength, glow = glow, contentAlignment = contentAlignment,
        content = content,
    )
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
private fun Modifier.combinedClickableCompat(
    interaction: MutableInteractionSource,
    enabled: Boolean,
    onClick: () -> Unit,
    onLongClick: () -> Unit,
) = this.then(
    Modifier.combinedClickable(interactionSource = interaction, indication = null, enabled = enabled, onClick = onClick, onLongClick = onLongClick)
)
