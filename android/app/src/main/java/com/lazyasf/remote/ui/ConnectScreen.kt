package com.lazyasf.remote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Laptop
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.lazyasf.remote.AppViewModel
import com.lazyasf.remote.net.Conn

@Composable
fun glassFieldColors() = OutlinedTextFieldDefaults.colors(
    focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text,
    focusedBorderColor = Color.White.copy(alpha = 0.6f), unfocusedBorderColor = Color.White.copy(alpha = 0.25f),
    cursorColor = Palette.Text, focusedLabelColor = Palette.TextDim, unfocusedLabelColor = Palette.TextDim,
    focusedContainerColor = Color.White.copy(alpha = 0.06f), unfocusedContainerColor = Color.White.copy(alpha = 0.04f),
)

@Composable
fun ConnectScreen(vm: AppViewModel) {
    val conn by vm.pc.conn.collectAsState()
    var manual by remember { mutableStateOf(vm.prefs.host ?: "") }
    var pin by remember { mutableStateOf("") }

    Column(
        Modifier
            .fillMaxSize()
            .systemBarsPadding()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Spacer(Modifier.height(24.dp))
        Text("LazyASF", color = Palette.Text, fontSize = 40.sp, fontWeight = FontWeight.Bold)
        Text("Запусти LazyASF на ноутбуке — он появится здесь сам.", color = Palette.TextDim, fontSize = 16.sp)

        // --- ввод кода
        val needPin = conn as? Conn.NeedPin
        if (needPin != null) {
            GlassSurface(Modifier.fillMaxWidth(), radius = 30.dp) {
                Column(Modifier.padding(22.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Text("Код с экрана ноутбука", color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
                    if (needPin.wrongPin) Text("Неверный код, попробуй ещё раз", color = Palette.Fail, fontSize = 14.sp)
                    OutlinedTextField(
                        value = pin,
                        onValueChange = { v ->
                            pin = v.filter(Char::isDigit).take(4)
                            if (pin.length == 4) { vm.submitPin(pin); pin = "" }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 32.sp, letterSpacing = 12.sp, textAlign = TextAlign.Center, color = Palette.Text),
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                        colors = glassFieldColors(),
                        shape = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
                    )
                }
            }
        }

        // --- найденные ноуты
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Ноутбуки в сети", color = Palette.TextDim, fontSize = 14.sp, modifier = Modifier.weight(1f))
            if (vm.scanning) Spinner(Modifier.size(18.dp))
            else GlassIconButton(Icons.Rounded.Refresh, "Искать снова", vm::scan, size = 40.dp)
        }
        if (vm.found.isEmpty() && !vm.scanning) {
            GlassSurface(Modifier.fillMaxWidth(), radius = 24.dp) {
                Text(
                    "Пока никого. Телефон и ноут в одной Wi-Fi сети? Брандмауэр Windows пропускает LazyASF?",
                    color = Palette.TextDim, fontSize = 14.sp, modifier = Modifier.padding(18.dp),
                )
            }
        }
        vm.found.forEach { pc ->
            GlassButton(onClick = { vm.connectTo(pc) }, modifier = Modifier.fillMaxWidth().height(76.dp), radius = 24.dp, contentAlignment = Alignment.CenterStart) {
                Row(Modifier.padding(horizontal = 20.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.Laptop, null, tint = Palette.Text, modifier = Modifier.size(30.dp))
                    Spacer(Modifier.width(16.dp))
                    Column {
                        Text(pc.name, color = Palette.Text, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
                        Text(pc.host, color = Palette.TextDim, fontSize = 13.sp)
                    }
                }
            }
        }

        // --- вручную
        Text("Или адрес вручную", color = Palette.TextDim, fontSize = 14.sp)
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = manual, onValueChange = { manual = it }, modifier = Modifier.weight(1f),
                placeholder = { Text("192.168.1.10", color = Palette.TextDim) }, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
                keyboardActions = KeyboardActions(onGo = { vm.connectManual(manual) }),
                colors = glassFieldColors(), shape = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
            )
            Spacer(Modifier.width(10.dp))
            GlassButton(onClick = { vm.connectManual(manual) }, modifier = Modifier.height(56.dp).width(80.dp), radius = 18.dp) {
                Text("OK", color = Palette.Text, fontWeight = FontWeight.SemiBold)
            }
        }

        Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            val status = when (val c = conn) {
                is Conn.Connecting -> "Подключаюсь к ${c.host}…"
                is Conn.Lost -> "Нет связи с ${c.host}: ${c.reason}"
                else -> ""
            }
            Text(status, color = Palette.TextDim, fontSize = 13.sp, textAlign = TextAlign.Center)
        }
    }
}
