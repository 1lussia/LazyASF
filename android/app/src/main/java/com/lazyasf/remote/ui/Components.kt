package com.lazyasf.remote.ui

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.Remove
import androidx.compose.material.icons.rounded.Tv
import androidx.compose.ui.draw.clip
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.lazyasf.remote.Site
import com.lazyasf.remote.net.Step
import kotlinx.coroutines.delay

@Composable
fun GlassIconButton(
    icon: ImageVector,
    contentDescription: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    size: Dp = 56.dp,
    tint: Color = Palette.Text,
    onLongClick: (() -> Unit)? = null,
) {
    GlassButton(onClick = onClick, onLongClick = onLongClick, modifier = modifier.size(size), radius = size / 2) {
        Icon(icon, contentDescription, tint = tint, modifier = Modifier.size(size * 0.45f))
    }
}

@Composable
fun GlassChip(text: String, icon: ImageVector?, onClick: () -> Unit, modifier: Modifier = Modifier, dot: Color? = null) {
    GlassButton(onClick = onClick, modifier = modifier, radius = 20.dp) {
        Row(Modifier.padding(horizontal = 14.dp, vertical = 9.dp), verticalAlignment = Alignment.CenterVertically) {
            if (dot != null) {
                Box(Modifier.size(8.dp).background(dot, CircleShape))
                Spacer(Modifier.width(8.dp))
            }
            if (icon != null) {
                Icon(icon, null, tint = Palette.Text, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
            }
            Text(text, color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium, maxLines = 1)
        }
    }
}

/** Нарисованные логотипы сайтов (без чужих картинок). */
@Composable
fun SiteLogo(site: Site, modifier: Modifier = Modifier) {
    Canvas(modifier) {
        val w = size.width
        when (site) {
            Site.YouTube -> {
                val h = w * 0.72f
                val top = (size.height - h) / 2
                drawRoundRect(Palette.YouTube, Offset(0f, top), Size(w, h), CornerRadius(w * 0.22f))
                val p = Path().apply {
                    moveTo(w * 0.40f, top + h * 0.28f); lineTo(w * 0.40f, top + h * 0.72f); lineTo(w * 0.70f, top + h * 0.5f); close()
                }
                drawPath(p, Color.White)
            }
            Site.Boosty -> {
                drawCircle(Brush.linearGradient(listOf(Color(0xFFFF9A3D), Color(0xFFFF5F1F))), radius = w / 2)
                // молния-«b»
                val p = Path().apply {
                    moveTo(w * 0.56f, w * 0.16f); lineTo(w * 0.30f, w * 0.56f); lineTo(w * 0.48f, w * 0.56f)
                    lineTo(w * 0.40f, w * 0.86f); lineTo(w * 0.72f, w * 0.42f); lineTo(w * 0.53f, w * 0.42f); close()
                }
                drawPath(p, Color.White)
            }
            Site.Kino -> {
                // кружок с буквой «p», как у kinopub
                drawCircle(Brush.linearGradient(listOf(Color(0xFFA78BFA), Color(0xFF5B21B6))), radius = w / 2)
                val stroke = Stroke(width = w * 0.09f, cap = StrokeCap.Round)
                drawLine(Color.White, Offset(w * 0.37f, w * 0.30f), Offset(w * 0.37f, w * 0.80f), strokeWidth = w * 0.09f, cap = StrokeCap.Round)
                drawCircle(Color.White, radius = w * 0.15f, center = Offset(w * 0.52f, w * 0.45f), style = stroke)
            }
            Site.Taplink -> {
                drawRoundRect(Brush.linearGradient(listOf(Color(0xFF2EE6B6), Color(0xFF0E9F87))), size = Size(w, size.height), cornerRadius = CornerRadius(w * 0.28f))
                // звенья цепочки-ссылки
                val stroke = Stroke(width = w * 0.08f, cap = StrokeCap.Round)
                rotate(-45f) {
                    drawRoundRect(Color.White, Offset(w * 0.20f, w * 0.38f), Size(w * 0.36f, w * 0.24f), CornerRadius(w * 0.12f), style = stroke)
                    drawRoundRect(Color.White, Offset(w * 0.44f, w * 0.38f), Size(w * 0.36f, w * 0.24f), CornerRadius(w * 0.12f), style = stroke)
                }
            }
        }
    }
}

@Composable
fun StepRow(step: Step) {
    Row(Modifier.padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(22.dp), contentAlignment = Alignment.Center) {
            when (step.status) {
                "running" -> CircularProgressIndicator(Modifier.size(18.dp), color = Palette.Text, strokeWidth = 2.dp)
                "ok" -> Icon(Icons.Rounded.Check, null, tint = Palette.Ok)
                "fail" -> Icon(Icons.Rounded.Close, null, tint = Palette.Fail)
                "skip" -> Icon(Icons.Rounded.Remove, null, tint = Palette.TextDim)
                else -> Box(Modifier.size(8.dp).background(Palette.TextDim, CircleShape))
            }
        }
        Spacer(Modifier.width(12.dp))
        Column {
            Text(step.title, color = if (step.status == "skip") Palette.TextDim else Palette.Text, fontSize = 15.sp)
            step.detail?.let { Text(it, color = Palette.TextDim, fontSize = 12.sp) }
        }
    }
}

