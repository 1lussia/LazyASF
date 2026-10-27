using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LazyASF;

/// <summary>
/// Включение/выключение AmneziaVPN на ноуте. Своей командной строки у Amnezia нет, поэтому нажимаем
/// её большую кнопку подключения через UI Automation (без мыши, работает и когда окно свёрнуто).
/// Включён ли VPN — смотрим по сетевому адаптеру AmneziaVPN.
/// </summary>
public static class VpnControl
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    public static bool Installed(AppConfig cfg) => File.Exists(cfg.AmneziaPath) || Process.GetProcessesByName("AmneziaVPN").Length > 0;

    public static bool IsOn()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                (n.Name.Contains("Amnezia", StringComparison.OrdinalIgnoreCase) ||
                 n.Description.Contains("Amnezia", StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }

    static AutomationElement? Window()
    {
        foreach (var p in Process.GetProcessesByName("AmneziaVPN"))
        {
            var wins = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
            foreach (AutomationElement w in wins)
                if (w.Current.ClassName.StartsWith("QQuickWindow", StringComparison.Ordinal)) return w;
        }
        return null;
    }

    /// <summary>Круглая кнопка на главном экране: «Подключено» / «Подключиться»; иначе — самая крупная кнопка.</summary>
    static AutomationElement? ConnectButton(AutomationElement win)
    {
        AutomationElement? named = null, largest = null;
        double namedArea = 0, largestArea = 0;
        var buttons = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement b in buttons)
        {
            var r = b.Current.BoundingRectangle;
            if (r.IsEmpty) continue;
            double area = r.Width * r.Height;
            var name = b.Current.Name;
            bool looksLikeConnect = new[] { "одключ", "тключ", "onnect" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
            if (looksLikeConnect && area > namedArea) { named = b; namedArea = area; }
            if (area > largestArea) { largest = b; largestArea = area; }
        }
        return named ?? largest;
    }

    public static async Task<(bool ok, string detail)> Set(AppConfig cfg, bool on)
    {
        if (IsOn() == on) return (true, on ? "уже включён" : "уже выключен");

        var win = Window();
        bool openedByUs = false;
        if (win is null)
        {
            if (!File.Exists(cfg.AmneziaPath)) return (false, $"AmneziaVPN не найден: {cfg.AmneziaPath}");
            // уже запущенная Amnezia при повторном запуске просто показывает своё окно
            Process.Start(new ProcessStartInfo(cfg.AmneziaPath) { UseShellExecute = true });
            openedByUs = true;
            for (int i = 0; i < 30 && (win = Window()) is null; i++) await Task.Delay(500);
            if (win is null) return (false, "окно AmneziaVPN не открылось");
            await Task.Delay(1500);
        }

        var btn = ConnectButton(win);
        if (btn is null) return (false, "не нашёл кнопку подключения в Amnezia");
        if (!btn.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return (false, "кнопка Amnezia не нажимается");
        Log.Write($"VPN: жму «{btn.Current.Name}» в Amnezia");
        ((InvokePattern)pattern).Invoke();

        for (int i = 0; i < 40 && IsOn() != on; i++) await Task.Delay(500);

        // если окно открыли мы — убираем, чтобы не висело поверх фильма на ТВ
        if (openedByUs) ShowWindow((IntPtr)win.Current.NativeWindowHandle, 6); // SW_MINIMIZE

        return IsOn() == on ? (true, on ? "включён" : "выключен") : (false, "Amnezia не переключилась за 20 с");
    }
}
