package com.lazyasf.remote

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.lazyasf.remote.data.Prefs
import com.lazyasf.remote.net.Conn
import com.lazyasf.remote.net.Discovery
import com.lazyasf.remote.net.FoundPc
import com.lazyasf.remote.net.PcConnection
import com.lazyasf.remote.phone.PhoneBluetooth
import com.lazyasf.remote.phone.SleepAlarm
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.min

enum class Screen { Connect, Home, Touchpad }

enum class Site(val key: String, val title: String, val subtitle: String) {
    YouTube("youtube", "YouTube", "Видео и подписки"),
    Boosty("boosty", "Boosty", "Авторы и посты"),
    Kino("kino", "kino.watch", "Фильмы и сериалы"),
}

sealed interface UiEvent {
    data class Toast(val text: String) : UiEvent
    data object OpenBluetoothSettings : UiEvent
}

class AppViewModel(app: Application) : AndroidViewModel(app) {
    val prefs = Prefs(app)
    val pc = PcConnection(prefs, viewModelScope)
    val events = MutableSharedFlow<UiEvent>(extraBufferCapacity = 8)

    var screen by mutableStateOf(Screen.Connect)
    val found = mutableStateListOf<FoundPc>()
    var scanning by mutableStateOf(false)
        private set
    var launching by mutableStateOf<Site?>(null)
        private set

    // настройки
    var headphones by mutableStateOf(prefs.headphones)
    var output by mutableStateOf(prefs.audioOutput)
        private set
    var sensitivity by mutableFloatStateOf(prefs.sensitivity)
    var scrollSpeed by mutableFloatStateOf(prefs.scrollSpeed)
    var tapToClick by mutableStateOf(prefs.tapToClick)
    var naturalScroll by mutableStateOf(prefs.naturalScroll)
    var toggleBluetooth by mutableStateOf(prefs.toggleBluetooth)

    init {
        prefs.host?.let { pc.connect(it, prefs.port, prefs.pcName) }
        scan()
        viewModelScope.launch {
            pc.conn.collect { c ->
                if (c is Conn.Connected && screen == Screen.Connect) screen = Screen.Home
            }
        }
    }

    fun saveSettings() {
        prefs.headphones = headphones
        prefs.sensitivity = sensitivity
        prefs.scrollSpeed = scrollSpeed
        prefs.tapToClick = tapToClick
        prefs.naturalScroll = naturalScroll
        prefs.toggleBluetooth = toggleBluetooth
    }

    fun scan() {
        if (scanning) return
        scanning = true
        viewModelScope.launch {
            val res = Discovery.scan(getApplication())
            found.clear()
            found.addAll(res)
            scanning = false
            // нашли «свой» ноут под новым IP — переподключаемся сами
            val known = res.firstOrNull { it.name == prefs.pcName }
            if (known != null && !pc.isConnected && pc.conn.value !is Conn.NeedPin && known.host != prefs.host)
                pc.connect(known.host, known.port, known.name)
        }
    }

    fun connectTo(p: FoundPc) = pc.connect(p.host, p.port, p.name)

    fun connectManual(address: String) {
        val parts = address.trim().split(":")
        val host = parts[0]
        if (host.isBlank()) return
        pc.connect(host, parts.getOrNull(1)?.toIntOrNull() ?: 47800, null)
    }

    fun submitPin(pin: String) = pc.submitPin(pin)

    fun forget() {
        prefs.forgetToken(prefs.pcName)
        prefs.host = null
        pc.disconnect()
        screen = Screen.Connect
        scan()
    }

