package com.lazyasf.remote.ui

import android.view.HapticFeedbackConstants
import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.VolumeDown
import androidx.compose.material.icons.automirrored.rounded.VolumeUp
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.FastForward
import androidx.compose.material.icons.rounded.FastRewind
import androidx.compose.material.icons.rounded.Fullscreen
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.lazyasf.remote.AppViewModel
import com.lazyasf.remote.Screen

@Composable
fun TouchpadScreen(vm: AppViewModel, openSheet: (Sheet) -> Unit) {
    val view = LocalView.current
    val session by vm.pc.session.collectAsState()
    val countdown = rememberCountdown(session.sleepEndsAt)
    DisposableEffect(Unit) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }
    BackHandler { vm.screen = Screen.Home }
    val tick = { view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP) }

    Column(
        Modifier
            .fillMaxSize()
            .systemBarsPadding()
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        // --- верх: домой, статус наушников, таймер
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            GlassIconButton(Icons.Rounded.Home, "Домой", { vm.screen = Screen.Home }, size = 44.dp)
            OutputToggle(vm.output, vm::changeOutput, Modifier.width(150.dp), compact = true)
            val hp = session.step("headphones")
            when {
                session.busy -> GlassIconButton(Icons.Rounded.Headphones, "Настраиваю…", {}, size = 44.dp, tint = Palette.Orange)
                hp?.status == "fail" -> GlassIconButton(Icons.Rounded.Refresh, "Повторить наушники", vm::reconnectHeadphones, size = 44.dp, tint = Palette.Fail)
            }
            Spacer(Modifier.weight(1f))
            GlassChip(countdown ?: "Сон", Icons.Rounded.Bedtime, { openSheet(Sheet.Sleep) })
        }

        // --- ЛКМ / ПКМ
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            MouseKey("ЛКМ", "left", vm, Modifier.weight(1f).height(104.dp))
            MouseKey("ПКМ", "right", vm, Modifier.weight(1f).height(104.dp))
        }

        // --- тачпад
        TouchPad(vm, Modifier.fillMaxWidth().weight(1f))

        // --- медиа и клавиши
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            GlassIconButton(Icons.Rounded.FastRewind, "Назад 5 с", { tick(); vm.key("left") })
            GlassIconButton(Icons.Rounded.PlayArrow, "Пауза/плей", { tick(); vm.key("playpause") }, onLongClick = { vm.key("space") })
            GlassIconButton(Icons.Rounded.FastForward, "Вперёд 5 с", { tick(); vm.key("right") })
            GlassIconButton(Icons.Rounded.Fullscreen, "Во весь экран (долго — F11)", { tick(); vm.key("f") }, onLongClick = { vm.key("f11") })
            GlassButton(onClick = { tick(); vm.key("esc") }, modifier = Modifier.size(56.dp), radius = 28.dp) {
                Text("Esc", color = Palette.Text, fontWeight = FontWeight.SemiBold, fontSize = 15.sp)
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            GlassIconButton(Icons.AutoMirrored.Rounded.VolumeDown, "Тише", { tick(); vm.key("voldown") })
            GlassIconButton(Icons.AutoMirrored.Rounded.VolumeUp, "Громче", { tick(); vm.key("volup") })
            GlassIconButton(Icons.Rounded.Keyboard, "Клавиатура", { openSheet(Sheet.Keyboard) })
            GlassIconButton(Icons.AutoMirrored.Rounded.ArrowBack, "Назад в браузере", { tick(); vm.key("browserback") })
            GlassButton(onClick = { tick(); vm.key("ctrl+w") }, modifier = Modifier.size(56.dp), radius = 28.dp) {
                Text("✕Tab", color = Palette.Text, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
            }
        }
    }
}

/** Квадрат-кнопка мыши: нажал — кнопка зажата, отпустил — отпущена (можно тащить, водя другим пальцем по тачпаду). */
@Composable
private fun MouseKey(label: String, button: String, vm: AppViewModel, modifier: Modifier) {
    val view = LocalView.current
    var pressed by remember { mutableStateOf(false) }
    val glow by animateFloatAsState(if (pressed) 1f else 0f, label = "glow")
    val scale by animateFloatAsState(if (pressed) 0.95f else 1f, spring(dampingRatio = 0.5f, stiffness = 600f), label = "scale")
    GlassSurface(
        modifier
            .graphicsLayer { scaleX = scale; scaleY = scale }
            .pointerInput(button) {
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    down.consume()
                    pressed = true
                    vm.button(button, true)
                    view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
                    while (true) {
                        val ev = awaitPointerEvent()
                        ev.changes.forEach { it.consume() }
                        if (ev.changes.none { it.id == down.id && it.pressed }) break
                    }
                    pressed = false
                    vm.button(button, false)
                }
            },
        radius = 30.dp, glow = glow, tint = Palette.Violet, tintStrength = glow * 0.8f,
        contentAlignment = Alignment.Center,
    ) {
        Text(label, color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
private fun TouchPad(vm: AppViewModel, modifier: Modifier) {
    val view = LocalView.current
    var fingers by remember { mutableStateOf(emptyList<Offset>()) }
    GlassSurface(
        modifier
            .pointerInput(Unit) {
                val slop = viewConfiguration.touchSlop
                awaitEachGesture {
                    val first = awaitFirstDown(requireUnconsumed = false)
                    val start = first.uptimeMillis
                    var maxPointers = 1
                    var travel = 0f
                    var end = start
                    fingers = listOf(first.position)
                    while (true) {
                        val ev = awaitPointerEvent()
                        val pressed = ev.changes.filter { it.pressed }
                        end = ev.changes.maxOf { it.uptimeMillis }
                        if (pressed.isEmpty()) break
                        maxPointers = maxOf(maxPointers, pressed.size)
                        if (pressed.size == 1) {
                            val d = pressed[0].positionChange()
                            travel += d.getDistance()
                            // после двухпальцевого жеста курсор не дёргаем
                            if (maxPointers == 1) vm.move(d.x / density, d.y / density)
                        } else {
                            var sx = 0f
                            var sy = 0f
                            pressed.forEach { val d = it.positionChange(); sx += d.x; sy += d.y }
                            sx /= pressed.size; sy /= pressed.size
                            travel += Offset(sx, sy).getDistance()
                            vm.scroll(sx / density, sy / density)
                        }
                        ev.changes.forEach { it.consume() }
                        fingers = pressed.map { it.position }
                    }
                    fingers = emptyList()
                    val quick = end - start < 260
                    if (vm.tapToClick && quick && travel < slop) {
                        if (maxPointers >= 2) vm.click("right") else vm.click("left")
                        view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                    }
                }
            },
        radius = 34.dp,
    ) {
        // следы пальцев
        Box(
            Modifier
                .fillMaxSize()
                .drawBehind {
                    fingers.forEach { p ->
                        drawCircle(
                            Brush.radialGradient(listOf(Color.White.copy(alpha = 0.35f), Color.Transparent), p, 90.dp.toPx()),
                            radius = 90.dp.toPx(), center = p,
                        )
                    }
                }
        )
        Column(Modifier.align(Alignment.BottomCenter).padding(bottom = 14.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Text("1 палец — курсор · тап — клик", color = Palette.TextDim.copy(alpha = 0.45f), fontSize = 12.sp)
            Spacer(Modifier.width(0.dp).height(2.dp))
            Text("2 пальца — прокрутка · тап двумя — ПКМ", color = Palette.TextDim.copy(alpha = 0.45f), fontSize = 12.sp)
        }
    }
}
