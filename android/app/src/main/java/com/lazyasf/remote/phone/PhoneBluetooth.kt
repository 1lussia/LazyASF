package com.lazyasf.remote.phone

import android.Manifest
import android.annotation.SuppressLint
import android.app.AlarmManager
import android.app.PendingIntent
import android.bluetooth.BluetoothManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import com.lazyasf.remote.data.Prefs

object PhoneBluetooth {
    private fun adapter(ctx: Context) =
        (ctx.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter

    fun hasPermission(ctx: Context) =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            ContextCompat.checkSelfPermission(ctx, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    @SuppressLint("MissingPermission")
    fun isOn(ctx: Context): Boolean = runCatching { adapter(ctx)?.isEnabled == true }.getOrDefault(false)

    /** Работает потому, что targetSdk = 32. На некоторых прошивках система ещё спросит подтверждение. */
    @SuppressLint("MissingPermission")
    @Suppress("DEPRECATION")
    fun setOn(ctx: Context, on: Boolean): Boolean = runCatching {
        val a = adapter(ctx) ?: return false
        if (a.isEnabled == on) true else if (on) a.enable() else a.disable()
    }.getOrDefault(false)
}

/** Включает Bluetooth телефона обратно, когда сработал таймер сна (телефон к этому моменту может спать). */
class BtRestoreReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val prefs = Prefs(context)
        if (prefs.btDisabledByApp) {
            PhoneBluetooth.setOn(context, true)
            prefs.btDisabledByApp = false
        }
    }
}

object SleepAlarm {
    private fun pending(ctx: Context) = PendingIntent.getBroadcast(
        ctx, 1, Intent(ctx, BtRestoreReceiver::class.java),
        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
    )

    fun schedule(ctx: Context, atMillis: Long) {
        val am = ctx.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        try {
            am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, atMillis, pending(ctx))
        } catch (_: SecurityException) {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, atMillis, pending(ctx))
        }
    }

    fun cancel(ctx: Context) {
        (ctx.getSystemService(Context.ALARM_SERVICE) as AlarmManager).cancel(pending(ctx))
    }
}