    // ------------------------------------------------------------ сценарии
    fun launch(site: Site) {
        if (launching != null) return
        launching = site
        viewModelScope.launch {
            try {
                val ctx = getApplication<Application>()
                if (output == "headphones" && toggleBluetooth && PhoneBluetooth.isOn(ctx)) {
                    if (PhoneBluetooth.setOn(ctx, false)) {
                        prefs.btDisabledByApp = true
                        // ждём, пока наушники отцепятся от телефона
                        repeat(10) { if (PhoneBluetooth.isOn(ctx)) delay(300) }
                        delay(1200)
                    } else {
                        events.tryEmit(UiEvent.Toast("Не смог выключить Bluetooth — выключи его вручную"))
                        events.tryEmit(UiEvent.OpenBluetoothSettings)
                    }
                }
                if (!pc.send("launch", "site" to site.key, "headphones" to (headphones ?: pc.defaultDevice.value), "output" to output))
                    events.tryEmit(UiEvent.Toast("Нет связи с ноутом"))
                else {
                    delay(600)
                    screen = Screen.Touchpad
                }
            } finally {
                launching = null
            }
        }
    }

    /** "headphones" | "tv". Если режим ТВ уже идёт — переключаем звук сразу. */
    fun changeOutput(target: String) {
        if (output == target) return
        output = target
        prefs.audioOutput = target
        if (!pc.session.value.active) return
        if (target == "headphones") {
            reconnectHeadphones()
            return
        }
        viewModelScope.launch {
            pc.send("audio_output", "target" to "tv")
            // ноут переводит звук на ТВ и отпускает наушники — телефон забирает их обратно
            delay(4000)
            if (prefs.btDisabledByApp) {
                PhoneBluetooth.setOn(getApplication(), true)
                prefs.btDisabledByApp = false
            }
        }
    }

    fun reconnectHeadphones() {
        viewModelScope.launch {
            val ctx = getApplication<Application>()
            if (toggleBluetooth && PhoneBluetooth.isOn(ctx) && PhoneBluetooth.setOn(ctx, false)) {
                prefs.btDisabledByApp = true
                delay(2000)
            }
            pc.send("headphones_connect", "headphones" to (headphones ?: pc.defaultDevice.value))
        }
    }

    fun restore() {
        pc.send("restore")
        SleepAlarm.cancel(getApplication())
        screen = Screen.Home
        viewModelScope.launch {
            // ноут сначала отпускает наушники, потом телефон их забирает
            delay(4000)
            if (prefs.btDisabledByApp) {
                PhoneBluetooth.setOn(getApplication(), true)
                prefs.btDisabledByApp = false
            }
        }
    }

    fun setSleep(minutes: Int) {
        pc.send("sleep", "minutes" to minutes)
        if (prefs.btDisabledByApp)
            SleepAlarm.schedule(getApplication(), System.currentTimeMillis() + minutes * 60_000L + 20_000L)
    }

    fun cancelSleep() {
        pc.send("sleep_cancel")
        SleepAlarm.cancel(getApplication())
    }

    // ------------------------------------------------------------ мышь и клавиатура
    /** dx/dy — в dp. Чем быстрее палец, тем сильнее ускорение. */
    fun move(dxDp: Float, dyDp: Float) {
        val speed = kotlin.math.sqrt(dxDp * dxDp + dyDp * dyDp)
        val accel = 1f + min(2.2f, (speed - 1.5f).coerceAtLeast(0f) / 6f)
        val k = sensitivity * accel
        pc.send("move", "dx" to (dxDp * k).toDouble(), "dy" to (dyDp * k).toDouble())
    }

    fun scroll(dxDp: Float, dyDp: Float) {
        val sign = if (naturalScroll) 1f else -1f
        val k = 6f * scrollSpeed * sign
        val dy = dyDp * k
        val dx = -dxDp * k
        if (abs(dy) > 0.01f || abs(dx) > 0.01f) pc.send("scroll", "dy" to dy.toDouble(), "dx" to dx.toDouble())
    }

    fun button(b: String, down: Boolean) = pc.send("btn", "b" to b, "down" to down)
    fun click(b: String = "left") = pc.send("click", "b" to b)
    fun key(k: String) = pc.send("key", "k" to k)
    fun text(s: String) = pc.send("text", "s" to s)
}
