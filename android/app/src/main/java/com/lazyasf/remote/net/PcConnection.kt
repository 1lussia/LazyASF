package com.lazyasf.remote.net

import android.os.Build
import com.lazyasf.remote.data.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import javax.net.SocketFactory

sealed interface Conn {
    data object Idle : Conn
    data class Connecting(val host: String) : Conn
    data class NeedPin(val host: String, val wrongPin: Boolean) : Conn
    data class Connected(val name: String, val host: String) : Conn
    data class Lost(val host: String, val reason: String) : Conn
}

data class Step(val id: String, val title: String, val status: String, val detail: String?)

data class Session(
    val active: Boolean = false,
    val busy: Boolean = false,
    val site: String? = null,
    val headphones: String = "",
    val headphonesConnected: Boolean = false,
    val sleepEndsAt: Long? = null,
    val output: String = "headphones",
    val steps: List<Step> = emptyList(),
) {
    fun step(id: String) = steps.firstOrNull { it.id == id }
}

/** WebSocket-соединение с программой на ноуте. Сам переподключается, если связь пропала. */
class PcConnection(
    private val prefs: Prefs,
    private val scope: CoroutineScope,
    /** Сокеты через Wi-Fi в обход VPN; null — как решит система. */
    private val socketFactory: () -> SocketFactory?,
) {
    private val baseHttp = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(4, TimeUnit.SECONDS)
        .build()

    private fun httpFor(host: String): OkHttpClient {
        if (host == "127.0.0.1" || host == "localhost") return baseHttp // adb reverse по USB
        return socketFactory()?.let { baseHttp.newBuilder().socketFactory(it).build() } ?: baseHttp
    }

    private data class Target(val host: String, val port: Int, val name: String?)

    private var ws: WebSocket? = null
    private var target: Target? = null
    private var pin: String? = null
    private var wanted = false
    private var reconnectJob: Job? = null

    private val _conn = MutableStateFlow<Conn>(Conn.Idle)
    val conn: StateFlow<Conn> = _conn
    private val _session = MutableStateFlow(Session())
    val session: StateFlow<Session> = _session
    private val _devices = MutableStateFlow<List<String>>(emptyList())
    val devices: StateFlow<List<String>> = _devices
    private val _defaultDevice = MutableStateFlow<String?>(null)
    val defaultDevice: StateFlow<String?> = _defaultDevice

    val isConnected get() = _conn.value is Conn.Connected

    fun connect(host: String, port: Int, name: String?, pin: String? = null) {
        reconnectJob?.cancel()
        ws?.cancel()
        target = Target(host, port, name)
        this.pin = pin
        wanted = true
        open()
    }

    fun submitPin(pin: String) {
        val t = target ?: return
        connect(t.host, t.port, t.name, pin)
    }

    fun disconnect() {
        wanted = false
        reconnectJob?.cancel()
        ws?.close(1000, null)
        ws = null
        _conn.value = Conn.Idle
    }

    /** false — команда не ушла (нет связи); заодно запускаем переподключение. */
    fun send(type: String, vararg fields: Pair<String, Any?>): Boolean {
        val o = JSONObject().put("t", type)
        fields.forEach { (k, v) -> o.put(k, v) }
        val sent = isConnected && ws?.send(o.toString()) == true
        if (!sent && wanted && reconnectJob?.isActive != true && _conn.value !is Conn.Connecting) reconnectNow()
        return sent
    }

    @Volatile private var lastRx = 0L

    /**
     * Вызывается, когда приложение снова на экране. Samsung «замораживает» фоновые приложения,
     * и сокет может умереть молча — поэтому шлём ping и, если за 1,5 с нет ответа, переподключаемся.
     */
    fun ensureAlive() {
        if (!wanted || _conn.value is Conn.NeedPin) return
        val socket = ws
        if (socket == null || !isConnected) { if (_conn.value !is Conn.Connecting) reconnectNow(); return }
        val pingAt = System.currentTimeMillis()
        socket.send(JSONObject().put("t", "ping").toString())
        scope.launch {
            delay(1500)
            if (lastRx < pingAt && ws === socket) reconnectNow()
        }
    }

    private fun reconnectNow() {
        reconnectJob?.cancel()
        ws?.cancel()
        ws = null
        open()
    }

    private fun open() {
        val t = target ?: return
        _conn.value = Conn.Connecting(t.host)
        val req = Request.Builder().url("ws://${t.host}:${t.port}/ws").build()
        ws = httpFor(t.host).newWebSocket(req, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                val device = "${Build.MANUFACTURER} ${Build.MODEL}"
                val p = pin
                val token = prefs.token(t.name)
                when {
                    p != null -> webSocket.send(JSONObject().put("t", "pair").put("pin", p).put("device", device).toString())
                    token != null -> webSocket.send(JSONObject().put("t", "hello").put("token", token).put("device", device).toString())
                    else -> {
                        _conn.value = Conn.NeedPin(t.host, wrongPin = false)
                        webSocket.close(1000, null)
                    }
                }
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (webSocket !== ws) return
                lastRx = System.currentTimeMillis()
                runCatching { handle(t, JSONObject(text)) }
            }

            override fun onFailure(webSocket: WebSocket, t2: Throwable, response: Response?) {
                if (webSocket !== ws) return
                if (t2.message?.contains("EPERM") == true && !WifiNet.bindForbidden) {
                    WifiNet.bindForbidden = true
                    ws = null
                    open()
                    return
                }
                dropped(t, t2.message ?: "нет связи")
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (webSocket !== ws) return
                dropped(t, "закрыто")
            }
        })
    }

    private fun dropped(t: Target, reason: String) {
        ws = null
        if (_conn.value is Conn.NeedPin) return
        _conn.value = Conn.Lost(t.host, reason)
        if (wanted) {
            reconnectJob?.cancel()
            reconnectJob = scope.launch {
                delay(1000)
                if (wanted && ws == null) open()
            }
        }
    }

    private fun handle(t: Target, m: JSONObject) {
        when (m.optString("t")) {
            "welcome" -> connected(t, m.optString("name", t.host))
            "paired" -> {
                val name = m.optString("name", t.host)
                prefs.saveToken(name, m.getString("token"))
                pin = null
                connected(t.copy(name = name), name)
            }
            "auth_failed" -> {
                val wrong = pin != null
                pin = null
                if (!wrong) prefs.forgetToken(t.name)
                _conn.value = Conn.NeedPin(t.host, wrongPin = wrong)
            }
            "state" -> _session.value = parseSession(m.getJSONObject("state"))
            "devices" -> {
                val arr = m.getJSONArray("devices")
                _devices.value = (0 until arr.length()).map { arr.getString(it) }
                _defaultDevice.value = m.optString("default").ifBlank { null }
            }
        }
    }

    private fun connected(t: Target, name: String) {
        target = t.copy(name = name)
        prefs.host = t.host
        prefs.port = t.port
        prefs.pcName = name
        _conn.value = Conn.Connected(name, t.host)
    }

    private fun parseSession(s: JSONObject): Session {
        val arr = s.optJSONArray("steps")
        val steps = (0 until (arr?.length() ?: 0)).map {
            val o = arr!!.getJSONObject(it)
            Step(o.optString("id"), o.optString("title"), o.optString("status"), o.optString("detail").takeIf { d -> d.isNotBlank() && d != "null" })
        }
        return Session(
            active = s.optBoolean("active"),
            busy = s.optBoolean("busy"),
            site = s.optString("site").takeIf { it.isNotBlank() && it != "null" },
            headphones = s.optString("headphones"),
            headphonesConnected = s.optBoolean("headphonesConnected"),
            sleepEndsAt = if (s.isNull("sleepEndsAt")) null else s.optLong("sleepEndsAt"),
            output = s.optString("output", "headphones"),
            steps = steps,
        )
    }
}
