package com.lazyasf.remote.net

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities

/**
 * Сеть Wi-Fi в обход VPN. Если на телефоне включён VPN, Android отправляет в туннель даже
 * домашние адреса (192.168.x.x), и ноутбук не виден. Сокеты, привязанные к этой сети, идут напрямую.
 */
object WifiNet {
    /** VPN без «allowBypass» запрещает привязку (EPERM) — тогда ходим как обычно. */
    @Volatile var bindForbidden = false

    @Suppress("DEPRECATION")
    fun get(context: Context): Network? {
        if (bindForbidden) return null
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
        return cm.allNetworks.firstOrNull { n ->
            val caps = cm.getNetworkCapabilities(n) ?: return@firstOrNull false
            (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) &&
                !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
        }
    }
}
