using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LazyASF.Native;

public static class Power
{
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Погасить экран(ы). Телевизор без сигнала сам уходит в ожидание.</summary>
    public static void MonitorOff() => PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2);

    public static void Do(string action)
    {
        switch (action.ToLowerInvariant())
        {
            case "sleep": SetSuspendState(false, false, false); break;
            case "hibernate": SetSuspendState(true, false, false); break;
            // 60 секунд, чтобы успеть отменить: shutdown /a
            case "shutdown": Process.Start(new ProcessStartInfo("shutdown", "/s /t 60") { CreateNoWindow = true, UseShellExecute = false }); break;
            default: MonitorOff(); break;
        }
    }
}
