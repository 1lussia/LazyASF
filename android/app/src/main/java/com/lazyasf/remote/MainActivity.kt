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
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.lazyasf.remote.net.Conn
import com.lazyasf.remote.ui.GlassButton
import com.lazyasf.remote.ui.Spinner
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

    override fun onResume() {
        super.onResume()
        vm.onResume()
    }

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
                    Box(Modifier.fillMaxSize()) {
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
                        // связь пропала — говорим об этом крупно, а не красной точкой
                        val conn by vm.pc.conn.collectAsState()
                        AnimatedVisibility(
                            visible = vm.screen != Screen.Connect && conn !is Conn.Connected,
                            enter = fadeIn() + slideInVertically { -it },
                            exit = fadeOut() + slideOutVertically { -it },
                            modifier = Modifier.align(Alignment.TopCenter).statusBarsPadding().padding(top = 8.dp, start = 16.dp, end = 16.dp),
                        ) {
                            GlassButton(onClick = vm::onResume, modifier = Modifier.fillMaxWidth(), radius = 22.dp, tint = Palette.Orange, tintStrength = 0.8f) {
                                Row(Modifier.padding(horizontal = 16.dp, vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Spinner(Modifier.size(18.dp))
                                    Spacer(Modifier.width(12.dp))
                                    Text("Нет связи с ноутом — переподключаюсь…", color = Palette.Text, fontWeight = FontWeight.SemiBold)
                                }
                            }
                        }
                    }
                    AppSheet(sheet, vm) { sheet = Sheet.None }
                }
            }
        }
    }
}
