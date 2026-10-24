package com.lazyasf.remote

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.togetherWith
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import com.lazyasf.remote.phone.PhoneBluetooth
import com.lazyasf.remote.ui.AppSheet
import com.lazyasf.remote.ui.AuroraRoot
import com.lazyasf.remote.ui.ConnectScreen
import com.lazyasf.remote.ui.HomeScreen
import com.lazyasf.remote.ui.Palette
import com.lazyasf.remote.ui.Sheet
import com.lazyasf.remote.ui.TouchpadScreen

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme(colorScheme = darkColorScheme(primary = Palette.Violet, background = Palette.Base1, surface = Palette.Base2)) {
                val ctx = LocalContext.current
                var sheet by remember { mutableStateOf(Sheet.None) }
                var pendingSite by remember { mutableStateOf<Site?>(null) }

                // без разрешения «Устройства поблизости» Android 12+ не даст выключить Bluetooth
                val btPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
                    pendingSite?.let(vm::launch)
                    pendingSite = null
                }
                val launch: (Site) -> Unit = { site ->
                    if (vm.toggleBluetooth && !PhoneBluetooth.hasPermission(ctx) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        pendingSite = site
                        btPermission.launch(Manifest.permission.BLUETOOTH_CONNECT)
                    } else vm.launch(site)
                }

                LaunchedEffect(Unit) {
                    vm.events.collect { e ->
                        when (e) {
                            is UiEvent.Toast -> Toast.makeText(ctx, e.text, Toast.LENGTH_LONG).show()
                            UiEvent.OpenBluetoothSettings -> runCatching {
                                ctx.startActivity(Intent(Settings.ACTION_BLUETOOTH_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                            }
                        }
                    }
                }

                AuroraRoot {
                    AnimatedContent(
                        targetState = vm.screen,
                        transitionSpec = { (fadeIn() + scaleIn(initialScale = 0.96f)) togetherWith fadeOut() },
                        label = "screen",
                    ) { s ->
                        when (s) {
                            Screen.Connect -> ConnectScreen(vm)
                            Screen.Home -> HomeScreen(vm, onLaunch = launch, openSheet = { sheet = it })
                            Screen.Touchpad -> TouchpadScreen(vm, openSheet = { sheet = it })
                        }
                    }
                    AppSheet(sheet, vm) { sheet = Sheet.None }
                }
            }
        }
    }
}