/** Оставшееся время таймера «ч:мм» / «мм:сс»; обновляется раз в секунду. */
@Composable
fun rememberCountdown(endsAt: Long?): String? {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(endsAt) {
        while (endsAt != null) { now = System.currentTimeMillis(); delay(1000) }
    }
    if (endsAt == null) return null
    val left = ((endsAt - now) / 1000).coerceAtLeast(0)
    val h = left / 3600
    val m = (left % 3600) / 60
    val s = left % 60
    return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
}

@Composable
fun Spinner(modifier: Modifier = Modifier) {
    val t = rememberInfiniteTransition(label = "spin")
    val a by t.animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = LinearEasing), RepeatMode.Restart), label = "a")
    CircularProgressIndicator(modifier.rotate(a), color = Palette.Text, strokeWidth = 2.dp)
}

@Composable
fun SectionTitle(text: String) {
    Text(text, color = Palette.TextDim, fontSize = 13.sp, fontWeight = FontWeight.Medium, modifier = Modifier.padding(start = 4.dp, bottom = 8.dp))
}

val FullWidthSpacing = Arrangement.spacedBy(12.dp)

/** Переключатель «куда звук»: стеклянная таблетка с ползунком. */
@Composable
fun OutputToggle(output: String, onChange: (String) -> Unit, modifier: Modifier = Modifier, compact: Boolean = false) {
    val tv = output == "tv"
    val bias by animateFloatAsState(if (tv) 1f else 0f, spring(dampingRatio = 0.7f, stiffness = 400f), label = "bias")
    val shape = RoundedCornerShape(if (compact) 18.dp else 22.dp)
    GlassSurface(modifier.height(if (compact) 44.dp else 52.dp), radius = if (compact) 22.dp else 26.dp) {
        BoxWithConstraints(Modifier.fillMaxSize().padding(4.dp)) {
            val half = maxWidth / 2
            Box(
                Modifier
                    .offset(x = half * bias)
                    .width(half)
                    .fillMaxHeight()
                    .clip(shape)
                    .background(Color.White.copy(alpha = 0.20f))
                    .border(1.dp, Color.White.copy(alpha = 0.40f), shape)
            )
            Row(Modifier.fillMaxSize()) {
                OutputSegment(Icons.Rounded.Headphones, if (compact) null else "Наушники", !tv, compact, Modifier.weight(1f)) { onChange("headphones") }
                OutputSegment(Icons.Rounded.Tv, if (compact) null else "Телевизор", tv, compact, Modifier.weight(1f)) { onChange("tv") }
            }
        }
    }
}

@Composable
private fun OutputSegment(icon: ImageVector, label: String?, selected: Boolean, compact: Boolean, modifier: Modifier, onClick: () -> Unit) {
    Row(
        modifier
            .fillMaxHeight()
            .clickable(remember { MutableInteractionSource() }, indication = null, onClick = onClick),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val c = if (selected) Palette.Text else Palette.TextDim.copy(alpha = 0.55f)
        Icon(icon, label, tint = c, modifier = Modifier.size(if (compact) 20.dp else 20.dp))
        if (label != null) {
            Spacer(Modifier.width(6.dp))
            Text(label, color = c, fontSize = 15.sp, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal)
        }
    }
}
