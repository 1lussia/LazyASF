using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LazyASF;

public static class Chrome
{
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

    /// <summary>
    /// Открывает сайт в новом окне и возвращает это окно (IntPtr.Zero — окно не нашлось, но Chrome запущен).
    /// null — Chrome не найден.
    /// </summary>
    public static async Task<IntPtr?> Open(AppConfig cfg, string url)
    {
        var exe = FindExe(cfg);
        if (exe is null) { Log.Write("chrome.exe не найден — укажи chromePath в config.json"); return null; }
        var before = Native.Win.ChromeWindows();
        var profile = ProfileDir(cfg);
        Log.Write($"Chrome [{profile}]: {url}");
        var args = $"--profile-directory=\"{profile}\" --new-window --start-maximized \"{url}\"";
        // от имени Проводника — иначе Chrome унаследует исключение LazyASF из VPN (см. ProcessLauncher)
        if (!Native.ProcessLauncher.StartAsChildOf("explorer", exe, args))
        {
            Log.Write("Не смог запустить Chrome от имени Проводника — запускаю напрямую (он может пойти мимо VPN)");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        }

        for (int i = 0; i < 50; i++)
        {
            var fresh = Native.Win.ChromeWindows().Except(before).FirstOrDefault();
            if (fresh != IntPtr.Zero) return fresh;
            await Task.Delay(200);
        }
        Log.Write("Новое окно Chrome не нашлось");
        return IntPtr.Zero;
    }

    /// <summary>Полный экран (F11) для окна. false — окно не удалось сделать активным.</summary>
    public static bool Fullscreen(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Native.Win.Activate(window);
            Thread.Sleep(300);
            if (Native.Win.IsForeground(window))
            {
                Native.Input.Combo("f11");
                return true;
            }
            Thread.Sleep(500);
        }
        return false;
    }
}
