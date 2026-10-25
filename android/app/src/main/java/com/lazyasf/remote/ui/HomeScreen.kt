package com.lazyasf.remote.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.Mouse
import androidx.compose.material.icons.rounded.SettingsBackupRestore
import androidx.compose.material.icons.rounded.Tune
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.lazyasf.remote.AppViewModel
import com.lazyasf.remote.Screen
import com.lazyasf.remote.Site
import com.lazyasf.remote.net.Conn

enum class Sheet { None, Sleep, Headphones, Settings, Keyboard }

@Composable
fun HomeScreen(vm: AppViewModel, onLaunch: (Site) -> Unit, openSheet: (Sheet) -> Unit) {
    val conn by vm.pc.conn.collectAsState()
    val session by vm.pc.session.collectAsState()
    val defaultDevice by vm.pc.defaultDevice.collectAsState()
    val countdown = rememberCountdown(session.sleepEndsAt)

    Column(
        Modifier
            .fillMaxSize()
            .systemBarsPadding()
            .padding(horizontal = 20.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        // --- верхняя строка
        Row(verticalAlignment = Alignment.CenterVertically) {
            val (label, dot) = when (val c = conn) {
                is Conn.Connected -> c.name to Palette.Ok
                is Conn.Connecting -> "Подключаюсь…" to Palette.Orange
                else -> "Нет связи" to Palette.Fail
            }
            GlassChip(label, null, onClick = {}, dot = dot)
            Spacer(Modifier.weight(1f))
            GlassIconButton(Icons.Rounded.Tune, "Настройки", { openSheet(Sheet.Settings) }, size = 46.dp)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            GlassChip(vm.headphones ?: defaultDevice ?: "Наушники", Icons.Rounded.Headphones, { openSheet(Sheet.Headphones) },
                dot = if (session.headphonesConnected) Palette.Ok else null)
            GlassChip(countdown ?: "Таймер сна", Icons.Rounded.Bedtime, { openSheet(Sheet.Sleep) })
        }
        OutputToggle(vm.output, vm::changeOutput, Modifier.fillMaxWidth())

        Text("Что смотрим?", color = Palette.Text, fontSize = 34.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp))

        // --- три большие кнопки
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Site.entries.forEach { site ->
                val tint = when (site) { Site.YouTube -> Palette.YouTube; Site.Boosty -> Palette.Boosty; Site.Kino -> Palette.Kino }
                val current = session.active && session.site == site.key
                GlassButton(
                    onClick = { onLaunch(site) },
                    modifier = Modifier.fillMaxWidth().weight(1f),
                    radius = 36.dp, tint = tint, tintStrength = if (current) 1f else 0.7f,
                    enabled = vm.launching == null,
                    contentAlignment = Alignment.CenterStart,
                ) {
                    Row(Modifier.padding(horizontal = 24.dp), verticalAlignment = Alignment.CenterVertically) {
                        SiteLogo(site, Modifier.size(62.dp))
                        Spacer(Modifier.width(20.dp))
                        Column(Modifier.weight(1f)) {
                            Text(site.title, color = Palette.Text, fontSize = 26.sp, fontWeight = FontWeight.Bold)
                            Text(if (current) "Сейчас на телевизоре" else site.subtitle, color = Palette.TextDim, fontSize = 14.sp)
                        }
                        if (vm.launching == site) Spinner(Modifier.size(26.dp))
                        else Icon(Icons.AutoMirrored.Rounded.ArrowForward, null, tint = Palette.TextDim)
                    }
                }
            }
        }

        // --- прогресс запуска
        var expanded by remember { mutableStateOf(true) }
        AnimatedVisibility(session.steps.isNotEmpty() && (session.busy || session.steps.any { it.status == "fail" })) {
            GlassButton(onClick = { expanded = !expanded }, modifier = Modifier.fillMaxWidth(), radius = 26.dp, contentAlignment = Alignment.TopStart) {
                Column(Modifier.padding(horizontal = 18.dp, vertical = 12.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(8.dp).background(if (session.busy) Palette.Orange else Palette.Fail, CircleShape))
                        Spacer(Modifier.width(8.dp))
                        Text(if (session.busy) "Настраиваю ноутбук…" else "Что-то не получилось", color = Palette.Text, fontWeight = FontWeight.SemiBold)
                    }
                    if (expanded) session.steps.forEach { StepRow(it) }
                }
            }
        }

        // --- нижние кнопки
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            GlassButton(onClick = { vm.screen = Screen.Touchpad }, modifier = Modifier.weight(1f).height(64.dp), radius = 24.dp) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.Mouse, null, tint = Palette.Text)
                    Spacer(Modifier.width(8.dp))
                    Text("Тачпад", color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                }
            }
            GlassButton(onClick = vm::restore, modifier = Modifier.weight(1f).height(64.dp), radius = 24.dp, enabled = session.active) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.SettingsBackupRestore, null, tint = Palette.Text)
                    Spacer(Modifier.width(8.dp))
                    Text("Вернуть всё", color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}
