using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LazyASF;

public static class Chrome
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    static readonly string LocalState = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Local State");

    public static string? FindExe(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ChromePath) && File.Exists(cfg.ChromePath)) return cfg.ChromePath;
        string[] candidates =
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Папка профиля по имени пользователя («Илья Сериков» → "Default").</summary>
    public static string ProfileDir(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ChromeProfileDir)) return cfg.ChromeProfileDir;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LocalState));
            foreach (var p in doc.RootElement.GetProperty("profile").GetProperty("info_cache").EnumerateObject())
            {
                foreach (var field in new[] { "gaia_name", "name", "gaia_given_name" })
                    if (p.Value.TryGetProperty(field, out var v) && string.Equals(v.GetString(), cfg.ChromeProfileName, StringComparison.OrdinalIgnoreCase))
                        return p.Name;
            }
        }
        catch (Exception e) { Log.Write($"Не прочитал профили Chrome: {e.Message}"); }
        return "Default";
    }

    static bool IsRunning() => Process.GetProcessesByName("chrome").Length > 0;

    static bool ForegroundIsChrome()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static async Task<bool> Open(AppConfig cfg, string url)
    {
        var exe = FindExe(cfg);
        if (exe is null) { Log.Write("chrome.exe не найден — укажи chromePath в config.json"); return false; }
        bool wasRunning = IsRunning();
        var profile = ProfileDir(cfg);
        var args = $"--profile-directory=\"{profile}\" --new-window --start-maximized";
        if (cfg.BrowserFullscreen && !wasRunning) args += " --start-fullscreen";
        args += $" \"{url}\"";
        Log.Write($"Chrome [{profile}]: {url}");
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });

        if (cfg.BrowserFullscreen && wasRunning)
        {
            // Уже запущенный Chrome игнорирует --start-fullscreen: ждём новое окно и жмём F11.
            for (int i = 0; i < 40 && !ForegroundIsChrome(); i++) await Task.Delay(200);
            await Task.Delay(1200);
            if (ForegroundIsChrome()) Native.Input.Combo("f11");
        }
        return true;
    }
}
