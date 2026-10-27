using System.Runtime.InteropServices;

namespace LazyASF.Native;

/// <summary>Переключение режима проекции (как Win+P) через SetDisplayConfig.</summary>
public static class Display
{
    public enum Topology : uint { Internal = 1, Clone = 2, Extend = 4, External = 8, Unknown = 0 }

    const uint QDC_DATABASE_CURRENT = 0x4, SDC_APPLY = 0x80;
    const int PathInfoSize = 72, ModeInfoSize = 64;

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, IntPtr paths, ref uint numModes, IntPtr modes, out uint topologyId);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);

    public static Topology Current()
    {
        if (GetDisplayConfigBufferSizes(QDC_DATABASE_CURRENT, out var np, out var nm) != 0) return Topology.Unknown;
        IntPtr paths = Marshal.AllocHGlobal((int)Math.Max(1, np) * PathInfoSize);
        IntPtr modes = Marshal.AllocHGlobal((int)Math.Max(1, nm) * ModeInfoSize);
        try
        {
            return QueryDisplayConfig(QDC_DATABASE_CURRENT, ref np, paths, ref nm, modes, out var topo) == 0
                ? (Topology)topo : Topology.Unknown;
        }
        finally { Marshal.FreeHGlobal(paths); Marshal.FreeHGlobal(modes); }
    }

    /// <summary>true при успехе. External = «Только второй экран».</summary>
    public static bool Set(Topology t)
    {
        if (t == Topology.Unknown) return false;
        int rc = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | (uint)t);
        if (rc != 0) Log.Write($"SetDisplayConfig({t}) вернул {rc}");
        return rc == 0;
    }

    /// <summary>
    /// С повторами: телевизор, который только что включили или вывели из сна, первые секунды
    /// не отвечает по HDMI, и Windows возвращает ошибку 31. Под конец пробуем DisplaySwitch (как Win+P).
    /// </summary>
    public static async Task<bool> SetWithRetry(Topology t)
    {
        for (int i = 0; i < 4; i++)
        {
            if (Set(t)) { await Task.Delay(2500); return true; }
            await Task.Delay(2000);
        }
        var arg = t switch { Topology.Internal => "/internal", Topology.Clone => "/clone", Topology.Extend => "/extend", _ => "/external" };
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("DisplaySwitch.exe", arg) { UseShellExecute = false, CreateNoWindow = true });
            await Task.Delay(4000);
        }
        catch (Exception e) { Log.Write($"DisplaySwitch: {e.Message}"); }
        return Current() == t;
    }

    public static int ScreenCount => Screen.AllScreens.Length;
}
