package com.lazyasf.remote.data

import android.content.Context

class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("lazyasf", Context.MODE_PRIVATE)

    var host: String?
        get() = sp.getString("host", null)
        set(v) = sp.edit().putString("host", v).apply()

    var port: Int
        get() = sp.getInt("port", 47800)
        set(v) = sp.edit().putInt("port", v).apply()

    var pcName: String?
        get() = sp.getString("pcName", null)
        set(v) = sp.edit().putString("pcName", v).apply()

    var lastToken: String?
        get() = sp.getString("lastToken", null)
        set(v) = sp.edit().putString("lastToken", v).apply()

    fun token(pcName: String?): String? =
        pcName?.let { sp.getString("token_$it", null) } ?: lastToken

    fun saveToken(pcName: String, token: String) {
        sp.edit().putString("token_$pcName", token).putString("lastToken", token).apply()
    }

    fun forgetToken(pcName: String?) {
        sp.edit().apply { pcName?.let { remove("token_$it") } }.remove("lastToken").apply()
    }

    var headphones: String?
        get() = sp.getString("headphones", null)
        set(v) = sp.edit().putString("headphones", v).apply()

    /** Куда звук: "headphones" | "tv". */
    var audioOutput: String
        get() = sp.getString("audioOutput", "headphones") ?: "headphones"
        set(v) = sp.edit().putString("audioOutput", v).apply()

    var sensitivity: Float
        get() = sp.getFloat("sensitivity", 2.0f)
        set(v) = sp.edit().putFloat("sensitivity", v).apply()

    var scrollSpeed: Float
        get() = sp.getFloat("scrollSpeed", 1.0f)
        set(v) = sp.edit().putFloat("scrollSpeed", v).apply()

    var tapToClick: Boolean
        get() = sp.getBoolean("tapToClick", true)
        set(v) = sp.edit().putBoolean("tapToClick", v).apply()

    var naturalScroll: Boolean
        get() = sp.getBoolean("naturalScroll", true)
        set(v) = sp.edit().putBoolean("naturalScroll", v).apply()

    /** Выключать Bluetooth телефона при старте, чтобы наушники ушли к ноуту. */
    var toggleBluetooth: Boolean
        get() = sp.getBoolean("toggleBluetooth", true)
        set(v) = sp.edit().putBoolean("toggleBluetooth", v).apply()

    /** Bluetooth выключили мы — значит, потом надо включить обратно. */
    var btDisabledByApp: Boolean
        get() = sp.getBoolean("btDisabledByApp", false)
        set(v) = sp.edit().putBoolean("btDisabledByApp", v).apply()
}
