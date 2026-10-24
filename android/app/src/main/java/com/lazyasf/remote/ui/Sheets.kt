package com.lazyasf.remote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardReturn
import androidx.compose.material.icons.automirrored.rounded.Send
import androidx.compose.material.icons.rounded.Backspace
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.SpaceBar
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.lazyasf.remote.AppViewModel

private val SheetBg = Color(0xF0141026)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AppSheet(sheet: Sheet, vm: AppViewModel, onDismiss: () -> Unit) {
    if (sheet == Sheet.None) return
    val state = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    ModalBottomSheet(
        onDismissRequest = { vm.saveSettings(); onDismiss() },
        sheetState = state,
        containerColor = SheetBg,
        contentColor = Palette.Text,
        shape = RoundedCornerShape(topStart = 32.dp, topEnd = 32.dp),
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .imePadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 20.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            when (sheet) {
                Sheet.Sleep -> SleepContent(vm, onDismiss)
                Sheet.Headphones -> HeadphonesContent(vm, onDismiss)
                Sheet.Settings -> SettingsContent(vm)
                Sheet.Keyboard -> KeyboardContent(vm)
                Sheet.None -> {}
            }
        }
    }
}

@Composable
private fun SheetTitle(text: String, sub: String? = null) {
    Column {
        Text(text, fontSize = 24.sp, fontWeight = FontWeight.Bold, color = Palette.Text)
        sub?.let { Text(it, fontSize = 14.sp, color = Palette.TextDim) }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun SleepContent(vm: AppViewModel, close: () -> Unit) {
    val session by vm.pc.session.collectAsState()
    val left = rememberCountdown(session.sleepEndsAt)
    SheetTitle("Таймер сна", left?.let { "Осталось $it" } ?: "Потом всё вернётся как было, ТВ и ноут уснут")
    FlowRow(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        listOf(15, 30, 45, 60, 90, 120).forEach { m ->
            GlassButton(onClick = { vm.setSleep(m); close() }, modifier = Modifier.size(width = 100.dp, height = 64.dp), radius = 22.dp) {
                Text(if (m < 60) "$m мин" else if (m % 60 == 0) "${m / 60} ч" else "${m / 60} ч ${m % 60}", fontSize = 18.sp, fontWeight = FontWeight.SemiBold, color = Palette.Text)
            }
        }
    }
    if (session.sleepEndsAt != null) {
        GlassButton(onClick = { vm.cancelSleep(); close() }, modifier = Modifier.fillMaxWidth().height(56.dp), radius = 22.dp, tint = Palette.Fail, tintStrength = 0.5f) {
            Text("Отменить таймер", fontWeight = FontWeight.SemiBold, color = Palette.Text)
        }
    }
}

@Composable
private fun HeadphonesContent(vm: AppViewModel, close: () -> Unit) {
    val devices by vm.pc.devices.collectAsState()
    val def by vm.pc.defaultDevice.collectAsState()
    LaunchedEffect(Unit) { vm.pc.send("devices") }
    val current = vm.headphones ?: def
    SheetTitle("Наушники", "К каким подключаться ноутбуку")
    devices.forEach { name ->
        GlassButton(
            onClick = { vm.headphones = name; vm.saveSettings(); close() },
            modifier = Modifier.fillMaxWidth().height(64.dp), radius = 22.dp,
            tint = Palette.Violet, tintStrength = if (name == current) 0.8f else 0f,
            contentAlignment = Alignment.CenterStart,
        ) {
            Row(Modifier.padding(horizontal = 18.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Rounded.Headphones, null, tint = Palette.Text)
                Spacer(Modifier.width(14.dp))
                Text(name, fontSize = 17.sp, color = Palette.Text, modifier = Modifier.weight(1f))
                if (name == current) Icon(Icons.Rounded.Check, null, tint = Palette.Ok)
            }
        }
    }
    if (devices.isEmpty()) Text("Ноутбук не прислал список — нет связи?", color = Palette.TextDim)
}

@Composable
private fun SettingsContent(vm: AppViewModel) {
    SheetTitle("Настройки")
    LabeledSlider("Скорость курсора", vm.sensitivity, 0.5f..5f) { vm.sensitivity = it }
    LabeledSlider("Скорость прокрутки", vm.scrollSpeed, 0.3f..3f) { vm.scrollSpeed = it }
    LabeledSwitch("Тап по тачпаду = клик", vm.tapToClick) { vm.tapToClick = it }
    LabeledSwitch("Естественная прокрутка", vm.naturalScroll) { vm.naturalScroll = it }
    LabeledSwitch("Выключать Bluetooth телефона при запуске", vm.toggleBluetooth) { vm.toggleBluetooth = it }
    GlassButton(onClick = vm::forget, modifier = Modifier.fillMaxWidth().height(52.dp), radius = 20.dp) {
        Text("Забыть этот ноутбук", color = Palette.Fail, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun LabeledSlider(label: String, value: Float, range: ClosedFloatingPointRange<Float>, onChange: (Float) -> Unit) {
    Column {
        Row {
            Text(label, color = Palette.Text, modifier = Modifier.weight(1f))
            Text("%.1f×".format(value), color = Palette.TextDim)
        }
        Slider(
            value = value, onValueChange = onChange, valueRange = range,
            colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = Palette.Violet, inactiveTrackColor = Color.White.copy(alpha = 0.15f)),
        )
    }
}

@Composable
private fun LabeledSwitch(label: String, value: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, color = Palette.Text, modifier = Modifier.weight(1f))
        Switch(
            checked = value, onCheckedChange = onChange,
            colors = SwitchDefaults.colors(checkedTrackColor = Palette.Violet, checkedThumbColor = Color.White, uncheckedTrackColor = Color.White.copy(alpha = 0.1f)),
        )
    }
}

@Composable
private fun KeyboardContent(vm: AppViewModel) {
    var text by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }
    SheetTitle("Ввод текста", "Например, поиск на YouTube")
    Row(verticalAlignment = Alignment.CenterVertically) {
        OutlinedTextField(
            value = text, onValueChange = { text = it },
            modifier = Modifier.weight(1f).focusRequester(focus),
            singleLine = true, colors = glassFieldColors(), shape = RoundedCornerShape(18.dp),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
            keyboardActions = KeyboardActions(onSend = { vm.text(text); vm.key("enter"); text = "" }),
            placeholder = { Text("Текст → на телевизор", color = Palette.TextDim) },
        )
        Spacer(Modifier.width(10.dp))
        GlassIconButton(Icons.AutoMirrored.Rounded.Send, "Отправить", { vm.text(text); text = "" })
    }
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        GlassIconButton(Icons.Rounded.Backspace, "Стереть", { vm.key("backspace") })
        GlassIconButton(Icons.Rounded.SpaceBar, "Пробел", { vm.key("space") })
        GlassIconButton(Icons.AutoMirrored.Rounded.KeyboardReturn, "Enter", { vm.key("enter") })
        GlassButton(onClick = { vm.key("tab") }, modifier = Modifier.size(56.dp), radius = 28.dp) { Text("Tab", color = Palette.Text) }
        GlassButton(onClick = { vm.key("ctrl+l") }, modifier = Modifier.size(56.dp), radius = 28.dp) { Text("URL", color = Palette.Text, fontSize = 13.sp) }
    }
}
