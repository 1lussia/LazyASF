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
        var before = Native.Win.ChromeWindows().ToHashSet();
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

    // ---------------------------------------------------------------- закладки

    /// <summary>Окно Chrome, с которым сейчас работаем: активное, иначе самое верхнее.</summary>
    static IntPtr CurrentWindow() => Native.Win.ChromeWindows().FirstOrDefault();

    public static bool HasWindow() => CurrentWindow() != IntPtr.Zero;

    /// <summary>Адрес и заголовок открытой страницы. null — Chrome не открыт.</summary>
    public static (string url, string title)? CurrentPage()
    {
        var h = CurrentWindow();
        if (h == IntPtr.Zero) return null;
        var title = Native.Win.Title(h);
        if (title.EndsWith(" - Google Chrome")) title = title[..^" - Google Chrome".Length];
        var url = OmniboxText(h) ?? CopyUrlViaKeyboard(h) ?? "";
        if (url.Length > 0 && !url.Contains("://")) url = "https://" + url;
        return (url, title.Trim());
    }

    /// <summary>Адресная строка через UI Automation — без нажатий и без буфера обмена.</summary>
    static string? OmniboxText(IntPtr h)
    {
        try
        {
            var win = System.Windows.Automation.AutomationElement.FromHandle(h);
            var box = win.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, "OmniboxViewViews"));
            if (box?.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var p) == true)
            {
                var v = ((System.Windows.Automation.ValuePattern)p).Current.Value?.Trim();
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch (Exception e) { Log.Write($"Адресная строка: {e.Message}"); }
        return null;
    }

    /// <summary>Запасной способ: Ctrl+L, Ctrl+C, Esc — и возвращаем буфер обмена как был.</summary>
    static string? CopyUrlViaKeyboard(IntPtr h)
    {
        if (!Native.Win.Activate(h, allowClick: false)) return null;
        string? result = null;
        var t = new Thread(() =>
        {
            string? old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
            Native.Input.Combo("ctrl+l"); Thread.Sleep(150);
            Native.Input.Combo("ctrl+c"); Thread.Sleep(200);
            Native.Input.Combo("esc");
            try { if (Clipboard.ContainsText()) result = Clipboard.GetText().Trim(); } catch { }
            try { if (old is not null) Clipboard.SetText(old); } catch { }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(3000);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// Открыть адрес в текущем окне Chrome (на ТВ оно уже на весь экран). Если окно не отдаёт фокус —
    /// открываем новой вкладкой в нём же через chrome.exe.
    /// </summary>
    public static void Navigate(AppConfig cfg, string url)
    {
        var h = CurrentWindow();
        if (h != IntPtr.Zero && Native.Win.Activate(h, allowClick: false))
        {
            Native.Input.Combo("ctrl+l"); Thread.Sleep(150);
            Native.Input.Text(url); Thread.Sleep(100);
            Native.Input.Combo("delete"); // убрать автодополнение, которое Chrome дописывает выделенным
            Native.Input.Combo("enter");
            return;
        }
        var exe = FindExe(cfg);
        if (exe is null) return;
        var args = $"--profile-directory=\"{ProfileDir(cfg)}\" \"{url}\"";
        if (!Native.ProcessLauncher.StartAsChildOf("explorer", exe, args))
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
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
