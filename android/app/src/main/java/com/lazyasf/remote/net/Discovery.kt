package com.lazyasf.remote.net

import android.content.Context
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

data class FoundPc(val name: String, val host: String, val port: Int)

/** Ищет ноутбук с LazyASF широковещательным UDP-запросом в локальной сети. */
object Discovery {
    private const val PORT = 47801

    private fun broadcastAddresses(): List<InetAddress> {
        val list = mutableListOf<InetAddress>(InetAddress.getByName("255.255.255.255"))
        runCatching {
            NetworkInterface.getNetworkInterfaces()?.toList().orEmpty()
                .filter { it.isUp && !it.isLoopback }
                .flatMap { it.interfaceAddresses }
                .filter { it.address is Inet4Address }
                .mapNotNull { it.broadcast }
                .forEach { if (it !in list) list += it }
        }
        return list
    }

    suspend fun scan(context: Context, rounds: Int = 3): List<FoundPc> = withContext(Dispatchers.IO) {
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val lock = wifi.createMulticastLock("lazyasf").apply { setReferenceCounted(false); acquire() }
        val found = linkedMapOf<String, FoundPc>()
        try {
            DatagramSocket().use { s ->
                s.broadcast = true
                s.soTimeout = 250
                val msg = "LAZYASF?".toByteArray()
                val targets = broadcastAddresses()
                repeat(rounds) {
                    targets.forEach { runCatching { s.send(DatagramPacket(msg, msg.size, it, PORT)) } }
                    val until = System.currentTimeMillis() + 600
                    val buf = ByteArray(2048)
                    while (System.currentTimeMillis() < until) {
                        val p = DatagramPacket(buf, buf.size)
                        try {
                            s.receive(p)
                            val json = JSONObject(String(p.data, 0, p.length))
                            if (json.optString("app") == "LazyASF") {
                                val host = p.address.hostAddress ?: continue
                                found[host] = FoundPc(json.optString("name", host), host, json.optInt("port", 47800))
                            }
                        } catch (_: SocketTimeoutException) {
                        } catch (_: Exception) {
                        }
                    }
                }
            }
        } catch (_: Exception) {
        } finally {
            lock.release()
        }
        found.values.toList()
    }
}
