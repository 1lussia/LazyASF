using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LazyASF.Native;

/// <summary>Поиск и активация окон.</summary>
public static class Win
{
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    public static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Видимые окна браузера Chrome (главные, с заголовком).</summary>
    public static HashSet<IntPtr> ChromeWindows()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1" || !Title(h).EndsWith("Google Chrome")) return true;
            GetWindowThreadProcessId(h, out var pid);
            try { if (Process.GetProcessById((int)pid).ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase)) set.Add(h); }
            catch { }
            return true;
        }, IntPtr.Zero);
        return set;
    }

    public static bool IsForeground(IntPtr h) => GetForegroundWindow() == h;

    /// <summary>
    /// Вывести окно вперёд. Windows не даёт фоновым программам забирать фокус,
    /// но разрешает тому, кто прислал последнее событие ввода, — поэтому сначала «нажимаем» Alt.
    /// </summary>
    public static bool Activate(IntPtr h)
    {
        if (IsForeground(h)) return true;
        Input.Combo("alt");
        ShowWindow(h, 3); // SW_MAXIMIZE
        BringWindowToTop(h);
        SetForegroundWindow(h);
        return IsForeground(h);
    }
}
